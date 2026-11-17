using CivicEyeGD.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CivicEyeGD.Services;

/// <summary>
/// Маха снимките, за които няма ред в базата. Такъв файл се получава,
/// когато процесът падне между записа на диска и записа на сигнала, или
/// когато изтриването на файл след изтрит сигнал се провали. Докато
/// файлът е млад, не се пипа: сигнал може да се записва точно в този
/// момент, а редът още не е стигнал до базата.
/// </summary>
public static class ReportImageJanitor
{
    // Един ден е много повече от времето за запис на сигнал и много
    // по-малко от времето, за което папката би се напълнила.
    public static readonly TimeSpan MinimumAge = TimeSpan.FromDays(1);

    /// <summary>
    /// Връща броя изтрити файлове. Изключение при един файл не спира
    /// останалите: при стартиране сайтът трябва да тръгне дори когато
    /// един файл е заключен от антивирусна програма.
    /// </summary>
    public static async Task<int> SweepAsync(
        ApplicationDbContext context,
        ReportImageStorage storage,
        DateTime nowUtc,
        ILogger logger)
    {
        var knownNames = (await context.ReportImages
                .AsNoTracking()
                .Select(image => image.StoredFileName)
                .ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var removed = 0;

        foreach (var file in storage.EnumerateFiles())
        {
            if (knownNames.Contains(file.Name))
            {
                continue;
            }

            if (nowUtc - file.LastWriteTimeUtc < MinimumAge)
            {
                continue;
            }

            try
            {
                file.Delete();
                removed++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Осиротяла снимка не може да се изтрие: {File}.", file.Name);
            }
        }

        return removed;
    }
}

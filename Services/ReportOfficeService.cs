using CivicEyeGD.Data;
using Microsoft.EntityFrameworkCore;

namespace CivicEyeGD.Services;

/// <summary>
/// Единственото място, където общината се намесва в сигнал: статус, публична
/// бележка и корекция на приоритета. Администраторското табло и панелът в
/// страницата на сигнала извикват тази услуга, а не копират логика — две
/// копия на едно правило се разминават, а тук числата трябва да съвпадат.
/// </summary>
public sealed class ReportOfficeService
{
    /// <summary>
    /// Бележката се показва публично в историята на сигнала, затова има
    /// таван: без него един запис може да раздуе страницата и да скрие
    /// останалите стъпки от дневника.
    /// </summary>
    public const int MaxCommentLength = 500;

    /// <summary>
    /// Резултатът от опита за намеса. Връщаме число, а не изключение, защото
    /// и двете страници трябва да покажат различно съобщение за всеки случай.
    /// </summary>
    public enum Outcome
    {
        /// <summary>Нещо е променено или е оставена бележка — записано е.</summary>
        Saved,

        /// <summary>Статусът, корекцията и бележката са същите — нищо не се пипа.</summary>
        Unchanged,

        /// <summary>Статусът не е от четирите в <see cref="ReportStatuses"/> — заявката е невалидна.</summary>
        UnknownStatus,

        /// <summary>Корекцията е извън скалата 0–10.</summary>
        AdjustmentOutOfRange,

        /// <summary>Сигналът го няма — връщаме 404, а не празна страница.</summary>
        NotFound,

        /// <summary>Сигналът и всичко около него са премахнати.</summary>
        Deleted
    }

    private readonly ApplicationDbContext _context;
    private readonly ReportImageStorage _storage;

    public ReportOfficeService(ApplicationDbContext context, ReportImageStorage storage)
    {
        _context = context;
        _storage = storage;
    }

    /// <summary>
    /// Записва едната стъпка на общината: сменя статуса, отбелязва корекцията
    /// и оставя публична бележка — всичко това в един запис на историята.
    /// Проверките за статус и за скалата са преди каквото и да е записване,
    /// за да не остане полузаписан сигнал след лош опит.
    /// </summary>
    public async Task<Outcome> UpdateAsync(
        string? officerUserId,
        int reportId,
        string newStatus,
        string? comment,
        int adjustment)
    {
        if (!ReportStatuses.IsValid(newStatus))
        {
            return Outcome.UnknownStatus;
        }

        // Корекцията е число 0–10 в края на сметката за приоритета, а не
        // скрита намеса: без ограничение едно щракване би направило който и
        // да е сигнал „висок приоритет“ и би обезсмислило класацията.
        if (adjustment < 0 || adjustment > 10)
        {
            return Outcome.AdjustmentOutOfRange;
        }

        var report = await _context.Reports.FindAsync(reportId);
        if (report is null)
        {
            return Outcome.NotFound;
        }

        var note = NormalizeComment(comment);
        var statusChanged = report.Status != newStatus;
        var adjustmentChanged = report.AdminPriorityAdjustment != adjustment;

        // Историята е дневник на намесите, не само на статусите: запис става
        // и когато се промени само приоритетът, и когато служителят остави
        // само бележка. Ако няма нито промяна, нито бележка, не оставяме
        // поредния ред, който нищо не казва.
        if (!statusChanged && !adjustmentChanged && note is null)
        {
            return Outcome.Unchanged;
        }

        var entry = new StatusHistory
        {
            ReportId = reportId,
            // Когато статусът не се мени, старият и новият са един и същ.
            // От това страницата на сигнала разбира, че записът е бележка
            // или корекция, а не преход между статуси.
            OldStatus = report.Status,
            NewStatus = newStatus,
            ChangedByUserId = officerUserId,
            ChangedAt = DateTime.UtcNow,
            Comment = note
        };

        // Двете числа влизат само при реална промяна: ред „3 → 3“ би
        // подсказвал намеса, каквато не е правена.
        if (adjustmentChanged)
        {
            entry.OldPriorityAdjustment = report.AdminPriorityAdjustment;
            entry.NewPriorityAdjustment = adjustment;
        }

        report.Status = newStatus;
        report.AdminPriorityAdjustment = adjustment;

        _context.StatusHistories.Add(entry);
        await _context.SaveChangesAsync();

        return Outcome.Saved;
    }

    /// <summary>
    /// Пълното премахване на един сигнал: той самият, снимките му от
    /// базата и от диска, оценките, потвържденията и дневника. Нужно е,
    /// защото законът за личните данни дава право на изтриване и потому
    /// че грешка, попаднала в публичния изглед, не се поправя със статус
    /// „отхвърлен“ — тя продължава да се вижда.
    /// Каскадата в DbContext-а се грижи за редовете; за файловете няма
    /// база, която да ги изтрие, затова се махат тук.
    /// </summary>
    public async Task<Outcome> DeleteAsync(int reportId)
    {
        var report = await _context.Reports
            // Имената на файловете са нужни преди изтриването: след
            // SaveChanges редовете ги няма и никой не би знаел кой файл
            // да махне от диска.
            .Include(r => r.Images)
            .FirstOrDefaultAsync(r => r.Id == reportId);

        if (report is null)
        {
            return Outcome.NotFound;
        }

        var storedFileNames = report.Images
            .Select(image => image.StoredFileName)
            .ToList();

        _context.Reports.Remove(report);
        await _context.SaveChangesAsync();

        // Чак сега, когато записът в базата е успешен: при провален
        // трансактор снимките остават на място и сигналът пак се отваря.
        foreach (var storedFileName in storedFileNames)
        {
            _storage.Delete(storedFileName);
        }

        return Outcome.Deleted;
    }

    /// <summary>
    /// Изчиства бележката преди запис: празен и само-интервал текст стават
    /// null, за да не се пазят празни кавички в публичната история.
    /// </summary>
    private static string? NormalizeComment(string? comment)
    {
        var trimmed = comment?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;

        return trimmed.Length > MaxCommentLength ? trimmed[..MaxCommentLength] : trimmed;
    }
}

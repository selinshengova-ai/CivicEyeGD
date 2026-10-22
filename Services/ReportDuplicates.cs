using CivicEyeGD.Data;

namespace CivicEyeGD.Services;

/// <summary>Един съществуващ сигнал, който е много близо до новата точка.</summary>
public sealed record NearbyReport(
    int Id,
    string Title,
    string Address,
    string Status,
    double DistanceMeters);

/// <summary>
/// Преди нов сигнал да се запише, проверяваме дали съсед не е описал
/// същия проблем на същото място. Дубликатите не са невинни: те делят
/// гласовете на два записа вместо да ги съберат в един, а общината вижда
/// две карточки за една дупка. Затова платформата първо предлага да потвърдиш
/// съществуващия сигнал, а нов се подава само след явно признание, че
/// проблемът е различен.
///
/// Решението е отделено от достъпа до базата, за да може правилото да се
/// тества без нито един запис в SQLite.
/// </summary>
public static class ReportDuplicates
{
    // Градската среда се повтаря на всеки десетина метра: две точки на 40 m
    // са един и същи тротоар, две на 120 m — вече различни улици.
    public const double RadiusMeters = 50;

    // Повече от три предложения не се четат на екран, а скриват полето
    // за признание под тях.
    public const int MaxMatches = 3;

    public static IReadOnlyList<NearbyReport> Find(
        IEnumerable<Report> candidates,
        string category,
        double? latitude,
        double? longitude)
    {
        if (latitude is null || longitude is null) return [];

        return candidates
            .Where(report => report.Category == category)
            .Where(report => !ReportStatuses.IsClosed(report.Status))
            .Where(report => report.Latitude is not null && report.Longitude is not null)
            .Select(report => new NearbyReport(
                report.Id,
                report.Title,
                report.Address,
                report.Status,
                ReportGeography.DistanceMeters(
                    latitude.Value,
                    longitude.Value,
                    report.Latitude!.Value,
                    report.Longitude!.Value)))
            .Where(match => match.DistanceMeters <= RadiusMeters)
            .OrderBy(match => match.DistanceMeters)
            .ThenBy(match => match.Id)
            .Take(MaxMatches)
            .ToList();
    }
}

namespace CivicEyeGD.Data;

// Единствен източник на истината за разрешените категории и статуси.
// Ползва се и от формите, и от сървърната валидация, за да не може
// през директна заявка да се запише стойност извън списъка.
public static class ReportCategories
{
    public const string Accessibility = "Достъпност";
    public const string Sidewalks = "Тротоари";
    public const string PedestrianPaths = "Пешеходни пътеки";
    public const string StreetLighting = "Улично осветление";
    public const string RoadSurface = "Пътна настилка";
    public const string Other = "Друго";

    public static readonly IReadOnlyList<string> All =
    [
        Accessibility,
        Sidewalks,
        PedestrianPaths,
        StreetLighting,
        RoadSurface,
        Other
    ];

    public static bool IsValid(string? category) =>
        category is not null && All.Contains(category);
}

public static class ReportStatuses
{
    public const string New = "Подаден";
    public const string InProgress = "В процес";
    public const string Resolved = "Решен";
    public const string Rejected = "Отхвърлен";

    public static readonly IReadOnlyList<string> All =
    [
        New,
        InProgress,
        Resolved,
        Rejected
    ];

    // Статуси, след които сигналът не изисква действие от общината.
    public static readonly IReadOnlyList<string> Closed = [Resolved, Rejected];

    public static bool IsValid(string? status) =>
        status is not null && All.Contains(status);

    public static bool IsClosed(string? status) =>
        status is not null && Closed.Contains(status);
}

// Границите на Гоце Делчев. Използват се да отхвърлят координати,
// поставени извън града — например нули или стойности от друга карта.
public static class ReportGeography
{
    public const double MinLatitude = 41.50;
    public const double MaxLatitude = 41.65;
    public const double MinLongitude = 23.65;
    public const double MaxLongitude = 23.80;

    public static bool IsWithinTown(double? latitude, double? longitude) =>
        latitude is null || longitude is null
            ? latitude is null && longitude is null
            : latitude >= MinLatitude && latitude <= MaxLatitude
              && longitude >= MinLongitude && longitude <= MaxLongitude;

    // Разстояние по голям кръг (формула на хаверсинуса). Градът е малък,
    // затова плоското разстояние би донесло грешка от няколко метра.
    public static double DistanceMeters(
        double fromLatitude,
        double fromLongitude,
        double toLatitude,
        double toLongitude)
    {
        const double earthRadiusMeters = 6_371_000;

        var dLatitude = (toLatitude - fromLatitude) * Math.PI / 180.0;
        var dLongitude = (toLongitude - fromLongitude) * Math.PI / 180.0;

        var a = Math.Sin(dLatitude / 2) * Math.Sin(dLatitude / 2)
            + Math.Cos(fromLatitude * Math.PI / 180.0)
              * Math.Cos(toLatitude * Math.PI / 180.0)
              * Math.Sin(dLongitude / 2) * Math.Sin(dLongitude / 2);

        return earthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}

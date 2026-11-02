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

    // Списъкът идва в този ред и в падащото меню на формата, и в
    // статистиката — единственият източник държи подреждането еднакво.
    public static readonly IReadOnlyList<string> All =
    [
        Accessibility,
        Sidewalks,
        PedestrianPaths,
        StreetLighting,
        RoadSurface,
        Other
    ];

    // Проверката е за точно равенство на текстовете, не за съдържание:
    // при директна заявка се допуска само стойност, която съвпада
    // изцяло с някоя от константите — „Тротоар “ с интервал или
    // измамен текст се отхвърля.
    public static bool IsValid(string? category) =>
        category is not null && All.Contains(category);
}

public static class ReportStatuses
{
    // Имената са константи, защото статусът участва в условия из целия
    // код; с буквален низ една печатна грешка би създала статус, който
    // никоя проверка не разпознава.
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
    // Един списък, защото и филтърът в административната страница, и
    // правилото за спешност на картата го четат; нов краен статус се
    // добавя на едно място, не на две.
    public static readonly IReadOnlyList<string> Closed = [Resolved, Rejected];

    public static bool IsValid(string? status) =>
        status is not null && All.Contains(status);

    // Отделен предикат, а не `status == Resolved || status == Rejected`
    // навсякъде: списъкът се променя веднъж, а четенето на кода остава
    // изречение като българското „затворен ли е“.
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

    // Кутията е груба, не точен контур на общината: целта е да хване
    // нули от четене без GPS и координати от чужда карта, а не да
    // отсъжда кадастрни спорове; затова е правоъгълник с малък запас.
    public static bool IsWithinTown(double? latitude, double? longitude) =>
        // Липсващи координати са законен сигнал (полетата в Report са
        // с разрешена липса), но едната сама без другата е грешка в
        // данните — редът или има двойка, или няма нищо.
        latitude is null || longitude is null
            ? latitude is null && longitude is null
            : latitude >= MinLatitude && latitude <= MaxLatitude
              && longitude >= MinLongitude && longitude <= MaxLongitude;

    // Разстояние по голям кръг (формула на хаверсинуса). Градът е малък,
    // затова плоското разстояние би донесло грешка от няколко метра.
    // Ползва се при проверката за припокриване: два сигнала за един
    // и същ проблем в близки точки се разпознават по разстоянието.
    public static double DistanceMeters(
        double fromLatitude,
        double fromLongitude,
        double toLatitude,
        double toLongitude)
    {
        // Средният радиус на Земята в метри — константа, за да се чете
        // формулата като в учебника, без магическо число в израза.
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

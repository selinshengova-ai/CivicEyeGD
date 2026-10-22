using CivicEyeGD.Data;

namespace CivicEyeGD.Services;

/// <summary>Една лента от диаграмата: как се казва, колко е голяма и какъв е резултатът ѝ.</summary>
public sealed record StatisticBar(string Label, int Count, double Score);

/// <summary>Клетка от топлинната матрица: категория срещу статус.</summary>
public sealed record HeatCell(string Category, string Status, int Count);

/// <summary>
/// Числата зад PDF отчета. Те са отделени от рисуването, защото тук се
/// решава какво е вярно, а документът само ги показва; така агрегатите
/// могат да се тестват без нито един байт PDF.
/// </summary>
public static class ReportStatistics
{
    // Пет локации се четат на една страница; повече биха изисквали
    // второ подзаглавие и биха крият останалите секции.
    public const int TopLocationCount = 5;

    public const string NoCategoryLabel = "Без категория";
    public const string NoLocationLabel = "Без посочен адрес";

    /// <summary>Брой сигнали по категория, с средния им приоритет.</summary>
    public static IReadOnlyList<StatisticBar> ByCategory(IReadOnlyList<ReportExportRow> rows) =>
        Group(rows, row => CategoryOf(row.Category))
            .OrderByDescending(bar => bar.Count)
            .ThenByDescending(bar => bar.Score)
            .ToList();

    /// <summary>Категорията, по която броим: празният текст не е категория.</summary>
    public static string CategoryOf(string? category) =>
        string.IsNullOrWhiteSpace(category) ? NoCategoryLabel : category.Trim();

    /// <summary>Най-натоварените локации, подредени по брой сигнали.</summary>
    public static IReadOnlyList<StatisticBar> TopLocations(
        IReadOnlyList<ReportExportRow> rows,
        int take = TopLocationCount) =>
        Group(rows, row => LocationOf(row.Address))
            .OrderByDescending(bar => bar.Count)
            .ThenByDescending(bar => bar.Score)
            .Take(take)
            .ToList();

    /// <summary>
    /// Локацията на сигнала се вади от свободния текст на адреса:
    /// „бул. „Гоце Делчев“ 14“ става „Гоце Делчев“. Номерът се маха,
    /// защото без него сигналите от една улица се събират на едно място,
    /// а точно това търси отчетът.
    /// </summary>
    public static string LocationOf(string? address)
    {
        var text = (address ?? string.Empty).Trim();
        if (text.Length == 0) return NoLocationLabel;

        foreach (var (open, close) in new[] { ('„', '“'), ('"', '"'), ('«', '»') })
        {
            var from = text.IndexOf(open);
            var to = from < 0 ? -1 : text.IndexOf(close, from + 1);

            if (to > from) return Cut(text[(from + 1)..to]);
        }

        return Cut(StripPrefix(text));
    }

    private static readonly string[] Prefixes = { "бул.", "ул.", "кв.", "с.", "гр." };

    private static string StripPrefix(string text)
    {
        foreach (var prefix in Prefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return text[prefix.Length..].TrimStart();
            }
        }

        return text;
    }

    private static readonly char[] Digits = "0123456789".ToCharArray();

    // Всичко след първата цифра е номер на сградата, а всичко след първата
    // запетая е ориентир („пред спирката“). Нито едното, нито другото е име
    // на локацията: без тях сигналите от една улица се събират в една лента.
    private static string Cut(string value)
    {
        var comma = value.IndexOf(',');
        if (comma >= 0) value = value[..comma];

        var digits = value.IndexOfAny(Digits);
        var head = digits < 0 ? value : value[..digits];
        head = head.Trim().TrimEnd(',', '.', '-');

        return head.Length == 0 ? NoLocationLabel : head;
    }

    /// <summary>Матрица категория × статус: колко сигнала има във всяка клетка.</summary>
    public static IReadOnlyList<HeatCell> HeatMatrix(IReadOnlyList<ReportExportRow> rows)
    {
        var categories = HeatCategories(rows);
        var cells = new List<HeatCell>(categories.Count * ReportStatuses.All.Count);

        foreach (var category in categories)
        {
            foreach (var status in ReportStatuses.All)
            {
                cells.Add(new HeatCell(
                    category,
                    status,
                    rows.Count(row => CategoryOf(row.Category) == category && row.Status == status)));
            }
        }

        return cells;
    }

    /// <summary>Категориите на матрицата, в същия ред като клетките.</summary>
    public static IReadOnlyList<string> HeatCategories(IReadOnlyList<ReportExportRow> rows) =>
        rows.Select(row => CategoryOf(row.Category))
            .Distinct()
            .OrderByDescending(name => rows.Count(row => CategoryOf(row.Category) == name))
            .ToList();

    /// <summary>Сигналите, които все още чакат решение.</summary>
    public static IReadOnlyList<ReportExportRow> Open(IReadOnlyList<ReportExportRow> rows) =>
        rows.Where(row => !ReportStatuses.IsClosed(row.Status)).ToList();

    private static IEnumerable<StatisticBar> Group(
        IReadOnlyList<ReportExportRow> rows,
        Func<ReportExportRow, string> keySelector) =>
        rows.GroupBy(keySelector).Select(group => new StatisticBar(
            group.Key,
            group.Count(),
            Math.Round(group.Average(row => row.Priority), 1)));
}

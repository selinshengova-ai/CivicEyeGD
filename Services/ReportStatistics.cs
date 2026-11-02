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
// Обединяването на числата в един клас е умишлено: таблото, работната
// книга, CSV и документът питат за едни и същи числа, и ако всяко си
// ги смята само, ще се различават при първата промяна на правилника.
public static class ReportStatistics
{
    // Пет места се четат на една страница; повече биха изисквали второ
    // подзаглавие и биха изместили останалите секции на следваща страница.
    public const int TopLocationCount = 5;

    public const string NoCategoryLabel = "Без категория";
    public const string NoLocationLabel = "Без посочен адрес";

    /// <summary>Брой сигнали по категория, с средния им приоритет.</summary>
    public static IReadOnlyList<StatisticBar> ByCategory(IReadOnlyList<ReportExportRow> rows) =>
        Group(rows, row => CategoryOf(row.Category))
            // Първо по брой, после по среден приоритет: служителят чете
            // отгоре надолу, затова най-натовареното е в началото, а
            // при равен брой по-спешната категория не остава зад тази
            // с много дребни проблеми.
            .OrderByDescending(bar => bar.Count)
            .ThenByDescending(bar => bar.Score)
            .ToList();

    /// <summary>Категорията, по която броим: празният текст не е категория.</summary>
    // Премахването на интервалите е задължително, защото категорията е
    // свободен текст в записи от стари версии: един и същ текст с
    // интервал в края иначе би дал две ленти за един и същ проблем.
    public static string CategoryOf(string? category) =>
        string.IsNullOrWhiteSpace(category) ? NoCategoryLabel : category.Trim();

    /// <summary>Най-натоварените места, подредени по брой сигнали.</summary>
    // Броят на местата е параметър със стойност по подразбиране:
    // отчетът за една страница иска пет, а един тест или друга секция
    // може да поиска повече, без това да променя начина на броене.
    public static IReadOnlyList<StatisticBar> TopLocations(
        IReadOnlyList<ReportExportRow> rows,
        int take = TopLocationCount) =>
        Group(rows, row => LocationOf(row.Address))
            .OrderByDescending(bar => bar.Count)
            .ThenByDescending(bar => bar.Score)
            .Take(take)
            .ToList();

    /// <summary>
    /// Мястото на сигнала се вади от свободния текст на адреса:
    /// „бул. „Гоце Делчев“ 14“ става „Гоце Делчев“. Номерът се маха,
    /// защото без него сигналите от една улица се събират на едно място,
    /// а точно това търси отчетът.
    /// </summary>
    public static string LocationOf(string? address)
    {
        var text = (address ?? string.Empty).Trim();
        if (text.Length == 0) return NoLocationLabel;

        // Кавичките се пробват първи и с трите двойки знаци, защото
        // адресите идват от свободен текст и са писвани както с
        // българската, така и с английската клавиатура: името на
        // булеварда е вътре в кавичките, а всичко около него е пояснение.
        foreach (var (open, close) in new[] { ('„', '“'), ('"', '"'), ('«', '»') })
        {
            var from = text.IndexOf(open);
            var to = from < 0 ? -1 : text.IndexOf(close, from + 1);

            if (to > from) return Cut(text[(from + 1)..to]);
        }

        return Cut(StripPrefix(text));
    }

    // Списъкът е с представките, с които се подписват адресите в
    // града: те казват какъв е обектът, а не как се казва, затова не
    // са име на място. Сравнението без разлика в главните и малките
    // букви хваща и изписването с главни букви.
    private static readonly string[] Prefixes = { "бул.", "ул.", "кв.", "с.", "гр." };

    private static string StripPrefix(string text)
    {
        foreach (var prefix in Prefixes)
        {
            // Маха се само първата представка и само от началото:
            // тя е служебната дума на адреса, а остатъкът е името, по
            // което сигналите се събират в една лента.
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
    // на мястото: без тях сигналите от една улица се събират в една лента.
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
    // Матрицата се строи изцяло: на всяка двойка има клетка, дори с
    // нула. Празната зона показва къде нищо не чака, а с пропуснати
    // двойки документът би приличал на непълен.
    // Броенето минава през списъка за всяка клетка поотделно; при
    // данните от един град това е достатъчно бързо и се чете по-лесно.
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
    // Имената минават през CategoryOf преди Distinct, за да се слеят
    // записите с различен интервал в текста. Редът по брой е същият
    // като на диаграмите: матрицата и лентите трябва да изброяват
    // категориите в една и съща последователност.
    public static IReadOnlyList<string> HeatCategories(IReadOnlyList<ReportExportRow> rows) =>
        rows.Select(row => CategoryOf(row.Category))
            .Distinct()
            .OrderByDescending(name => rows.Count(row => CategoryOf(row.Category) == name))
            .ToList();

    /// <summary>Сигналите, които все още чакат решение.</summary>
    // Приключените са Решен и Отхвърлен — те не изискват действие,
    // но остават в числата на другите функции: статистиката описва
    // целия период, а този списък е само работната опашка.
    public static IReadOnlyList<ReportExportRow> Open(IReadOnlyList<ReportExportRow> rows) =>
        rows.Where(row => !ReportStatuses.IsClosed(row.Status)).ToList();

    private static IEnumerable<StatisticBar> Group(
        IReadOnlyList<ReportExportRow> rows,
        Func<ReportExportRow, string> keySelector) =>
        // Резултатът е средният приоритет, а не сборът: категориите са
        // с различен брой сигнали, и само средното число се сравнява
        // честно между малка и голяма група.
        // Една група никога не е празна, затова усредняването е безопасно.
        rows.GroupBy(keySelector).Select(group => new StatisticBar(
            group.Key,
            group.Count(),
            Math.Round(group.Average(row => row.Priority), 1)));
}

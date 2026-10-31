using System.Text;
using CivicEyeGD.Data;
using CivicEyeGD.Pages.Admin;
using CivicEyeGD.Services;
using Microsoft.AspNetCore.Mvc;

namespace CivicEyeGD.Tests;

/// <summary>
/// PDF отчетът е най-важният експортен формат: той е документът, който
/// служителят оставя в папката си. Тестове пазят двете му половини —
/// числата (агрегатите) и самото рисуване на документа.
/// </summary>
public sealed class ReportPdfTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Theory]
    [InlineData("бул. „Гоце Делчев“ 14", "Гоце Делчев")]
    [InlineData("ул. Пирин 3", "Пирин")]
    [InlineData("кв. Отдоян", "Отдоян")]
    [InlineData("Възрожденска 5", "Възрожденска")]
    [InlineData("бул. Сандански", "Сандански")]
    [InlineData("бул. Гоце Делчев, пред спирката", "Гоце Делчев")]
    [InlineData("бул. „България“ до номер 12, Гоце Делчев", "България")]
    [InlineData("", ReportStatistics.NoLocationLabel)]
    [InlineData(null, ReportStatistics.NoLocationLabel)]
    public void LocationGroupsAddressesByStreetNotByNumber(string? address, string expected)
        => Assert.Equal(expected, ReportStatistics.LocationOf(address));

    [Fact]
    public void EmptyCategoryBecomesItsOwnGroup()
    {
        var rows = new[]
        {
            Row(1, "Дупка", ReportCategories.RoadSurface, ReportStatuses.New, 50),
            Row(2, "Мрачно", ReportCategories.StreetLighting, ReportStatuses.New, 30),
            Row(3, "Без категория", string.Empty, ReportStatuses.New, 20)
        };

        var bars = ReportStatistics.ByCategory(rows);

        Assert.Equal(3, bars.Count);
        Assert.Equal(ReportStatistics.NoCategoryLabel, bars[^1].Label);
        Assert.Equal(3, bars.Sum(bar => bar.Count));
        Assert.All(bars, bar => Assert.Equal(1, bar.Count));
    }

    [Fact]
    public void CategoriesSortByCountAndAveragePriority()
    {
        var rows = new[]
        {
            Row(1, "А", ReportCategories.RoadSurface, ReportStatuses.New, 90),
            Row(2, "Б", ReportCategories.RoadSurface, ReportStatuses.New, 70),
            Row(3, "В", ReportCategories.Sidewalks, ReportStatuses.New, 10)
        };

        var categories = ReportStatistics.ByCategory(rows);

        Assert.Equal(ReportCategories.RoadSurface, categories[0].Label);
        Assert.Equal(2, categories[0].Count);
        Assert.Equal(80, categories[0].Score, 1);
    }

    [Fact]
    public void TopLocationsMergeStreetNumbersAndAreCapped()
    {
        string[] streets =
        [
            "бул. „Гоце Делчев“ 14",
            "бул. „Гоце Делчев“ 2",
            "бул. „Гоце Делчев“ 33",
            "ул. „Пирин“ 5",
            "ул. „Пирин“ 7",
            "ул. „Възраждане“ 1",
            "ул. „Сандански“ 9",
            "ул. „Манастирска“ 4",
            "ул. „Черно море“ 11",
            "ул. „Лозен“ 6"
        ];

        var rows = streets
            .Select((address, index) =>
                Row(index + 1, $"Сигнал {index + 1}", ReportCategories.RoadSurface,
                    ReportStatuses.New, 40) with { Address = address })
            .ToList();

        // Пет локации се побират на страницата; шестата би скрила
        // списъка със сигналите, който е по-важен от диаграмата.
        var locations = ReportStatistics.TopLocations(rows);

        Assert.Equal(
            ["Гоце Делчев", "Пирин", "Възраждане", "Сандански", "Манастирска"],
            locations.Select(bar => bar.Label).ToArray());
        Assert.Equal(3, locations[0].Count);
    }

    [Fact]
    public void HeatMatrixHasOneCellPerCategoryAndStatus()
    {
        var rows = new[]
        {
            Row(1, "А", ReportCategories.RoadSurface, ReportStatuses.New, 40),
            Row(2, "Б", ReportCategories.RoadSurface, ReportStatuses.Resolved, 40),
            Row(3, "В", ReportCategories.RoadSurface, ReportStatuses.Resolved, 40),
            Row(4, "Г", ReportCategories.Sidewalks, ReportStatuses.InProgress, 40)
        };

        var heat = ReportStatistics.HeatMatrix(rows);

        Assert.Equal(2 * ReportStatuses.All.Count, heat.Count);

        var resolved = heat.Single(cell =>
            cell.Category == ReportCategories.RoadSurface && cell.Status == ReportStatuses.Resolved);
        Assert.Equal(2, resolved.Count);

        var empty = heat.Single(cell =>
            cell.Category == ReportCategories.Sidewalks && cell.Status == ReportStatuses.Rejected);
        Assert.Equal(0, empty.Count);

        // Сумата на клетките е броят на редовете: матрицата не измисля
        // сигнали и не губи нищо.
        Assert.Equal(rows.Length, heat.Sum(cell => cell.Count));
    }

    [Fact]
    public void OpenListKeepsOnlyReportsAwaitingDecision()
    {
        var rows = new[]
        {
            Row(1, "Чака", ReportCategories.RoadSurface, ReportStatuses.New, 40),
            Row(2, "Работи се", ReportCategories.RoadSurface, ReportStatuses.InProgress, 40),
            Row(3, "Готово", ReportCategories.RoadSurface, ReportStatuses.Resolved, 40),
            Row(4, "Отхвърлен", ReportCategories.RoadSurface, ReportStatuses.Rejected, 40)
        };

        var open = ReportStatistics.Open(rows);

        Assert.Equal(2, open.Count);
        Assert.Equal(["Чака", "Работи се"], open.Select(row => row.Title).ToArray());
    }

    [Fact]
    public async Task AdminDownloadsPdfReportWithBulgarianText()
    {
        var author = await _host.CreateUserAsync("pdf.author");
        await AddReportAsync(author, "Пъпка до автобусната спирка", ReportCategories.RoadSurface,
            ReportStatuses.New, 5, "бул. „Гоце Делчев“ 14");
        await AddReportAsync(author, "Счупена пейка", ReportCategories.Sidewalks,
            ReportStatuses.InProgress, 2, "ул. „Възраждане“ 3");
        await AddReportAsync(author, "Ремонтиран пад", ReportCategories.RoadSurface,
            ReportStatuses.Resolved, 4, "бул. „Гоце Делчев“ 14");

        var model = _host.CreatePageModel<IndexModel>();
        var file = Assert.IsType<FileContentResult>(await model.OnGetExportPdfAsync(null));

        Assert.Equal("application/pdf", file.ContentType);
        Assert.StartsWith("civiceye-gd-otchet-", file.FileDownloadName);
        Assert.EndsWith(".pdf", file.FileDownloadName);

        var text = Encoding.Latin1.GetString(file.FileContents);
        Assert.StartsWith("%PDF-1.", text);
        Assert.Contains("%%EOF", text);
        Assert.True(file.FileContents.Length > 5_000, "Документът е твърде малък да съдържа съдържание.");

        // Документът е истински: има поне една страница със съдържание.
        Assert.NotEmpty(System.Text.RegularExpressions.Regex.Matches(text, "/Type\\s*/Page[^s]"));
    }

    [Fact]
    public async Task PdfReportIsGeneratedForAnEmptyFilter()
    {
        await _host.CreateUserAsync("pdf.nobody");

        var model = _host.CreatePageModel<IndexModel>();
        var file = Assert.IsType<FileContentResult>(
            await model.OnGetExportPdfAsync(ReportStatuses.Resolved));

        Assert.StartsWith("%PDF-1.", Encoding.Latin1.GetString(file.FileContents));
    }

    [Fact]
    public void BulgarianGlyphsArePresentInTheBundledFont()
    {
        // Тестът хвърля, ако липсва глаиф: без него отчетът би показал
        // квадратчета вместо българските букви, а това се вижда чак
        // когато документът се отвори.
        var previous = QuestPDF.Settings.ThrowOnMissingTextGlyphs;
        QuestPDF.Settings.ThrowOnMissingTextGlyphs = true;

        try
        {
            var pdf = new ReportPdfService().Build(
                [Row(1, "Кръстовище с пешеходна пътека и улично осветление",
                    ReportCategories.PedestrianPaths, ReportStatuses.New, 88)],
                "Активни сигнали",
                new DateTime(2026, 10, 31, 8, 0, 0, DateTimeKind.Utc));

            Assert.StartsWith("%PDF-1.", Encoding.Latin1.GetString(pdf));
        }
        finally
        {
            QuestPDF.Settings.ThrowOnMissingTextGlyphs = previous;
        }
    }

    private async Task AddReportAsync(
        string userId,
        string title,
        string category,
        string status,
        int rating,
        string address)
    {
        var report = await _host.AddReportAsync(userId, r =>
        {
            r.Title = title;
            r.Category = category;
            r.Status = status;
            r.Address = address;
        });

        var rater = await _host.CreateUserAsync($"rater-{report.Id}");
        _host.Context.SeverityRatings.Add(new SeverityRating
        {
            ReportId = report.Id,
            UserId = rater,
            Rating = rating
        });

        await _host.Context.SaveChangesAsync();
    }

    private static ReportExportRow Row(
        int id,
        string title,
        string category,
        string status,
        double priority) =>
        new(id, title, "Описание", category, status, "бул. „Гоце Делчев“ 14",
            new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc), 3.5, 2, priority, "Среден приоритет");
}

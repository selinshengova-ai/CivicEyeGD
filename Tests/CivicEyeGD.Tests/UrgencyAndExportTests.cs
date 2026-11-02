using System.Text;
using System.Text.Json;
using CivicEyeGD.Data;
using CivicEyeGD.Pages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CivicEyeGD.Tests;

/// <summary>
/// Правилото за „спешен“ сигнал и експортът към Excel и CSV. И двете са
/// нова логика, затова тестовете проверяват дали маркерите, броячите и
/// файловете говорят едно и също число.
/// </summary>
public sealed class UrgencyAndExportTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Theory]
    [InlineData(5.0, 0, 0, ReportStatuses.New, true)]
    [InlineData(5.0, 0, 0, ReportStatuses.InProgress, true)]
    [InlineData(4.0, 10, 0, ReportStatuses.New, true)]
    [InlineData(4.0, 0, 5, ReportStatuses.New, false)]
    [InlineData(4.0, 0, 0, ReportStatuses.New, false)]
    [InlineData(3.9, 0, 0, ReportStatuses.New, false)]
    [InlineData(0.0, 0, 0, ReportStatuses.New, false)]
    [InlineData(5.0, 0, 0, ReportStatuses.Resolved, false)]
    [InlineData(5.0, 0, 0, ReportStatuses.Rejected, false)]
    public void IsUrgentUsesThePriorityScoreNotTheRawSeverity(
        double severity,
        int confirmations,
        int adjustment,
        string status,
        bool expected)
    {
        // В това е целият смисъл на теста: прагът е 70 от формулата,
        // а не 4 от скалата, и приключен сигнал не е спешен колкото и да
        // е висок. Картата, списъкът и таблото питат точно този метод.
        Assert.Equal(
            expected,
            _host.Priority.IsUrgent(severity, confirmations, adjustment, status));
    }

    [Fact]
    public async Task MapListAndDashboardNameTheSameUrgentReports()
    {
        var author = await _host.CreateUserAsync("urgent.author");

        var top = await AddUrgentReportAsync(author, 5, 0, ReportStatuses.New, "Затънал отвод");
        var mid = await AddUrgentReportAsync(author, 4, 0, ReportStatuses.New, "Счупена плоча");
        var crowd = await AddUrgentReportAsync(author, 4, 10, ReportStatuses.New, "Тъмна алея");
        var done = await AddUrgentReportAsync(author, 5, 0, ReportStatuses.Resolved, "Ремонтиран мост");

        var map = _host.CreatePageModel<MapModel>();
        await map.OnGetAsync(showClosed: true);

        var list = _host.CreatePageModel<ReportsModel>();
        await list.OnGetAsync(null, null, null, null);

        var dashboard = _host.CreatePageModel<CivicEyeGD.Pages.Admin.IndexModel>();
        await dashboard.OnGetAsync(null);

        // Трите страници питат различни неща — JSON, ред от списък и
        // брояч в таблото — и отговарят с едни и същи две заглавия.
        Assert.Equal(2, map.UrgentCount);
        Assert.Equal(2, list.Reports.Count(list.IsUrgent));
        Assert.Equal(2, dashboard.HighPriorityCount);

        var pulseTitles = PulseTitles(map.ReportsJson);
        Assert.Equal(
            new HashSet<string> { "Затънал отвод", "Тъмна алея" },
            pulseTitles);

        Assert.True(_host.Priority.IsUrgent(top));
        Assert.False(_host.Priority.IsUrgent(mid));
        Assert.True(_host.Priority.IsUrgent(crowd));
        Assert.False(_host.Priority.IsUrgent(done));

        Assert.Equal(list.IsUrgent(mid), dashboard.IsHighPriority(mid));
        Assert.Equal(list.IsUrgent(crowd), dashboard.IsHighPriority(crowd));
    }

    // Пулсацията се чете от JSON-а, защото точно той решава кой маркер
    // трепти: проверката е за изпратеното, не за поредното копие на
    // правилото в C#.
    private static HashSet<string> PulseTitles(string reportsJson)
    {
        using var document = JsonDocument.Parse(reportsJson);

        var urgent = new HashSet<string>();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.GetProperty("isUrgent").GetBoolean())
            {
                urgent.Add(element.GetProperty("title").GetString()!);
            }
        }

        return urgent;
    }

    private async Task<Report> AddUrgentReportAsync(
        string userId,
        int rating,
        int confirmations,
        string status,
        string title)
    {
        var report = await AddReportWithSeverityAsync(userId, rating, status, title);

        for (var index = 0; index < confirmations; index++)
        {
            // Всеки глас идва от отделен гражданин: UniqueIndex-ът би
            // отхвърлил второ потвърждение от същия, а формулата брои
            // именно хора, не щраквания.
            var voter = await _host.CreateUserAsync($"confirm-{report.Id}-{index}");
            _host.Context.Confirmations.Add(new Confirmation
            {
                ReportId = report.Id,
                UserId = voter
            });
        }

        await _host.Context.SaveChangesAsync();

        return report;
    }

    [Fact]
    public async Task MapModelCountsUrgentReportsAndSendsThemAsJson()
    {
        var author = await _host.CreateUserAsync("map.author");

        await AddReportWithSeverityAsync(author, 5, ReportStatuses.New, "Пъпка пред училището");
        await AddReportWithSeverityAsync(author, 2, ReportStatuses.New, "Счупена пейка");
        await AddReportWithSeverityAsync(author, 5, ReportStatuses.Resolved, "Ремонтиран пад");

        var model = _host.CreatePageModel<MapModel>();
        await model.OnGetAsync();

        // Приключилият сигнал не е задача, която чака, затова го няма в
        // изгледа по подразбиране; числото му остава в статистиката.
        Assert.Equal(2, model.ReportsCount);
        Assert.Equal(1, model.ClosedCount);
        Assert.Equal(1, model.UrgentCount);
        Assert.DoesNotContain("\"isClosed\":true", model.ReportsJson);

        // Ключовете са camelCase, защото картата ги чете точно така.
        Assert.Contains("\"isUrgent\":true", model.ReportsJson);
        Assert.Contains("\"isUrgent\":false", model.ReportsJson);

        await model.OnGetAsync(showClosed: true);

        Assert.Equal(3, model.ReportsCount);
        Assert.Contains("\"isClosed\":true", model.ReportsJson);
    }

    [Fact]
    public async Task MapStatisticsCountEveryStatusIncludingHiddenClosed()
    {
        var author = await _host.CreateUserAsync("map.stats");

        await _host.AddReportAsync(author);
        await _host.AddReportAsync(author, r => r.Status = ReportStatuses.Resolved);
        await _host.AddReportAsync(author, r => r.Status = ReportStatuses.Rejected);

        var model = _host.CreatePageModel<MapModel>();
        await model.OnGetAsync();

        Assert.Equal(3, model.TotalReportCount);
        Assert.Equal(2, model.ClosedCount);
        Assert.Equal(3, model.StatusStatistics.Sum(row => row.Count));
        Assert.Equal(1, model.StatusStatistics.Single(row => row.Status == ReportStatuses.New).Count);
        Assert.Equal(1, model.StatusStatistics.Single(row => row.Status == ReportStatuses.Rejected).Count);
    }

    [Fact]
    public async Task MapModelGivesZeroSeverityToReportsWithoutRatings()
    {
        var author = await _host.CreateUserAsync("map.unrated");
        await _host.AddReportAsync(author);

        var model = _host.CreatePageModel<MapModel>();
        await model.OnGetAsync();

        Assert.Equal(0, model.UrgentCount);
        Assert.Contains("\"severity\":0", model.ReportsJson);
    }

    [Fact]
    public async Task AdminExportsCsvWithBomAndReadableHeader()
    {
        var author = await _host.CreateUserAsync("export.author");
        await AddReportWithSeverityAsync(author, 5, ReportStatuses.New, "Пъпка до автобусната спирка");

        var model = _host.CreatePageModel<CivicEyeGD.Pages.Admin.IndexModel>();
        var result = await model.OnGetExportAsync(null);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv; charset=utf-8", file.ContentType);
        Assert.StartsWith("civiceye-gd-signali-", file.FileDownloadName);
        Assert.EndsWith(".csv", file.FileDownloadName);

        var text = Encoding.UTF8.GetString(file.FileContents);

        // BOM-ът е причината Excel да отвори българския текст правилно.
        Assert.StartsWith("\ufeff", text);
        Assert.StartsWith("\"№\",\"Заглавие\",\"Описание\",\"Категория\",\"Статус\",", text);
        Assert.Contains("Пъпка до автобусната спирка", text);
        Assert.Contains("Висок приоритет", text);
    }

    [Fact]
    public async Task AdminExportsWorkbookThatExcelReadsAsColumns()
    {
        var author = await _host.CreateUserAsync("xlsx.author");
        await AddReportWithSeverityAsync(author, 5, ReportStatuses.New, "Пъпка до спирката");

        var model = _host.CreatePageModel<CivicEyeGD.Pages.Admin.IndexModel>();
        var file = Assert.IsType<FileContentResult>(await model.OnGetExportXlsxAsync(null));

        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            file.ContentType);
        Assert.StartsWith("civiceye-gd-signali-", file.FileDownloadName);
        Assert.EndsWith(".xlsx", file.FileDownloadName);

        using var stream = new MemoryStream(file.FileContents);
        using var workbook = new ClosedXML.Excel.XLWorkbook(stream);

        var sheet = workbook.Worksheet("Сигнали");

        // Заглавният ред и редът със сигнала са в отделни колони:
        // точно това CSV-ът не гарантира при друга регионална настройка.
        Assert.Equal("№", sheet.Cell(1, 1).GetString());
        Assert.Equal("Етикет", sheet.Cell(1, 11).GetString());
        Assert.Equal("Пъпка до спирката", sheet.Cell(2, 2).GetString());
        Assert.Equal("Висок приоритет", sheet.Cell(2, 11).GetString());
        Assert.Equal(5.0, sheet.Cell(2, 8).GetDouble(), 1);
        Assert.Equal(71.4, sheet.Cell(2, 10).GetDouble(), 1);
        Assert.True(sheet.Cell(1, 1).Style.Font.Bold);
    }

    [Fact]
    public async Task AdminWorkbookKeepsFormulaLookingTitlesAsText()
    {
        var author = await _host.CreateUserAsync("xlsx.inject");

        await _host.AddReportAsync(author, report =>
        {
            report.Title = "=1+1";
        });

        var model = _host.CreatePageModel<CivicEyeGD.Pages.Admin.IndexModel>();
        var file = Assert.IsType<FileContentResult>(await model.OnGetExportXlsxAsync(null));

        using var stream = new MemoryStream(file.FileContents);
        using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
        var sheet = workbook.Worksheet("Сигнали");

        // Клетката остава текст: Excel я изобразява, но не я изпълнява.
        Assert.Equal(ClosedXML.Excel.XLDataType.Text, sheet.Cell(2, 2).DataType);
        Assert.Equal("=1+1", sheet.Cell(2, 2).GetString());
    }

    [Fact]
    public async Task AdminCsvQuotesAndNeutralisesFormulas()
    {
        var author = await _host.CreateUserAsync("inject.author");

        await _host.AddReportAsync(author, report =>
        {
            report.Title = "=1+1";
            report.Description = "Кажи \"здравей\" на кмета";
            report.Address = "+359 88 000 0000";
        });

        var model = _host.CreatePageModel<CivicEyeGD.Pages.Admin.IndexModel>();
        var result = Assert.IsType<FileContentResult>(await model.OnGetExportAsync(null));

        var text = Encoding.UTF8.GetString(result.FileContents);

        Assert.Contains("\"'=1+1\"", text);
        Assert.Contains("\"'+359 88 000 0000\"", text);
        Assert.Contains("\"\"здравей\"\"", text);
    }

    [Fact]
    public async Task AdminExportFollowsTheSelectedStatusFilter()
    {
        var author = await _host.CreateUserAsync("filter.author");
        await AddReportWithSeverityAsync(author, 3, ReportStatuses.New, "Активен сигнал");
        await AddReportWithSeverityAsync(author, 3, ReportStatuses.Resolved, "Приключен сигнал");

        var model = _host.CreatePageModel<CivicEyeGD.Pages.Admin.IndexModel>();
        var result = Assert.IsType<FileContentResult>(
            await model.OnGetExportAsync(ReportStatuses.Resolved));

        var lines = Encoding.UTF8.GetString(result.FileContents)
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.Contains("Приключен сигнал", lines[1]);
        Assert.DoesNotContain("Активен сигнал", lines[1]);
    }

    [Fact]
    public async Task MapIgnoresTheAuthorsOwnSeverityRating()
    {
        var author = await _host.CreateUserAsync("map.selfrating");

        var report = await _host.AddReportAsync(author, r =>
        {
            r.Title = "Само моята оценка";
            r.Status = ReportStatuses.New;
        });

        _host.Context.SeverityRatings.Add(new SeverityRating
        {
            ReportId = report.Id,
            UserId = author,
            Rating = 5
        });

        await _host.Context.SaveChangesAsync();

        var model = _host.CreatePageModel<MapModel>();
        await model.OnGetAsync();

        // Същото правило като в Details и в таблото: без независима
        // оценка сигналът не става спешен, дори авторът да е дал 5.
        Assert.Equal(0, model.UrgentCount);
        Assert.DoesNotContain("\"isUrgent\":true", model.ReportsJson);
    }

    [Fact]
    public async Task AdminMarksOnlyOpenReportsAsHighPriority()
    {
        var author = await _host.CreateUserAsync("priority.author");

        var open = await AddReportWithSeverityAsync(author, 5, ReportStatuses.New, "Отворен");
        var closed = await AddReportWithSeverityAsync(author, 5, ReportStatuses.Resolved, "Затворен");

        var model = _host.CreatePageModel<CivicEyeGD.Pages.Admin.IndexModel>();
        await model.OnGetAsync(null);

        Assert.Equal(71.4, model.PriorityOf(open), 1);
        Assert.True(model.IsHighPriority(open));
        Assert.False(model.IsHighPriority(closed));
        Assert.Equal(1, model.HighPriorityCount);
        Assert.Equal("Висок приоритет", model.PriorityLabelOf(open));
    }

    private async Task<Report> AddReportWithSeverityAsync(
        string userId,
        int rating,
        string status,
        string title)
    {
        var report = await _host.AddReportAsync(userId, r =>
        {
            r.Title = title;
            r.Status = status;
        });

        // Един потребител може да оцени даден сигнал веднъж, затова
        // за средна стойност, равна на оценката, стига един запис.
        var rater = await _host.CreateUserAsync($"rater-{report.Id}");
        _host.Context.SeverityRatings.Add(new SeverityRating
        {
            ReportId = report.Id,
            UserId = rater,
            Rating = rating
        });

        await _host.Context.SaveChangesAsync();

        return report;
    }
}

using System.Text;
using CivicEyeGD.Data;
using CivicEyeGD.Services;
using Microsoft.AspNetCore.Mvc;
using AdminDashboard = CivicEyeGD.Pages.Admin.IndexModel;

namespace CivicEyeGD.Tests;

/// <summary>
/// Времето на общината е единственото число в приложението, което се
/// мери в дни, а не в точки, и същото число се чете на таблото, в
/// работната книга, в CSV и в PDF. Тук се пази и двете: че се брои от
/// първата стъпка на дневника, и че навсякъде другото казва същото.
/// </summary>
public sealed class ReportResponseTests
{
    // Един ред със сигнал и неговия дневник, без база данни: сметката
    // за дните не зависи от нищо друго освен от двата момента.
    private static Report Signal(DateTime createdUtc, string status, params DateTime[] actionsUtc)
    {
        var report = new Report
        {
            Title = "Пъпка до спирката",
            Status = status,
            CreatedAt = createdUtc
        };

        foreach (var moment in actionsUtc)
        {
            report.StatusHistories.Add(new StatusHistory
            {
                OldStatus = ReportStatuses.New,
                NewStatus = ReportStatuses.InProgress,
                ChangedAt = moment
            });
        }

        return report;
    }

    [Fact]
    public void ReactionCountsTheFirstDiaryStepNotTheLastOne()
    {
        var created = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

        var report = Signal(
            created,
            ReportStatuses.Resolved,
            new DateTime(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 9, 18, 0, 0, DateTimeKind.Utc));

        // Втората стъпка е по-късна: ако тя дава числото, документът би
        // криел колко дни човекът е чакал някой да го забележи.
        Assert.Equal(6, ReportResponse.ResponseDays(report));
    }

    [Fact]
    public void SignalWithoutDiary_HasNoReactionNumberAtAll()
    {
        var report = Signal(new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc), ReportStatuses.New);

        Assert.Null(ReportResponse.ResponseDays(report));

        // Липсата се изписва с тире, а не с нула: нулата е най-бързата
        // възможна реакция, а тук няма никаква.
        Assert.Equal("—", ReportResponse.Days(ReportResponse.ResponseDays(report)));
    }

    [Fact]
    public void OnlyFullDaysAreCounted()
    {
        var created = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

        var almostTwo = Signal(created, ReportStatuses.InProgress, new DateTime(2026, 10, 3, 7, 59, 0, DateTimeKind.Utc));
        var exactlyTwo = Signal(created, ReportStatuses.InProgress, new DateTime(2026, 10, 3, 8, 0, 0, DateTimeKind.Utc));

        Assert.Equal(1, ReportResponse.ResponseDays(almostTwo));
        Assert.Equal(2, ReportResponse.ResponseDays(exactlyTwo));
    }

    [Fact]
    public void ADiaryStepBeforeTheSignalCannotPoisonTheAverage()
    {
        // Нагласен дневник, какъвто нормалната работа не ражда: намеса
        // три дни преди подаването.
        var broken = Signal(
            new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc),
            ReportStatuses.InProgress,
            new DateTime(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc));

        Assert.Equal(0, ReportResponse.ResponseDays(broken));
        Assert.Equal(0, ReportResponse.AverageResponseDays([broken]));
    }

    [Fact]
    public void AverageIgnoresTheSignalsThatStillWait()
    {
        var october = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

        var reports = new[]
        {
            Signal(october, ReportStatuses.InProgress, october.AddDays(2)),
            Signal(october, ReportStatuses.Resolved, october.AddDays(4)),
            Signal(october, ReportStatuses.New)
        };

        // Третият сигнал не влиза: дните му са изчакване, а не реакция,
        // и смесени на едно място двете биха лъгали и за двете.
        Assert.Equal(3.0, ReportResponse.AverageResponseDays(reports));

        // Единственото, което празният списък може честно да каже.
        Assert.Null(ReportResponse.AverageResponseDays([Signal(october, ReportStatuses.New)]));
    }

    [Fact]
    public void LongestReactionSkipsTheSameSignalsAsTheAverage()
    {
        var october = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

        var reports = new[]
        {
            Signal(october, ReportStatuses.InProgress, october.AddDays(2)),
            Signal(october, ReportStatuses.New),
            Signal(october, ReportStatuses.Resolved, october.AddDays(7))
        };

        // Списъкът с липси минава и тук: без филтъра максимумът би се
        // спънал в първия сигнал, по който никой не е работил.
        Assert.Equal(7, ReportResponse.LongestResponseDays(reports));
        Assert.Null(ReportResponse.LongestResponseDays([Signal(october, ReportStatuses.New)]));
    }

    [Theory]
    [InlineData(0, "0 дни")]
    [InlineData(1, "1 ден")]
    [InlineData(2, "2 дни")]
    [InlineData(21, "21 дни")]
    public void Days_WritesTheWordInTheNumberBulgarianUses(int count, string expected) =>
        Assert.Equal(expected, ReportResponse.Days(count));

    [Fact]
    public void AnAverageIsReadInTheSameWholeDaysAsAReaction()
    {
        Assert.Equal("14 дни", ReportResponse.Days(13.7));
        Assert.Equal("1 ден", ReportResponse.Days(1.4));
        Assert.Equal("—", ReportResponse.Days((double?)null));
    }

    [Fact]
    public void TheSlowMarkIsAMonthAndNothingIsAnnouncedByIt()
    {
        // Прагът съществува само за багрите по екрана и на хартията;
        // никой статус не се променя заради него.
        Assert.Equal(30, ReportResponse.SlowWaitDays);
    }
}

/// <summary>
/// Таблото и експортите четат едни и същи дни. Тестове държат
/// обещанието, че числото на картичката, числото в работната книга и
/// числото в резюмето на PDF-а идват от един и същи дневник.
/// </summary>
public sealed class ResponseDashboardTests : IDisposable
{
    private readonly TestHost _host = new();

    // Един-единствен момент за целия тест: от него тръгват и подаванията,
    // и намесите, за да не остане разлика с миг от часовника.
    private readonly DateTime _now = DateTime.UtcNow;

    [Fact]
    public async Task Dashboard_CountsTheDaysTheDiaryAlreadyHolds()
    {
        var admin = await _host.CreateAdministratorAsync("sluzhitel-sla");
        var author = await _host.CreateUserAsync("avtor-sla");

        await AddAsync(author, "Пъпка до спирката", ReportStatuses.InProgress, _now.AddDays(-20), _now.AddDays(-16));
        await AddAsync(author, "Кърпена плоча", ReportStatuses.Resolved, _now.AddDays(-30), _now.AddDays(-20));
        await AddAsync(author, "Тъмна лампа", ReportStatuses.New, _now.AddDays(-6));

        var page = _host.CreatePageModel<AdminDashboard>(_host.PrincipalFor(admin));
        await page.OnGetAsync(filterStatus: null);

        Assert.Equal(7.0, page.AverageResponseDays);
        Assert.Equal(10, page.LongestResponseDays);
        Assert.Equal(2, page.MeasuredResponseCount);

        // По опашката се брои само това, което наистина чака: двамата
        // поетни сигнали не са вина на службата.
        Assert.Equal(1, page.AwaitingFirstActionCount);
        Assert.Equal(6, page.OldestWaitDays);
    }

    [Fact]
    public async Task EmptyOffice_HasNoAverageToClaim()
    {
        var admin = await _host.CreateAdministratorAsync("sluzhitel-prazno-sla");
        var author = await _host.CreateUserAsync("avtor-prazno-sla");

        await AddAsync(author, "Пъпка до спирката", ReportStatuses.New, _now.AddDays(-3));

        var page = _host.CreatePageModel<AdminDashboard>(_host.PrincipalFor(admin));
        await page.OnGetAsync(filterStatus: null);

        // Няма намеси — няма и средно число: картата остава с тире, а
        // не с нула, която би рекла „никой не чака“.
        Assert.Null(page.AverageResponseDays);
        Assert.Null(page.LongestResponseDays);
        Assert.Equal(0, page.MeasuredResponseCount);
        Assert.Equal(1, page.AwaitingFirstActionCount);
        Assert.Equal(3, page.OldestWaitDays);
    }

    [Fact]
    public async Task CardLine_NamesTheNumberItShows()
    {
        var admin = await _host.CreateAdministratorAsync("sluzhitel-kartichka");
        var author = await _host.CreateUserAsync("avtor-kartichka");

        var answered = await AddAsync(author, "Пъпка до спирката", ReportStatuses.InProgress, _now.AddDays(-20), _now.AddDays(-16));
        var waiting = await AddAsync(author, "Тъмна лампа", ReportStatuses.New, _now.AddDays(-6));
        var closedSilently = await AddAsync(author, "Грешен адрес", ReportStatuses.Rejected, _now.AddDays(-40));

        var page = _host.CreatePageModel<AdminDashboard>(_host.PrincipalFor(admin));
        await page.OnGetAsync(filterStatus: null);

        // Двете имена на едно и също поле се сменят заедно с числото,
        // за да не може изчакването да се прочете като реакция.
        Assert.Equal(("Реакция", "4 дни"), Describe(page, answered.Id));
        Assert.Equal(("Чака", "6 дни"), Describe(page, waiting.Id));

        // Приключен сигнал без дневник не обяснява нищо: той не чака и
        // не е поет, и трето тълкувание няма.
        await page.OnGetAsync(filterStatus: AdminDashboard.ClosedFilter);
        Assert.Null(page.ResponseOf(page.Reports.Single(report => report.Id == closedSilently.Id)));
    }

    [Fact]
    public async Task Workbook_GivesTheDaysToTheFirstAction()
    {
        var admin = await _host.CreateAdministratorAsync("sluzhitel-xlsx-sla");
        var author = await _host.CreateUserAsync("avtor-xlsx-sla");

        var answered = await AddAsync(author, "Пъпка до спирката", ReportStatuses.InProgress, _now.AddDays(-20), _now.AddDays(-16));
        var waiting = await AddAsync(author, "Тъмна лампа", ReportStatuses.New, _now.AddDays(-6));

        var page = _host.CreatePageModel<AdminDashboard>(_host.PrincipalFor(admin));
        var file = Assert.IsType<FileContentResult>(await page.OnGetExportXlsxAsync(null));

        using var stream = new MemoryStream(file.FileContents);
        using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
        var sheet = workbook.Worksheet("Сигнали");

        Assert.Equal("Реакция, дни", sheet.Cell(1, 12).GetString());

        // Редът се търси по номера на сигнала: списъкът идва подреден
        // по приоритет, а не по реда на създаването в теста.
        Assert.Equal(4, sheet.Cell(LineOf(sheet, answered.Id), 12).GetDouble(), 0);

        // Празната клетка е отговорът „няма още намеса“: нула би се
        // броила за дни и би излязла по-бърза от всяка реакция.
        Assert.Equal(ClosedXML.Excel.XLDataType.Blank, sheet.Cell(LineOf(sheet, waiting.Id), 12).DataType);
    }

    [Fact]
    public async Task Csv_CarriesTheSameDaysInTheLastField()
    {
        var admin = await _host.CreateAdministratorAsync("sluzhitel-csv-sla");
        var author = await _host.CreateUserAsync("avtor-csv-sla");

        await AddAsync(author, "Пъпка до спирката", ReportStatuses.InProgress, _now.AddDays(-20), _now.AddDays(-16));

        var page = _host.CreatePageModel<AdminDashboard>(_host.PrincipalFor(admin));
        var file = Assert.IsType<FileContentResult>(await page.OnGetExportAsync(null));

        var lines = Encoding.UTF8.GetString(file.FileContents).TrimStart('\ufeff').Split("\r\n");

        Assert.EndsWith(",\"Реакция, дни\"", lines[0]);

        // Заглавието на колоната носи запетая, затова е в кавички по
        // RFC 4180; всяка стойност минава през същия път, за да не
        // търси четецът границата между две полета на сляпо.
        Assert.EndsWith(",\"4\"", lines[1]);
    }

    [Fact]
    public void Pdf_PrintsTheReactionAndSurvivesADayWithoutOne()
    {
        var rows = new[]
        {
            Row(1, ReportStatuses.InProgress, new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc), 4),
            Row(2, ReportStatuses.New, new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc), null),
            Row(3, ReportStatuses.Resolved, new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc), 10)
        };

        var generated = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);

        var pdf = new ReportPdfService().Build(rows, "Активни сигнали", generated);
        Assert.StartsWith("%PDF-1.", Encoding.Latin1.GetString(pdf));

        // Списък без нито един чакащ ред не трябва да сменя адреса с
        // изключение: документът се пуща и когато опашката е празна.
        var closed = new[]
        {
            Row(4, ReportStatuses.Resolved, new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc), 3)
        };

        Assert.StartsWith(
            "%PDF-1.",
            Encoding.Latin1.GetString(new ReportPdfService().Build(closed, "Приключени сигнали", generated)));
    }

    private async Task<Report> AddAsync(
        string author,
        string title,
        string status,
        DateTime createdUtc,
        DateTime? firstActionUtc = null)
    {
        var report = await _host.AddReportAsync(author, r =>
        {
            r.Title = title;
            r.Status = status;
            r.CreatedAt = createdUtc;
        });

        if (firstActionUtc is { } action)
        {
            _host.Context.StatusHistories.Add(new StatusHistory
            {
                ReportId = report.Id,
                OldStatus = ReportStatuses.New,
                NewStatus = ReportStatuses.InProgress,
                ChangedAt = action
            });

            await _host.Context.SaveChangesAsync();
        }

        return report;
    }

    private static (string Label, string Text)? Describe(AdminDashboard page, int id) =>
        page.ResponseOf(page.Reports.Single(report => report.Id == id)) is { } line
            ? (line.Label, line.Text)
            : null;

    private static int LineOf(ClosedXML.Excel.IXLWorksheet sheet, int reportId)
    {
        for (var line = 2; line <= 20; line++)
        {
            var cell = sheet.Cell(line, 1);

            if (cell.DataType == ClosedXML.Excel.XLDataType.Number && cell.GetDouble() == reportId)
            {
                return line;
            }
        }

        throw new InvalidOperationException($"Сигнал #{reportId} липсва в работната книга.");
    }

    private static ReportExportRow Row(
        int id,
        string status,
        DateTime createdAt,
        int? responseDays) =>
        new(id, "Пъпка до спирката", "Описание", ReportCategories.RoadSurface, status,
            "бул. „Гоце Делчев“ 14", createdAt, 3.5, 2, 45, "Среден приоритет", responseDays);

    public void Dispose() => _host.Dispose();
}

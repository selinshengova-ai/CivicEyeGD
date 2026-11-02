using System.Globalization;
using CivicEyeGD.Data;
using CivicEyeGD.Services;
using AdminDashboard = CivicEyeGD.Pages.Admin.IndexModel;
using DetailsPage = CivicEyeGD.Pages.DetailsModel;
using ReportsList = CivicEyeGD.Pages.ReportsModel;

namespace CivicEyeGD.Tests;

/// <summary>
/// По числата и по реда се чете списъкът: формата на оценката и мястото
/// на сигнала в него. Тук се пази обещанието от документацията — точката
/// остава точка и на лаптопа с българска култура, и на сайта с друга,
/// а скриптовете, които четат същото число, не бива да разбират две
/// различни формати.
/// </summary>
public sealed class NumberTextTests
{
    [Fact]
    public void Score_KeepsTheDecimalDotUnderBulgarianCulture()
    {
        UnderBulgarianCulture(() =>
        {
            Assert.Equal("62.9", NumberText.Score(62.9));
            Assert.Equal("5.0", NumberText.Score(5));

            // Проверката без помощта: същото число, форматирано с
            // културата на машината, дава запетая — и точно тогава двата
            // екрана показват различно.
            Assert.Equal("62,9", 62.9.ToString("0.0", CultureInfo.CurrentCulture));
        });
    }

    [Fact]
    public void Points_SkipsThePlacesThatChangeNothing()
    {
        UnderBulgarianCulture(() =>
        {
            Assert.Equal("40", NumberText.Points(40));
            Assert.Equal("71.5", NumberText.Points(71.5));
            Assert.Equal("0.5", NumberText.Points(0.5));
        });
    }

    [Fact]
    public void Severity_ReservesTwoPlacesForTheDigitThatSeparatesSignals()
    {
        UnderBulgarianCulture(() =>
        {
            Assert.Equal("4.25", NumberText.Severity(4.25));
            Assert.Equal("3.00", NumberText.Severity(3));
        });
    }

    [Fact]
    public void Coordinate_KeepsTheFivePlacesTheMapsUnderstand()
    {
        UnderBulgarianCulture(() =>
        {
            Assert.Equal("41.57130", NumberText.Coordinate(41.5713));
            Assert.Equal("23.73610", NumberText.Coordinate(23.7361));
        });
    }

    [Fact]
    public void Score_RoundsToTheSameNumberThePagesAssert()
    {
        // Пропадане с оценка 5 и без потвърждения: 100 от 140 т.
        Assert.Equal("71.4", NumberText.Score(100.0 / 140.0 * 100.0));
    }

    private static void UnderBulgarianCulture(Action check)
    {
        var original = CultureInfo.CurrentCulture;

        // Българската култура е най-лошият възможен случай: десетичният
        // ѝ разделител е запетая, а точно там числото се показва на
        // служителя.
        Thread.CurrentThread.CurrentCulture = new CultureInfo("bg-BG");

        try
        {
            check();
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }
}

/// <summary>
/// Публичният списък е витрината: той нищо не променя и само подрежда.
/// Тук се проверява обещанието, че приключилият сигнал не заема място
/// пред тези, които все още чакат, и че адрес с измислена подредба не
/// оставя списъка без ред.
/// </summary>
public sealed class ReportsListOrderTests : IDisposable
{
    private readonly TestHost _host = new();

    [Theory]
    [InlineData("priority")]
    [InlineData("newest")]
    [InlineData("oldest")]
    public async Task ClosedSignals_AreLastUnderEverySort(string sort)
    {
        var author = await _host.CreateUserAsync("avtor-red");

        // Трите активни сигнала са с различен приоритет и с различна
        // дата, за да може всяка подредба да даде различен отговор.
        var heavy = await AddAsync(
            author, "Пропадане в асфалта", ReportStatuses.New, severity: 5,
            new DateTime(2026, 10, 19, 8, 0, 0, DateTimeKind.Utc));
        var middle = await AddAsync(
            author, "Счупена плоча на тротоара", ReportStatuses.InProgress, severity: 3,
            new DateTime(2026, 10, 18, 8, 0, 0, DateTimeKind.Utc));
        var light = await AddAsync(
            author, "Погаснала улична лампа", ReportStatuses.New, severity: 2,
            new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Utc));

        // Приключилият е с висока оценка и е най-нов: ако затварянето не
        // го изместваше, щеше да е първи по дата и втори по приоритет.
        var finished = await AddAsync(
            author, "Кърпена пъпка при спирката", ReportStatuses.Resolved, severity: 4,
            new DateTime(2026, 10, 28, 8, 0, 0, DateTimeKind.Utc));

        var page = _host.CreatePageModel<ReportsList>();
        await page.OnGetAsync(search: null, category: null, status: null, sort: sort);

        int[] expected = sort switch
        {
            "newest" => [light.Id, heavy.Id, middle.Id, finished.Id],
            "oldest" => [middle.Id, heavy.Id, light.Id, finished.Id],
            _ => [heavy.Id, middle.Id, light.Id, finished.Id],
        };

        Assert.Equal(expected, page.Reports.Select(report => report.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("priorityy")]
    [InlineData("Priority")]
    public async Task UnknownSortValue_FallsBackToTheDefault(string? sort)
    {
        var author = await _host.CreateUserAsync("avtor-padane");
        await AddAsync(
            author, "Пропадане в асфалта", ReportStatuses.New, severity: 4,
            new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc));

        var page = _host.CreatePageModel<ReportsList>();
        await page.OnGetAsync(search: null, category: null, status: null, sort: sort);

        Assert.Equal(ReportsList.DefaultSort, page.Sort);
        Assert.Equal("По приоритет", page.SortText);
        Assert.Single(page.Reports);
    }

    [Fact]
    public void SortOptions_AreTheThreeOrdersTheCitizenReads()
    {
        Assert.Equal(
            [("priority", "По приоритет"),
             ("newest", "Най-новите първи"),
             ("oldest", "Най-старите първи")],
            ReportsList.SortOptions);
    }

    [Theory]
    [InlineData("priority", "По приоритет")]
    [InlineData("newest", "Най-новите първи")]
    [InlineData("oldest", "Най-старите първи")]
    public async Task SortText_NamesTheOrderActuallyApplied(string sort, string expected)
    {
        var page = _host.CreatePageModel<ReportsList>();
        await page.OnGetAsync(search: null, category: null, status: null, sort: sort);

        Assert.Equal(expected, page.SortText);
    }

    [Fact]
    public async Task EqualScores_KeepAStableOrderById()
    {
        var author = await _host.CreateUserAsync("avtor-rovni");

        var first = await AddAsync(
            author, "Плоча при училището", ReportStatuses.New, severity: 3,
            new DateTime(2026, 10, 26, 8, 0, 0, DateTimeKind.Utc));
        var second = await AddAsync(
            author, "Плоча при читалището", ReportStatuses.New, severity: 3,
            new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc));

        var page = _host.CreatePageModel<ReportsList>();
        await page.OnGetAsync(search: null, category: null, status: null, sort: "priority");

        // Равни по приоритет: без третия ключ редът щеше да се мени при
        // всяко презареждане и служителят не би могъл да разчита на
        // „първия“.
        Assert.Equal([first.Id, second.Id], page.Reports.Select(report => report.Id));
    }

    [Fact]
    public async Task UrgentChip_IsRedOnlyForAnActiveSeriousSignal()
    {
        var author = await _host.CreateUserAsync("avtor-chip");

        var open = await AddAsync(
            author, "Пропадане в асфалта", ReportStatuses.New, severity: 5,
            new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc));
        var closed = await AddAsync(
            author, "Кърпена пъпка при спирката", ReportStatuses.Resolved, severity: 5,
            new DateTime(2026, 10, 21, 8, 0, 0, DateTimeKind.Utc));
        var mild = await AddAsync(
            author, "Погаснала улична лампа", ReportStatuses.New, severity: 1,
            new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Utc));

        var page = _host.CreatePageModel<ReportsList>();
        await page.OnGetAsync(search: null, category: null, status: null, sort: null);

        Assert.True(page.IsUrgent(Open(page, open.Id)));
        Assert.False(page.IsUrgent(Open(page, closed.Id)));
        Assert.False(page.IsUrgent(Open(page, mild.Id)));
    }

    [Fact]
    public async Task TheAuthorsOwnVoice_DoesNotLiftHisSignalInTheList()
    {
        var author = await _host.CreateUserAsync("avtor-samoaz");

        var selfRated = await _host.AddReportAsync(author, report =>
        {
            report.Title = "Моят собствен висок приоритет";
            report.CreatedAt = new DateTime(2026, 10, 27, 8, 0, 0, DateTimeKind.Utc);
        });

        // И двата гласа са на подателя: собственият му отговор не се брои.
        _host.Context.SeverityRatings.Add(new SeverityRating
        {
            ReportId = selfRated.Id,
            UserId = author,
            Rating = 5
        });
        _host.Context.Confirmations.Add(new Confirmation
        {
            ReportId = selfRated.Id,
            UserId = author
        });
        await _host.Context.SaveChangesAsync();

        var neighbours = await AddAsync(
            author, "Дърво прегради тротоара", ReportStatuses.New, severity: 4,
            new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc));

        var page = _host.CreatePageModel<ReportsList>();
        await page.OnGetAsync(search: null, category: null, status: null, sort: "priority");

        Assert.Equal([neighbours.Id, selfRated.Id], page.Reports.Select(report => report.Id));
        Assert.Equal(57.1, page.ScoreOf(Open(page, neighbours.Id)), 1);
        Assert.Equal(14.3, page.ScoreOf(Open(page, selfRated.Id)), 1);
    }

    [Fact]
    public void TheCitizenListChangesNothing()
    {
        // Промяната е само в таблото; списъкът пред гражданите е за четене.
        Assert.Empty(typeof(ReportsList)
            .GetMethods()
            .Where(method => method.Name.StartsWith("OnPost", StringComparison.Ordinal))
            .Select(method => method.Name));
    }

    private async Task<Report> AddAsync(
        string userId,
        string title,
        string status,
        int severity,
        DateTime createdAt)
    {
        var report = await _host.AddReportAsync(userId, r =>
        {
            r.Title = title;
            r.Status = status;
            r.CreatedAt = createdAt;
        });

        // Оценката е от друг човек, защото гласът на подателя не влиза в
        // сметката — същото правило проверява и списъкът.
        var rater = await _host.CreateUserAsync($"ocench-{report.Id}");
        _host.Context.SeverityRatings.Add(new SeverityRating
        {
            ReportId = report.Id,
            UserId = rater,
            Rating = severity
        });

        await _host.Context.SaveChangesAsync();

        return report;
    }

    // Проверката чете редовете от списъка, който страницата току-що е
    // подредила: само там колекциите с оценки и потвърждения са заредени.
    private static Report Open(ReportsList page, int id) =>
        page.Reports.Single(report => report.Id == id);

    public void Dispose() => _host.Dispose();
}

/// <summary>
/// Таблото и страницата на сигнала говорят за един и същ приоритет,
/// затова и числата идват от едно и също място. Тук се проверява
/// тежестта на корекцията, с която служителят ориентира сигнала, и че
/// дневникът на сигнала показва най-новите стъпки първи.
/// </summary>
public sealed class AdminPanelNumbersTests : IDisposable
{
    private readonly TestHost _host = new();

    [Fact]
    public void OneAdjustmentPoint_IsTheSameNumberForThePageAndTheScript()
    {
        // Корекцията се въвежда единствено на страницата на сигнала,
        // затова и числото, и неговият текст за скрипта живеят там.
        Assert.Equal(100.0 / 140.0, DetailsPage.AdjustmentWeightPerPoint, 6);

        // Форматът за скрипта е с точка: браузърът не познава българската
        // запетая и с нея числото би се прочело като нула.
        Assert.Equal("0.7143", DetailsPage.AdjustmentWeightForScript);

        // Корекция от пет точки излиза пред служителя като +3.6 от 100.
        Assert.Equal("3.6", NumberText.Score(5 * DetailsPage.AdjustmentWeightPerPoint));
    }

    [Fact]
    public async Task SignalPage_SaysTheSameNumberAsTheDashboardCard()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-panel");
        var author = await _host.CreateUserAsync("avtor-panel");

        await AddRatedAsync(author, "Пропадане в асфалта", ReportStatuses.New, 4);
        await AddRatedAsync(author, "Кърпена пъпка при спирката", ReportStatuses.Resolved, 4);

        var dashboard = _host.CreatePageModel<AdminDashboard>(_host.PrincipalFor(adminId));
        var page = _host.CreatePageModel<DetailsPage>(_host.PrincipalFor(adminId));

        // И двата реда на еднаква сериозност: приключеният носи същото
        // число, което е носел и преди да затвори.
        await dashboard.OnGetAsync(filterStatus: AdminDashboard.ClosedFilter);
        var closedRow = Assert.Single(dashboard.Reports);

        await page.OnGetAsync(closedRow.Id);

        Assert.Equal(57.1, page.PriorityScore, 1);
        Assert.Equal("Среден приоритет", page.PriorityLabel);
        Assert.Equal(dashboard.PriorityOf(closedRow), page.PriorityScore, 6);
        Assert.Equal(dashboard.PriorityLabelOf(closedRow), page.PriorityLabel);
        Assert.True(ReportStatuses.IsClosed(page.Report!.Status));

        await dashboard.OnGetAsync(filterStatus: null);
        var openRow = Assert.Single(dashboard.Reports);

        await page.OnGetAsync(openRow.Id);

        // Едно и също число, една и съща дума: две обяснения на един и
        // същия приоритет с времето се разминават.
        Assert.Equal(dashboard.PriorityOf(openRow), page.PriorityScore, 6);
        Assert.Equal(dashboard.PriorityLabelOf(openRow), page.PriorityLabel);
        Assert.False(ReportStatuses.IsClosed(page.Report!.Status));
    }

    [Fact]
    public async Task Diary_ShowsWithTheThreeNewestStepsFirst()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-dnevnik");
        var author = await _host.CreateUserAsync("avtor-dnevnik");
        var report = await _host.AddReportAsync(author);

        var started = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);

        // Стъпките имат собствени моменти от времето, за да не зависи
        // проверката от точността на часовника в деня, в който се изпълнява.
        for (var step = 1; step <= 5; step++)
        {
            _host.Context.StatusHistories.Add(new StatusHistory
            {
                ReportId = report.Id,
                OldStatus = ReportStatuses.New,
                NewStatus = ReportStatuses.InProgress,
                ChangedByUserId = adminId,
                ChangedAt = started.AddHours(step),
                Comment = $"Стъпка {step}"
            });
        }

        await _host.Context.SaveChangesAsync();

        var page = _host.CreatePageModel<DetailsPage>(_host.PrincipalFor(adminId));
        await page.OnGetAsync(report.Id);

        // Целият дневник се изписва на страницата на сигнала: там е
        // доказателството за направеното и редът се чете отгоре надолу.
        Assert.Equal(
            ["Стъпка 5", "Стъпка 4", "Стъпка 3", "Стъпка 2", "Стъпка 1"],
            page.DiarySteps.Select(step => step.Comment));

        // Последната промяна се чете от самия дневник, а не от второ
        // число: две места за едно и също нещо с времето се разминават.
        Assert.Equal(page.DiarySteps[0].ChangedAt, page.LastChange);
    }

    [Fact]
    public async Task Diary_OnASignalNobodyHasTouched_IsSilent()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-prazen");
        var author = await _host.CreateUserAsync("avtor-prazen");
        var report = await _host.AddReportAsync(author);

        var page = _host.CreatePageModel<DetailsPage>(_host.PrincipalFor(adminId));
        await page.OnGetAsync(report.Id);

        // Празният дневник е честен отговор: страницата не измисля стъпка,
        // която общината не е направила.
        Assert.Empty(page.DiarySteps);
        Assert.Null(page.LastChange);
    }

    private async Task AddRatedAsync(string userId, string title, string status, int severity)
    {
        var report = await _host.AddReportAsync(userId, r =>
        {
            r.Title = title;
            r.Status = status;
        });

        var rater = await _host.CreateUserAsync($"ocench-{report.Id}");
        _host.Context.SeverityRatings.Add(new SeverityRating
        {
            ReportId = report.Id,
            UserId = rater,
            Rating = severity
        });

        await _host.Context.SaveChangesAsync();
    }

    public void Dispose() => _host.Dispose();
}

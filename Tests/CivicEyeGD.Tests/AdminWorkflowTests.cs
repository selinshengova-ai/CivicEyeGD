using CivicEyeGD.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AdminDashboard = CivicEyeGD.Pages.Admin.IndexModel;
using SignalModel = CivicEyeGD.Pages.DetailsModel;

namespace CivicEyeGD.Tests;

/// <summary>
/// Страницата на сигнала е единственото място, където общината променя
/// данните: таблото подбира и подрежда реда, после препраща натам. Тук се
/// пазят обещанията от документацията — всяка смяна на статуса остава в
/// историята с автор и бележка, административната намеса в приоритета е
/// ограничена, записването е с един бутон, а изтриването оставя
/// обяснение на таблото.
/// </summary>
public sealed class AdminWorkflowTests : IDisposable
{
    private readonly TestHost _host = new();

    [Fact]
    public async Task StatusChange_WritesHistoryTheCitizenCanRead()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-1");
        var report = await OpenReportAsync();
        var page = SignalPage(adminId);

        var result = await page.OnPostOfficeAsync(
            report.Id, ReportStatuses.InProgress, "Екипът е изпратен.", adjustment: 0);

        Assert.IsType<PageResult>(result);
        Assert.Contains("записани", page.SuccessMessage);

        var entry = Assert.Single(await _host.Context.StatusHistories.ToListAsync());
        Assert.Equal(ReportStatuses.New, entry.OldStatus);
        Assert.Equal(ReportStatuses.InProgress, entry.NewStatus);
        Assert.Equal("Екипът е изпратен.", entry.Comment);
        Assert.Equal(adminId, entry.ChangedByUserId);
        Assert.Equal(report.Id, entry.ReportId);
    }

    // Един бутон за трите: служителят решава статуса, бележката и
    // намесата в приоритета в един момент, не на два пъти.
    [Fact]
    public async Task SingleSave_ChangesStatusAndAdjustmentTogether()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-2");
        var report = await OpenReportAsync();

        await SignalPage(adminId).OnPostOfficeAsync(
            report.Id, ReportStatuses.InProgress, "Кръпката е в програмата.", adjustment: 7);

        var saved = await ReloadAsync(report.Id);

        Assert.Equal(ReportStatuses.InProgress, saved.Status);
        Assert.Equal(7, saved.AdminPriorityAdjustment);

        // Двете намеси са в един запис: историята е дневник на щракванията,
        // не на думите, и два реда за един бутон биха лъгали.
        var entry = Assert.Single(await _host.Context.StatusHistories.ToListAsync());
        Assert.Equal(ReportStatuses.New, entry.OldStatus);
        Assert.Equal(ReportStatuses.InProgress, entry.NewStatus);
        Assert.Equal(0, entry.OldPriorityAdjustment);
        Assert.Equal(7, entry.NewPriorityAdjustment);
    }

    [Fact]
    public async Task Save_WithoutAnythingDifferent_WritesNoHistoryAndSaysSo()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-3");
        var report = await OpenReportAsync();
        var page = SignalPage(adminId);

        var result = await page.OnPostOfficeAsync(
            report.Id, ReportStatuses.New, null, adjustment: 0);

        Assert.IsType<PageResult>(result);
        Assert.Contains("не се промени", page.SuccessMessage);
        Assert.Empty(await _host.Context.StatusHistories.ToListAsync());
    }

    // Бележката е сама по себе си намеса: служителят обяснява какво е
    // видял, и гражданът трябва да може да прочете обяснението по-късно,
    // дори статусът и приоритетът да не са мърднати.
    [Fact]
    public async Task NoteWithoutAnyChange_IsStillRecordedInTheHistory()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-4");
        var report = await OpenReportAsync();

        await SignalPage(adminId).OnPostOfficeAsync(
            report.Id, ReportStatuses.New, "Проверих мястото.", adjustment: 0);

        var entry = Assert.Single(await _host.Context.StatusHistories.ToListAsync());
        Assert.Equal("Проверих мястото.", entry.Comment);
        // Равните статуси казват на страницата: това не е преход.
        Assert.Equal(entry.OldStatus, entry.NewStatus);
        Assert.Null(entry.OldPriorityAdjustment);
        Assert.Null(entry.NewPriorityAdjustment);
    }

    // Корекцията на приоритета се записва, защото без нея числото на
    // страницата скоква без обяснение.
    [Fact]
    public async Task AdjustingOnlyThePriority_IsRecordedWithoutAStatusChange()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-5");
        var report = await OpenReportAsync();

        await SignalPage(adminId).OnPostOfficeAsync(
            report.Id, ReportStatuses.New, null, adjustment: 5);

        Assert.Equal(5, (await ReloadAsync(report.Id)).AdminPriorityAdjustment);

        var entry = Assert.Single(await _host.Context.StatusHistories.ToListAsync());
        Assert.Equal(ReportStatuses.New, entry.OldStatus);
        Assert.Equal(ReportStatuses.New, entry.NewStatus);
        Assert.Equal(0, entry.OldPriorityAdjustment);
        Assert.Equal(5, entry.NewPriorityAdjustment);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task StatusChange_ToTheSameStatus_LeavesNoExtraEntry(string? comment)
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-6");
        var report = await OpenReportAsync();

        await SignalPage(adminId).OnPostOfficeAsync(
            report.Id, ReportStatuses.New, comment, adjustment: 0);

        Assert.Empty(await _host.Context.StatusHistories.ToListAsync());
    }

    [Fact]
    public async Task StatusChange_WithoutComment_StoresNoEmptyText()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-6");
        var report = await OpenReportAsync();

        await SignalPage(adminId).OnPostOfficeAsync(
            report.Id, ReportStatuses.Resolved, "   ", adjustment: 0);

        var entry = Assert.Single(await _host.Context.StatusHistories.ToListAsync());
        Assert.Null(entry.Comment);
    }

    [Fact]
    public async Task StatusComment_LongerThanTheLimit_IsCut()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-7");
        var report = await OpenReportAsync();

        await SignalPage(adminId).OnPostOfficeAsync(
            report.Id,
            ReportStatuses.Resolved,
            new string('а', SignalModel.MaxCommentLength + 120),
            adjustment: 0);

        var entry = Assert.Single(await _host.Context.StatusHistories.ToListAsync());
        Assert.Equal(SignalModel.MaxCommentLength, entry.Comment!.Length);
    }

    [Theory]
    [InlineData("Ще го оправим")]
    [InlineData("")]
    [InlineData(null)]
    public async Task StatusChange_WithUnknownStatus_IsRejected(string? newStatus)
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-8");
        var report = await OpenReportAsync();

        var badRequest = Assert.IsType<BadRequestObjectResult>(
            await SignalPage(adminId).OnPostOfficeAsync(
                report.Id, newStatus!, null, adjustment: 0));

        Assert.Equal("Непознат статус.", badRequest.Value);
        Assert.Equal(ReportStatuses.New, (await ReloadAsync(report.Id)).Status);
        Assert.Empty(await _host.Context.StatusHistories.ToListAsync());
    }

    [Fact]
    public async Task StatusChange_OnMissingReport_ReturnsNotFound()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-9");

        Assert.IsType<NotFoundResult>(
            await SignalPage(adminId).OnPostOfficeAsync(
                id: 4242, ReportStatuses.Resolved, null, adjustment: 0));
    }

    // Проверката за корекцията е преди каквото и да е записване, затова
    // лош опит не оставя полузаписан статус зад гърба си.
    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    [InlineData(1000)]
    public async Task PriorityAdjustment_OutsideTheScale_IsRejected(int adjustment)
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-10");
        var report = await OpenReportAsync();

        var badRequest = Assert.IsType<BadRequestObjectResult>(
            await SignalPage(adminId).OnPostOfficeAsync(
                report.Id, ReportStatuses.Resolved, null, adjustment));

        Assert.Equal("Корекцията на приоритета е между 0 и 10.", badRequest.Value);
        Assert.Equal(ReportStatuses.New, (await ReloadAsync(report.Id)).Status);
        Assert.Equal(0, (await ReloadAsync(report.Id)).AdminPriorityAdjustment);
        Assert.Empty(await _host.Context.StatusHistories.ToListAsync());
    }

    // Скритият панел не е разрешение: форматът може да се изпрати и без
    // него, затова обработващият метод пита за ролята наново.
    [Fact]
    public async Task OfficePanel_OnASignalPage_IsRefusedForAnOrdinaryCitizen()
    {
        var citizenId = await _host.CreateUserAsync("grazhdanin-yanaki");
        var report = await OpenReportAsync();

        var result = await SignalPage(citizenId).OnPostOfficeAsync(
            report.Id, ReportStatuses.Resolved, "Моята дума е последна.", adjustment: 10);

        Assert.IsType<ForbidResult>(result);
        Assert.Equal(ReportStatuses.New, (await ReloadAsync(report.Id)).Status);
        Assert.Empty(await _host.Context.StatusHistories.ToListAsync());
    }

    [Fact]
    public async Task PriorityAdjustment_MovesTheScoreOnTheSignalPage()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-11");
        var report = await OpenReportAsync();
        var page = SignalPage(adminId);

        await page.OnGetAsync(report.Id);
        var before = page.PriorityScore;

        await page.OnPostOfficeAsync(
            report.Id, ReportStatuses.New, null, adjustment: 10);

        // Същата страница, презаредена след записването: числото, което
        // служителят вижда, е сметнато отново от същата формула.
        await page.OnGetAsync(report.Id);

        // Никакви оценки и потвърждения: 20/140 -> 30/140, т.е. 14.3 -> 21.4.
        Assert.Equal(14.3, before, 1);
        Assert.Equal(21.4, page.PriorityScore, 1);
    }

    [Fact]
    public async Task Dashboard_ByDefaultListsOnlyActiveReports()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-12");
        await SeedThreeStatusesAsync();

        var page = Dashboard(adminId);
        await page.OnGetAsync(filterStatus: null);

        Assert.Equal(1, page.TotalActiveCount);
        Assert.Equal(2, page.ClosedCount);
        Assert.Single(page.Reports);
        Assert.Equal("Активни сигнали", page.ListTitle);
    }

    // Резюме, не работен плот: картичката носи три неща, които служат за
    // избор на ред, и връзката натам, където се работи.
    [Fact]
    public async Task Dashboard_CardCarriesThreeOrderedThumbnailsAndTheOpenLink()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-kartoni");
        var authorId = await _host.CreateUserAsync("avtor-kartoni");

        var report = await _host.AddReportAsync(authorId);

        for (var shot = 1; shot <= 5; shot++)
        {
            var stored = await _host.Storage.SaveAsync(FakeFiles.Jpeg());
            _host.Context.ReportImages.Add(new ReportImage
            {
                ReportId = report.Id,
                StoredFileName = stored.StoredFileName,
                ContentType = stored.ContentType,
                SizeBytes = stored.SizeBytes
            });
        }

        await _host.Context.SaveChangesAsync();

        var page = Dashboard(adminId);
        await page.OnGetAsync(filterStatus: null);

        var loaded = Assert.Single(page.Reports);
        var thumbs = page.ThumbnailsOf(loaded);

        Assert.Equal(3, thumbs.Count);

        // Редът се фиксира по номера: без него при всяко презареждане
        // снимките си менят местата и служителят не помни коя кое показва.
        Assert.Equal(
            loaded.Images.OrderBy(image => image.Id).Take(3).Select(image => image.Id),
            thumbs.Select(image => image.Id));
    }

    [Fact]
    public async Task AfterDelete_TheDashboardExplainsWhatHappened()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-13");
        var page = Dashboard(adminId);

        await page.OnGetAsync(filterStatus: null, deletedId: 12, notice: "deleted");
        Assert.Equal("Сигнал #12 е изтрит заедно със снимките, оценките и дневника.", page.Notice);

        await page.OnGetAsync(filterStatus: null);
        Assert.Null(page.Notice);
    }

    [Fact]
    public async Task ClosedFilter_ListsBothResolvedAndRejected()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-14");
        await SeedThreeStatusesAsync();

        var page = Dashboard(adminId);
        await page.OnGetAsync(AdminDashboard.ClosedFilter);

        Assert.Equal(2, page.Reports.Count);
        Assert.Equal("Приключени сигнали", page.ListTitle);
        Assert.Equal(AdminDashboard.ClosedFilter, page.FilterStatus);
    }

    [Fact]
    public async Task UnknownFilterValue_FallsBackToActiveReports()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-15");
        await SeedThreeStatusesAsync();

        var page = Dashboard(adminId);
        await page.OnGetAsync("какво и да е");

        Assert.Equal(string.Empty, page.FilterStatus);
        Assert.Single(page.Reports);
    }

    // Изтриването е единственото админско действие, което не се връща:
    // затова тестовете проверяват не само реда в Reports, а и четирите
    // зависимите таблици, и файла на диска.
    [Fact]
    public async Task Delete_RemovesReportWithItsRowsAndItsImageFile()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-16");
        var authorId = await _host.CreateUserAsync("avtor-za-iztrivane");

        var report = await _host.AddReportAsync(authorId, r => r.Title = "Дъска под крака");

        var stored = await _host.Storage.SaveAsync(FakeFiles.Jpeg());
        _host.Context.ReportImages.Add(new ReportImage
        {
            ReportId = report.Id,
            StoredFileName = stored.StoredFileName,
            ContentType = stored.ContentType,
            SizeBytes = stored.SizeBytes
        });

        var raterId = await _host.CreateUserAsync("ocenia-16");
        _host.Context.SeverityRatings.Add(new SeverityRating
        {
            ReportId = report.Id,
            UserId = raterId,
            Rating = 5
        });

        var voterId = await _host.CreateUserAsync("potvardi-16");
        _host.Context.Confirmations.Add(new Confirmation { ReportId = report.Id, UserId = voterId });

        _host.Context.StatusHistories.Add(new StatusHistory
        {
            ReportId = report.Id,
            OldStatus = ReportStatuses.New,
            NewStatus = ReportStatuses.Rejected,
            ChangedByUserId = adminId,
            Comment = "Не е от общината."
        });

        await _host.Context.SaveChangesAsync();

        Assert.NotNull(_host.Storage.GetFullPath(stored.StoredFileName));

        var result = await SignalPage(adminId).OnPostDeleteAsync(report.Id, filterStatus: null);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("deleted", redirect.RouteValues!["notice"]);
        Assert.Equal(report.Id, redirect.RouteValues!["deletedId"]);

        Assert.False(await _host.Context.Reports.AnyAsync(r => r.Id == report.Id));
        Assert.False(await _host.Context.ReportImages.AnyAsync(i => i.ReportId == report.Id));
        Assert.False(await _host.Context.SeverityRatings.AnyAsync(s => s.ReportId == report.Id));
        Assert.False(await _host.Context.Confirmations.AnyAsync(c => c.ReportId == report.Id));
        Assert.False(await _host.Context.StatusHistories.AnyAsync(h => h.ReportId == report.Id));

        // Файлът не е в нито една база: ако остане, дискът се пълни с
        // снимки, които никой не може вече да отвори през сигнала.
        Assert.Null(_host.Storage.GetFullPath(stored.StoredFileName));
    }

    [Fact]
    public async Task Delete_KeepsTheOtherReportsAndTheirHistory()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-17");
        var authorId = await _host.CreateUserAsync("avtor-na-sosedite");

        var doomed = await _host.AddReportAsync(authorId, r => r.Title = "Този ще изчезне");
        var neighbour = await _host.AddReportAsync(authorId, r => r.Title = "Този остава");

        _host.Context.StatusHistories.Add(new StatusHistory
        {
            ReportId = neighbour.Id,
            OldStatus = ReportStatuses.New,
            NewStatus = ReportStatuses.InProgress,
            ChangedByUserId = adminId
        });
        await _host.Context.SaveChangesAsync();

        await SignalPage(adminId).OnPostDeleteAsync(doomed.Id, filterStatus: null);

        var page = Dashboard(adminId);
        await page.OnGetAsync(null);

        Assert.Equal("Този остава", Assert.Single(page.Reports).Title);
        Assert.Single(await _host.Context.StatusHistories.ToListAsync());
    }

    [Fact]
    public async Task Delete_UnknownReportIsNotFoundAndChangesNothing()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-18");
        await OpenReportAsync();

        var result = await SignalPage(adminId).OnPostDeleteAsync(424242, filterStatus: null);

        Assert.IsType<NotFoundResult>(result);
        Assert.Single(await _host.Context.Reports.ToListAsync());
    }

    // И тук границата е ролята, не видимостта: адресът на формата е
    // известен и заявка може да се сглоби ръчно, а без правата остава
    // отказана.
    [Fact]
    public async Task Delete_IsRefusedForAnOrdinaryCitizen()
    {
        var citizenId = await _host.CreateUserAsync("grazhdanin-iztriva4");
        var report = await OpenReportAsync();

        var result = await SignalPage(citizenId).OnPostDeleteAsync(report.Id, filterStatus: null);

        Assert.IsType<ForbidResult>(result);
        Assert.True(await _host.Context.Reports.AnyAsync(r => r.Id == report.Id));
    }

    private async Task<Report> OpenReportAsync()
    {
        var userId = await _host.CreateUserAsync("avtor-" + Guid.NewGuid().ToString("N")[..8]);
        return await _host.AddReportAsync(userId);
    }

    private async Task SeedThreeStatusesAsync()
    {
        var userId = await _host.CreateUserAsync("avtor-" + Guid.NewGuid().ToString("N")[..8]);

        await _host.AddReportAsync(userId);
        await _host.AddReportAsync(userId, r => r.Status = ReportStatuses.Resolved);
        await _host.AddReportAsync(userId, r => r.Status = ReportStatuses.Rejected);
    }

    private Task<Report> ReloadAsync(int reportId) =>
        _host.Context.Reports.SingleAsync(r => r.Id == reportId);

    // Таблото вече само чете; всяка промяна минава през страницата на
    // сигнала, затова и двете страници се създават отделно.
    private AdminDashboard Dashboard(string adminId) =>
        _host.CreatePageModel<AdminDashboard>(_host.PrincipalFor(adminId));

    private SignalModel SignalPage(string userId) =>
        _host.CreatePageModel<SignalModel>(_host.PrincipalFor(userId));

    public void Dispose() => _host.Dispose();
}

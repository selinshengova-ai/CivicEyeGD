using CivicEyeGD.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AdminDashboard = CivicEyeGD.Pages.Admin.IndexModel;

namespace CivicEyeGD.Tests;

/// <summary>
/// Таблото е единственото място, където общината променя данните.
/// Тук се пазят обещанията от документацията: всяка смяна на статуса
/// остава в историята с автор и бележка, административната намеса в
/// приоритета е ограничена, записването е с един бутон, а филтърът за
/// приключени сигнали наистина ги намира.
/// </summary>
public sealed class AdminWorkflowTests : IDisposable
{
    private readonly TestHost _host = new();

    [Fact]
    public async Task StatusChange_WritesHistoryTheCitizenCanRead()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-1");
        var report = await OpenReportAsync();

        var result = await AdminPage(adminId).OnPostUpdateAsync(
            report.Id, ReportStatuses.InProgress, "Екипът е изпратен.", adjustment: 0, filterStatus: null);

        AssertSavedNotice(result, "saved");

        var entry = Assert.Single(await _host.Context.StatusHistories.ToListAsync());
        Assert.Equal(ReportStatuses.New, entry.OldStatus);
        Assert.Equal(ReportStatuses.InProgress, entry.NewStatus);
        Assert.Equal("Екипът е изпратен.", entry.Comment);
        Assert.Equal(adminId, entry.ChangedByUserId);
        Assert.Equal(report.Id, entry.ReportId);
    }

    // Един бутон за трите неща: служителят решава статуса, бележката и
    // намесата в приоритета в един момент, не на два пъти.
    [Fact]
    public async Task SingleSave_ChangesStatusAndAdjustmentTogether()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-2");
        var report = await OpenReportAsync();

        await AdminPage(adminId).OnPostUpdateAsync(
            report.Id, ReportStatuses.InProgress, "Кръпката е в програмата.", adjustment: 7, filterStatus: null);

        var saved = await ReloadAsync(report.Id);

        Assert.Equal(ReportStatuses.InProgress, saved.Status);
        Assert.Equal(7, saved.AdminPriorityAdjustment);
        Assert.Single(await _host.Context.StatusHistories.ToListAsync());
    }

    [Fact]
    public async Task Save_WithoutAnythingDifferent_WritesNoHistoryAndSaysSo()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-3");
        var report = await OpenReportAsync();

        var result = await AdminPage(adminId).OnPostUpdateAsync(
            report.Id, ReportStatuses.New, "Бележка, но статусът е същият.", adjustment: 0, filterStatus: null);

        AssertSavedNotice(result, "unchanged");
        Assert.Empty(await _host.Context.StatusHistories.ToListAsync());
    }

    [Fact]
    public async Task AdjustingOnlyThePriority_LeavesTheStatusHistoryEmpty()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-4");
        var report = await OpenReportAsync();

        await AdminPage(adminId).OnPostUpdateAsync(
            report.Id, ReportStatuses.New, null, adjustment: 5, filterStatus: null);

        Assert.Equal(5, (await ReloadAsync(report.Id)).AdminPriorityAdjustment);
        Assert.Empty(await _host.Context.StatusHistories.ToListAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" Novo ")]
    [InlineData("")]
    public async Task StatusChange_ToTheSameStatus_LeavesNoExtraEntry(string? comment)
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-5");
        var report = await OpenReportAsync();

        await AdminPage(adminId).OnPostUpdateAsync(
            report.Id, ReportStatuses.New, comment, adjustment: 0, filterStatus: null);

        Assert.Empty(await _host.Context.StatusHistories.ToListAsync());
    }

    [Fact]
    public async Task StatusChange_WithoutComment_StoresNoEmptyText()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-6");
        var report = await OpenReportAsync();

        await AdminPage(adminId).OnPostUpdateAsync(
            report.Id, ReportStatuses.Resolved, "   ", adjustment: 0, filterStatus: null);

        var entry = Assert.Single(await _host.Context.StatusHistories.ToListAsync());
        Assert.Null(entry.Comment);
    }

    [Fact]
    public async Task StatusComment_LongerThanTheLimit_IsCut()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-7");
        var report = await OpenReportAsync();

        await AdminPage(adminId).OnPostUpdateAsync(
            report.Id,
            ReportStatuses.Resolved,
            new string('а', AdminDashboard.MaxCommentLength + 120),
            adjustment: 0,
            filterStatus: null);

        var entry = Assert.Single(await _host.Context.StatusHistories.ToListAsync());
        Assert.Equal(AdminDashboard.MaxCommentLength, entry.Comment!.Length);
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
            await AdminPage(adminId).OnPostUpdateAsync(
                report.Id, newStatus!, null, adjustment: 0, filterStatus: null));

        Assert.Equal("Непознат статус.", badRequest.Value);
        Assert.Equal(ReportStatuses.New, (await ReloadAsync(report.Id)).Status);
        Assert.Empty(await _host.Context.StatusHistories.ToListAsync());
    }

    [Fact]
    public async Task StatusChange_OnMissingReport_ReturnsNotFound()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-9");

        Assert.IsType<NotFoundResult>(
            await AdminPage(adminId).OnPostUpdateAsync(
                id: 4242, ReportStatuses.Resolved, null, adjustment: 0, filterStatus: null));
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
            await AdminPage(adminId).OnPostUpdateAsync(
                report.Id, ReportStatuses.Resolved, null, adjustment, filterStatus: null));

        Assert.Equal("Корекцията на приоритета е между 0 и 10.", badRequest.Value);
        Assert.Equal(ReportStatuses.New, (await ReloadAsync(report.Id)).Status);
        Assert.Equal(0, (await ReloadAsync(report.Id)).AdminPriorityAdjustment);
        Assert.Empty(await _host.Context.StatusHistories.ToListAsync());
    }

    [Fact]
    public async Task PriorityAdjustment_MovesTheScoreOnTheDashboard()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-11");
        var report = await OpenReportAsync();
        var page = AdminPage(adminId);

        var before = page.PriorityOf(report);

        await page.OnPostUpdateAsync(
            report.Id, ReportStatuses.New, null, adjustment: 10, filterStatus: null);

        var after = page.PriorityOf(await ReloadAsync(report.Id));

        // Никакви оценки и потвърждения: 20/140 -> 30/140, т.е. 14.3 -> 21.4.
        Assert.Equal(14.3, before, 1);
        Assert.Equal(21.4, after, 1);
    }

    [Fact]
    public async Task Dashboard_ByDefaultListsOnlyActiveReports()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-12");
        await SeedThreeStatusesAsync();

        var page = AdminPage(adminId);
        await page.OnGetAsync(filterStatus: null);

        Assert.Equal(1, page.TotalActiveCount);
        Assert.Equal(2, page.ClosedCount);
        Assert.Single(page.Reports);
        Assert.Equal("Активни сигнали", page.ListTitle);
    }

    [Fact]
    public async Task AfterSave_TheDashboardExplainsWhatHappened()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-13");
        var page = AdminPage(adminId);

        await page.OnGetAsync(filterStatus: null, savedId: 12, notice: "saved");
        Assert.Equal("Промените по сигнал #12 са записани.", page.Notice);

        await page.OnGetAsync(filterStatus: null, savedId: 12, notice: "unchanged");
        Assert.Contains("не се промени", page.Notice);

        await page.OnGetAsync(filterStatus: null);
        Assert.Null(page.Notice);
    }

    [Fact]
    public async Task ClosedFilter_ListsBothResolvedAndRejected()
    {
        var adminId = await _host.CreateAdministratorAsync("sluzhitel-14");
        await SeedThreeStatusesAsync();

        var page = AdminPage(adminId);
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

        var page = AdminPage(adminId);
        await page.OnGetAsync("какво и да е");

        Assert.Equal(string.Empty, page.FilterStatus);
        Assert.Single(page.Reports);
    }

    private static void AssertSavedNotice(IActionResult result, string expectedNotice)
    {
        var redirect = Assert.IsType<RedirectToPageResult>(result);

        Assert.Equal(expectedNotice, redirect.RouteValues!["notice"]);
        Assert.NotNull(redirect.RouteValues!["savedId"]);
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

    private AdminDashboard AdminPage(string adminId) =>
        _host.CreatePageModel<AdminDashboard>(_host.PrincipalFor(adminId));

    public void Dispose() => _host.Dispose();
}

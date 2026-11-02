using CivicEyeGD.Data;
using CivicEyeGD.Pages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CivicEyeGD.Tests;

/// <summary>
/// Панелът на служителя в самия сигнал е същата намеса като от таблото,
/// но без служителят да търси номера в дългия списък. Проверката за
/// роля е в handler-а, защото един скрит бутон не е разрешение:
/// формата може да се изпрати и без панелът да се вижда.
/// </summary>
public sealed class DetailsOfficeTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Officer_ChangesStatusAndPriorityFromTheReportPage()
    {
        var adminId = await _host.CreateAdministratorAsync("office-admin");
        var report = await OpenReportAsync();

        var page = DetailsPage(adminId);
        var result = await page.OnPostOfficeAsync(
            report.Id, ReportStatuses.InProgress, "Екипът излезе за оглед.", adjustment: 2);

        Assert.IsType<PageResult>(result);
        Assert.Contains("записани", page.SuccessMessage!);

        var saved = await _host.Context.Reports.SingleAsync(r => r.Id == report.Id);
        Assert.Equal(ReportStatuses.InProgress, saved.Status);
        Assert.Equal(2, saved.AdminPriorityAdjustment);

        // Историята е публична: citizen чете какво е направено и от кого.
        var entry = Assert.Single(await _host.Context.StatusHistories.ToListAsync());
        Assert.Equal(adminId, entry.ChangedByUserId);
        Assert.Equal("Екипът излезе за оглед.", entry.Comment);
    }

    [Fact]
    public async Task Citizen_PostingToTheOfficerPanel_ChangesNothing()
    {
        var citizenId = await _host.CreateUserAsync("office-citizen");
        var report = await OpenReportAsync();

        var page = DetailsPage(citizenId);

        Assert.IsType<ForbidResult>(
            await page.OnPostOfficeAsync(report.Id, ReportStatuses.Resolved, "опит", adjustment: 0));

        Assert.Equal(ReportStatuses.New, (await _host.Context.Reports.SingleAsync(r => r.Id == report.Id)).Status);
        Assert.Empty(await _host.Context.StatusHistories.ToListAsync());
    }

    [Fact]
    public async Task SignedOutPost_ToTheOfficerPanel_IsForbidden()
    {
        var report = await OpenReportAsync();

        Assert.IsType<ForbidResult>(
            await DetailsPage(userId: null).OnPostOfficeAsync(
                report.Id, ReportStatuses.Resolved, null, adjustment: 0));
    }

    [Fact]
    public async Task OnlyTheOfficerSeesThePanel()
    {
        var adminId = await _host.CreateAdministratorAsync("office-viewer");
        var citizenId = await _host.CreateUserAsync("office-viewer-citizen");
        var report = await OpenReportAsync();

        Assert.True(await IsOfficerAsync(adminId, report.Id));
        Assert.False(await IsOfficerAsync(citizenId, report.Id));
        Assert.False(await IsOfficerAsync(userId: null, report.Id));
    }

    private async Task<bool> IsOfficerAsync(string? userId, int reportId)
    {
        var page = DetailsPage(userId);
        await page.OnGetAsync(reportId);

        return page.IsOfficer;
    }

    private DetailsModel DetailsPage(string? userId) => userId is null
        ? _host.CreatePageModel<DetailsModel>()
        : _host.CreatePageModel<DetailsModel>(_host.PrincipalFor(userId));

    private async Task<Report> OpenReportAsync()
    {
        var userId = await _host.CreateUserAsync("avtor-" + Guid.NewGuid().ToString("N")[..8]);

        return await _host.AddReportAsync(userId);
    }
}

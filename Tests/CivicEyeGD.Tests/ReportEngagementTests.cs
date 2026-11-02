using CivicEyeGD.Data;
using CivicEyeGD.Pages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CivicEyeGD.Tests;

/// <summary>
/// Оценката и потвърждението оформят приоритета. Правилата са три:
/// един потребител може само по веднъж от двете, подателят не участва
/// в собствената си сметка и приключен сигнал вече не приема гласове.
/// </summary>
public sealed class ReportEngagementTests : IDisposable
{
    private readonly TestHost _host = new();

    [Fact]
    public async Task FirstRating_IsStored()
    {
        var (_, voterId, reportId) = await ReportWithVoterAsync();
        var model = DetailsPage(voterId);

        var result = await model.OnPostRateAsync(reportId, rating: 5);

        Assert.IsType<PageResult>(result);
        Assert.Null(model.ErrorMessage);
        Assert.Equal("Оценката ти е записана.", model.SuccessMessage);

        var rating = Assert.Single(await RatingsAsync(reportId));
        Assert.Equal(5, rating.Rating);
    }

    [Fact]
    public async Task SecondRatingBySameUser_UpdatesInsteadOfDuplicating()
    {
        var (_, voterId, reportId) = await ReportWithVoterAsync();

        await DetailsPage(voterId).OnPostRateAsync(reportId, rating: 2);
        await DetailsPage(voterId).OnPostRateAsync(reportId, rating: 5);

        var rating = Assert.Single(await RatingsAsync(reportId));
        Assert.Equal(5, rating.Rating);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-3)]
    public async Task RatingOutsideTheScale_IsRejected(int rating)
    {
        var (_, voterId, reportId) = await ReportWithVoterAsync();
        var model = DetailsPage(voterId);

        var result = await model.OnPostRateAsync(reportId, rating);

        Assert.IsType<PageResult>(result);
        Assert.Equal("Оценката трябва да е между 1 и 5.", model.ErrorMessage);
        Assert.Empty(await RatingsAsync(reportId));
    }

    [Fact]
    public async Task RatingOnMissingReport_ReturnsNotFound()
    {
        var voterId = await _host.CreateUserAsync("igrivach2");
        var model = DetailsPage(voterId);

        var result = await model.OnPostRateAsync(id: 9999, rating: 3);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Author_CannotRateTheirOwnReport()
    {
        var (authorId, _, reportId) = await ReportWithVoterAsync();
        var model = DetailsPage(authorId);

        var result = await model.OnPostRateAsync(reportId, rating: 5);

        Assert.IsType<PageResult>(result);
        Assert.Equal("Подателят не участва в оценката на собствения си сигнал.", model.ErrorMessage);
        Assert.Empty(await RatingsAsync(reportId));
    }

    [Fact]
    public async Task Author_CannotConfirmTheirOwnReport()
    {
        var (authorId, _, reportId) = await ReportWithVoterAsync();
        var model = DetailsPage(authorId);

        var badRequest = Assert.IsType<BadRequestObjectResult>(await model.OnPostConfirmAsync(reportId));

        Assert.Equal("Не можеш да потвърдиш собствения си сигнал.", badRequest.Value);
        Assert.Equal(0, await _host.Context.Confirmations.CountAsync());
    }

    [Fact]
    public async Task FirstConfirmation_IsStored()
    {
        var (_, voterId, reportId) = await ReportWithVoterAsync();
        var model = DetailsPage(voterId);

        var result = await model.OnPostConfirmAsync(reportId);

        Assert.IsType<PageResult>(result);
        Assert.Equal("Благодарим! Потвърждението ти е записано.", model.SuccessMessage);
        Assert.Equal(1, await _host.Context.Confirmations.CountAsync());
    }

    [Fact]
    public async Task ClosedReport_RejectsNewConfirmation()
    {
        var (_, voterId, reportId) = await ReportWithVoterAsync(ReportStatuses.Resolved);
        var model = DetailsPage(voterId);

        var badRequest = Assert.IsType<BadRequestObjectResult>(await model.OnPostConfirmAsync(reportId));

        Assert.Equal("Приключен сигнал не приема нови потвърждения.", badRequest.Value);
        Assert.Equal(0, await _host.Context.Confirmations.CountAsync());
    }

    [Fact]
    public async Task ClosedReport_RejectsNewRating()
    {
        var (_, voterId, reportId) = await ReportWithVoterAsync(ReportStatuses.Rejected);
        var model = DetailsPage(voterId);

        var result = await model.OnPostRateAsync(reportId, rating: 4);

        Assert.IsType<PageResult>(result);
        Assert.Equal("Приключен сигнал вече не се оценява.", model.ErrorMessage);
        Assert.Empty(await RatingsAsync(reportId));
    }

    [Fact]
    public async Task RepeatedConfirmation_IsRejected()
    {
        var (_, voterId, reportId) = await ReportWithVoterAsync();

        await DetailsPage(voterId).OnPostConfirmAsync(reportId);

        var model = DetailsPage(voterId);
        var badRequest = Assert.IsType<BadRequestObjectResult>(await model.OnPostConfirmAsync(reportId));

        Assert.Equal("Вече си потвърдил този проблем.", badRequest.Value);
        Assert.Equal(1, await _host.Context.Confirmations.CountAsync());
    }

    [Fact]
    public async Task AnonymousVisitor_IsSentToSignInBeforeAnythingIsWritten()
    {
        var (_, _, reportId) = await ReportWithVoterAsync();

        // PageContext без бисквита: PageModel-ът се създава с null потребител.
        var confirmModel = _host.CreatePageModel<DetailsModel>();
        Assert.IsType<ChallengeResult>(await confirmModel.OnPostConfirmAsync(reportId));

        var rateModel = _host.CreatePageModel<DetailsModel>();
        Assert.IsType<ChallengeResult>(await rateModel.OnPostRateAsync(reportId, rating: 5));

        Assert.Equal(0, await _host.Context.Confirmations.CountAsync());
        Assert.Empty(await RatingsAsync(reportId));
    }

    [Fact]
    public async Task Details_ExcludeTheAuthorsVoteFromTheDisplayedAverage()
    {
        var (authorId, voterId, reportId) = await ReportWithVoterAsync();

        // Опитът на подателя да вдигне сигнала си не стига до базата.
        await DetailsPage(authorId).OnPostRateAsync(reportId, rating: 3);
        await DetailsPage(voterId).OnPostRateAsync(reportId, rating: 5);
        await DetailsPage(voterId).OnPostConfirmAsync(reportId);

        var model = DetailsPage(voterId);
        var result = await model.OnGetAsync(reportId);

        Assert.IsType<PageResult>(result);
        Assert.Equal(5.0, model.AverageSeverity);
        Assert.Equal(1, model.IndependentRaterCount);
        Assert.Equal(1, model.ConfirmationCount);

        // 20 * 5 + 3 * 1 = 103 от 140 -> 73.6, само от един глас на гражданин.
        Assert.Equal(73.6, model.PriorityScore, 1);
        Assert.Equal("Висок приоритет", model.PriorityLabel);
    }

    [Fact]
    public async Task Details_ExplanationAddsUpToTheDisplayedScore()
    {
        var (_, voterId, reportId) = await ReportWithVoterAsync(adjustment: 7);
        await DetailsPage(voterId).OnPostRateAsync(reportId, rating: 2);

        var model = DetailsPage(voterId);
        await model.OnGetAsync(reportId);

        var parts = model.Breakdown!;
        var raw = parts.SeverityPoints + parts.ConfirmationPoints + parts.AdminPoints;

        Assert.Equal(2.0, parts.UsedSeverity);
        Assert.Equal(7.0, parts.AdminPoints);
        Assert.Equal(Math.Round(raw / 140.0 * 100.0, 1), parts.Score, 1);
        Assert.Equal(parts.Score, model.PriorityScore, 1);
    }

    [Fact]
    public async Task Details_RemembersWhatTheVisitorAlreadyDid()
    {
        var (_, voterId, reportId) = await ReportWithVoterAsync();

        await DetailsPage(voterId).OnPostRateAsync(reportId, rating: 4);
        await DetailsPage(voterId).OnPostConfirmAsync(reportId);

        var model = DetailsPage(voterId);
        await model.OnGetAsync(reportId);

        Assert.True(model.IsSignedIn);
        Assert.False(model.CurrentUserIsAuthor);
        Assert.True(model.CurrentUserHasRated);
        Assert.Equal(4, model.CurrentUserRating);
        Assert.True(model.CurrentUserHasConfirmed);
    }

    [Fact]
    public async Task Details_TellsTheAuthorTheyAreNotVoting()
    {
        var (authorId, _, reportId) = await ReportWithVoterAsync();

        var model = DetailsPage(authorId);
        await model.OnGetAsync(reportId);

        Assert.True(model.IsSignedIn);
        Assert.True(model.CurrentUserIsAuthor);
    }

    [Fact]
    public async Task Details_ShowsTheRoleOfThePersonWhoChangedTheStatus()
    {
        var (_, _, reportId) = await ReportWithVoterAsync();
        var adminId = await _host.CreateAdministratorAsync("sluzhitel");

        // Намесата се прави на страницата на сигнала, не на таблото.
        var officePage = _host.CreatePageModel<DetailsModel>(_host.PrincipalFor(adminId));
        await officePage.OnPostOfficeAsync(reportId, ReportStatuses.Resolved, "Кърпена е дупката на 12 октомври.", 0);

        var model = _host.CreatePageModel<DetailsModel>();
        await model.OnGetAsync(reportId);

        var entry = Assert.Single(model.Report!.StatusHistories);
        Assert.Equal("Администратор на платформата", model.HistoryAuthorLabels[entry.Id]);
        Assert.Equal("Кърпена е дупката на 12 октомври.", entry.Comment);

        // Имейлът на служителя не се показва публично.
        Assert.DoesNotContain("@", model.HistoryAuthorLabels.Values);
    }

    [Fact]
    public async Task Details_OnMissingReport_ReturnsNotFound()
    {
        var voterId = await _host.CreateUserAsync("igrivach7");
        var model = DetailsPage(voterId);

        Assert.IsType<NotFoundResult>(await model.OnGetAsync(id: 4242));
        Assert.IsType<NotFoundResult>(await model.OnPostConfirmAsync(id: 4242));
    }

    [Fact]
    public async Task Database_RejectsTwoConfirmationsFromOneUser()
    {
        var (userId, _, reportId) = await ReportWithVoterAsync();

        _host.Context.Confirmations.AddRange(
            new Confirmation { ReportId = reportId, UserId = userId },
            new Confirmation { ReportId = reportId, UserId = userId });

        await Assert.ThrowsAsync<DbUpdateException>(() => _host.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_RejectsTwoRatingsFromOneUser()
    {
        var (userId, _, reportId) = await ReportWithVoterAsync();

        _host.Context.SeverityRatings.AddRange(
            new SeverityRating { ReportId = reportId, UserId = userId, Rating = 2 },
            new SeverityRating { ReportId = reportId, UserId = userId, Rating = 5 });

        await Assert.ThrowsAsync<DbUpdateException>(() => _host.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task RemovingAReport_AlsoRemovesItsEngagement()
    {
        var (_, voterId, reportId) = await ReportWithVoterAsync();

        await DetailsPage(voterId).OnPostRateAsync(reportId, rating: 4);
        await DetailsPage(voterId).OnPostConfirmAsync(reportId);

        var report = await _host.Context.Reports.SingleAsync(r => r.Id == reportId);
        _host.Context.Reports.Remove(report);
        await _host.Context.SaveChangesAsync();

        Assert.Equal(0, await _host.Context.SeverityRatings.CountAsync());
        Assert.Equal(0, await _host.Context.Confirmations.CountAsync());
    }

    /// <summary>
    /// Сигнал с подател и отделен гласоподавател: правилата за самогласуване
    /// се тестват единствено ако двете роли не са един и същи човек.
    /// </summary>
    private async Task<(string AuthorId, string VoterId, int ReportId)> ReportWithVoterAsync(
        string status = ReportStatuses.New,
        int adjustment = 0)
    {
        var authorId = await _host.CreateUserAsync("avtor-" + Guid.NewGuid().ToString("N")[..8]);
        var voterId = await _host.CreateUserAsync("igrivach-" + Guid.NewGuid().ToString("N")[..8]);

        var report = await _host.AddReportAsync(authorId, report =>
        {
            report.Status = status;
            report.AdminPriorityAdjustment = adjustment;
        });

        return (authorId, voterId, report.Id);
    }

    private DetailsModel DetailsPage(string userId)
        => _host.CreatePageModel<DetailsModel>(_host.PrincipalFor(userId));

    private Task<List<SeverityRating>> RatingsAsync(int reportId)
        => _host.Context.SeverityRatings.Where(r => r.ReportId == reportId).ToListAsync();

    public void Dispose() => _host.Dispose();
}

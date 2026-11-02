using System.Text.Json;
using CivicEyeGD.Data;
using CivicEyeGD.Pages;
using CivicEyeGD.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CivicEyeGD.Tests;

/// <summary>
/// Един и същи тротоар го подават трима. Без проверка за близост
/// платформата получава три картички с по един глас, а общината брои
/// три проблема там, където има един. Тестовете минават през правилото
/// в паметта и през сървърната проверка във формата „Нов сигнал“.
/// </summary>
public sealed class DuplicateReportTests : IDisposable
{
    private readonly TestHost _host = new();
    private string? _userId;

    [Fact]
    public void NearbyOpenReportInSameCategory_IsOffered()
    {
        var matches = ReportDuplicates.Find(
            [Existing(1, ReportCategories.Sidewalks, ReportStatuses.New, 41.5713, 23.7361)],
            ReportCategories.Sidewalks,
            41.5716,
            23.7361);

        var match = Assert.Single(matches);

        Assert.Equal(1, match.Id);
        Assert.True(match.DistanceMeters <= ReportDuplicates.RadiusMeters);
    }

    [Fact]
    public void ReportFromAnotherCategory_IsNotADuplicate()
    {
        var matches = ReportDuplicates.Find(
            [Existing(1, ReportCategories.StreetLighting, ReportStatuses.New, 41.5713, 23.7361)],
            ReportCategories.Sidewalks,
            41.5713,
            23.7361);

        Assert.Empty(matches);
    }

    [Fact]
    public void ClosedReport_IsNotOfferedForConfirmation()
    {
        // Решен проблем не чака нищо: потвърждението му няма смисъл,
        // а нов сигнал на същото място е напълно легитимен.
        var matches = ReportDuplicates.Find(
            [Existing(1, ReportCategories.Sidewalks, ReportStatuses.Resolved, 41.5713, 23.7361)],
            ReportCategories.Sidewalks,
            41.5713,
            23.7361);

        Assert.Empty(matches);
    }

    [Fact]
    public void ReportTwoHundredMetresAway_IsNotADuplicate()
    {
        var matches = ReportDuplicates.Find(
            [Existing(1, ReportCategories.Sidewalks, ReportStatuses.New, 41.5731, 23.7361)],
            ReportCategories.Sidewalks,
            41.5713,
            23.7361);

        Assert.Empty(matches);
    }

    [Fact]
    public void Matches_AreOrderedByDistanceAndCapped()
    {
        var candidates = Enumerable.Range(1, 5)
            .Select(step => Existing(
                step,
                ReportCategories.Sidewalks,
                ReportStatuses.New,
                41.5713 + step * 0.0001,
                23.7361))
            .ToList();

        var matches = ReportDuplicates.Find(
            candidates,
            ReportCategories.Sidewalks,
            41.5713,
            23.7361);

        Assert.Equal(ReportDuplicates.MaxMatches, matches.Count);
        Assert.True(matches[0].DistanceMeters < matches[1].DistanceMeters);
        Assert.Equal(1, matches[0].Id);
    }

    [Fact]
    public void WithoutCoordinates_ThereIsNothingToCompare()
    {
        var matches = ReportDuplicates.Find(
            [Existing(1, ReportCategories.Sidewalks, ReportStatuses.New, 41.5713, 23.7361)],
            ReportCategories.Sidewalks,
            null,
            null);

        Assert.Empty(matches);
    }

    [Fact]
    public void DistanceMeters_IsZeroBetweenTheSamePoint()
    {
        var distance = ReportGeography.DistanceMeters(41.5713, 23.7361, 41.5713, 23.7361);

        Assert.Equal(0, distance, 3);
    }

    [Fact]
    public void DistanceMeters_MatchesKnownGroundSteps()
    {
        // 0.001° географска ширина са около 111 m, а същата стъпка по
        // дължина на тази ширина дава около 83 m. Без тези две мерки
        // една грешка във формулата би останала скрита.
        var north = ReportGeography.DistanceMeters(41.5713, 23.7361, 41.5723, 23.7361);
        var east = ReportGeography.DistanceMeters(41.5713, 23.7361, 41.5713, 23.7371);

        Assert.InRange(north, 110, 112);
        Assert.InRange(east, 82, 84);
    }

    [Fact]
    public async Task PostBesideAnExistingReport_IsBlocked()
    {
        var model = await PageAsync();
        await SeededAsync();

        model.Report = Input(41.5714, 23.7361);

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains("вече има подаден сигнал", ErrorsFor(model, string.Empty));
        Assert.Single(model.NearbyDuplicates);
        Assert.Equal(1, await _host.Context.Reports.CountAsync());
    }

    [Fact]
    public async Task AcknowledgedPost_IsSavedBesideTheExistingOne()
    {
        var model = await PageAsync();
        await SeededAsync();

        model.Report = Input(41.5714, 23.7361);
        model.Report.DuplicateAcknowledged = true;

        var result = await model.OnPostAsync();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(2, await _host.Context.Reports.CountAsync());
    }

    [Fact]
    public async Task PostOnTheOtherEndOfTheStreet_IsSavedWithoutAcknowledgement()
    {
        var model = await PageAsync();
        await SeededAsync();

        model.Report = Input(41.5753, 23.7361);

        var result = await model.OnPostAsync();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(2, await _host.Context.Reports.CountAsync());
    }

    [Fact]
    public async Task ResolvedNeighbour_LeavesTheFormFree()
    {
        var model = await PageAsync();
        await _host.CreateUserAsync("sosed");
        await _host.AddReportAsync(_userId!, report => report.Status = ReportStatuses.Resolved);

        model.Report = Input(41.5713, 23.7361);

        var result = await model.OnPostAsync();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(2, await _host.Context.Reports.CountAsync());
    }

    [Fact]
    public async Task NearbyHandler_DescribesTheExistingReport()
    {
        var model = await PageAsync();
        await SeededAsync();

        var result = await model.OnGetNearbyAsync(ReportCategories.RoadSurface, 41.5714, 23.7361);

        var match = Assert.Single(MatchesOf(result));

        Assert.Equal(1, match.GetProperty("Id").GetInt32());
        Assert.Equal(ReportStatuses.New, match.GetProperty("Status").GetString());
        Assert.True(match.GetProperty("DistanceMeters").GetDouble() < 20);
    }

    [Fact]
    public async Task NearbyHandler_MarksAnotherAuthorsReportAsForeign()
    {
        var model = await PageAsync();
        var neighbour = await _host.CreateUserAsync("sosed");
        await _host.AddReportAsync(neighbour);

        var result = await model.OnGetNearbyAsync(ReportCategories.RoadSurface, 41.5714, 23.7361);

        var match = Assert.Single(MatchesOf(result));

        // Чужд сигнал се предлага за потвърждане; собственият — не.
        Assert.False(match.GetProperty("IsMine").GetBoolean());
    }

    [Fact]
    public async Task NearbyHandler_MarksTheReportOfTheSignedInUser()
    {
        var model = await PageAsync();
        await SeededAsync();

        var result = await model.OnGetNearbyAsync(ReportCategories.RoadSurface, 41.5714, 23.7361);

        var match = Assert.Single(MatchesOf(result));

        // Прозорецът сменя текста си за собствения сигнал и му дава датата:
        // „този, който подаде преди месец" е друг случай от двойното изпращане.
        Assert.True(match.GetProperty("IsMine").GetBoolean());
        Assert.Equal("20.10.2026", match.GetProperty("CreatedAtText").GetString());
    }

    [Fact]
    public async Task NearbyHandler_IgnoresCategoryFromOutsideTheList()
    {
        var model = await PageAsync();
        await SeededAsync();

        var result = await model.OnGetNearbyAsync("Личен проблем", 41.5714, 23.7361);

        Assert.Empty(MatchesOf(result));
    }

    [Fact]
    public async Task NearbyHandler_IgnoresCoordinatesOutsideTheTown()
    {
        var model = await PageAsync();
        await SeededAsync();

        var result = await model.OnGetNearbyAsync(ReportCategories.RoadSurface, 42.6977, 23.3219);

        Assert.Empty(MatchesOf(result));
    }

    // Отговорът се чете като JSON, защото точно така го получава браузърът:
    // проверка върху текста би пропуснала и екранирана кирилица, и
    // неочаквани полета в записите.
    private static JsonElement[] MatchesOf(IActionResult result)
    {
        var json = Assert.IsType<JsonResult>(result);
        var text = JsonSerializer.Serialize(json.Value);

        using var document = JsonDocument.Parse(text);

        return document.RootElement
            .GetProperty("matches")
            .EnumerateArray()
            .Select(element => element.Clone())
            .ToArray();
    }

    private static Report Existing(
        int id,
        string category,
        string status,
        double latitude,
        double longitude) => new()
    {
        Id = id,
        Title = "Сигнал " + id,
        Category = category,
        Status = status,
        Address = "ул. „Пирин“ 12",
        Latitude = latitude,
        Longitude = longitude
    };

    private static CreateModel.ReportInput Input(double latitude, double longitude) => new()
    {
        Title = "Смъкнат бордюр",
        Category = ReportCategories.RoadSurface,
        Address = "бул. „Гоце Делчев“ 21",
        Description = "Бордюрът е счупен точно на пешеходната пътека.",
        Latitude = latitude,
        Longitude = longitude,
        Severity = 3
    };

    /// <summary>Запис в базата на същото място и в същата категория.</summary>
    private async Task SeededAsync() => await _host.AddReportAsync(_userId!);

    private static string ErrorsFor(PageModel model, string key)
    {
        var errors = model.ModelState.TryGetValue(key, out var entry)
            ? entry?.Errors
            : null;

        return string.Join(" ", errors?.Select(error => error.ErrorMessage) ?? []);
    }

    private async Task<CreateModel> PageAsync()
    {
        _userId ??= await _host.CreateUserAsync("igrivach");

        return _host.CreatePageModel<CreateModel>(_host.PrincipalFor(_userId));
    }

    public void Dispose() => _host.Dispose();
}

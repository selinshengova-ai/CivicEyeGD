using CivicEyeGD.Data;
using CivicEyeGD.Services;

namespace CivicEyeGD.Tests;

/// <summary>
/// Каталога от категории, статуси и географските граници на града са
/// единственият източник на истина за сървърната валидация.
/// </summary>
public class ReportVocabularyTests
{
    [Theory]
    [InlineData(ReportCategories.Accessibility)]
    [InlineData(ReportCategories.Sidewalks)]
    [InlineData(ReportCategories.PedestrianPaths)]
    [InlineData(ReportCategories.StreetLighting)]
    [InlineData(ReportCategories.RoadSurface)]
    [InlineData(ReportCategories.Other)]
    public void IsValid_AcceptsEveryListedCategory(string category)
    {
        Assert.True(ReportCategories.IsValid(category));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Мостове")]
    [InlineData("Друго ")]
    [InlineData("тротоари")]
    [InlineData(null)]
    public void IsValid_RejectsAnythingElse(string? category)
    {
        Assert.False(ReportCategories.IsValid(category));
    }

    [Fact]
    public void AllCategories_HaveUniqueNames()
    {
        Assert.Equal(ReportCategories.All.Count, ReportCategories.All.Distinct().Count());
    }

    [Theory]
    [InlineData(ReportStatuses.New, false)]
    [InlineData(ReportStatuses.InProgress, false)]
    [InlineData(ReportStatuses.Resolved, true)]
    [InlineData(ReportStatuses.Rejected, true)]
    [InlineData(null, false)]
    public void IsClosed_MarksOnlyFinishedSignals(string? status, bool expected)
    {
        Assert.Equal(expected, ReportStatuses.IsClosed(status));
    }

    [Fact]
    public void Statuses_AreCoveredByBothHelpers()
    {
        foreach (var status in ReportStatuses.All)
        {
            Assert.True(ReportStatuses.IsValid(status));

            // Затворен е точно когато не изисква действие от общината.
            Assert.Equal(ReportStatuses.Closed.Contains(status), ReportStatuses.IsClosed(status));
        }
    }

    // Надписите с бройки също са част от българския речник на приложението:
    // след „1“ същественото е в единствено число.
    [Theory]
    [InlineData(0, "потвърждения")]
    [InlineData(1, "потвърждение")]
    [InlineData(2, "потвърждения")]
    [InlineData(10, "потвърждения")]
    public void Plural_UsesSingularOnlyAfterOne(int count, string expected)
    {
        Assert.Equal(
            expected,
            BulgarianPlural.Of(count, "потвърждение", "потвърждения"));
    }

    [Fact]
    public void Plural_DistinguishesCitizenFromCitizens()
    {
        Assert.Equal("гражданин", BulgarianPlural.Of(1, "гражданин", "граждани"));
        Assert.Equal("граждани", BulgarianPlural.Of(4, "гражданин", "граждани"));
    }
}

public class ReportGeographyTests
{
    [Theory]
    // център на града и четирите ъгъла на разрешената квадратка
    [InlineData(41.5713, 23.7361, true)]
    [InlineData(41.50, 23.65, true)]
    [InlineData(41.65, 23.80, true)]
    [InlineData(41.50, 23.80, true)]
    [InlineData(41.65, 23.65, true)]
    [InlineData(42.70, 23.32, false)]   // София
    [InlineData(0.0, 0.0, false)]       // незапълнени координати
    [InlineData(41.49, 23.73, false)]
    [InlineData(41.66, 23.73, false)]
    [InlineData(41.57, 23.64, false)]
    [InlineData(41.57, 23.81, false)]
    public void IsWithinTown_ChecksTheBoundingBox(double? latitude, double? longitude, bool expected)
    {
        Assert.Equal(expected, ReportGeography.IsWithinTown(latitude, longitude));
    }

    [Fact]
    public void IsWithinTown_RejectsHalfMissingCoordinates()
    {
        Assert.False(ReportGeography.IsWithinTown(41.5713, null));
        Assert.False(ReportGeography.IsWithinTown(null, 23.7361));
    }

    [Fact]
    public void IsWithinTown_TreatsBothMissingAsUnset()
    {
        // Няма отметнато място — решението за грешка го взима формата,
        // което тук се описва, за да не се промени незабелязано.
        Assert.True(ReportGeography.IsWithinTown(null, null));
    }

    [Fact]
    public void BoundingBox_ContainsTheTownCentre()
    {
        Assert.True(ReportGeography.MinLatitude < 41.5713);
        Assert.True(ReportGeography.MaxLatitude > 41.5713);
        Assert.True(ReportGeography.MinLongitude < 23.7361);
        Assert.True(ReportGeography.MaxLongitude > 23.7361);
    }
}

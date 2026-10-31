using CivicEyeGD.Services;

namespace CivicEyeGD.Tests;

/// <summary>
/// Формулата на приоритета е P = 20*S + 3*min(C,10) + A, нормализирана
/// към 0..100. Тези тестове заключват всяка нейна част: запасния
/// вариант при липса на оценки, ограниченията на потвържденията и
/// административната корекция, закръглянето и границите на категориите.
/// </summary>
public class ReportPriorityServiceTests
{
    private readonly ReportPriorityService _service = new();

    [Theory]
    // средна сериозност, потвърждения, админ корекция, очакван резултат
    [InlineData(0.0, 0, 0, 14.3)]   // няма оценки -> се приема 1
    [InlineData(-3.0, 0, 0, 14.3)]  // отрицателна стойност не разваля формулата
    [InlineData(1.0, 0, 0, 14.3)]   // 20 / 140
    [InlineData(3.0, 1, 0, 45.0)]   // 63 / 140
    [InlineData(4.0, 0, 0, 57.1)]   // 80 / 140 — стойността от живия сайт
    [InlineData(2.0, 3, 4, 37.9)]   // 53 / 140
    [InlineData(5.0, 10, 10, 100.0)]// максимумът на формулата
    public void Calculate_MatchesFormula(double severity, int confirmations, int adjustment, double expected)
    {
        var actual = _service.Calculate(severity, confirmations, adjustment);

        Assert.Equal(expected, actual, 1);
    }

    [Fact]
    public void Calculate_WithoutRatings_FallsBackToLowestSeverity()
    {
        Assert.Equal(
            _service.Calculate(1.0, 0, 0),
            _service.Calculate(0.0, 0, 0));
    }

    [Fact]
    public void Calculate_CapsConfirmationsAtTen()
    {
        var atLimit = _service.Calculate(1.0, 10, 0);
        var flooded = _service.Calculate(1.0, 500, 0);

        Assert.Equal(atLimit, flooded);
    }

    [Fact]
    public void Calculate_CapsAdminAdjustmentAtTen()
    {
        Assert.Equal(_service.Calculate(1.0, 0, 10), _service.Calculate(1.0, 0, 999));
    }

    [Fact]
    public void Calculate_IgnoresNegativeAdminAdjustment()
    {
        Assert.Equal(_service.Calculate(2.0, 0, 0), _service.Calculate(2.0, 0, -50));
    }

    [Fact]
    public void Calculate_IsMonotonicInSeverity()
    {
        var scores = Enumerable.Range(1, 5)
            .Select(severity => _service.Calculate(severity, 2, 1))
            .ToList();

        Assert.True(scores.SequenceEqual(scores.OrderBy(score => score)));
        Assert.True(scores[4] > scores[0]);
    }

    [Theory]
    [InlineData(5.0, 10, 10)]
    [InlineData(1.0, 0, 0)]
    [InlineData(0.0, 0, 0)]
    [InlineData(500.0, 500, 500)]
    [InlineData(-1.0, -1, -1)]
    public void Calculate_StaysWithinPercentRange(double severity, int confirmations, int adjustment)
    {
        var score = _service.Calculate(severity, confirmations, adjustment);

        Assert.InRange(score, 0.0, 100.0);
    }

    [Theory]
    [InlineData(0.0, 0, 0)]
    [InlineData(3.0, 1, 0)]
    [InlineData(4.0, 7, 3)]
    [InlineData(5.0, 500, 999)]
    [InlineData(-2.0, -5, -7)]
    public void Breakdown_SumsToTheScoreThePageShows(double severity, int confirmations, int adjustment)
    {
        var parts = _service.Breakdown(severity, confirmations, adjustment);
        var raw = parts.SeverityPoints + parts.ConfirmationPoints + parts.AdminPoints;

        // Разбивката е единственият източник на числото: ако двете се
        // разминат, гражданинът вижда обяснение, което не е сметката.
        Assert.Equal(_service.Calculate(severity, confirmations, adjustment), parts.Score, 1);
        Assert.Equal(Math.Round(raw / 140.0 * 100.0, 1), parts.Score, 1);
    }

    [Fact]
    public void Breakdown_ReportsTheCappedValues()
    {
        var parts = _service.Breakdown(6.0, 500, 999);

        Assert.Equal(5.0, parts.UsedSeverity);
        Assert.Equal(10, parts.CappedConfirmations);
        Assert.Equal(10, parts.CappedAdjustment);
        Assert.Equal(100.0, parts.Score, 1);
    }

    [Theory]
    [InlineData(100.0, "Висок приоритет")]
    [InlineData(70.0, "Висок приоритет")]
    [InlineData(69.9, "Среден приоритет")]
    [InlineData(40.0, "Среден приоритет")]
    [InlineData(39.9, "Нисък приоритет")]
    [InlineData(0.0, "Нисък приоритет")]
    public void GetPriorityLabel_UsesDocumentedThresholds(double score, string expected)
    {
        Assert.Equal(expected, _service.GetPriorityLabel(score));
    }
}

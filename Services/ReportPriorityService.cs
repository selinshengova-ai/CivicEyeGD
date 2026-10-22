namespace CivicEyeGD.Services;

// Изчислява приоритетен резултат за сигнал.
// Формулата е опростена версия на предложената в концепцията:
//   P = 20 * S + 3 * min(C, 10) + A
// където:
//   S = средна оценка за сериозност (1..5)
//   C = брой уникални потвърждения
//   A = административна корекция (0..10)
//
// Резултатът е нормализиран до 0..100 за по-лесно показване.
public sealed class ReportPriorityService
{
    // Частите на формулата, показани на гражданина, за да разбере
    // защо един сигнал е по-спешен от друг.
    public sealed record PriorityBreakdown(
        double UsedSeverity,
        int CappedConfirmations,
        int CappedAdjustment,
        double SeverityPoints,
        double ConfirmationPoints,
        double AdminPoints,
        double Score);

    public PriorityBreakdown Breakdown(
        double averageSeverity,
        int confirmationCount,
        int adminAdjustment)
    {
        var severity = NormalizeSeverity(averageSeverity);
        var cappedConfirmations = CapConfirmations(confirmationCount);
        var adjustment = CapAdjustment(adminAdjustment);

        var severityPoints = 20.0 * severity;
        var confirmationPoints = 3.0 * cappedConfirmations;

        return new PriorityBreakdown(
            severity,
            cappedConfirmations,
            adjustment,
            severityPoints,
            confirmationPoints,
            adjustment,
            Normalize(severityPoints + confirmationPoints + adjustment));
    }

    public double Calculate(
        double averageSeverity,
        int confirmationCount,
        int adminAdjustment) =>
        Breakdown(averageSeverity, confirmationCount, adminAdjustment).Score;

    // Връща текстова категория за приоритета.
    public string GetPriorityLabel(double priorityScore)
    {
        return priorityScore switch
        {
            >= 70 => "Висок приоритет",
            >= 40 => "Среден приоритет",
            _     => "Нисък приоритет"
        };
    }

    private static double NormalizeSeverity(double averageSeverity)
    {
        // Ако няма независими оценки, приемаме средна 1 (най-ниска).
        // Оценките са по скала 1..5, затова и горната граница се
        // прилага — тя е условието резултатът да остане в 0..100.
        return Math.Clamp(averageSeverity > 0 ? averageSeverity : 1.0, 1.0, 5.0);
    }

    private static int CapConfirmations(int confirmationCount) =>
        // Ограничаваме потвържденията до 10, за да не „удавят"
        // сериозността на проблема.
        Math.Min(confirmationCount, 10);

    private static int CapAdjustment(int adminAdjustment) =>
        Math.Clamp(adminAdjustment, 0, 10);

    private static double Normalize(double raw)
    {
        // Суровият максимум е 20*5 + 3*10 + 10 = 140.
        return Math.Round(raw / 140.0 * 100.0, 1);
    }
}
using CivicEyeGD.Data;

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

    // Всички числа, от които се сглобява блокът „Как се получава
    // приоритетът“. Носи и заключващия ред — точния резултат и
    // словесната му категория — защото и гражданинът, и служителят
    // трябва да видят едно и също обяснение, а не две близки.
    public sealed record PriorityPanel(
        PriorityBreakdown Parts,
        double Score,
        string Label,
        bool Closed);

    // Толкова добавя една точка корекция към приоритета: десетте точки
    // са част от знаменателя 140, затова и тежестта им е по-малка от
    // числото, което служителят въвежда.
    public const double AdjustmentWeightPerPoint = 100.0 / 140.0;

    // Прагът, от който сигналът се брои за спешен. Правилото е записано
    // веднъж тук, защото картата, списъкът и таблото описват една и съща
    // дума — „чака спешно решение“ — и тя не бива да значи три неща.
    public const double UrgentScoreFrom = 70.0;

    // Приключените не са спешни, колкото и да е висок приоритетът им:
    // те са свършен факт, а не работа, която чака.
    public bool IsUrgent(
        double averageSeverity,
        int confirmationCount,
        int adminAdjustment,
        string status) =>
        !ReportStatuses.IsClosed(status) &&
        Calculate(averageSeverity, confirmationCount, adminAdjustment) >= UrgentScoreFrom;

    // Същото правило върху готовия запис, за да не преписва всяка
    // страница трите числа от формулата.
    public bool IsUrgent(Report report) =>
        IsUrgent(
            report.IndependentAverageSeverity,
            report.IndependentConfirmationCount,
            report.AdminPriorityAdjustment,
            report.Status);

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

    // Защо нов сигнал започва с нисък приоритет, и защо това е
    // отговорът, а не дефект: приоритетът мери обществена тежест, не
    // яда на подателя. Влезе ли неговата собствена оценка в сметката,
    // всеки би изкарал сигнала си „висок“ с едно щракване — оценки от по
    // пет, дадени от един човек или от създадени и купени акаунти, — и подредбата, от
    // която общината чете реда на работа, би спряла да значи каквото и
    // да е. Затова Report.IndependentRatings изключва подателя и от
    // средната сериозност, и от потвържденията; числото надигат само
    // гласовете на други и именуваната корекция на служителя.
    private static double NormalizeSeverity(double averageSeverity)
    {
        // Ако няма независими оценки, приемаме средна 1 (най-ниска).
        // Оценките са по скала 1..5, затова и горната граница се
        // прилага — тя е условието резултатът да остане в 0..100.
        return Math.Clamp(averageSeverity > 0 ? averageSeverity : 1.0, 1.0, 5.0);
    }

    private static int CapConfirmations(int confirmationCount) =>
        // Ограничаваме потвържденията до 10, за да не „удавят“
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
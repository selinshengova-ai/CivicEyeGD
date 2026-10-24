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
    public double Calculate(
        double averageSeverity,
        int confirmationCount,
        int adminAdjustment)
    {
        // Ако няма оценки, приемаме средна 1 (най-ниска).
        var severity = averageSeverity > 0 ? averageSeverity : 1.0;

        // Ограничаваме потвържденията до 10, за да не „удавят“
        // сериозността на проблема.
        var cappedConfirmations = Math.Min(confirmationCount, 10);

        // Ограничаваме административната корекция до 0..10.
        var adjustment = Math.Clamp(adminAdjustment, 0, 10);

        // Суров резултат: максимум 20*5 + 3*10 + 10 = 140.
        var raw = 20.0 * severity
                + 3.0 * cappedConfirmations
                + adjustment;

        // Нормализираме до 0..100.
        return Math.Round(raw / 140.0 * 100.0, 1);
    }

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
}
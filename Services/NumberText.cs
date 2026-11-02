using System.Globalization;

namespace CivicEyeGD.Services;

// Приоритетът и сериозността се показват с десетична точка навсякъде:
// на екрана на служителя, на страницата на сигнала и в документите,
// където числата са цитирани. Без това българската култура на машината
// би изписала „62,9“, а живият сайт „62.9“, и човекът срещу екрана би
// помислил, че двете числа са различни. Форматът е неизменен и за
// скриптовете, които разбират само точка.
public static class NumberText
{
    // Самото число от 0 до 100 — с една десетична цифра, защото
    // следващата не променя решението, а само претрупва реда.
    public static string Score(double value) =>
        value.ToString("0.0", CultureInfo.InvariantCulture);

    // Точките в разбивката: те са цели или наполовина, затова
    // лишващите нули се изпускат.
    public static string Points(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    // Средната сериозност се чете с две цифри: при четири гласа
    // втората цифра е тази, която разлива един сигнал от друг.
    public static string Severity(double value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    // Пет знака за географските координати са около метър, а точката
    // е задължителна: ширината и дължината се препрочитат от карти и
    // адресни услуги, които не познават българската запетая.
    public static string Coordinate(double value) =>
        value.ToString("0.00000", CultureInfo.InvariantCulture);
}

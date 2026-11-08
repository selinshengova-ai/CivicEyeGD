using CivicEyeGD.Data;

namespace CivicEyeGD.Services;

/// <summary>
/// Времето, в което общината реагира. Една и съща намеса се описва
/// от три числа: колко дни сигналът е чакал първата реакция, колко
/// дълго чака все още и какъв е средният резултат за периода.
/// </summary>
// Броенето е в цели дни, а не в часове: дневникът го попълва човек с
// мишка, не сензор, и „3 дни“ е точността, която служителите могат да
// защитят. Сметките са отделени от рисуването, защото таблото, работната
// книга и PDF-ът питат за едни и същи числа — ако всяко си ги смята
// само, двете места ще кажат различни неща за един и същи сигнал.
public static class ReportResponse
{
    // Прагът за „бавно“ е показателен, а не служебен срок: общинските
    // отговори се движат по месец, и всичко над трийсет дни е излязло
    // из обичайното. Кодът го ползва само за багра по екрана и на
    // хартия — по него нищо не се обявява за нарушение.
    public const int SlowWaitDays = 30;

    // Първата намеса е най-ранният ред от дневника. Не последният:
    // въпросът на гражданина е „Показа ли се някой на място“, а той се
    // отговаря от първия запис, дори след него сигналът да е минал през
    // още няколко статуса.
    public static DateTime? FirstActionAt(Report report) =>
        report.StatusHistories.Count == 0
            ? null
            : report.StatusHistories.Min(step => step.ChangedAt);

    // Колко дни отнема първата намеса. Липсва там, където тя все още
    // не е направена: нула би изглеждала като реакция в деня на
    // подаването, а това е обратното на истината.
    public static int? ResponseDays(Report report) =>
        FirstActionAt(report) is { } action ? DaysBetween(report.CreatedAt, action) : null;

    // Колко дни сигналът е на опашката в даден момент. Моментът идва
    // отвън, а не се чете от часовника на машината: цялата страница
    // трябва да брои от едно и също време, иначе две картички в един
    // и същи списък биха могли да се различат с една секунда.
    public static int WaitingDays(Report report, DateTime nowUtc) =>
        DaysBetween(report.CreatedAt, nowUtc);

    // Средното време за реакция се вади само от сигналите, по които
    // има намеса. Ако в средното влязат и тези, които още чакат,
    // дните им се броят като реакция, която не е направена: числото
    // расте, но вече не описва реално забавяне. Празен списък дава
    // нищо, а не нула.
    // Списъкът с липси е общият вход, защото и работната книга, и
    // базата дават едни и същи дни — едното ги носи в записа на
    // сигнала, другото вече ги е преброило.
    public static double? Average(IEnumerable<int?> days)
    {
        var measured = days.Where(value => value.HasValue).ToList();

        return measured.Count == 0 ? null : Math.Round(measured.Average(value => value!.Value), 1);
    }

    // Най-бавната реакция е същият списък, но с максимум: средното
    // число скрива едно забравено съобщение между десет бързи.
    public static int? Longest(IEnumerable<int?> days)
    {
        var measured = days.Where(value => value.HasValue).ToList();

        return measured.Count == 0 ? null : measured.Max();
    }

    public static double? AverageResponseDays(IEnumerable<Report> reports) =>
        Average(reports.Select(ResponseDays));

    public static int? LongestResponseDays(IEnumerable<Report> reports) =>
        Longest(reports.Select(ResponseDays));

    // Сигналите, за които дневникът е празен. Методът очаква вече
    // подбран активен списък: приключен сигнал без ред не чака нищо и
    // не трябва да влиза в бройката.
    public static int AwaitingFirstActionCount(IEnumerable<Report> reports) =>
        reports.Count(report => FirstActionAt(report) is null);

    // Дните се изписват с правилния брой на думата: „1 ден“ и „2 дни“
    // не се образуват по правило, което програма да познае, затова
    // формите идват от BulgarianPlural.
    public static string Days(int? days) =>
        days is null ? "—" : $"{days} {BulgarianPlural.Of(days.Value, "ден", "дни")}";

    // Закръгляването е на едно място, тук: средното число живее с
    // една десета, а по картите се чете като цели дни.
    public static string Days(double? days) =>
        days is null ? "—" : Days((int)Math.Round(days.Value));

    // Пълният ден, а не закръгленият: сигнал, подаден снощи, е
    // изчакал един ден, а не нула, и „0 дни“ би звучало като
    // „нищо не закъснява“. Обратният ред — намеса преди подаването —
    // не може да стане от нормален дневник, но нулата го спира,
    // преди да е отровила средното число.
    // Двете моменти трябва да са от един и същи часовник (и двата в
    // UTC или и двата местни): тук се мери разликата, а не денят.
    public static int DaysBetween(DateTime from, DateTime to)
    {
        var elapsed = Math.Floor((to - from).TotalDays);

        return elapsed < 0 ? 0 : (int)elapsed;
    }
}

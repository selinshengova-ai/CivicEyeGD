
using CivicEyeGD.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CivicEyeGD.Pages;

// Витрината на платформата се гледа и без акаунт: журито и гражданинът
// трябва да видят сигнала, преди да решат дали да регистрират.
// Отметката е по класа, защото само там Razor Pages я чете; без нея
// глобалната FallbackPolicy би поискала вход и за началната страница.
[AllowAnonymous]
public class IndexModel : PageModel
{
    // Само базата данни: витрината е огледало на сигналите и не трябва
    // да знае нищо за роля, вход или записи на снимки.
    private readonly ApplicationDbContext _context;

    public IndexModel(ApplicationDbContext context)
    {
        _context = context;
    }

    // Трите числа и списъкът се четат от изгледа, но не се променят от
    // него (private set): никоя стойност не може да се презапише след
    // като е изчислена от базата.
    public List<Report> Reports { get; private set; } = new();

    public int TotalReports { get; private set; }

    public int OpenReports { get; private set; }

    public int ResolvedReports { get; private set; }

    // Решените през текущия месец: числото показва темпото, а не труда
    // за последните десетилетия. Броим по дневника на статусите, защото
    // самият сигнал пази само сегашното си състояние, а не кога е
    // дотигнал до него.
    public int ResolvedThisMonth { get; private set; }

    public async Task OnGetAsync()
    {
        // Един и същ източник за четирите заявки: редовете не се
        // следят за промени, защото тук нищо не се записва.
        var reports = _context.Reports.AsNoTracking();

        // Общ брой сигнали
        // CountAsync остава в SQLite и връща само число; зареждането
        // на таблица в паметта и преброяване с LINQ над обекти би
        // дърпало заглавия, описания и адреси, които тук не трябват.
        TotalReports = await reports.CountAsync();

        // Брой решени сигнали
        // Статусите се сравняват с константите от ReportVocabulary, а
        // не с написан текст: промени ли се думата „Решен“, сравнение
        // по стар текст спира да брои и числото мълчаливо става нула.
        ResolvedReports = await reports.CountAsync(
            report => report.Status == ReportStatuses.Resolved);

        // Отворените са тези, които все още чакат действие: отхвърлените
        // също са приключили и не бива да се броят като нерешени.
        // Така един сигнал влиза най-много в едно от двете числа и
        // числата не си противоречат.
        OpenReports = await reports.CountAsync(
            report => report.Status != ReportStatuses.Resolved
                && report.Status != ReportStatuses.Rejected);

        // Решените този месец
        // Границата е в UTC, защото и дневникът се записва в UTC: с
        // местен час първият ден на месеца би започвал в различен час на
        // всяка машина, която гледа страницата.
        var monthStart = new DateTime(
            DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // Бройката е по записания в дневника преход, а не по датата на
        // подаване: сигнал, чакал три месеца и решен сега, е успех на
        // текущия месец. Условието за сегашния статус пази честността —
        // ако служителят е върнал сигнала обратно, той спира да се брои.
        ResolvedThisMonth = await reports.CountAsync(report =>
            report.Status == ReportStatuses.Resolved
            && report.StatusHistories.Any(history =>
                history.NewStatus == ReportStatuses.Resolved
                && history.ChangedAt >= monthStart));

        // Последните три сигнала
        // Три, не всички: това е витрината, а пълният архив е на
        // /Reports. Ограничението е в самата заявка, за да не сваля
        // целия архив и той да се реже чак в паметта.
        Reports = await reports
            .OrderByDescending(report => report.CreatedAt)
            .Take(3)
            .ToListAsync();
    }
}

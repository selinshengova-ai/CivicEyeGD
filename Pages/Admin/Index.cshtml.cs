using CivicEyeGD.Data;
using CivicEyeGD.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CivicEyeGD.Pages.Admin;

// Таблото е преглед: подрежда сигналите по приоритет, брои групите и
// дава експорт. Намесата — статус, корекция, бележка и изтриване —
// се върши единствено на страницата на сигнала (Pages/Details.cshtml.cs
// с handler-ите Office и Delete), защото там служителят вижда снимката,
// мястото и хода на сигнала, а не ги търси в претрупана картичка.
[Authorize(Roles = "Administrator")]
public class IndexModel : PageModel
{
    // Прагът съвпада с етикета „Висок приоритет“ в ReportPriorityService,
    // за да не вижда служителят две различни числа за едно и също нещо.
    public const double HighPriorityFrom = ReportPriorityService.UrgentScoreFrom;

    // Стойност на филтъра, която не е статус, а група статуси. Държа се тук,
    // а не като низ в .cshtml, за да не се разминава с URL-а от картите.
    public const string ClosedFilter = "Приключени";

    // Толкова снимки се побират в картичката, без редът да се издължи;
    // останалите се виждат на страницата на сигнала, където служат.
    public const int PreviewImages = 3;

    private readonly ApplicationDbContext _context;
    private readonly ReportPriorityService _priorityService;
    private readonly ReportExportService _exportService;
    private readonly ReportPdfService _pdfService;

    public IndexModel(
        ApplicationDbContext context,
        ReportPriorityService priorityService,
        ReportExportService exportService,
        ReportPdfService pdfService)
    {
        _context = context;
        _priorityService = priorityService;
        _exportService = exportService;
        _pdfService = pdfService;
    }

    public List<Report> Reports { get; private set; } = new();

    public int TotalActiveCount { get; private set; }
    public int NewCount { get; private set; }
    public int InProgressCount { get; private set; }

    // Решените и отхвърлените са един филтър („Приключени“), затова
    // и бройката е една: различните числа в картата и в списъка
    // звучат като грешка в данните.
    public int ClosedCount { get; private set; }

    public int HighPriorityCount { get; private set; }

    // Трите числа на реакцията се смятат винаги върху всички сигнали,
    // като четирите карти над тях: служителят сравнява опашката с
    // миналото, а не с избор на филтър.
    public double? AverageResponseDays { get; private set; }
    public int? LongestResponseDays { get; private set; }

    // Бройката на сигналите, по които има какво да се усреднява: без
    // нея средното число вися само, а една реакция не е резултат.
    public int MeasuredResponseCount { get; private set; }

    // Колко активни сигнала все още нямат нито една намеса и колко
    // дни е изчакал най-старият от тях.
    public int AwaitingFirstActionCount { get; private set; }
    public int? OldestWaitDays { get; private set; }

    // Едно и също време за цялата заявка: ако всяка картичка прочете
    // часовника сама, две числа, събрани за секунда, биха могли да се
    // различат, а служителът ги сравнява с поглед.
    private DateTime _nowUtc;

    public string FilterStatus { get; private set; } = string.Empty;

    public string ListTitle { get; private set; } = "Активни сигнали";

    // Кратка реплика след изтриването. Идва през URL-а, защото след
    // redirect няма къде другаде да се съхранява между заявките: самият
    // сигнал вече го няма, а с него и страницата, която би го показа.
    public string? Notice { get; private set; }

    public async Task OnGetAsync(string? filterStatus, int? deletedId = null, string? notice = null)
    {
        Notice = notice == "deleted" && deletedId is not null
            ? $"Сигнал #{deletedId} е изтрит заедно със снимките, оценките и дневника."
            : null;

        _nowUtc = DateTime.UtcNow;

        LoadFilters(filterStatus);

        var all = await ReadReportsAsync();

        CountStatuses(all);
        MeasureResponse(all);
        Reports = SortByPriority(ApplyFilter(all));
    }

    // Експортът минава през същите заявки и същия филтър като таблото,
    // така файлът никога не се различава от това, което служителят вижда.
    // Основният формат за защита на сигнала пред общината: документ с
    // диаграми, а не таблица, която никой не отваря.
    public async Task<IActionResult> OnGetExportPdfAsync(string? filterStatus)
    {
        var rows = await ExportRowsAsync(filterStatus);
        var fileName = $"civiceye-gd-otchet-{DateTime.UtcNow:yyyyMMdd-HHmm}.pdf";

        return File(
            _pdfService.Build(rows, ListTitle, DateTime.UtcNow),
            "application/pdf",
            fileName);
    }

    public async Task<IActionResult> OnGetExportAsync(string? filterStatus)
    {
        var rows = await ExportRowsAsync(filterStatus);
        var fileName = $"civiceye-gd-signali-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv";

        return File(_exportService.BuildCsv(rows), "text/csv; charset=utf-8", fileName);
    }

    public async Task<IActionResult> OnGetExportXlsxAsync(string? filterStatus)
    {
        var rows = await ExportRowsAsync(filterStatus);
        var fileName = $"civiceye-gd-signali-{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx";

        return File(
            _exportService.BuildWorkbook(rows),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    private async Task<List<ReportExportRow>> ExportRowsAsync(string? filterStatus)
    {
        LoadFilters(filterStatus);

        var all = await ReadReportsAsync();

        return ExportRows(SortByPriority(ApplyFilter(all)));
    }

    private List<ReportExportRow> ExportRows(List<Report> reports)
    {
        var rows = new List<ReportExportRow>(reports.Count);

        foreach (var report in reports)
        {
            var priority = PriorityOf(report);

            rows.Add(new ReportExportRow(
                report.Id,
                report.Title,
                report.Description,
                report.Category,
                report.Status,
                report.Address,
                report.CreatedAt.ToSofia(),
                AverageSeverity(report),
                report.IndependentConfirmationCount,
                priority,
                _priorityService.GetPriorityLabel(priority),
                ReportResponse.ResponseDays(report)));
        }

        return rows;
    }

    /// <summary>Точният приоритет на сигнала по формулата от услугата.</summary>
    public double PriorityOf(Report report) =>
        _priorityService.Calculate(
            report.IndependentAverageSeverity,
            report.IndependentConfirmationCount,
            report.AdminPriorityAdjustment);

    // Миниатюрите за картичката: три на брой, по номер на записа.
    // Реда се фиксира тук, защото без подредба при всяко презареждане
    // снимките си сменят местата и служителят не може да запомни коя кое
    // показва; останалите се гледат на страницата на сигнала.
    public IReadOnlyList<ReportImage> ThumbnailsOf(Report report) =>
        report.Images
            .OrderBy(image => image.Id)
            .Take(PreviewImages)
            .ToList();

    // Последната промяна се чете от самия дневник, а не от второ число:
    // две места за едно и също нещо с времето се разминават. Карточката
    // дава само датата — стъпките си остават на страницата на сигнала.
    public DateTime? LastChangeOf(Report report) =>
        report.StatusHistories.Count == 0
            ? null
            : report.StatusHistories.Max(step => step.ChangedAt);

    public string PriorityLabelOf(Report report) =>
        _priorityService.GetPriorityLabel(PriorityOf(report));

    public bool IsHighPriority(Report report) => _priorityService.IsUrgent(report);

    // Едно число за времето на всеки ред и надпис, което казва какво
    // е то: когато общината е реагирала — колко дни отне първата
    // намеса; когато сигналът все още чака — колко дни е изчакал
    // дотогава. Приключен сигнал без дневник не показва нищо, защото
    // той не чака и не е реагиран.
    public WaitingLine? ResponseOf(Report report)
    {
        if (ReportResponse.ResponseDays(report) is { } responded)
        {
            return new WaitingLine("Реакция", ReportResponse.Days(responded));
        }

        return ReportStatuses.IsClosed(report.Status)
            ? null
            : new WaitingLine("Чака", ReportResponse.Days(ReportResponse.WaitingDays(report, _nowUtc)));
    }

    public sealed record WaitingLine(string Label, string Text);

    private void LoadFilters(string? filterStatus)
    {
        FilterStatus = ReportStatuses.IsValid(filterStatus) || filterStatus == ClosedFilter
            ? filterStatus!
            : string.Empty;

        if (FilterStatus.Length > 0)
        {
            ListTitle = FilterStatus == ClosedFilter || ReportStatuses.IsClosed(FilterStatus)
                ? "Приключени сигнали"
                : "Активни сигнали";
        }
    }

    private async Task<List<Report>> ReadReportsAsync() =>
        await _context.Reports
            .Include(r => r.Images)
            .Include(r => r.Confirmations)
            .Include(r => r.SeverityRatings)
            // Дневникът е нужен на разгъваемия преглед. Без Include
            // колекцията остава празна и картичката би твърдяла, че
            // промени не е имало, макар те да са в базата.
            .Include(r => r.StatusHistories)
            .AsNoTracking()
            .ToListAsync();

    // Филтърът за приключени сигнали търси в пълния списък: активните
    // по дефиниция не съдържат тези два статуса.
    private List<Report> ApplyFilter(List<Report> all)
    {
        IEnumerable<Report> pool = FilterStatus switch
        {
            ClosedFilter => all.Where(r => ReportStatuses.IsClosed(r.Status)),
            var status when status.Length > 0 => all.Where(r => r.Status == status),
            _ => all.Where(r => !ReportStatuses.IsClosed(r.Status))
        };

        return pool.ToList();
    }

    private void CountStatuses(List<Report> all)
    {
        var active = all.Where(r => !ReportStatuses.IsClosed(r.Status)).ToList();

        TotalActiveCount = active.Count;
        NewCount = active.Count(r => r.Status == ReportStatuses.New);
        InProgressCount = active.Count(r => r.Status == ReportStatuses.InProgress);
        ClosedCount = all.Count(r => ReportStatuses.IsClosed(r.Status));
        // Същият метод, който оцветява картичките: иначе числото горе
        // и червените долу могат да се разминат за един и същ сигнал.
        HighPriorityCount = active.Count(IsHighPriority);
    }

    // Реакцията се мери по всички сигнали, а изчакването — само по
    // активните: приключените вече нямат на какво да чакат, и ако
    // влязат в бройката, таблото би търсело вина там, където работата
    // е свършена.
    private void MeasureResponse(List<Report> all)
    {
        AverageResponseDays = ReportResponse.AverageResponseDays(all);
        LongestResponseDays = ReportResponse.LongestResponseDays(all);
        MeasuredResponseCount = all.Count(report => ReportResponse.ResponseDays(report) is not null);

        var waiting = all
            .Where(report => !ReportStatuses.IsClosed(report.Status))
            .Where(report => ReportResponse.FirstActionAt(report) is null)
            .ToList();

        AwaitingFirstActionCount = waiting.Count;
        OldestWaitDays = waiting.Count == 0
            ? null
            : waiting.Max(report => ReportResponse.WaitingDays(report, _nowUtc));
    }

    private List<Report> SortByPriority(List<Report> reports) =>
        reports.OrderByDescending(PriorityOf).ToList();

    // Правилото е в Report: оценките на подателя не теглят йерархията.
    private static double AverageSeverity(Report report) =>
        report.IndependentAverageSeverity;
}

using CivicEyeGD.Data;
using CivicEyeGD.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CivicEyeGD.Pages.Admin;

[Authorize(Roles = "Administrator")]
public class IndexModel : PageModel
{
    // Прагът съвпада с етикета „Висок приоритет" в ReportPriorityService,
    // за да не вижда служителят две различни числа за едно и също нещо.
    public const double HighPriorityFrom = 70;

    // Стойност на филтъра, която не е статус, а група статуси. Държа се тук,
    // а не като низ в .cshtml, за да не се разминава с URL-а от картите.
    public const string ClosedFilter = "Приключени";

    // Бележката се показва публично в историята на сигнала, затова има
    // таван: без него един постър може да напълни страницата.
    public const int MaxCommentLength = 500;

    private readonly ApplicationDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ReportPriorityService _priorityService;
    private readonly ReportExportService _exportService;
    private readonly ReportPdfService _pdfService;

    public IndexModel(
        ApplicationDbContext context,
        UserManager<IdentityUser> userManager,
        ReportPriorityService priorityService,
        ReportExportService exportService,
        ReportPdfService pdfService)
    {
        _context = context;
        _userManager = userManager;
        _priorityService = priorityService;
        _exportService = exportService;
        _pdfService = pdfService;
    }

    public List<Report> Reports { get; private set; } = new();

    public int TotalActiveCount { get; private set; }
    public int NewCount { get; private set; }
    public int InProgressCount { get; private set; }

    // Решените и отхвърлените са един филтър („Приключени"), затова
    // и бройката е една: различните числа в картата и в списъка
    // звучат като грешка в данните.
    public int ClosedCount { get; private set; }

    public int HighPriorityCount { get; private set; }

    public string FilterStatus { get; private set; } = string.Empty;

    public string ListTitle { get; private set; } = "Активни сигнали";

    // Кратка реплика след записването. Идва през URL-а, защото след
    // redirect няма къде другаде да се съхранява между заявките.
    public string? Notice { get; private set; }

    public async Task OnGetAsync(string? filterStatus, int? savedId = null, string? notice = null)
    {
        Notice = notice switch
        {
            "saved" when savedId is not null => $"Промените по сигнал #{savedId} са записани.",
            "unchanged" when savedId is not null => $"Сигнал #{savedId} е същият – нищо не се промени.",
            _ => null
        };

        LoadFilters(filterStatus);

        var all = await ReadReportsAsync();

        CountStatuses(all);
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
                _priorityService.GetPriorityLabel(priority)));
        }

        return rows;
    }

    // Една форма и един бутон за сигнал: статусът, бележката и корекцията
    // се решават в един момент, а два отделни бутона караха служителя да
    // гадае кой от двата трябва да натисне.
    public async Task<IActionResult> OnPostUpdateAsync(
        int id,
        string newStatus,
        string? comment,
        int adjustment,
        string? filterStatus)
    {
        if (!ReportStatuses.IsValid(newStatus))
        {
            return BadRequest("Непознат статус.");
        }

        // Административната намеса е число 0-10 в края на сметката, а не
        // скрито препращане: без ограничение един клик би сделал който и
        // да е сигнал „висок приоритет".
        if (adjustment < 0 || adjustment > 10)
        {
            return BadRequest("Корекцията на приоритета е между 0 и 10.");
        }

        var report = await _context.Reports.FindAsync(id);
        if (report is null)
        {
            return NotFound();
        }

        var changed = false;

        if (report.Status != newStatus)
        {
            var oldStatus = report.Status;
            report.Status = newStatus;

            _context.StatusHistories.Add(new StatusHistory
            {
                ReportId = id,
                OldStatus = oldStatus,
                NewStatus = newStatus,
                ChangedByUserId = _userManager.GetUserId(User),
                ChangedAt = DateTime.UtcNow,
                Comment = NormalizeComment(comment)
            });

            changed = true;
        }

        if (report.AdminPriorityAdjustment != adjustment)
        {
            report.AdminPriorityAdjustment = adjustment;
            changed = true;
        }

        if (changed)
        {
            await _context.SaveChangesAsync();
        }

        return RedirectToPage(new { savedId = id, notice = changed ? "saved" : "unchanged", filterStatus });
    }

    private static string? NormalizeComment(string? comment)
    {
        var trimmed = comment?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;

        return trimmed.Length > MaxCommentLength ? trimmed[..MaxCommentLength] : trimmed;
    }

    /// <summary>Точният приоритет на сигнала по формулата от услугата.</summary>
    public double PriorityOf(Report report) =>
        _priorityService.Calculate(
            report.IndependentAverageSeverity,
            report.IndependentConfirmationCount,
            report.AdminPriorityAdjustment);

    public string PriorityLabelOf(Report report) =>
        _priorityService.GetPriorityLabel(PriorityOf(report));

    public bool IsHighPriority(Report report) =>
        !ReportStatuses.IsClosed(report.Status) && PriorityOf(report) >= HighPriorityFrom;

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
        HighPriorityCount = active.Count(r => PriorityOf(r) >= HighPriorityFrom);
    }

    private List<Report> SortByPriority(List<Report> reports) =>
        reports.OrderByDescending(PriorityOf).ToList();

    // Правилото е в Report: оценките на подателя не теглят йерархията.
    private static double AverageSeverity(Report report) =>
        report.IndependentAverageSeverity;
}

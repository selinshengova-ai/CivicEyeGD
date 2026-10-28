
using CivicEyeGD.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CivicEyeGD.Pages;

// Витрината на платформата се гледа и без акаунт: журито и гражданинът
// трябва да видят сигнала, преди да решат дали да регистрират.
[AllowAnonymous]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public IndexModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public List<Report> Reports { get; private set; } = new();

    public int TotalReports { get; private set; }

    public int OpenReports { get; private set; }

    public int ResolvedReports { get; private set; }

    public async Task OnGetAsync()
    {
        var reports = _context.Reports.AsNoTracking();

        // Общ брой сигнали
        TotalReports = await reports.CountAsync();

        // Брой решени сигнали
        ResolvedReports = await reports.CountAsync(
            report => report.Status == ReportStatuses.Resolved);

        // Отворените са тези, които все още чакат действие: отхвърлените
        // също са приключили и не бива да се броят като нерешени.
        OpenReports = await reports.CountAsync(
            report => report.Status != ReportStatuses.Resolved
                && report.Status != ReportStatuses.Rejected);

        // Последните три сигнала
        Reports = await reports
            .OrderByDescending(report => report.CreatedAt)
            .Take(3)
            .ToListAsync();
    }
}

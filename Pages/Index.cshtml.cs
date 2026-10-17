
using CivicEyeGD.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CivicEyeGD.Pages;

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
            report => report.Status == "Решен");

        // Брой нерешени сигнали
        OpenReports = TotalReports - ResolvedReports;

        // Последните три сигнала
        Reports = await reports
            .OrderByDescending(report => report.CreatedAt)
            .Take(3)
            .ToListAsync();
    }
}

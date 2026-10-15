
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

    public List<Report> Reports { get; set; } = new();

    public int TotalReports { get; set; }

    public int NewReports { get; set; }

    public int InProgressReports { get; set; }

    public int ResolvedReports { get; set; }

    public async Task OnGetAsync()
    {
        Reports = await _context.Reports
            .OrderByDescending(report => report.CreatedAt)
            .Take(5)
            .ToListAsync();

        TotalReports = await _context.Reports.CountAsync();

        NewReports = await _context.Reports
            .CountAsync(report => report.Status == "Подаден");

        InProgressReports = await _context.Reports
            .CountAsync(report => report.Status == "В процес");

        ResolvedReports = await _context.Reports
            .CountAsync(report => report.Status == "Решен");
    }
}

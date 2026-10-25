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
    private readonly ApplicationDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ReportPriorityService _priorityService;

    public IndexModel(
        ApplicationDbContext context,
        UserManager<IdentityUser> userManager,
        ReportPriorityService priorityService)
    {
        _context = context;
        _userManager = userManager;
        _priorityService = priorityService;
    }

    public List<Report> Reports { get; private set; } = new();

    public int TotalActiveCount { get; private set; }
    public int NewCount { get; private set; }
    public int InProgressCount { get; private set; }
    public int ResolvedCount { get; private set; }

    public string FilterStatus { get; private set; } = string.Empty;

    public async Task OnGetAsync(string? filterStatus)
    {
        FilterStatus = filterStatus ?? string.Empty;

        // Зареждаме всички активни (за статистиките)
        var allActive = await _context.Reports
            .Include(r => r.Images)
            .Include(r => r.Confirmations)
            .Include(r => r.SeverityRatings)
            .Where(r => r.Status != "Решен" && r.Status != "Отхвърлен")
            .AsNoTracking()
            .ToListAsync();

        TotalActiveCount = allActive.Count;
        NewCount = allActive.Count(r => r.Status == "Подаден");
        InProgressCount = allActive.Count(r => r.Status == "В процес");

        ResolvedCount = await _context.Reports
            .CountAsync(r => r.Status == "Решен" || r.Status == "Отхвърлен");

        // Филтриране по избран статус
        var filtered = allActive;
        if (!string.IsNullOrEmpty(FilterStatus))
        {
            filtered = allActive.Where(r => r.Status == FilterStatus).ToList();
        }

        // Подреждане по приоритет
        Reports = filtered
            .OrderByDescending(r => _priorityService.Calculate(
                r.SeverityRatings.Any() ? r.SeverityRatings.Average(sr => sr.Rating) : 0,
                r.Confirmations.Count,
                r.AdminPriorityAdjustment))
            .ToList();
    }

    public async Task<IActionResult> OnPostChangeStatusAsync(int id, string newStatus)
    {
        var report = await _context.Reports.FindAsync(id);
        if (report != null && report.Status != newStatus)
        {
            var oldStatus = report.Status;
            report.Status = newStatus;

            _context.StatusHistories.Add(new StatusHistory
            {
                ReportId = id,
                OldStatus = oldStatus,
                NewStatus = newStatus,
                ChangedByUserId = _userManager.GetUserId(User),
                ChangedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
        }
        return RedirectToPage();
    }
}
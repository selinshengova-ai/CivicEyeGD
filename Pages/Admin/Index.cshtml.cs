using CivicEyeGD.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CivicEyeGD.Pages.Admin;

[Authorize(Roles = "Administrator")]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public IndexModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public List<Report> Reports { get; private set; } = new();

    public async Task OnGetAsync()
    {
        // Зареждаме всички сигнали и техните снимки
        Reports = await _context.Reports
            .Include(r => r.Images)
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    // Метод за смяна на статуса
    public async Task<IActionResult> OnPostChangeStatusAsync(int id, string newStatus)
    {
        var report = await _context.Reports.FindAsync(id);
        if (report != null)
        {
            report.Status = newStatus;
            await _context.SaveChangesAsync();
        }
        return RedirectToPage();
    }
}
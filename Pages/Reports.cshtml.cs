using CivicEyeGD.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CivicEyeGD.Pages;

public class ReportsModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public ReportsModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public List<Report> Reports { get; private set; } = new();

    public List<string> Categories { get; private set; } = new();

    public List<string> Statuses { get; private set; } = new();

    public string Search { get; private set; } = string.Empty;

    public string Category { get; private set; } = string.Empty;

    public string Status { get; private set; } = string.Empty;

    public async Task OnGetAsync(
        string? search,
        string? category,
        string? status)
    {
        Search = search?.Trim() ?? string.Empty;
        Category = category?.Trim() ?? string.Empty;
        Status = status?.Trim() ?? string.Empty;

        Categories = await _context.Reports
            .AsNoTracking()
            .Select(r => r.Category)
            .Where(c => c != null && c != "")
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();

        Statuses = await _context.Reports
            .AsNoTracking()
            .Select(r => r.Status)
            .Where(s => s != null && s != "")
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync();

        // ВАЖНО: Include за Confirmations, за да покажем броя им в списъка
        var query = _context.Reports
            .Include(r => r.Confirmations)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(Search))
        {
            query = query.Where(r =>
                r.Title.Contains(Search) ||
                r.Description.Contains(Search) ||
                r.Address.Contains(Search));
        }

        if (!string.IsNullOrWhiteSpace(Category))
        {
            query = query.Where(r => r.Category == Category);
        }

        if (!string.IsNullOrWhiteSpace(Status))
        {
            query = query.Where(r => r.Status == Status);
        }

        Reports = await query
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }
}
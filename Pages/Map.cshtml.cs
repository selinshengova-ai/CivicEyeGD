
using System.Text.Json;
using CivicEyeGD.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CivicEyeGD.Pages;

public class MapModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public MapModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public string ReportsJson { get; private set; } = "[]";

    public int ReportsCount { get; private set; }

    public async Task OnGetAsync()
    {
        var reports = await _context.Reports
            .AsNoTracking()
            .Where(r =>
                r.Latitude.HasValue &&
                r.Longitude.HasValue &&
                r.Latitude >= -90 &&
                r.Latitude <= 90 &&
                r.Longitude >= -180 &&
                r.Longitude <= 180)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.Id,
                r.Title,
                r.Description,
                r.Category,
                r.Address,
                r.Status,
                Latitude = r.Latitude!.Value,
                Longitude = r.Longitude!.Value
            })
            .ToListAsync();

        ReportsCount = reports.Count;

        ReportsJson = JsonSerializer.Serialize(
            reports,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
    }
}

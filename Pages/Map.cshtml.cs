using System.Text.Json;
using CivicEyeGD.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CivicEyeGD.Pages;

// Публичната карта е обещанието на проекта: без вход тя не е публична.
// Тук няма Google нищо: картата е само Leaflet, за да остане лека и да
// не показва грешка на чужд сервиз пред гражданите.
[AllowAnonymous]
public class MapModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public MapModel(ApplicationDbContext context)
    {
        _context = context;
    }

    public string ReportsJson { get; private set; } = "[]";

    public int ReportsCount { get; private set; }

    public int UrgentCount { get; private set; }

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
            .Select(r => new MapReport
            {
                Id = r.Id,
                Title = r.Title,
                Description = r.Description,
                Category = r.Category,
                Address = r.Address,
                Status = r.Status,
                Latitude = r.Latitude!.Value,
                Longitude = r.Longitude!.Value
            })
            .ToListAsync();

        // Правилото е същото като в Report.IndependentAverageSeverity:
        // оценката на автора не се брои, защото той самият не е
        // независим свидетел. Съгласуваност с Details, таблото и експорта.
        var averages = await (
            from rating in _context.SeverityRatings
            join report in _context.Reports on rating.ReportId equals report.Id
            where rating.UserId != report.UserId
            group rating by rating.ReportId into group_
            select new
            {
                ReportId = group_.Key,
                Average = group_.Average(rating => rating.Rating)
            })
            .AsNoTracking()
            .ToListAsync();

        var averageByReport = averages.ToDictionary(
            average => average.ReportId,
            average => average.Average);

        foreach (var report in reports)
        {
            report.Severity = averageByReport.TryGetValue(report.Id, out var average)
                ? Math.Round(average, 1)
                : 0;
        }

        UrgentCount = reports.Count(report => report.IsUrgent);

        ReportsCount = reports.Count;

        ReportsJson = JsonSerializer.Serialize(
            reports,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
    }

    /// <summary>Редът, който картата получава като JSON.</summary>
    public sealed class MapReport
    {
        // Средна сериозност от 4 нагоре е сигнал, който чака.
        public const double UrgentSeverityFrom = 4.0;

        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Severity { get; set; }

        // Правилото е тук, а не в скрипта на картата, за да е едно и също
        // за маркерите, за брояча в заглавката и за тестовете.
        public bool IsUrgent =>
            Severity >= UrgentSeverityFrom && !ReportStatuses.IsClosed(Status);
    }
}

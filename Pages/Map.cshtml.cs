using System.Text.Json;
using CivicEyeGD.Data;
using CivicEyeGD.Services;
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

    private readonly ReportPriorityService _priorityService;

    public MapModel(ApplicationDbContext context, ReportPriorityService priorityService)
    {
        _context = context;
        _priorityService = priorityService;
    }

    public string ReportsJson { get; private set; } = "[]";

    public int ReportsCount { get; private set; }

    public int UrgentCount { get; private set; }

    // Картата показва приключените само когато някой ги поисква изрично.
    public bool ShowClosedReports { get; private set; }

    /// <summary>Приключени сигнали с координати, сега скрити от изгледа.</summary>
    public int ClosedCount { get; private set; }

    public int TotalReportCount { get; private set; }

    public IReadOnlyList<StatusCount> StatusStatistics { get; private set; } = Array.Empty<StatusCount>();

    public async Task OnGetAsync(bool showClosed = false)
    {
        ShowClosedReports = showClosed;

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
                AdminPriorityAdjustment = r.AdminPriorityAdjustment,
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

        // Потвържденията се броят по същата логика като средната
        // сериозност: гласът на подателя не е независим. Без тази
        // крачка картата би дала по-висок приоритет от таблото.
        var confirmations = await (
            from confirmation in _context.Confirmations
            join report in _context.Reports on confirmation.ReportId equals report.Id
            where confirmation.UserId != report.UserId
            group confirmation by confirmation.ReportId into group_
            select new
            {
                ReportId = group_.Key,
                Count = group_.Count()
            })
            .AsNoTracking()
            .ToListAsync();

        var confirmationByReport = confirmations.ToDictionary(
            confirmation => confirmation.ReportId,
            confirmation => confirmation.Count);

        foreach (var report in reports)
        {
            var independentAverage = averageByReport.TryGetValue(report.Id, out var average)
                ? Math.Round(average, 2)
                : 0;
            var independentConfirmations = confirmationByReport.TryGetValue(report.Id, out var count)
                ? count
                : 0;

            report.Severity = Math.Round(independentAverage, 1);
            report.IsUrgent = _priorityService.IsUrgent(
                independentAverage,
                independentConfirmations,
                report.AdminPriorityAdjustment,
                report.Status);
        }

        UrgentCount = reports.Count(report => report.IsUrgent);

        // Приключените сигнали остават в числата, но не в изгледа по
        // подразбиране: картата е за това какво чака общината, а не за
        // архив. Отметката ги връща, когато някой иска историчен изглед.
        ClosedCount = reports.Count(report => report.IsClosed);

        var shown = ShowClosedReports
            ? reports
            : reports.Where(report => !report.IsClosed).ToList();

        ReportsCount = shown.Count;

        await LoadStatusStatisticsAsync();

        ReportsJson = JsonSerializer.Serialize(
            shown,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
    }

    // Статистиката е за всички сигнали, не само за тези с координати:
    // иначе числата в таблото и под картата се различават за един и
    // същия град.
    private async Task LoadStatusStatisticsAsync()
    {
        var counted = await _context.Reports
            .AsNoTracking()
            .GroupBy(r => r.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync();

        StatusStatistics = ReportStatuses.All
            .Select(status => new StatusCount(
                status,
                counted.FirstOrDefault(item => item.Status == status)?.Count ?? 0))
            .ToList();

        TotalReportCount = counted.Sum(item => item.Count);
    }

    /// <summary>Брой сигнали по един статус.</summary>
    public sealed record StatusCount(string Status, int Count);

    /// <summary>Редът, който картата получава като JSON.</summary>
    public sealed class MapReport
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int AdminPriorityAdjustment { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Severity { get; set; }

        // Правилото се изчислява в ReportPriorityService, а не тук, за
        // да е едно и също за маркерите, за списъка и за таблото.
        public bool IsUrgent { get; set; }

        // Същото правило, разделено на две: маркерите трябва да знаят кой
        // е приключен, за да го рисуват като факт, а не като задача.
        public bool IsClosed => ReportStatuses.IsClosed(Status);
    }
}

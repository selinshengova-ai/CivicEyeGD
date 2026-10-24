using CivicEyeGD.Data;
using CivicEyeGD.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CivicEyeGD.Pages;

public class DetailsModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ReportPriorityService _priorityService;

    public DetailsModel(ApplicationDbContext context, UserManager<IdentityUser> userManager, ReportPriorityService priorityService)
    {
        _context = context;
        _userManager = userManager;
        _priorityService = priorityService;
    }

    public Report? Report { get; private set; }
    public double AverageSeverity { get; private set; }
    public int ConfirmationCount { get; private set; }
    public double PriorityScore { get; private set; }
    public string PriorityLabel { get; private set; } = string.Empty;
    public bool CurrentUserHasRated { get; private set; }
    public bool CurrentUserHasConfirmed { get; private set; }
    public int CurrentUserRating { get; private set; }
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        await LoadReportAsync(id);
        if (Report is null) return NotFound();
        return Page();
    }

    // Променен метод за потвърждение, който работи и с AJAX (fetch)
    public async Task<IActionResult> OnPostConfirmAsync(int id)
    {
        await LoadReportAsync(id);
        if (Report is null) return NotFound();

        var userId = _userManager.GetUserId(User);
        if (userId is null) return Challenge();

        if (CurrentUserHasConfirmed)
        {
            // Връщаме статус 400, за да го хванем в JS
            return BadRequest("Вече си потвърдил този проблем.");
        }

        _context.Confirmations.Add(new Confirmation
        {
            ReportId = id,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
        
        // Ако е AJAX заявка, връщаме OK без да презареждаме страницата
        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            return new OkResult();
        }

        await LoadReportAsync(id);
        SuccessMessage = "Благодарим! Потвърждението ти е записано.";
        return Page();
    }

    public async Task<IActionResult> OnPostRateAsync(int id, int rating)
    {
        // ... (остави съществуващата логика за оценка, тя е същата) ...
        await LoadReportAsync(id);
        if (Report is null) return NotFound();

        if (rating < 1 || rating > 5)
        {
            ErrorMessage = "Оценката трябва да е между 1 и 5.";
            return Page();
        }

        var userId = _userManager.GetUserId(User);
        if (userId is null) return Challenge();

        var existing = await _context.SeverityRatings.FirstOrDefaultAsync(r => r.ReportId == id && r.UserId == userId);

        if (existing is null)
        {
            _context.SeverityRatings.Add(new SeverityRating { ReportId = id, UserId = userId, Rating = rating, CreatedAt = DateTime.UtcNow });
        }
        else
        {
            existing.Rating = rating;
            existing.CreatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        await LoadReportAsync(id);
        SuccessMessage = "Оценката ти е записана.";
        return Page();
    }

    private async Task LoadReportAsync(int id)
    {
        Report = await _context.Reports
            .Include(r => r.Images)
            .Include(r => r.Confirmations)
            .Include(r => r.SeverityRatings)
            .Include(r => r.StatusHistories)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id);

        if (Report is null) return;

        ConfirmationCount = Report.Confirmations.Count;
        AverageSeverity = Report.SeverityRatings.Count > 0 ? Math.Round(Report.SeverityRatings.Average(sr => sr.Rating), 2) : 0;
        PriorityScore = _priorityService.Calculate(AverageSeverity, ConfirmationCount, Report.AdminPriorityAdjustment);
        PriorityLabel = _priorityService.GetPriorityLabel(PriorityScore);

        var userId = _userManager.GetUserId(User);
        if (userId is not null)
        {
            var userRating = Report.SeverityRatings.FirstOrDefault(sr => sr.UserId == userId);
            CurrentUserHasRated = userRating is not null;
            CurrentUserRating = userRating?.Rating ?? 0;
            CurrentUserHasConfirmed = Report.Confirmations.Any(c => c.UserId == userId);
        }
    }
}
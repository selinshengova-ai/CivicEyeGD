using CivicEyeGD.Data;
using CivicEyeGD.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CivicEyeGD.Pages;

// Сигналът се гледа и без акаунт: публичната карта води насам, а
// зад вход страницата показва същото като таблото. Гласуването пак
// изисква вход — без бисквита Challenge() праща към страницата за
// вход, преди нещо да е записано. Атрибутът е по класа, защото
// поставен върху обработващ метод на Razor Page той не стига до
// крайната точка и не действа (MVC1001).
//
// Отделно ограничение за гласуването няма смисъл: един акаунт може да
// потвърди сигнал веднъж (уникален индекс), подателят не гласува, а
// самото създаване на акаунти е ограничено с политиката "forms".
[AllowAnonymous]
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
    public int IndependentRaterCount { get; private set; }
    public int ConfirmationCount { get; private set; }
    public double PriorityScore { get; private set; }
    public string PriorityLabel { get; private set; } = string.Empty;
    public ReportPriorityService.PriorityBreakdown? Breakdown { get; private set; }
    public bool CurrentUserHasRated { get; private set; }
    public bool CurrentUserHasConfirmed { get; private set; }
    public bool CurrentUserIsAuthor { get; private set; }
    public bool IsSignedIn { get; private set; }
    public int CurrentUserRating { get; private set; }
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }

    // Идентификатор на запис от историята -> показвано име. Имейлите не
    // се изписват никъде: подателят остава анонимен за другите граждани.
    public Dictionary<int, string> HistoryAuthorLabels { get; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        await LoadReportAsync(id);
        if (Report is null) return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmAsync(int id)
    {
        await LoadReportAsync(id);
        if (Report is null) return NotFound();

        var userId = _userManager.GetUserId(User);
        if (userId is null) return Challenge();

        if (ReportStatuses.IsClosed(Report.Status))
        {
            return BadRequest("Приключен сигнал не приема нови потвърждения.");
        }

        if (Report.WasSubmittedBy(userId))
        {
            return BadRequest("Не можеш да потвърдиш собствения си сигнал.");
        }

        if (CurrentUserHasConfirmed)
        {
            return BadRequest("Вече си потвърдил този проблем.");
        }

        _context.Confirmations.Add(new Confirmation
        {
            ReportId = id,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        });

        // Два клика подред минават през проверката и двата, преди
        // първият запис да е готов. Уникалният индекс спечелва, а
        // потребителят трябва да види обяснение, не сървърна грешка.
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return BadRequest("Вече си потвърдил този проблем.");
        }

        await LoadReportAsync(id);
        SuccessMessage = "Благодарим! Потвърждението ти е записано.";
        return Page();
    }

    public async Task<IActionResult> OnPostRateAsync(int id, int rating)
    {
        await LoadReportAsync(id);
        if (Report is null) return NotFound();

        if (rating < 1 || rating > 5)
        {
            ErrorMessage = "Оценката трябва да е между 1 и 5.";
            return Page();
        }

        var userId = _userManager.GetUserId(User);
        if (userId is null) return Challenge();

        if (ReportStatuses.IsClosed(Report.Status))
        {
            ErrorMessage = "Приключен сигнал вече не се оценява.";
            return Page();
        }

        if (Report.WasSubmittedBy(userId))
        {
            ErrorMessage = "Подателят не участва в оценката на собствения си сигнал.";
            return Page();
        }

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

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            ErrorMessage = "Оценката вече е записана.";
            return Page();
        }

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
                .ThenInclude(h => h.ChangedByUser)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id);

        if (Report is null) return;

        AverageSeverity = Report.IndependentAverageSeverity;
        IndependentRaterCount = Report.IndependentRaterCount;
        ConfirmationCount = Report.IndependentConfirmationCount;

        Breakdown = _priorityService.Breakdown(
            AverageSeverity,
            ConfirmationCount,
            Report.AdminPriorityAdjustment);

        PriorityScore = Breakdown.Score;
        PriorityLabel = _priorityService.GetPriorityLabel(PriorityScore);

        var userId = _userManager.GetUserId(User);
        IsSignedIn = userId is not null;
        CurrentUserIsAuthor = Report.WasSubmittedBy(userId);

        if (userId is not null)
        {
            var userRating = Report.SeverityRatings.FirstOrDefault(sr => sr.UserId == userId);
            CurrentUserHasRated = userRating is not null;
            CurrentUserRating = userRating?.Rating ?? 0;
            CurrentUserHasConfirmed = Report.Confirmations.Any(c => c.UserId == userId);
        }

        await LoadHistoryAuthorsAsync();
    }

    private async Task LoadHistoryAuthorsAsync()
    {
        if (Report is null) return;

        var labels = new Dictionary<string, string>();

        foreach (var changedById in Report.StatusHistories
                     .Select(h => h.ChangedByUserId)
                     .Where(id => id is not null)
                     .Distinct())
        {
            var user = await _userManager.FindByIdAsync(changedById!);

            labels[changedById!] = user is null
                ? "Служител с премахнат акаунт"
                : (await _userManager.GetRolesAsync(user)).Contains("Administrator")
                    ? "Администратор на платформата"
                    : "Служител на общината";
        }

        foreach (var entry in Report.StatusHistories)
        {
            if (entry.ChangedByUserId is not null
                && labels.TryGetValue(entry.ChangedByUserId, out var label))
            {
                HistoryAuthorLabels[entry.Id] = label;
            }
        }
    }
}

using System.Globalization;
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
    private readonly ReportOfficeService _office;

    public DetailsModel(
        ApplicationDbContext context,
        UserManager<IdentityUser> userManager,
        ReportPriorityService priorityService,
        ReportOfficeService office)
    {
        _context = context;
        _userManager = userManager;
        _priorityService = priorityService;
        _office = office;
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

    // Служителят вижда панела за намеса. Определя се по роля, не по
    // имейл или име, защото само ролята е обещанието в кода.
    public bool IsOfficer { get; private set; }
    public int CurrentUserRating { get; private set; }
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }

    // Идентификатор на запис от историята -> показвано име. Имейлите не
    // се изписват никъде: подателят остава анонимен за другите граждани.
    public Dictionary<int, string> HistoryAuthorLabels { get; } = new();

    // Стъпките на дневника, готови за четене: най-новата първо. Редът се
    // подрежда тук, а не в листа, защото страницата на сигнала е единното
    // място, което обяснява миналото на сигнала, и проверката за реда
    // трябва да е върху същото число, което видът показва.
    public IReadOnlyList<StatusHistory> DiarySteps { get; private set; } = new List<StatusHistory>();

    // Последната намеса или null, ако общината не се е мякала. Чете се от
    // самия дневник: второ число за едно и също нещо с времето се разминава
    // с първото.
    public DateTime? LastChange => DiarySteps.Count > 0 ? DiarySteps[0].ChangedAt : null;

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

        // Две щраквания подред: и двете минават през проверката, преди
        // първият запис да е готов. Тогава правилото за уникалност спира
        // втория, а потребителят трябва да види обяснение, не сървърна
        // грешка.
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

    // Колко тежи една точка от корекцията по скалата 0..100. Числото стои
    // тук, защото страницата на сигнала е единственото място, където
    // служителят я въвежда: подсещането под полето и скриптът, който
    // показва резултата предварително, го четат оттук, а не го преписват.
    public static double AdjustmentWeightPerPoint =>
        ReportPriorityService.AdjustmentWeightPerPoint;

    // Същото число като текст за скрипта. Форматът е неизменен, защото
    // браузърът разбира единствено десетична точка: с българска
    // запетая числото се чете като нула и надписът би излягал.
    public static string AdjustmentWeightForScript =>
        AdjustmentWeightPerPoint.ToString("0.0000", CultureInfo.InvariantCulture);

    // Таванът на бележката е един и същ навсякъде: полето в листа и
    // проверката в услугата го споделят, за да не обещава интерфейс
    // повече, отколкото сървърът приема.
    public const int MaxCommentLength = ReportOfficeService.MaxCommentLength;

    // Намесва се същата услуга като в таблото, затова статусът, бележката
    // и корекцията изглеждат по един и същи начин и на двете места.
    public async Task<IActionResult> OnPostOfficeAsync(
        int id,
        string newStatus,
        string? comment,
        int adjustment)
    {
        await LoadReportAsync(id);
        if (Report is null) return NotFound();

        // Панелът не се показва на обикновения гражданин, но проверката е
        // тук: скрит бутон не е разрешение, а формата може да се изпрати
        // и без него.
        if (!IsOfficer) return Forbid();

        var outcome = await _office.UpdateAsync(
            _userManager.GetUserId(User), id, newStatus, comment, adjustment);

        switch (outcome)
        {
            case ReportOfficeService.Outcome.UnknownStatus:
                return BadRequest("Непознат статус.");
            case ReportOfficeService.Outcome.AdjustmentOutOfRange:
                return BadRequest("Корекцията на приоритета е между 0 и 10.");
            case ReportOfficeService.Outcome.NotFound:
                return NotFound();
        }

        await LoadReportAsync(id);

        // Двете съобщения са едни и същи и на таблото, и тук: service-ът
        // решава дали е имало намеса, страницата само я описва.
        SuccessMessage = outcome == ReportOfficeService.Outcome.Saved
            ? $"Промените по сигнала са записани. Сега е „{Report!.Status}“."
            : "Нищо не се промени — статусът и корекцията са същите, а бележка не е оставена.";

        return Page();
    }

    // Изтриването живее тук, заедно с останалите админски действия: то
    // действа върху един сигнал, а страницата на сигнала е мястото, където
    // служителят вижда какво точно губи — снимките, оценките и дневника.
    public async Task<IActionResult> OnPostDeleteAsync(int id, string? filterStatus)
    {
        // Проверката за ролята се повтаря и при изтриването: форматът не
        // се изпраща на гражданин, но заявка може да сглоби и конзолата.
        var userId = _userManager.GetUserId(User);
        if (userId is null) return Challenge();
        if (!await IsAdministratorAsync(userId)) return Forbid();

        var outcome = await _office.DeleteAsync(id);

        // След изтриването адресът на сигнала е безсмислен, затова
        // връщането е към таблото и носи номера на изтрития ред: служителят
        // вижда потвърждението точно там, където списъкът току-що е
        // съкратил с един.
        return outcome switch
        {
            ReportOfficeService.Outcome.NotFound => NotFound(),
            _ => RedirectToPage("/Admin/Index", new
            {
                deletedId = id,
                notice = "deleted",
                filterStatus
            })
        };
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

        // Дневникът се подрежда тук, в лист, най-новата стъпка първо: видът
        // го чете готов, за да не повтаря правилото за реда на две места.
        DiarySteps = Report.StatusHistories
            .OrderByDescending(h => h.ChangedAt)
            .ThenByDescending(h => h.Id)
            .ToList();

        var userId = _userManager.GetUserId(User);
        IsSignedIn = userId is not null;
        IsOfficer = userId is not null && await IsAdministratorAsync(userId);
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

    // Ролята се чете от базата, не от бисквитата: премахнат служител
    // спира да намесва сигнали веднага, без да изчаква нов вход.
    private async Task<bool> IsAdministratorAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);

        return user is not null
            && (await _userManager.GetRolesAsync(user)).Contains("Administrator");
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

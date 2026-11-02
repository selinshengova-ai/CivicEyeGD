using CivicEyeGD.Data;
using CivicEyeGD.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace CivicEyeGD.Pages;

[RequestFormLimits(MultipartBodyLengthLimit = 16 * 1024 * 1024)]
[RequestSizeLimit(16 * 1024 * 1024)]
[EnableRateLimiting("forms")]
public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ReportImageStorage _imageStorage;
    private readonly ReportPriorityService _priorityService;
    private readonly ILogger<CreateModel> _logger;

    public CreateModel(
        ApplicationDbContext context,
        UserManager<IdentityUser> userManager,
        ReportImageStorage imageStorage,
        ReportPriorityService priorityService,
        IConfiguration configuration,
        ILogger<CreateModel> logger)
    {
        _context = context;
        _userManager = userManager;
        _imageStorage = imageStorage;
        _priorityService = priorityService;
        GoogleMapsApiKey = configuration["GoogleMapsApi:ApiKey"] ?? string.Empty;
        _logger = logger;
    }

    // Ключът идва от настройките (за локална работа — от user secrets),
    // за да не попадне в изходния код и в публичния архив.
    public string GoogleMapsApiKey { get; }

    public IReadOnlyList<string> Categories => ReportCategories.All;

    [BindProperty]
    public ReportInput Report { get; set; } = new();

    [BindProperty]
    public List<IFormFile> Photos { get; set; } = new();

    // Сигналите, които вече описват същото място. Показват се като
    // предложение да се потвърди съществуващият запис, преди някой да е
    // попълнил напълно нов формуляр за вече видян проблем.
    public IReadOnlyList<NearbyReport> NearbyDuplicates { get; private set; } = [];

    // Картата и категорията се попълват в различен ред — проверката се
    // пуска само когато и двете стойности вече са в заявката.
    public async Task<IActionResult> OnGetNearbyAsync(string? category, double? latitude, double? longitude)
    {
        if (category is null || !ReportCategories.IsValid(category)
            || latitude is null || longitude is null
            || !double.IsFinite(latitude.Value) || !double.IsFinite(longitude.Value)
            || !ReportGeography.IsWithinTown(latitude, longitude))
        {
            return new JsonResult(new { matches = Array.Empty<NearbyReport>() });
        }

        var matches = await FindNearbyAsync(category, latitude.Value, longitude.Value);

        // Прозорецът с вече подадения сигнал трябва да покаже снимката и
        // приоритета му, не само текста: гражданинът преценява дали е
        // същият проблем на поглед, а не на дума. Разбивката идва от същата
        // услуга, от която я чете публичната карта, затова числата съвпадат.
        return new JsonResult(new { matches = await DescribeAsync(matches) });
    }

    // Близките записи се връщат като по-богати редове за скрипта. Полетата,
    // които тестът и старият код вече четат (Id, Status, DistanceMeters),
    // остават с същите имена; добавят се само нови, за да не се чупи нищо.
    private async Task<IReadOnlyList<object>> DescribeAsync(IReadOnlyList<NearbyReport> matches)
    {
        if (matches.Count == 0)
        {
            return [];
        }

        var ids = matches.Select(match => match.Id).ToList();

        var full = await _context.Reports
            .AsNoTracking()
            .Include(report => report.Images)
            .Include(report => report.Confirmations)
            .Include(report => report.SeverityRatings)
            .Where(report => ids.Contains(report.Id))
            .ToDictionaryAsync(report => report.Id);

        return matches.Select(match =>
        {
            string? photo = null;
            double? priorityScore = null;
            string? priorityLabel = null;
            bool? isUrgent = null;

            if (full.TryGetValue(match.Id, out var report))
            {
                // Първата снимка по номер е тази, която гражданинът е сложил
                // най-рано — тя е най-близка до кадрите под картата.
                photo = report.Images
                    .OrderBy(image => image.Id)
                    .Select(image => $"/report-images/{image.Id}")
                    .FirstOrDefault();

                var breakdown = _priorityService.Breakdown(
                    report.IndependentAverageSeverity,
                    report.IndependentConfirmationCount,
                    report.AdminPriorityAdjustment);

                priorityScore = breakdown.Score;
                priorityLabel = _priorityService.GetPriorityLabel(breakdown.Score);
                isUrgent = _priorityService.IsUrgent(report);
            }

            return (object)new
            {
                match.Id,
                match.Title,
                match.Address,
                match.Status,
                match.DistanceMeters,
                Photo = photo,
                PriorityScore = priorityScore,
                PriorityLabel = priorityLabel,
                IsUrgent = isUrgent,
                // Собствения сигнал не се потвърждава: текстът в прозореца
                // се сменя, а бутонът отпада. Датата влиза в него, защото
                // „този, който подаде преди месец" е друг от „този, който
                // току-що опита два пъти".
                IsMine = IsOwnReport(match),
                CreatedAtText = match.CreatedAt.ToDayText()
            };
        }).ToList();
    }

    // Един сигнал е „мой", когато авторът му е влезлият потребител.
    // Сравнението е на ниво страница, не в базата: близките се избират
    // по категория и разстояние, а не по автор.
    public bool IsOwnReport(NearbyReport match) =>
        match.AuthorUserId is not null && match.AuthorUserId == _userManager.GetUserId(User);

    public async Task<IActionResult> OnPostAsync()
    {
        ValidatePhotos();
        ValidateLocation();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Изскачащият прозорец предупреждава още докато маркерът се мести,
        // но проверката тук е задължителна: заявка може да го заобиколи.
        // Един наистина различен проблем не се блокира — нужно е само
        // признаването му да е изрично, а не подразбиращо се.
        if (!Report.DuplicateAcknowledged)
        {
            NearbyDuplicates = await FindNearbyAsync(
                Report.Category, Report.Latitude!.Value, Report.Longitude!.Value);

            if (NearbyDuplicates.Count > 0)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "На това място вече има подаден сигнал от същата категория. "
                    + "Отвори го и го потвърди вместо да създадеш дубликат. "
                    + "Ако проблемът е различен, отметни „Това е различен проблем“ "
                    + "и изпрати отново.");
                return Page();
            }
        }

        var newReport = new Data.Report
        {
            Title = Report.Title.Trim(),
            Category = Report.Category,
            Address = Report.Address.Trim(),
            Description = Report.Description.Trim(),
            Status = ReportStatuses.New,
            CreatedAt = DateTime.UtcNow,
            Latitude = Report.Latitude,
            Longitude = Report.Longitude,
            UserId = _userManager.GetUserId(User)
        };

        // Добавяме оценката на потребителя към сигнала
        newReport.SeverityRatings.Add(new SeverityRating
        {
            UserId = _userManager.GetUserId(User)!,
            Rating = Report.Severity,
            CreatedAt = DateTime.UtcNow
        });

        var savedFileNames = new List<string>();

        try
        {
            foreach (var photo in Photos)
            {
                if (photo.Length == 0) continue;

                var stored = await _imageStorage.SaveAsync(photo);
                savedFileNames.Add(stored.StoredFileName);

                newReport.Images.Add(new ReportImage
                {
                    StoredFileName = stored.StoredFileName,
                    ContentType = stored.ContentType,
                    SizeBytes = stored.SizeBytes,
                    CreatedAt = DateTime.UtcNow
                });
            }

            _context.Reports.Add(newReport);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            foreach (var fileName in savedFileNames)
            {
                _imageStorage.Delete(fileName);
            }
            _logger.LogError(ex, "Неуспешно записване на нов сигнал.");
            ModelState.AddModelError(string.Empty, "Сигналът не беше записан заради временен проблем. Моля, опитай отново.");
            return Page();
        }

        return RedirectToPage("/Details", new { id = newReport.Id });
    }

    // SQLite не смята разстояние по голям кръг, затова от базата идва
    // само съседният квадрат. В рамките на един град това са нула или
    // два сигнала, а точната мерка се решава тук в паметта.
    private async Task<IReadOnlyList<NearbyReport>> FindNearbyAsync(
        string category,
        double latitude,
        double longitude)
    {
        var latitudeMargin = ReportDuplicates.RadiusMeters / 111_320.0;
        var longitudeMargin = latitudeMargin / Math.Max(
            0.2, Math.Cos(latitude * Math.PI / 180.0));

        var candidates = await _context.Reports
            .AsNoTracking()
            .Where(report => report.Category == category
                && report.Latitude != null
                && report.Longitude != null
                && report.Latitude >= latitude - latitudeMargin
                && report.Latitude <= latitude + latitudeMargin
                && report.Longitude >= longitude - longitudeMargin
                && report.Longitude <= longitude + longitudeMargin)
            .ToListAsync();

        return ReportDuplicates.Find(candidates, category, latitude, longitude);
    }

    private void ValidatePhotos()
    {
        if (Photos.Count == 0) return;
        if (Photos.Count > ReportImageStorage.MaxImagesPerReport)
        {
            ModelState.AddModelError(nameof(Photos), $"Може да прикачиш най-много {ReportImageStorage.MaxImagesPerReport} снимки.");
        }
        foreach (var photo in Photos)
        {
            var error = _imageStorage.ValidateFile(photo);
            if (error is not null) ModelState.AddModelError(nameof(Photos), error);
        }
    }

    // Браузърът пази стойностите в списъци, но заявка може да дойде и
    // заобикаляйки формата — затова проверяваме и тук.
    // Ключовете на грешките трябва да съвпадат с пълните имена на полетата,
    // иначе съобщенията не се изписват нищо на екрана.
    private void ValidateLocation()
    {
        if (!ReportCategories.IsValid(Report.Category))
        {
            ModelState.AddModelError(
                $"{nameof(Report)}.{nameof(Report.Category)}",
                "Избери категория от списъка.");
        }

        // Координатите са в скрити полета, затова грешките отиват в
        // общия список над формата.
        if (Report.Latitude is null || Report.Longitude is null)
        {
            ModelState.AddModelError(
                string.Empty,
                "Отбележи мястото на картата — без него сигналът не се вижда в публичната карта.");
        }
        else if (!ReportGeography.IsWithinTown(Report.Latitude, Report.Longitude))
        {
            ModelState.AddModelError(
                string.Empty,
                "Отбелязаната точка е извън границите на Гоце Делчев.");
        }
    }

    public class ReportInput
    {
        [Required(ErrorMessage = "Въведи заглавие.")]
        [StringLength(100)]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Избери категория.")]
        public string Category { get; set; } = string.Empty;

        [Required(ErrorMessage = "Въведи местоположение.")]
        [StringLength(200)]
        public string Address { get; set; } = string.Empty;

        [Required(ErrorMessage = "Въведи описание.")]
        [StringLength(2000)]
        public string Description { get; set; } = string.Empty;

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        [Required(ErrorMessage = "Моля, изберете оценка за сериозност.")]
        [Range(1, 5, ErrorMessage = "Оценката трябва да е между 1 и 5.")]
        public int Severity { get; set; } = 3; // По подразбиране 3

        // Отметнато, когато потребителят е виждал предупреждението за
        // същия проблем на това място и настоява за нов сигнал.
        public bool DuplicateAcknowledged { get; set; }
    }
}
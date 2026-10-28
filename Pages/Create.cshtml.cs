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
    private readonly ILogger<CreateModel> _logger;

    public CreateModel(
        ApplicationDbContext context,
        UserManager<IdentityUser> userManager,
        ReportImageStorage imageStorage,
        IConfiguration configuration,
        ILogger<CreateModel> logger)
    {
        _context = context;
        _userManager = userManager;
        _imageStorage = imageStorage;
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

        return new JsonResult(new { matches });
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ValidatePhotos();
        ValidateLocation();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Предупреждението се появява и в браузъра, докато маркерът се
        // мести, но проверката тук е задължителна: заявка може да го
        // заобиколи. Един наистина различен проблем не се блокира —
        // иска се само явно признание под предупреждението.
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
                    + "Ако проблемът е различен, отметни полето под предупреждението и изпрати отново.");
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
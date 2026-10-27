using CivicEyeGD.Data;
using CivicEyeGD.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CivicEyeGD.Pages;

[RequestFormLimits(MultipartBodyLengthLimit = 16 * 1024 * 1024)]
[RequestSizeLimit(16 * 1024 * 1024)]
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
        ILogger<CreateModel> logger)
    {
        _context = context;
        _userManager = userManager;
        _imageStorage = imageStorage;
        _logger = logger;
    }

    [BindProperty]
    public ReportInput Report { get; set; } = new();

    [BindProperty]
    public List<IFormFile> Photos { get; set; } = new();

    public string ExistingReportsJson { get; private set; } = "[]";

    public async Task OnGetAsync()
    {
        await LoadExistingReportsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ValidatePhotos();

        if (!ModelState.IsValid)
        {
            await LoadExistingReportsAsync();
            return Page();
        }

        var newReport = new Data.Report
        {
            Title = Report.Title.Trim(),
            Category = Report.Category,
            Address = Report.Address.Trim(),
            Description = Report.Description.Trim(),
            Status = "Подаден",
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
            await LoadExistingReportsAsync();
            return Page();
        }

        return RedirectToPage("/Details", new { id = newReport.Id });
    }

    private async Task LoadExistingReportsAsync()
    {
        var activeReports = await _context.Reports
            .AsNoTracking()
            .Where(r => r.Status != "Решен" && r.Status != "Отхвърлен")
            .Select(r => new
            {
                r.Id,
                r.Title,
                r.Category,
                r.Description,
                r.Latitude,
                r.Longitude,
                r.Status
            })
            .ToListAsync();

        ExistingReportsJson = JsonSerializer.Serialize(activeReports, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
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
    }
}
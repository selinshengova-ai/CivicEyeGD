
using CivicEyeGD.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace CivicEyeGD.Pages;

public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _context;

    public CreateModel(ApplicationDbContext context)
    {
        _context = context;
    }

    [BindProperty]
    public ReportInput Report { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
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
            Longitude = Report.Longitude
        };

        _context.Reports.Add(newReport);
        await _context.SaveChangesAsync();

        return RedirectToPage("/Index");
    }

    public class ReportInput
    {
        [Required(ErrorMessage = "Въведи заглавие.")]
        [StringLength(100, ErrorMessage = "Заглавието трябва да е до 100 символа.")]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Избери категория.")]
        public string Category { get; set; } = string.Empty;

        [Required(ErrorMessage = "Въведи местоположение.")]
        [StringLength(200)]
        public string Address { get; set; } = string.Empty;

        [Required(ErrorMessage = "Въведи описание.")]
        [StringLength(2000, ErrorMessage = "Описанието трябва да е до 2000 символа.")]
        public string Description { get; set; } = string.Empty;

        public double? Latitude { get; set; }

        public double? Longitude { get; set; }
    }
}

using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CivicEyeGD.Data;

public class Report
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(2000)]
    public string Description { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Category { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Address { get; set; } = string.Empty;

    public string Status { get; set; } = "Подаден";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Географски координати
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // Административна корекция на приоритета (0-10)
    public int AdminPriorityAdjustment { get; set; } = 0;

    // Връзка към подателя
    public string? UserId { get; set; }
    public IdentityUser? User { get; set; }

    // Снимки
    public ICollection<ReportImage> Images { get; set; } = new List<ReportImage>();

    // Обратни навигации за оценките, потвържденията и историята на статуса.
    public ICollection<SeverityRating> SeverityRatings { get; set; } = new List<SeverityRating>();
    public ICollection<Confirmation> Confirmations { get; set; } = new List<Confirmation>();
    public ICollection<StatusHistory> StatusHistories { get; set; } = new List<StatusHistory>();

    // Оценките и потвържденията на подателя не участват в приоритета.
    // Без това правило един човек вдига собствения си сигнал до
    // „висок приоритет" с един клик, а точността на йерархията е
    // целият смисъл на системата за общината.
    //
    // [NotMapped] е задължителен: EF Core иначе разбира
    // IndependentRatings като втора навигация към SeverityRatings и
    // създава сянка външен ключ ReportId1, което развали миграцията.
    [NotMapped]
    public IEnumerable<SeverityRating> IndependentRatings =>
        SeverityRatings.Where(rating => rating.UserId != UserId);

    [NotMapped]
    public int IndependentRaterCount => IndependentRatings.Count();

    [NotMapped]
    public double IndependentAverageSeverity =>
        IndependentRaterCount > 0
            ? Math.Round(IndependentRatings.Average(rating => rating.Rating), 2)
            : 0;

    [NotMapped]
    public int IndependentConfirmationCount =>
        Confirmations.Count(confirmation => confirmation.UserId != UserId);

    public bool WasSubmittedBy(string? userId) =>
        userId is not null && UserId == userId;
}
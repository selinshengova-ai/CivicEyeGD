using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

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

    // --- НОВИ КОЛЕКЦИИ (тези редове липсваха) ---
    public ICollection<SeverityRating> SeverityRatings { get; set; } = new List<SeverityRating>();
    public ICollection<Confirmation> Confirmations { get; set; } = new List<Confirmation>();
    public ICollection<StatusHistory> StatusHistories { get; set; } = new List<StatusHistory>();
}
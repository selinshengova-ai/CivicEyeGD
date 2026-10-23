using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace CivicEyeGD.Data;

public class SeverityRating
{
    public int Id { get; set; }

    public int ReportId { get; set; }
    public Report Report { get; set; } = null!;

    public string UserId { get; set; } = string.Empty;
    public IdentityUser User { get; set; } = null!;

    [Range(1, 5)]
    public int Rating { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
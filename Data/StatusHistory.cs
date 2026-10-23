using Microsoft.AspNetCore.Identity;

namespace CivicEyeGD.Data;

public class StatusHistory
{
    public int Id { get; set; }

    public int ReportId { get; set; }
    public Report Report { get; set; } = null!;

    public string? OldStatus { get; set; }
    public string NewStatus { get; set; } = string.Empty;

    public string? ChangedByUserId { get; set; }
    public IdentityUser? ChangedByUser { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    public string? Comment { get; set; }
}
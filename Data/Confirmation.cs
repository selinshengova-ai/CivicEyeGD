using Microsoft.AspNetCore.Identity;

namespace CivicEyeGD.Data;

public class Confirmation
{
    public int Id { get; set; }

    public int ReportId { get; set; }
    public Report Report { get; set; } = null!;

    public string UserId { get; set; } = string.Empty;
    public IdentityUser User { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
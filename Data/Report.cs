
namespace CivicEyeGD.Data;

public class Report
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public string Status { get; set; } = "Подаден";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Географски координати на сигнала
    public double? Latitude { get; set; }

    public double? Longitude { get; set; }
}

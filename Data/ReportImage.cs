
namespace CivicEyeGD.Data;

// Една снимка, прикачена към сигнал (Report).
// Самият файл се пази на диска ИЗВЪН wwwroot, а тук пазим само
// служебното име и метаданните. Реалният път до файла никога
// не се показва на потребителя.
public class ReportImage
{
    public int Id { get; set; }

    // Сигналът, към който принадлежи снимката.
    public int ReportId { get; set; }

    public Report Report { get; set; } = null!;

    // Служебно (генерирано) име на файла на диска: GUID + разширение.
    // Оригиналното име от потребителя НЕ се използва като име за съхранение.
    public string StoredFileName { get; set; } = string.Empty;

    // MIME типът, определен и потвърден на сървъра.
    public string ContentType { get; set; } = string.Empty;

    // Размер на файла в байтове.
    public long SizeBytes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

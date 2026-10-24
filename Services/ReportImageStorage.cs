
using Microsoft.AspNetCore.Http;

namespace CivicEyeGD.Services;

// Съхранява снимките на сигналите на диска ИЗВЪН wwwroot
// и валидира размера и формата им на СЪРВЪРА (не разчита само
// на проверките в браузъра).
public sealed class ReportImageStorage
{
    // Максимум снимки към един сигнал.
    public const int MaxImagesPerReport = 3;

    // Максимален размер на една снимка: 5 MB.
    public const long MaxFileSizeBytes = 5 * 1024 * 1024;

    // Позволени типове -> разширение на файла.
    private static readonly Dictionary<string, string> AllowedTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = ".jpg",
            ["image/png"] = ".png",
            ["image/webp"] = ".webp"
        };

    // Проверява дали даден MIME тип е сред позволените.
    // Използва се и при ПОДНАСЯНЕ на снимка, за да не се върне файл с
    // неочакван (например манипулиран) тип.
    public static bool IsAllowedContentType(string? contentType)
    {
        return contentType is not null
            && AllowedTypes.ContainsKey(contentType);
    }

    private readonly string _rootDirectory;

    public ReportImageStorage(IWebHostEnvironment environment)
    {
        // Папката е извън wwwroot, затова файловете не се
        // обслужват директно от статичните файлове.
        _rootDirectory = Path.Combine(
            environment.ContentRootPath, "App_Data", "report-images");

        Directory.CreateDirectory(_rootDirectory);
    }

    // Връща съобщение за грешка или null, ако файлът е валиден.
    public string? ValidateFile(IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            return "Един от избраните файлове е празен.";
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return $"„{file.FileName}“ е по-голям от 5 MB.";
        }

        using var stream = file.OpenReadStream();

        var detectedType = DetectImageType(stream);

        if (detectedType is null || !AllowedTypes.ContainsKey(detectedType))
        {
            return $"„{file.FileName}“ не е сред поддържаните формати "
                 + "(JPG, PNG, WEBP).";
        }

        return null;
    }

    // Записва файла на диска и връща метаданните му.
    public async Task<StoredImage> SaveAsync(IFormFile file)
    {
        var error = ValidateFile(file);

        if (error is not null)
        {
            throw new InvalidOperationException(error);
        }

        string detectedType;

        using (var stream = file.OpenReadStream())
        {
            detectedType = DetectImageType(stream)
                ?? throw new InvalidOperationException(
                    "Неподдържан формат на изображението.");
        }

        var extension = AllowedTypes[detectedType];

        // Безопасно, уникално име – не използваме оригиналното име.
        var storedFileName = Guid.NewGuid().ToString("N") + extension;

        var fullPath = Path.Combine(_rootDirectory, storedFileName);

        try
        {
            await using (var target = new FileStream(
                fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var source = file.OpenReadStream();
                await source.CopyToAsync(target);
            }
        }
        catch
        {
            // Не оставяме недовършен файл на диска при провален запис.
            Delete(storedFileName);
            throw;
        }

        return new StoredImage(storedFileName, detectedType, file.Length);
    }

    // Връща пълния път до файла само ако съществува и е в нашата папка.
    public string? GetFullPath(string storedFileName)
    {
        if (string.IsNullOrWhiteSpace(storedFileName))
        {
            return null;
        }

        // Path.GetFileName премахва евентуални „..“ или пътища,
        // за да не се излезе извън папката.
        var safeName = Path.GetFileName(storedFileName);
        var fullPath = Path.Combine(_rootDirectory, safeName);

        return File.Exists(fullPath) ? fullPath : null;
    }

    // Изтрива файл (използва се при неуспешно записване).
    public void Delete(string storedFileName)
    {
        var fullPath = GetFullPath(storedFileName);

        if (fullPath is not null)
        {
            File.Delete(fullPath);
        }
    }

    // Определя типа по „магическите“ байтове на файла,
    // а не по това, което твърди браузърът.
    private static string? DetectImageType(Stream stream)
    {
        Span<byte> header = stackalloc byte[12];

        var read = 0;

        while (read < header.Length)
        {
            var count = stream.Read(header[read..]);

            if (count == 0)
            {
                break;
            }

            read += count;
        }

        if (read < header.Length)
        {
            return null;
        }

        // JPEG: FF D8 FF
        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return "image/jpeg";
        }

        // PNG: 89 50 4E 47
        if (header[0] == 0x89 && header[1] == 0x50 &&
            header[2] == 0x4E && header[3] == 0x47)
        {
            return "image/png";
        }

        // WEBP: "RIFF" .... "WEBP"
        if (header[0] == 0x52 && header[1] == 0x49 &&
            header[2] == 0x46 && header[3] == 0x46 &&
            header[8] == 0x57 && header[9] == 0x45 &&
            header[10] == 0x42 && header[11] == 0x50)
        {
            return "image/webp";
        }

        return null;
    }
}

// Резултат от записването на един файл.
public sealed record StoredImage(
    string StoredFileName,
    string ContentType,
    long SizeBytes);

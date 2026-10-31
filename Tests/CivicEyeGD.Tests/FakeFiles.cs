using Microsoft.AspNetCore.Http;

namespace CivicEyeGD.Tests;

/// <summary>
/// Фалшиви качени файлове. Съдържанието им започва с истинските
/// „магически“ байтове на съответния формат, защото именно те,
/// а не заявеният MIME тип или името, решават дали файлът се приема.
/// </summary>
public static class FakeFiles
{
    public const int OneMegabyte = 1024 * 1024;

    // JPEG: FF D8 FF
    public static readonly byte[] JpegHeader =
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46,
        0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01
    ];

    // PNG: 89 50 4E 47 0D 0A 1A 0A
    public static readonly byte[] PngHeader =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52
    ];

    // WEBP: "RIFF" + дължина + "WEBP"
    public static readonly byte[] WebpHeader =
    [
        0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00,
        0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38, 0x20
    ];

    // EXE: „MZ“ — знакът, с който започва всеки изпълним файл под Windows.
    public static readonly byte[] ExecutableHeader =
    [
        0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00,
        0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00
    ];

    // --- Цели, структурно валидни файлове -------------------------------
    // Ползват се там, където снимката наистина се записва: прочистването на
    // метаданните чете файла до края и отказва непълен поток.

    /// <summary>
    /// JPEG с APP0 (JFIF), APP1 (EXIF — той трябва да изчезне), един SOS
    /// блок и компресирани байтове, включително „набия“ маркер FF 00.
    /// </summary>
    public static readonly byte[] JpegWithExif =
    [
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x10,
        0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00,
        0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
        0xFF, 0xE1, 0x00, 0x12,
        0x45, 0x78, 0x69, 0x66, 0x00, 0x00, 0x4D, 0x4D,
        0x00, 0x2A, 0x00, 0x00, 0x00, 0x08, 0x00, 0x01,
        0xFF, 0xDA, 0x00, 0x0C,
        0x03, 0x01, 0x00, 0x02, 0x11, 0x03, 0x11, 0x00,
        0x3F, 0x00,
        0x11, 0x22, 0xFF, 0x00, 0x33, 0x44,
        0xFF, 0xD9
    ];

    /// <summary>PNG с IHDR, един eXIf блок, IDAT и IEND.</summary>
    public static readonly byte[] PngWithExif =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
        0xDE,
        0x00, 0x00, 0x00, 0x04, 0x65, 0x58, 0x49, 0x66,
        0x4D, 0x4D, 0x00, 0x2A, 0x12, 0x34, 0x56, 0x78,
        0x00, 0x00, 0x00, 0x02, 0x49, 0x44, 0x41, 0x54,
        0x78, 0x9C, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44,
        0xAE, 0x42, 0x60, 0x82
    ];

    /// <summary>WEBP с кадрови блок VP8  и един EXIF блок.</summary>
    public static readonly byte[] WebpWithExif =
    [
        0x52, 0x49, 0x46, 0x46, 0x1E, 0x00, 0x00, 0x00,
        0x57, 0x45, 0x42, 0x50,
        0x56, 0x50, 0x38, 0x20, 0x06, 0x00, 0x00, 0x00,
        0x30, 0x01, 0x00, 0x9D, 0x01, 0x2A,
        0x45, 0x58, 0x49, 0x46, 0x04, 0x00, 0x00, 0x00,
        0x4D, 0x4D, 0x00, 0x2A
    ];

    /// <summary>
    /// JPEG какъвто го праща телефонът: APP1 с истински TIFF вътре,
    /// ориентация на кадъра и указател към GPS-подблок, а след него
    /// разпознаваем текст за модела на устройството. Ориентацията е
    /// единственото, което прочистването трябва да запази.
    /// </summary>
    public static byte[] JpegWithOrientation(int orientation, bool littleEndian = false)
    {
        var tiff = new List<byte>();

        // TIFF полетата са 2 или 4 байта и редът на байтовете важи за
        // цялото число, не за половините му.
        void Put(int value, int length)
        {
            for (var i = 0; i < length; i++)
            {
                var shift = littleEndian ? i * 8 : (length - 1 - i) * 8;
                tiff.Add((byte)((value >> shift) & 0xFF));
            }
        }

        void U16(int value) => Put(value, 2);
        void U32(int value) => Put(value, 4);

        // TIFF хедър: подпис, номер 42 и адрес на първото IFD.
        tiff.AddRange(littleEndian ? "II"u8 : "MM"u8);
        U16(42);
        U32(8);

        // IFD0: ориентация и указател към GPS-подблока на адрес 38.
        U16(2);
        U16(0x0112); U16(3); U32(1); U16((ushort)orientation); U16(0);
        U16(0x8825); U16(4); U32(1); U32(38);
        U32(0);

        // GPS подблок: празен, но зад него остава името на устройството.
        U16(0);
        U32(0);
        tiff.AddRange("Nikon D40X"u8);

        var exif = new List<byte> { 0x45, 0x78, 0x69, 0x66, 0x00, 0x00 };
        exif.AddRange(tiff);

        var jpeg = new List<byte>
        {
            0xFF, 0xD8,
            0xFF, 0xE0, 0x00, 0x10,
            0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00,
            0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
            0xFF, 0xE1, (byte)((exif.Count + 2) >> 8), (byte)((exif.Count + 2) & 0xFF)
        };

        jpeg.AddRange(exif);
        jpeg.AddRange([
            0xFF, 0xDA, 0x00, 0x0C,
            0x03, 0x01, 0x00, 0x02, 0x11, 0x03, 0x11, 0x00,
            0x3F, 0x00,
            0x11, 0x22, 0xFF, 0x00, 0x33, 0x44,
            0xFF, 0xD9
        ]);

        return jpeg.ToArray();
    }

    public static IFormFile Jpeg(string fileName = "snimka.jpg") =>
        Build(fileName, "image/jpeg", JpegWithExif);

    public static IFormFile Png(string fileName = "snimka.png") =>
        Build(fileName, "image/png", PngWithExif);

    public static IFormFile Webp(string fileName = "snimka.webp") =>
        Build(fileName, "image/webp", WebpWithExif);

    public static IFormFile Image(string fileName, byte[] header)
    {
        return Build(fileName, "image/jpeg", WithPadding(header));
    }

    public static IFormFile Empty(string fileName = "prazno.jpg")
    {
        return Build(fileName, "image/jpeg", Array.Empty<byte>());
    }

    /// <summary>Файл, по-голям от лимита от 5 MB на приложението.</summary>
    public static IFormFile LargerThanLimit(string fileName = "golyamo.jpg")
    {
        return Build(fileName, "image/jpeg", new byte[6 * OneMegabyte]);
    }

    private static byte[] WithPadding(byte[] header)
    {
        var bytes = new byte[Math.Max(header.Length, 64)];

        Array.Copy(header, bytes, header.Length);

        return bytes;
    }

    private static IFormFile Build(string fileName, string contentType, byte[] bytes)
    {
        var stream = new MemoryStream(bytes);

        var file = new FormFile(stream, 0, bytes.Length, name: "Photos", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

        return file;
    }
}

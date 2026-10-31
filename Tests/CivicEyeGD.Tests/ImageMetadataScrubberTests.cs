using CivicEyeGD.Services;

namespace CivicEyeGD.Tests;

/// <summary>
/// Прочистването на метаданните е защитата на автора на сигнала: в EXIF-а
/// на всяка телефонна снимка има GPS-координати. Тестовете проверяват,
/// че те изчезват, а самата снимка остава непокътната.
/// </summary>
public sealed class ImageMetadataScrubberTests
{
    private static string Ascii(byte[] bytes) =>
        System.Text.Encoding.ASCII.GetString(bytes);

    [Fact]
    public void Jpeg_LosesExifButKeepsPictureData()
    {
        var cleaned = ImageMetadataScrubber.Scrub(FakeFiles.JpegWithExif, "image/jpeg");

        Assert.NotNull(cleaned);

        // APP1 с EXIF-ът го няма, JFIF-ът (APP0) остава.
        Assert.DoesNotContain("Exif", Ascii(cleaned));
        Assert.Contains("JFIF", Ascii(cleaned));

        // Маркерите на снимката са за запазване.
        Assert.Equal(0xFF, cleaned[0]);
        Assert.Equal(0xD8, cleaned[1]);
        Assert.Equal(0xFF, cleaned[^2]);
        Assert.Equal(0xD9, cleaned[^1]);

        // Компресираните байтове, включително набитият FF 00, са копирани.
        Assert.Contains((byte)0x11, cleaned);
        Assert.Contains((byte)0x22, cleaned);
        Assert.Contains((byte)0x44, cleaned);
        Assert.True(cleaned.Length < FakeFiles.JpegWithExif.Length);
    }

    [Fact]
    public void Png_LosesExifChunkAndKeepsImageChunks()
    {
        var cleaned = ImageMetadataScrubber.Scrub(FakeFiles.PngWithExif, "image/png");

        Assert.NotNull(cleaned);
        Assert.DoesNotContain("eXIf", Ascii(cleaned));
        Assert.Contains("IHDR", Ascii(cleaned));
        Assert.Contains("IDAT", Ascii(cleaned));
        Assert.Contains("IEND", Ascii(cleaned));

        // Подписът на PNG-а е непокътнат.
        Assert.Equal(0x89, cleaned[0]);
        Assert.Equal(0x4E, cleaned[2]);
    }

    [Fact]
    public void Webp_LosesExifChunkAndFixesContainerSize()
    {
        var cleaned = ImageMetadataScrubber.Scrub(FakeFiles.WebpWithExif, "image/webp");

        Assert.NotNull(cleaned);
        Assert.DoesNotContain("EXIF", Ascii(cleaned));
        Assert.Contains("VP8 ", Ascii(cleaned));
        Assert.Equal("RIFF", Ascii(cleaned.AsSpan(0, 4).ToArray()));
        Assert.Equal("WEBP", Ascii(cleaned.AsSpan(8, 4).ToArray()));

        // Дължината в RIFF-а сочи към реалния размер на файла.
        var declared = cleaned[4] | (cleaned[5] << 8) | (cleaned[6] << 16) | (cleaned[7] << 24);

        Assert.Equal(cleaned.Length - 8, declared);
    }

    [Fact]
    public void TruncatedJpeg_IsRefusedInsteadOfStoredWithMetadata()
    {
        // Файл, който свършва преди EOI: не може да се твърди, че е чист,
        // затова се отказва.
        var truncated = FakeFiles.JpegWithExif[..^2];

        Assert.Null(ImageMetadataScrubber.Scrub(truncated, "image/jpeg"));
    }

    [Fact]
    public void TruncatedPng_IsRefused()
    {
        var truncated = FakeFiles.PngWithExif[..20];

        Assert.Null(ImageMetadataScrubber.Scrub(truncated, "image/png"));
    }

    [Fact]
    public void UnknownContentType_IsRefused()
    {
        Assert.Null(ImageMetadataScrubber.Scrub(FakeFiles.JpegWithExif, "image/svg+xml"));
        Assert.Null(ImageMetadataScrubber.Scrub(FakeFiles.ExecutableHeader, "image/jpeg"));
    }

    [Fact]
    public void ScrubbingTwiceChangesNothing()
    {
        var once = ImageMetadataScrubber.Scrub(FakeFiles.JpegWithExif, "image/jpeg");
        var twice = ImageMetadataScrubber.Scrub(once!, "image/jpeg");

        Assert.Equal(once!, twice);
    }

    // --- Ориентация на кадъра ------------------------------------------
    // Пикселите на портретна телефонна снимка се съхраняват завъртяни, а
    // браузърът ги изправя по числото в EXIF-а. То не е личен данък, затова
    // оцелява прочистването — всичко останало отива навън.

    private static int OrientationOf(byte[] jpeg)
    {
        var text = Ascii(jpeg);
        var exif = text.IndexOf("Exif", StringComparison.Ordinal);

        if (exif < 0)
        {
            return 0;
        }

        // Синтетичният блок, който записваме, е винаги малко-endian:
        // подпис "II", после IFD с един запис и числото в неговите данни.
        Assert.Equal("II", text.Substring(exif + 6, 2));

        var entries = jpeg[exif + 14] | (jpeg[exif + 15] << 8);
        Assert.Equal(1, entries);

        var tag = jpeg[exif + 16] | (jpeg[exif + 17] << 8);
        Assert.Equal(0x0112, tag);

        return jpeg[exif + 24] | (jpeg[exif + 25] << 8);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(6, false)]
    [InlineData(8, true)]
    public void Jpeg_KeepsOrientationAndDropsTheRestOfExif(int orientation, bool littleEndian)
    {
        var cleaned = ImageMetadataScrubber.Scrub(
            FakeFiles.JpegWithOrientation(orientation, littleEndian), "image/jpeg");

        Assert.NotNull(cleaned);
        Assert.Equal(orientation, OrientationOf(cleaned));

        // Координати, модел на телефона и часа ги няма.
        Assert.DoesNotContain("Nikon", Ascii(cleaned));

        // Самата снимка е непокътната: JFIF, компресирани байтове и EOI.
        Assert.Contains("JFIF", Ascii(cleaned));
        Assert.Equal(0xFF, cleaned[^2]);
        Assert.Equal(0xD9, cleaned[^1]);
        Assert.Contains((byte)0x33, cleaned);
    }

    [Fact]
    public void Jpeg_ReadsOrientationFromLittleEndianExifToo()
    {
        // Голяма част от камерите пишат TIFF-а с голям ред на байтовете,
        // а повечето телефони — с малък. И двата реда трябва да се четат.
        var cleaned = ImageMetadataScrubber.Scrub(
            FakeFiles.JpegWithOrientation(8, littleEndian: true), "image/jpeg");

        Assert.NotNull(cleaned);
        Assert.Equal(8, OrientationOf(cleaned));
    }

    [Fact]
    public void Jpeg_WithNormalOrientation_GainsNoExifBlock()
    {
        // Стойност 1 означава „кадрът не е завъртян“: добавянето на блок
        // само би уголемило файла без полза.
        var cleaned = ImageMetadataScrubber.Scrub(
            FakeFiles.JpegWithOrientation(1), "image/jpeg");

        Assert.NotNull(cleaned);
        Assert.DoesNotContain("Exif", Ascii(cleaned));
    }

    [Fact]
    public void Jpeg_OrientationSurvivesASecondScrub()
    {
        var once = ImageMetadataScrubber.Scrub(
            FakeFiles.JpegWithOrientation(8), "image/jpeg");
        var twice = ImageMetadataScrubber.Scrub(once!, "image/jpeg");

        Assert.NotNull(twice);
        Assert.Equal(8, OrientationOf(twice));
        Assert.Equal(once!, twice);
    }

    [Fact]
    public void Jpeg_WithBrokenExif_IsStillCleanedAndNotRefused()
    {
        var damaged = FakeFiles.JpegWithOrientation(6);

        // Разваме подписа на TIFF-а: четенето на ориентация се отказва,
        // но прочистването на останалото не спира.
        damaged[30] = 0x5A;
        damaged[31] = 0x5A;

        var cleaned = ImageMetadataScrubber.Scrub(damaged, "image/jpeg");

        Assert.NotNull(cleaned);
        Assert.DoesNotContain("Nikon", Ascii(cleaned));
        Assert.DoesNotContain("Exif", Ascii(cleaned));
    }
}

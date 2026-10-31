using CivicEyeGD.Services;

namespace CivicEyeGD.Tests;

/// <summary>
/// Проверките за снимки се правят на сървъра и по съдържанието на
/// файла, а не по името или типа, който е заявил браузърът.
/// </summary>
public sealed class ReportImageStorageTests : IDisposable
{
    private readonly TestHost _host = new();

    private ReportImageStorage Storage => _host.Storage;

    [Theory]
    [InlineData("image/jpeg", true)]
    [InlineData("image/png", true)]
    [InlineData("image/webp", true)]
    [InlineData("IMAGE/JPEG", true)]
    [InlineData("text/html", false)]
    [InlineData("application/x-msdownload", false)]
    [InlineData("image/svg+xml", false)]
    [InlineData(null, false)]
    public void IsAllowedContentType_OnlyAcceptsImageTypes(string? contentType, bool expected)
    {
        Assert.Equal(expected, ReportImageStorage.IsAllowedContentType(contentType));
    }

    [Fact]
    public void ValidateFile_RejectsEmptyFile()
    {
        var error = Storage.ValidateFile(FakeFiles.Empty());

        Assert.Equal("Един от избраните файлове е празен.", error);
    }

    [Fact]
    public void ValidateFile_RejectsFileOverFiveMegabytes()
    {
        var error = Storage.ValidateFile(FakeFiles.LargerThanLimit("ogromnata.jpg"));

        Assert.NotNull(error);
        Assert.Contains("5 MB", error);
        Assert.Contains("ogromnata.jpg", error);
    }

    [Fact]
    public void ValidateFile_RejectsExecutableRenamedToJpg()
    {
        var disguised = FakeFiles.Image("virus.exe", FakeFiles.ExecutableHeader);

        var error = Storage.ValidateFile(disguised);

        Assert.NotNull(error);
        Assert.Contains("не е сред поддържаните формати", error);
    }

    [Theory]
    [MemberData(nameof(ValidHeaders))]
    public void ValidateFile_AcceptsRealImageSignatures(string fileName, byte[] header)
    {
        Assert.Null(Storage.ValidateFile(FakeFiles.Image(fileName, header)));
    }

    public static TheoryData<string, byte[]> ValidHeaders() => new()
    {
        { "shnipka.jpg", FakeFiles.JpegHeader },
        { "shnipka.png", FakeFiles.PngHeader },
        { "shnipka.webp", FakeFiles.WebpHeader }
    };

    [Fact]
    public async Task SaveAsync_IgnoresClientFileName_AndUsesDetectedExtension()
    {
        var uploaded = FakeFiles.Png("vazmozhno-opasno.exe");

        var stored = await Storage.SaveAsync(uploaded);

        Assert.EndsWith(".png", stored.StoredFileName);
        Assert.Equal("image/png", stored.ContentType);

        // Името е генерирано, затова не съдържа нищо от заявеното име.
        Assert.DoesNotContain("vazmozhno", stored.StoredFileName);
        Assert.True(Guid.TryParse(stored.StoredFileName[..32], out _));

        var fullPath = Storage.GetFullPath(stored.StoredFileName);

        Assert.NotNull(fullPath);
        Assert.StartsWith(
            Path.Combine(_host.RootDirectory, "App_Data", "report-images"),
            fullPath);
    }

    [Fact]
    public async Task SaveAsync_ThrowsOnUnsupportedContent()
    {
        var disguised = FakeFiles.Image("notMasked.jpg", FakeFiles.ExecutableHeader);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Storage.SaveAsync(disguised));

        Assert.Contains("не е сред поддържаните формати", error.Message);
    }

    [Fact]
    public void GetFullPath_ReturnsNullForMissingOrEmptyName()
    {
        Assert.Null(Storage.GetFullPath("niamatakavfail.png"));
        Assert.Null(Storage.GetFullPath(""));
        Assert.Null(Storage.GetFullPath("   "));
    }

    [Fact]
    public async Task GetFullPath_StaysInsideTheImagesFolder()
    {
        // Файл извън папката на снимките — не трябва да е достъпен.
        var outsidePath = Path.Combine(_host.RootDirectory, "secret.txt");
        await File.WriteAllTextAsync(outsidePath, "нещо");

        var stored = await Storage.SaveAsync(FakeFiles.Jpeg("s.jpg"));

        // Опитът за излизане от папката се свежда до самото име,
        // така че никога не сочи към файла до корена.
        Assert.Null(Storage.GetFullPath("../secret.txt"));
        Assert.NotNull(Storage.GetFullPath(stored.StoredFileName));
    }

    [Fact]
    public async Task Delete_RemovesStoredFile()
    {
        var stored = await Storage.SaveAsync(FakeFiles.Jpeg("s.jpg"));

        Storage.Delete(stored.StoredFileName);

        Assert.Null(Storage.GetFullPath(stored.StoredFileName));
    }

    [Fact]
    public void Delete_IgnoresUnknownName()
    {
        Storage.Delete("niamatakavfail.png");
    }

    [Fact]
    public void Constants_MatchTheDocumentedLimits()
    {
        Assert.Equal(3, ReportImageStorage.MaxImagesPerReport);
        Assert.Equal(5 * 1024 * 1024, ReportImageStorage.MaxFileSizeBytes);
    }

    public void Dispose() => _host.Dispose();
}

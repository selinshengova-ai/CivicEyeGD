using CivicEyeGD.Data;
using CivicEyeGD.Pages;
using CivicEyeGD.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;

namespace CivicEyeGD.Tests;

/// <summary>
/// Формата „Нов сигнал" проверява полетата и на сървъра, защото заявка
/// може да заобиколи браузъра. Тези тестове минават през истинския
/// handler върху временна SQLite база.
/// </summary>
public sealed class CreateReportTests : IDisposable
{
    private readonly TestHost _host = new();
    private string? _userId;

    [Fact]
    public async Task ValidReport_IsSavedWithAuthorAndFirstRating()
    {
        var model = await PageAsync();
        model.Report = ValidInput();

        Assert.True(model.ValidateInput());

        var result = await model.OnPostAsync();

        Assert.IsType<RedirectToPageResult>(result);

        var report = await _host.Context.Reports
            .Include(r => r.SeverityRatings)
            .SingleAsync();

        Assert.Equal(ReportStatuses.New, report.Status);
        Assert.Equal(_userId, report.UserId);

        var rating = Assert.Single(report.SeverityRatings);
        Assert.Equal(4, rating.Rating);
        Assert.Equal(_userId, rating.UserId);
    }

    [Fact]
    public async Task SavedReport_HasTrimmedText()
    {
        var model = await PageAsync();
        model.Report = ValidInput();
        model.Report.Title = "   Счупена лампа   ";
        model.Report.Description = "  Тъмно е през нощта.  ";

        await model.OnPostAsync();

        var report = await _host.Context.Reports.SingleAsync();

        Assert.Equal("Счупена лампа", report.Title);
        Assert.Equal("Тъмно е през нощта.", report.Description);
    }

    [Fact]
    public async Task Photos_AreStoredOnDiskAndAttachedToTheReport()
    {
        var model = await PageAsync();
        model.Report = ValidInput();
        model.Photos =
        [
            FakeFiles.Jpeg("pyt.jpg"),
            FakeFiles.Png("park.png")
        ];

        await model.OnPostAsync();

        var report = await _host.Context.Reports.Include(r => r.Images).SingleAsync();

        Assert.Equal(2, report.Images.Count);
        Assert.All(report.Images, image =>
            Assert.NotNull(_host.Storage.GetFullPath(image.StoredFileName)));

        // Снимката на диска вече няма EXIF: GPS-координатите на автора
        // не трябва да стигат до публичната страница.
        var jpeg = report.Images.Single(image => image.ContentType == "image/jpeg");
        var storedAscii = System.Text.Encoding.ASCII.GetString(
            File.ReadAllBytes(_host.Storage.GetFullPath(jpeg.StoredFileName)!));

        Assert.DoesNotContain("Exif", storedAscii);
        Assert.Contains("JFIF", storedAscii);
    }

    [Fact]
    public async Task MoreThanThreePhotos_IsRejected()
    {
        var model = await PageAsync();
        model.Report = ValidInput();
        model.Photos = Enumerable.Range(1, 4)
            .Select(index => FakeFiles.Image($"snimka{index}.jpg", FakeFiles.JpegHeader))
            .ToList();

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains("най-много 3 снимки", ErrorsFor(model, nameof(CreateModel.Photos)));
        Assert.Equal(0, await _host.Context.Reports.CountAsync());
    }

    [Fact]
    public async Task DisguisedExecutablePhoto_IsRejectedOnTheServer()
    {
        var model = await PageAsync();
        model.Report = ValidInput();
        model.Photos = [FakeFiles.Image("snimka.jpg", FakeFiles.ExecutableHeader)];

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains(
            "не е сред поддържаните формати",
            ErrorsFor(model, nameof(CreateModel.Photos)));
        Assert.Equal(0, await _host.Context.Reports.CountAsync());
    }

    [Fact]
    public async Task MissingCoordinates_IsRejected()
    {
        var model = await PageAsync();
        model.Report = ValidInput();
        model.Report.Latitude = null;
        model.Report.Longitude = null;

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains("Отбележи мястото на картата", ErrorsFor(model, string.Empty));
        Assert.Equal(0, await _host.Context.Reports.CountAsync());
    }

    [Fact]
    public async Task CoordinatesOutsideTheTown_IsRejected()
    {
        var model = await PageAsync();
        model.Report = ValidInput();
        model.Report.Latitude = 42.6977;
        model.Report.Longitude = 23.3219;

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains("извън границите на Гоце Делчев", ErrorsFor(model, string.Empty));
        Assert.Equal(0, await _host.Context.Reports.CountAsync());
    }

    [Fact]
    public async Task CategoryFromOutsideTheList_IsRejected()
    {
        var model = await PageAsync();
        model.Report = ValidInput();
        model.Report.Category = "Личен проблем";

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains(
            "Избери категория от списъка",
            ErrorsFor(model, CategoryKey));
        Assert.Equal(0, await _host.Context.Reports.CountAsync());
    }

    [Fact]
    public async Task EmptyTitle_IsRejected()
    {
        var model = await PageAsync();
        model.Report = ValidInput();
        model.Report.Title = string.Empty;

        Assert.False(model.ValidateInput());

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Equal(0, await _host.Context.Reports.CountAsync());
    }

    [Fact]
    public async Task SeverityOutOfRange_IsRejected()
    {
        var model = await PageAsync();
        model.Report = ValidInput();
        model.Report.Severity = 9;

        Assert.False(model.ValidateInput());

        await model.OnPostAsync();

        Assert.Contains("между 1 и 5", ErrorsFor(model, SeverityKey));
    }

    [Fact]
    public async Task TitleOverOneHundredCharacters_IsRejected()
    {
        var model = await PageAsync();
        model.Report = ValidInput();
        model.Report.Title = new string('а', 101);

        Assert.False(model.ValidateInput());
        Assert.Equal(0, await _host.Context.Reports.CountAsync());
    }

    [Fact]
    public async Task AnonymousPost_FailsGracefullyInsteadOfCrashing()
    {
        // Формата изисква влизане, но handler-ът не трябва да чупи
        // сървъра при директна заявка без потребителска идентичност.
        var model = _host.CreatePageModel<TestCreateModel>();
        model.Report = ValidInput();

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains("не беше записан", ErrorsFor(model, string.Empty));
        Assert.Equal(0, await _host.Context.Reports.CountAsync());
    }

    private const string CategoryKey =
        nameof(CreateModel.Report) + "." + nameof(CreateModel.ReportInput.Category);

    private const string SeverityKey =
        nameof(CreateModel.Report) + "." + nameof(CreateModel.ReportInput.Severity);

    private static CreateModel.ReportInput ValidInput() => new()
    {
        Title = "Дупка на тротоара",
        Category = ReportCategories.Sidewalks,
        Address = "ул. „Пирин“ 12",
        Description = "Тротарната плоча е хлътнала и събира вода.",
        Latitude = 41.5713,
        Longitude = 23.7361,
        Severity = 4
    };

    private static string ErrorsFor(PageModel model, string key)
    {
        var errors = model.ModelState.TryGetValue(key, out var entry)
            ? entry?.Errors
            : null;

        return string.Join(" ", errors?.Select(error => error.ErrorMessage) ?? []);
    }

    private async Task<TestCreateModel> PageAsync()
    {
        _userId ??= await _host.CreateUserAsync("igrivach1");

        return _host.CreatePageModel<TestCreateModel>(_host.PrincipalFor(_userId));
    }

    public void Dispose() => _host.Dispose();

    /// <summary>
    /// Същият model, но с достъп до проверка на анотациите. Платформата
    /// прави това сама преди OnPostAsync през TryValidateModel, който
    /// извън истинския invoker на страницата няма нужните обекти,
    /// затова тук валидаторът се извиква директно, а съобщенията се
    /// записват с същите ключове, за да минават през гейта на handler-а.
    /// </summary>
    private sealed class TestCreateModel : CreateModel
    {
        public TestCreateModel(
            ApplicationDbContext context,
            UserManager<IdentityUser> userManager,
            ReportImageStorage imageStorage,
            IConfiguration configuration,
            ILogger<CreateModel> logger)
            : base(context, userManager, imageStorage, configuration, logger)
        {
        }

        public bool ValidateInput()
        {
            var results = new List<ValidationResult>();

            var isValid = Validator.TryValidateObject(
                Report,
                new ValidationContext(Report),
                results,
                validateAllProperties: true);

            foreach (var result in results)
            {
                var field = result.MemberNames.FirstOrDefault();

                ModelState.AddModelError(
                    $"{nameof(Report)}.{field}",
                    result.ErrorMessage ?? "Полето не е попълнено.");
            }

            return isValid;
        }
    }
}

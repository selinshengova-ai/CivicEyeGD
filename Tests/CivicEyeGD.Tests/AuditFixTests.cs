using System.Data;
using System.Text;
using CivicEyeGD.Data;
using CivicEyeGD.Pages;
using CivicEyeGD.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicEyeGD.Tests;

/// <summary>
/// Проверки на поправките от одита: или по-скоро на свойствата, на
/// които тези поправки разчитат. Тестът за транзакцията не проверява
/// кода на страницата, а обещанието на доставчика — ако то се смени,
/// защитата срещу дубликати тихо изчезва.
/// </summary>
public sealed class AuditFixTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task TheDuplicateCheckTransactionBlocksASecondWriter()
    {
        // Проверката за близък сигнал и записът са в една транзакция.
        // Тя има смисъл само ако заключването се взима още при
        // отварянето: тогава втора заявка изчаква първата и вижда реда
        // ѝ, вместо да прочете старо състояние и да запише дубликат.
        // Тестът проверява точно това обещание на доставчика — ако
        // транзакцията стане отложена, вторият BeginTransaction ще мине
        // и тестът ще падне, преди дубликатите да се появят в сайта.
        var path = Path.Combine(
            Path.GetTempPath(),
            "civiceye-lock-" + Guid.NewGuid().ToString("N") + ".db");

        try
        {
            using var first = new SqliteConnection($"Data Source={path}");
            first.Open();
            using var transaction = first.BeginTransaction(deferred: false);

            // EF отваря транзакцията си по същия начин. Кратката
            // граница на изчакване пази теста от 30-те секунди по
            // подразбиране: интересува ни отказът, не чакането.
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite($"Data Source={path};Default Timeout=1")
                .Options;

            await using var second = new ApplicationDbContext(options);

            var error = await Assert.ThrowsAsync<SqliteException>(async () =>
                await second.Database.BeginTransactionAsync(IsolationLevel.Serializable));

            // 5 = SQLITE_BUSY: заключването е заето, а не отложено.
            Assert.Equal(5, error.SqliteErrorCode);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SearchTreatsPercentSignAsText()
    {
        // Търсенето е по това, което човек е написал. Знакът % не бива
        // да работи като „всичко“ — иначе заявка с един знак връща целия
        // град и страницата става безсмислено тежка.
        var user = await _host.CreateUserAsync("tarsesht");
        await _host.AddReportAsync(user, report => report.Title = "Дупка на ъгъла");

        var model = _host.CreatePageModel<ReportsModel>();
        await model.OnGetAsync(search: "%", category: null, status: null, sort: null);

        Assert.Empty(model.Reports);
    }

    [Fact]
    public async Task ThePublicListStopsAtThreeHundredReports()
    {
        // Таванът пази сървъра от заявка, която зарежда цялата база с
        // гласовете и снимките. Страницата казва, че е ограничена.
        var user = await _host.CreateUserAsync("mnogo");

        for (var index = 0; index < ReportsModel.MaxShownReports + 5; index++)
        {
            _host.Context.Reports.Add(new Report
            {
                Title = "Сигнал " + index,
                Description = "Описание",
                Category = ReportCategories.RoadSurface,
                Address = "ул. „Пирин“ " + index,
                Status = ReportStatuses.New,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(index),
                Latitude = null,
                Longitude = null,
                UserId = user
            });
        }

        await _host.Context.SaveChangesAsync();

        var model = _host.CreatePageModel<ReportsModel>();
        await model.OnGetAsync(search: null, category: null, status: null, sort: null);

        Assert.Equal(ReportsModel.MaxShownReports, model.Reports.Count);
        Assert.True(model.IsLimited);
    }

    [Fact]
    public async Task TheOfficePanelDoesNotEraseTheAdjustmentWhenTheNumberIsMissing()
    {
        // Празно поле не е нула. Ако сървърът го прочете като нула,
        // корекцията на приоритета се изтрива, без никой да го е искал.
        var adminId = await _host.CreateAdministratorAsync("administrator");
        var citizenId = await _host.CreateUserAsync("grazhdanin");
        var report = await _host.AddReportAsync(
            citizenId,
            item => item.AdminPriorityAdjustment = 4);

        var model = _host.CreatePageModel<DetailsModel>(_host.PrincipalFor(adminId));

        var result = await model.OnPostOfficeAsync(
            report.Id,
            ReportStatuses.InProgress,
            "Екипът е изпратен.",
            adjustment: null);

        Assert.IsType<PageResult>(result);
        Assert.NotNull(model.ErrorMessage);

        var stored = await _host.Context.Reports
            .AsNoTracking()
            .SingleAsync(item => item.Id == report.Id);

        Assert.Equal(4, stored.AdminPriorityAdjustment);
        Assert.Equal(ReportStatuses.New, stored.Status);
    }

    [Fact]
    public void CsvNeutralizesLeadingTabAndNewLine()
    {
        // Excel прескача табулатор и нов ред преди да реши, че редът е
        // формула. Затова неутрализирането не е само за = + - @.
        var csv = Encoding.UTF8.GetString(new ReportExportService().BuildCsv(
        [
            new ReportExportRow(
                1,
                "\t=1+1",
                "ред\nнов",
                ReportCategories.RoadSurface,
                ReportStatuses.New,
                "@адрес",
                new DateTime(2026, 11, 17, 9, 0, 0),
                3.0,
                0,
                42.9,
                "Среден приоритет")
        ]));

        Assert.Contains("\"'\t=1+1\"", csv);
        Assert.Contains("\"'@адрес\"", csv);
    }

    [Fact]
    public void TheWorkbookDropsCharactersThatXmlForbids()
    {
        // .xlsx е архив от XML части. Управляващ знак в описанието би
        // направил книгата невалидна и Excel би отказал всички редове,
        // не само един.
        var bytes = new ReportExportService().BuildWorkbook(
        [
            new ReportExportRow(
                1,
                "Заглавие\u0001",
                "Описание\u0002 с текст",
                ReportCategories.RoadSurface,
                ReportStatuses.New,
                "ул. „Пирин“ 4",
                new DateTime(2026, 11, 17, 9, 0, 0),
                3.0,
                0,
                42.9,
                "Среден приоритет")
        ]);

        using var stream = new MemoryStream(bytes);
        using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
        var sheet = workbook.Worksheet(1);

        Assert.Equal("Заглавие", sheet.Cell(2, 2).GetString());
        Assert.Equal("Описание с текст", sheet.Cell(2, 3).GetString());
    }

    [Fact]
    public async Task TheJournalSurvivesDeletingAnOfficerTheContextDoesNotTrack()
    {
        // Правото на изтриване трябва да работи и когато редът от
        // дневника не е зареден в контекста. Тогава EF не може да
        // нулира връзката сам — това прави базата, затова външният
        // ключ е с ON DELETE SET NULL.
        var officerId = await _host.CreateAdministratorAsync("sluzhitel");
        var citizenId = await _host.CreateUserAsync("grazhdanin");
        var report = await _host.AddReportAsync(citizenId);

        _host.Context.StatusHistories.Add(new StatusHistory
        {
            ReportId = report.Id,
            ChangedByUserId = officerId,
            OldStatus = ReportStatuses.New,
            NewStatus = ReportStatuses.InProgress,
            Comment = "Екипът е насочен."
        });

        await _host.Context.SaveChangesAsync();

        // Нищо от тези редове не бива да е в паметта: тестът проверява
        // именно поведението на базата.
        _host.Context.ChangeTracker.Clear();

        var users = _host.Services.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Microsoft.AspNetCore.Identity.IdentityUser>>();
        var officer = await users.FindByIdAsync(officerId);
        Assert.NotNull(officer);

        var deleted = await users.DeleteAsync(officer);
        Assert.True(deleted.Succeeded);

        var entry = await _host.Context.StatusHistories
            .AsNoTracking()
            .SingleAsync(item => item.ReportId == report.Id);

        Assert.Null(entry.ChangedByUserId);
        Assert.Equal("Екипът е насочен.", entry.Comment);
    }

    [Fact]
    public async Task TheSweepRemovesOnlyOldFilesWithoutARow()
    {
        var user = await _host.CreateUserAsync("chistach");
        await _host.AddReportAsync(user, report => report.Images.Add(new ReportImage
        {
            StoredFileName = "kept.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 32,
            CreatedAt = DateTime.UtcNow
        }));

        await _host.Context.SaveChangesAsync();

        var directory = Path.Combine(_host.RootDirectory, "App_Data", "report-images");
        var now = DateTime.UtcNow;

        var kept = Path.Combine(directory, "kept.jpg");
        var oldOrphan = Path.Combine(directory, "orphan-old.jpg");
        var youngOrphan = Path.Combine(directory, "orphan-new.jpg");

        File.WriteAllBytes(kept, [1, 2, 3]);
        File.WriteAllBytes(oldOrphan, [1, 2, 3]);
        File.WriteAllBytes(youngOrphan, [1, 2, 3]);
        File.SetLastWriteTimeUtc(oldOrphan, now.AddDays(-2));

        var removed = await ReportImageJanitor.SweepAsync(
            _host.Context,
            _host.Storage,
            now,
            NullLogger.Instance);

        Assert.Equal(1, removed);
        Assert.True(File.Exists(kept));
        Assert.True(File.Exists(youngOrphan));
        Assert.False(File.Exists(oldOrphan));
    }
}

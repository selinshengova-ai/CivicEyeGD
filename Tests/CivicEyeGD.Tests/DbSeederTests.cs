using CivicEyeGD.Data;
using CivicEyeGD.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CivicEyeGD.Tests;

/// <summary>
/// Seed-ът на демо акаунтите е единствената част от приложението, която
/// работи с пароли. Тестове заключват, че те идват само от конфигурация:
/// без ключ акаунт няма, слаба парола не минава, а новата парола в
/// конфигурацията заменя старата в базата.
///
/// Втората група пази самите примерни сигнали: те трябва да покриват
/// всяка категория и статус, дневникът им да е свързан с делото, за
/// което говори, и да оставят поне един спешен сигнал за демонстрацията.
/// </summary>
public sealed class DbSeederTests : IDisposable
{
    private const string CitizenEmail = "citizen.demo@civiceye.test";
    private const string AdminEmail = "admin.demo@civiceye.test";
    private const string SecondEmail = "citizen.two@civiceye.test";
    private const string CitizenPassword = "Parsvata!2026a";

    private readonly TestHost _host = new();

    private UserManager<IdentityUser> Users => _host.Services.GetRequiredService<UserManager<IdentityUser>>();

    [Fact]
    public async Task WithoutConfiguredPasswords_OnlyRolesAreCreated()
    {
        await DbSeeder.SeedAsync(_host.Services, TestHost.Config());

        Assert.Empty(await _host.Context.Users.ToListAsync());
        Assert.Empty(await _host.Context.Reports.ToListAsync());
        Assert.Equal(2, await _host.Context.Roles.CountAsync());
    }

    [Fact]
    public async Task ConfiguredPasswords_CreateAccountsSampleSignalsAndHistory()
    {
        await DbSeeder.SeedAsync(_host.Services, WithAllVoters());

        var admin = await Users.FindByEmailAsync(AdminEmail);
        Assert.NotNull(admin);
        Assert.True(await Users.IsInRoleAsync(admin, "Administrator"));

        var citizen = await Users.FindByEmailAsync(CitizenEmail);
        Assert.NotNull(citizen);
        Assert.True(await Users.IsInRoleAsync(citizen, "Citizen"));
        Assert.True(await Users.CheckPasswordAsync(citizen, CitizenPassword));

        Assert.NotNull(await Users.FindByEmailAsync(SecondEmail));
        Assert.Equal(17, await _host.Context.Reports.CountAsync());

        // Преходите по статус ги пише администраторът, защото и в таблата
        // само той може да ги направи.
        var history = await _host.Context.StatusHistories.ToListAsync();

        Assert.NotEmpty(history);
        Assert.All(history, entry => Assert.Equal(admin.Id, entry.ChangedByUserId));
        Assert.All(history, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Comment)));
    }

    [Fact]
    public async Task PasswordInConfiguration_ReplacesTheOneInDatabase()
    {
        await DbSeeder.SeedAsync(_host.Services, TestHost.Config(
            ("SeedAccounts:CitizenPassword", CitizenPassword)));

        var citizen = await Users.FindByEmailAsync(CitizenEmail);

        Assert.NotNull(citizen);
        Assert.True(await Users.CheckPasswordAsync(citizen, CitizenPassword));

        await DbSeeder.SeedAsync(_host.Services, TestHost.Config(
            ("SeedAccounts:CitizenPassword", "Novata!2026c")));

        citizen = await Users.FindByEmailAsync(CitizenEmail);

        Assert.NotNull(citizen);
        Assert.True(await Users.CheckPasswordAsync(citizen, "Novata!2026c"));
        Assert.False(await Users.CheckPasswordAsync(citizen, CitizenPassword));
    }

    [Fact]
    public async Task PasswordBelowTheRules_LeavesNoHalfMadeAccount()
    {
        await DbSeeder.SeedAsync(_host.Services, TestHost.Config(
            ("SeedAccounts:CitizenPassword", "abc")));

        Assert.Null(await Users.FindByEmailAsync(CitizenEmail));
        Assert.Empty(await _host.Context.Users.ToListAsync());
    }

    [Fact]
    public async Task MissingSecondVoter_KeepsTheSampleSignalsOut()
    {
        await DbSeeder.SeedAsync(_host.Services, TestHost.Config(
            ("SeedAccounts:CitizenPassword", CitizenPassword),
            ("SeedAccounts:AdminPassword", "Adminovata!26")));

        Assert.Equal(2, await _host.Context.Users.CountAsync());
        Assert.Empty(await _host.Context.Reports.ToListAsync());
    }

    [Fact]
    public async Task EmailFromConfiguration_IsTheOneUsed()
    {
        await DbSeeder.SeedAsync(_host.Services, TestHost.Config(
            ("SeedAccounts:CitizenEmail", "moeto.demo@civiceye.test"),
            ("SeedAccounts:CitizenPassword", CitizenPassword)));

        Assert.Null(await Users.FindByEmailAsync(CitizenEmail));
        Assert.NotNull(await Users.FindByEmailAsync("moeto.demo@civiceye.test"));
    }

    [Fact]
    public async Task DemoReportsSwitchedOff_KeepsTheTownAsTheAuthorLeftIt()
    {
        await DbSeeder.SeedAsync(_host.Services, TestHost.Config(
            ("Seed:DemoReports", "false"),
            ("SeedAccounts:CitizenPassword", CitizenPassword),
            ("SeedAccounts:AdminPassword", "Adminovata!26"),
            ("SeedAccounts:SecondCitizenPassword", "Vtoriata!2026")));

        // Акаунтите са задължителни по регламента, затова се създават и без
        // примерните сигнали – само те се изключват с ключа.
        Assert.Equal(3, await _host.Context.Users.CountAsync());
        Assert.Empty(await _host.Context.Reports.ToListAsync());
    }

    [Fact]
    public async Task SampleSignalsCoverEveryCategoryAndStatus()
    {
        await DbSeeder.SeedAsync(_host.Services, WithAllVoters());

        var reports = await _host.Context.Reports.ToListAsync();

        // Една категория или статус без пример се вижда веднага по
        // топлинната матрица в PDF отчета: редът или колоната остават
        // празни и числата зад тях изглеждат сгрешени.
        foreach (var category in ReportCategories.All)
        {
            Assert.Contains(reports, report => report.Category == category);
        }

        foreach (var status in ReportStatuses.All)
        {
            Assert.Contains(reports, report => report.Status == status);
        }
    }

    [Fact]
    public async Task SampleSignalDatesStayBehindTheMomentTheyDescribe()
    {
        await DbSeeder.SeedAsync(_host.Services, WithAllVoters());

        var now = DateTime.UtcNow;

        var reports = await _host.Context.Reports
            .Include(report => report.StatusHistories)
            .Include(report => report.SeverityRatings)
            .Include(report => report.Confirmations)
            .ToListAsync();

        // Дните в seed-а са относителни спрямо старта. Сгрешена стойност
        // ги пуска в бъдещето и тогава витрината брои „решени този месец“
        // за сигнал, който още не е станал.
        foreach (var report in reports)
        {
            Assert.True(report.CreatedAt <= now);

            foreach (var step in report.StatusHistories)
            {
                Assert.True(step.ChangedAt > report.CreatedAt);
                Assert.True(step.ChangedAt <= now);
            }

            foreach (var rating in report.SeverityRatings)
            {
                Assert.True(rating.CreatedAt >= report.CreatedAt && rating.CreatedAt <= now);
            }

            foreach (var confirmation in report.Confirmations)
            {
                Assert.True(confirmation.CreatedAt >= report.CreatedAt && confirmation.CreatedAt <= now);
            }
        }
    }

    [Fact]
    public async Task SampleSignalsLeaveOneReportUrgentOnTheMap()
    {
        await DbSeeder.SeedAsync(_host.Services, WithAllVoters());

        var priority = new ReportPriorityService();

        var reports = await _host.Context.Reports
            .Include(report => report.SeverityRatings)
            .Include(report => report.Confirmations)
            .ToListAsync();

        // Пулсиращият маркер се ражда само от сигнал, минал прага по
        // гласовете на Други. Без такъв в примерните данни най-видното
        // доказателство на защитата просто липсва.
        Assert.Contains(
            reports,
            report => report.Status == ReportStatuses.New && priority.IsUrgent(report));
    }

    [Fact]
    public async Task SampleSignalsShowBothASlowAndAFastOfficeReaction()
    {
        await DbSeeder.SeedAsync(_host.Services, WithAllVoters());

        var reports = await _host.Context.Reports
            .Include(report => report.StatusHistories)
            .ToListAsync();

        var reactions = reports
            .Where(report => report.StatusHistories.Count > 0)
            .Select(report => report.StatusHistories.Min(step => (step.ChangedAt - report.CreatedAt).TotalDays))
            .ToList();

        Assert.NotEmpty(reactions);

        // Средното време за реакция има смисъл само ако извадката е
        // различна: един и същ брой дни за всеки сигнал би показал
        // число, което нищо не описва.
        Assert.True(reactions.Max() >= 10);
        Assert.True(reactions.Min() <= 5);
        Assert.True(reactions.Distinct().Count() > 3);
    }

    [Fact]
    public async Task SampleSignalsRecordTheAdministratorAdjustmentWhereClaimed()
    {
        await DbSeeder.SeedAsync(_host.Services, WithAllVoters());

        var reports = await _host.Context.Reports
            .Include(report => report.StatusHistories)
            .Where(report => report.AdminPriorityAdjustment != 0)
            .ToListAsync();

        Assert.NotEmpty(reports);

        // Корекцията без запис в дневника е невидима намеса, а тя е
        // точно противното на това, което страницата на сигнала обещава.
        foreach (var report in reports)
        {
            Assert.Contains(
                report.StatusHistories,
                step => step.NewPriorityAdjustment == report.AdminPriorityAdjustment);
        }
    }

    [Fact]
    public async Task SampleSignalsAreSpreadAcrossTheTownWithoutColliding()
    {
        await DbSeeder.SeedAsync(_host.Services, WithAllVoters());

        var places = (await _host.Context.Reports.ToListAsync())
            .Select(report => (report.Id, Latitude: report.Latitude!.Value, Longitude: report.Longitude!.Value))
            .ToList();

        // И двата набора от числа са за картата: точка извън кутията не
        // се вижда изобщо, а две в рамките на радиуса за дубликати се
        // сливат в един маркер — и примерните данни започват да си
        // противоречат сами със собствената си проверка.
        foreach (var (id, latitude, longitude) in places)
        {
            Assert.True(
                ReportGeography.IsWithinTown(latitude, longitude),
                $"Сигнал {id} е извън границите на града.");
        }

        foreach (var first in places)
        {
            foreach (var second in places.Where(other => other.Id > first.Id))
            {
                var meters = ReportGeography.DistanceMeters(
                    first.Latitude,
                    first.Longitude,
                    second.Latitude,
                    second.Longitude);

                Assert.True(
                    meters > ReportDuplicates.RadiusMeters,
                    $"Сигнали {first.Id} и {second.Id} са на {meters:F0} m — "
                    + "една точка за два различни проблема.");
            }
        }
    }

    private static IConfiguration WithAllVoters() => TestHost.Config(
        ("SeedAccounts:CitizenPassword", CitizenPassword),
        ("SeedAccounts:AdminPassword", "Adminovata!26"),
        ("SeedAccounts:SecondCitizenPassword", "Vtoriata!2026"));

    public void Dispose() => _host.Dispose();
}

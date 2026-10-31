using CivicEyeGD.Data;
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
        Assert.Equal(8, await _host.Context.Reports.CountAsync());

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

    private static IConfiguration WithAllVoters() => TestHost.Config(
        ("SeedAccounts:CitizenPassword", CitizenPassword),
        ("SeedAccounts:AdminPassword", "Adminovata!26"),
        ("SeedAccounts:SecondCitizenPassword", "Vtoriata!2026"));

    public void Dispose() => _host.Dispose();
}

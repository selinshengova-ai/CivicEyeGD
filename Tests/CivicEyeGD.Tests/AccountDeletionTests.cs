using System.ComponentModel.DataAnnotations;
using CivicEyeGD.Data;
using CivicEyeGD.Pages.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicEyeGD.Tests;

/// <summary>
/// Изтриването на профил е единствената страница, която унищожава акаунт,
/// затова тестовете гледат две неща: кога то изобщо се случва и какво
/// остава след него. Правото на забравяне не трябва да изтрие чужд профил
/// нито сигналите на града — и двете са записани като поведение на базата
/// в Data/ApplicationDbContext.cs, а не като намерение в текста.
/// </summary>
public sealed class AccountDeletionTests : IDisposable
{
    private const string Password = "Parsvata!2026a";

    private const string PasswordKey = nameof(DeleteModel.Input) + "." + nameof(DeleteModel.DeleteInput.Password);
    private const string ConfirmedKey = nameof(DeleteModel.Input) + "." + nameof(DeleteModel.DeleteInput.Confirmed);

    private readonly TestHost _host = new();

    private UserManager<IdentityUser> Users
        => _host.Services.GetRequiredService<UserManager<IdentityUser>>();

    [Fact]
    public async Task ConfirmedDeletion_RemovesTheAccountAndLeavesTheSite()
    {
        var userId = await CreateUserAsync("gost1");
        var model = Page<DeleteModel>(userId);
        model.Input = Form(Password, confirmed: true);

        var result = await model.OnPostAsync();

        // Посоката е публичната страница: след изтриването человекът вече
        // не е влязъл, а профилът не съществува, за да го върне обратно.
        Assert.Equal("/Welcome", Assert.IsType<RedirectToPageResult>(result).PageName);
        Assert.Null(await Users.FindByIdAsync(userId));
        Assert.Equal(0, await _host.Context.Users.CountAsync());
    }

    [Fact]
    public async Task WithoutTheCheckbox_NeitherPasswordIsEnough()
    {
        var userId = await CreateUserAsync("gost2");
        var model = Page<DeleteModel>(userId);
        model.Input = Form(Password, confirmed: false);

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains(
            "Отбележи, че разбираш",
            ErrorsFor(model, ConfirmedKey));

        Assert.NotNull(await Users.FindByIdAsync(userId));
    }

    [Fact]
    public async Task WrongPassword_LeavesTheAccountExactlyAsItWas()
    {
        var userId = await CreateUserAsync("gost3");
        var model = Page<DeleteModel>(userId);
        model.Input = Form("Gresna!2026x", confirmed: true);

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains("Невалидна парола.", ErrorsFor(model, PasswordKey));

        var user = await Users.FindByIdAsync(userId);

        Assert.NotNull(user);
        Assert.True(await Users.CheckPasswordAsync(user, Password));
    }

    [Fact]
    public async Task EmptyPassword_IsRefusedBeforeTheStoreIsAsked()
    {
        var userId = await CreateUserAsync("gost4");
        var model = Page<TestDeleteModel>(userId);
        model.Input = Form(string.Empty, confirmed: true);

        Assert.False(model.ValidateInput());
        Assert.Contains("Въведи паролата си.", ErrorsFor(model, PasswordKey));

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.NotNull(await Users.FindByIdAsync(userId));
    }

    [Fact]
    public async Task AFormLeftOpenPastTheCookie_SendsTheVisitorToLogin()
    {
        await CreateUserAsync("gost5");

        // Празно удостоверение: няма как да се изтрие профил, който
        // заявката не сочи, затова входът е единственият честен отговор.
        var model = _host.CreatePageModel<DeleteModel>();
        model.Input = Form(Password, confirmed: true);

        var result = await model.OnPostAsync();

        Assert.Equal("/Account/Login", Assert.IsType<RedirectToPageResult>(result).PageName);
        Assert.Equal(1, await _host.Context.Users.CountAsync());
    }

    [Fact]
    public async Task TheVisitor_DeletesOnlyTheirOwnAccount()
    {
        var otherId = await CreateUserAsync("sosed");
        var model = Page<DeleteModel>(await CreateUserAsync("gost6"));
        model.Input = Form(Password, confirmed: true);

        await model.OnPostAsync();

        // В заявката няма поле „потребител“: профилът се взема от
        // удостоверението, затова чуждият акаунт не може да бъде посочен.
        var other = await Users.FindByIdAsync(otherId);

        Assert.NotNull(other);
        Assert.True(await Users.CheckPasswordAsync(other, Password));
    }

    [Fact]
    public async Task SignalsOfTheDeletedAuthor_RemainWithoutHim()
    {
        var userId = await CreateUserAsync("gost7");
        var report = await _host.AddReportAsync(userId);

        var model = Page<DeleteModel>(userId);
        model.Input = Form(Password, confirmed: true);

        await model.OnPostAsync();

        // SetNull от DbContext-а: сигналът е обществена информация за
        // града и статистиката на общината не трябва да се променя от
        // напускането на един човек.
        var survivor = await _host.Context.Reports.AsNoTracking()
            .SingleAsync(item => item.Id == report.Id);

        Assert.Null(survivor.UserId);
        Assert.Equal(report.Title, survivor.Title);
    }

    [Fact]
    public async Task RatingsAndConfirmationsOfTheDeletedAccount_GoWithIt()
    {
        var authorId = await CreateUserAsync("avtor");
        var voterId = await CreateUserAsync("glasuvach");
        var report = await _host.AddReportAsync(authorId);

        _host.Context.SeverityRatings.Add(new SeverityRating
        {
            ReportId = report.Id,
            UserId = voterId,
            Rating = 5,
        });

        _host.Context.Confirmations.Add(new Confirmation
        {
            ReportId = report.Id,
            UserId = voterId,
        });

        await _host.Context.SaveChangesAsync();

        var model = Page<DeleteModel>(voterId);
        model.Input = Form(Password, confirmed: true);

        await model.OnPostAsync();

        // Гласовете са лични данни на човека, а не част от сигнала:
        // каскадното изтриване от DbContext-а ги премахва, а средната
        // сериозност на сигнала остава без неговия принос.
        Assert.Equal(0, await _host.Context.SeverityRatings.CountAsync());
        Assert.Equal(0, await _host.Context.Confirmations.CountAsync());
        Assert.NotNull(await _host.Context.Reports.FindAsync(report.Id));
    }

    [Fact]
    public async Task TheJournalOfADeletedOfficer_StaysWithTheSignal()
    {
        // Служителят е с парола като всички тук: страница за изтриване
        // без парола не изтрива нищо, а тестът проверява именно нея.
        var officerId = await CreateUserAsync("sluzhitel");
        var citizenId = await CreateUserAsync("gost8");
        var report = await _host.AddReportAsync(citizenId);

        _host.Context.StatusHistories.Add(new StatusHistory
        {
            ReportId = report.Id,
            ChangedByUserId = officerId,
            OldStatus = ReportStatuses.New,
            NewStatus = ReportStatuses.InProgress,
            Comment = "Екипът е насочен към адреса.",
        });

        await _host.Context.SaveChangesAsync();

        var model = Page<DeleteModel>(officerId);
        model.Input = Form(Password, confirmed: true);

        await model.OnPostAsync();

        // Дневникът е доказателство, а не лични данни на служителя:
        // връзката му с акаунта е с разрешена липса, затова редът остава
        // и след изтриването — само без име зад него.
        Assert.Null(await Users.FindByIdAsync(officerId));

        var entry = await _host.Context.StatusHistories.AsNoTracking()
            .SingleAsync(item => item.ReportId == report.Id);

        Assert.Null(entry.ChangedByUserId);
        Assert.Equal("Екипът е насочен към адреса.", entry.Comment);
    }

    private async Task<string> CreateUserAsync(string userName)
    {
        var user = new IdentityUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = userName,
            Email = userName + "@civiceye.test",
            EmailConfirmed = true
        };

        var result = await Users.CreateAsync(user, Password);

        Assert.True(result.Succeeded);

        return user.Id;
    }

    private TModel Page<TModel>(string userId)
        where TModel : DeleteModel
        => _host.CreatePageModel<TModel>(_host.PrincipalFor(userId));

    private static DeleteModel.DeleteInput Form(string password, bool confirmed) => new()
    {
        Password = password,
        Confirmed = confirmed,
    };

    private static string ErrorsFor(PageModel model, string key)
    {
        var errors = model.ModelState.TryGetValue(key, out var entry)
            ? entry?.Errors
            : null;

        return string.Join(" ", errors?.Select(error => error.ErrorMessage) ?? []);
    }

    public void Dispose() => _host.Dispose();

    /// <summary>
    /// Същият модел, но с достъп до проверката на атрибутите, която
    /// платформата прави сама преди handler-а; извън истинската страница
    /// тя се извиква директно и съобщенията се записват с ключовете на
    /// формите.
    /// </summary>
    private sealed class TestDeleteModel : DeleteModel
    {
        public TestDeleteModel(
            UserManager<IdentityUser> userManager,
            ApplicationDbContext db)
            : base(userManager, db)
        {
        }

        public bool ValidateInput()
        {
            var results = new List<ValidationResult>();

            var isValid = Validator.TryValidateObject(
                Input,
                new ValidationContext(Input),
                results,
                validateAllProperties: true);

            foreach (var result in results)
            {
                var field = result.MemberNames.FirstOrDefault();

                ModelState.AddModelError(
                    $"{nameof(Input)}.{field}",
                    result.ErrorMessage ?? "Полето не е попълнено.");
            }

            return isValid;
        }
    }
}

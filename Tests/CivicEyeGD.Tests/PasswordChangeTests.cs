using System.ComponentModel.DataAnnotations;
using CivicEyeGD.Pages.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace CivicEyeGD.Tests;

/// <summary>
/// Смяната на парола е единствената страница, която проверява сегашната
/// парола на влезлия, затова грешка в нея означава или заключен профил,
/// или сменена парола от чужда ръка. Тестовете минават през истинския
/// handler върху временна SQLite база и гледат два неща: хешът в базата
/// наистина ли се е сменил и кое точно се показва, когато не трябва.
/// </summary>
public sealed class PasswordChangeTests : IDisposable
{
    private const string OldPassword = "Starata!2026";
    private const string NewPassword = "Novata!2026";

    private const string CurrentKey = nameof(ProfileModel.Input) + "." + nameof(ProfileModel.ChangePasswordInput.CurrentPassword);
    private const string NewPasswordKey = nameof(ProfileModel.Input) + "." + nameof(ProfileModel.ChangePasswordInput.NewPassword);
    private const string ConfirmKey = nameof(ProfileModel.Input) + "." + nameof(ProfileModel.ChangePasswordInput.ConfirmPassword);

    private readonly TestHost _host = new();

    private UserManager<IdentityUser> Users
        => _host.Services.GetRequiredService<UserManager<IdentityUser>>();

    [Fact]
    public async Task ValidChange_WritesTheNewHashAndRedirectsWithTheNotice()
    {
        var userId = await CreateUserAsync();
        var model = Page<ProfileModel>(userId);
        model.Input = Form(OldPassword, NewPassword, NewPassword);

        var result = await model.OnPostAsync();

        // След изпращане на пароли браузърът пита дали да преизпрати
        // формата. Пренасочването спира въпроса и носи съобщението.
        var redirect = Assert.IsType<RedirectToPageResult>(result);

        Assert.Equal("/Account/Profile", redirect.PageName);
        Assert.True(redirect.RouteValues?["changed"] is true);

        var user = await Users.FindByIdAsync(userId);

        Assert.NotNull(user);
        Assert.True(await Users.CheckPasswordAsync(user, NewPassword));
        Assert.False(await Users.CheckPasswordAsync(user, OldPassword));
    }

    [Fact]
    public async Task WrongCurrentPassword_LeavesTheAccountUntouched()
    {
        var userId = await CreateUserAsync();
        var model = Page<ProfileModel>(userId);
        model.Input = Form("Gresna!2026x", NewPassword, NewPassword);

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);

        // Текстът е преведеният от BulgarianIdentityErrorDescriber,
        // същият, който гражданинът вижда на екрана.
        Assert.Contains("Невалидна парола.", ErrorsFor(model, string.Empty));

        var user = await Users.FindByIdAsync(userId);

        Assert.NotNull(user);
        Assert.True(await Users.CheckPasswordAsync(user, OldPassword));
        Assert.False(await Users.CheckPasswordAsync(user, NewPassword));
    }

    [Fact]
    public async Task WrongCurrentPassword_IsTheMismatchCodeFromIdentity()
    {
        var user = await Users.FindByIdAsync(await CreateUserAsync());

        var result = await Users.ChangePasswordAsync(user!, "Gresna!2026x", NewPassword);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == nameof(IdentityErrorDescriber.PasswordMismatch));
    }

    [Fact]
    public async Task APasswordBreakingTheRules_IsRefusedEvenWithTheRightCurrentOne()
    {
        var userId = await CreateUserAsync();
        var model = Page<ProfileModel>(userId);

        // Осем и повече символа с главна, малка и знак, но без нито една
        // цифра: правилата на страницата ги описват само наполовина, а
        // решението остава за политиката от Program.cs.
        model.Input = Form(OldPassword, "BezTsifra!Abc", "BezTsifra!Abc");

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains("поне една цифра", ErrorsFor(model, string.Empty));

        var user = await Users.FindByIdAsync(userId);

        Assert.NotNull(user);
        Assert.True(await Users.CheckPasswordAsync(user, OldPassword));
    }

    [Fact]
    public async Task NewPasswordShorterThanTheForm_QuitsBeforeTheStoreIsAsked()
    {
        var userId = await CreateUserAsync();
        var model = Page<TestProfileModel>(userId);
        model.Input = Form(OldPassword, "Nova!1", "Nova!1");

        // Полетата минават през същия атрибут, който действа и в браузъра:
        // шест знака са достатъчни за Identity настройките по
        // подразбиране, но не и за правилото, с което профилът е създаден.
        Assert.False(model.ValidateInput());

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains(
            "Паролата трябва да е поне 8 символа.",
            ErrorsFor(model, NewPasswordKey));

        var user = await Users.FindByIdAsync(userId);

        Assert.NotNull(user);
        Assert.True(await Users.CheckPasswordAsync(user, OldPassword));
    }

    [Fact]
    public async Task ConfirmationDifferentFromTheNewPassword_IsRefused()
    {
        var userId = await CreateUserAsync();
        var model = Page<TestProfileModel>(userId);
        model.Input = Form(OldPassword, NewPassword, "Druga!2026x");

        Assert.False(model.ValidateInput());
        Assert.Contains("Паролите не съвпадат.", ErrorsFor(model, ConfirmKey));
    }

    [Fact]
    public async Task EmptyCurrentPassword_IsRefused()
    {
        var userId = await CreateUserAsync();
        var model = Page<TestProfileModel>(userId);
        model.Input = Form(string.Empty, NewPassword, NewPassword);

        Assert.False(model.ValidateInput());
        Assert.Contains("Въведи сегашната парола.", ErrorsFor(model, CurrentKey));

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
    }

    [Fact]
    public async Task AFormLeftOpenPastTheCookie_SendsTheVisitorToLogin()
    {
        await CreateUserAsync();

        // Празно удостоверение: бисквитата е изтекла, докато формата е
        // стояла отворена. Няма как да се обнови парола на някого, когото
        // заявката не сочи, затова входът е единственият честен отговор.
        var model = _host.CreatePageModel<ProfileModel>();
        model.Input = Form(OldPassword, NewPassword, NewPassword);

        var result = await model.OnPostAsync();

        Assert.Equal("/Account/Login", Assert.IsType<RedirectToPageResult>(result).PageName);
    }

    [Fact]
    public async Task TheVisitor_ChangesOnlyTheirOwnPassword()
    {
        var otherId = await CreateUserAsync("drug.grazdanin");

        var model = Page<ProfileModel>(await CreateUserAsync());
        model.Input = Form(OldPassword, NewPassword, NewPassword);

        await model.OnPostAsync();

        // В заявката няма поле „потребител“: другият профил не може да
        // бъде посочен с нищо друго освен с влезлия, затова и остава
        // със старата си парола.
        var other = await Users.FindByIdAsync(otherId);

        Assert.NotNull(other);
        Assert.True(await Users.CheckPasswordAsync(other, OldPassword));
    }

    private async Task<string> CreateUserAsync(string userName = "grazdanin1")
    {
        var user = new IdentityUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = userName,
            Email = userName + "@civiceye.test",
            EmailConfirmed = true
        };

        var result = await Users.CreateAsync(user, OldPassword);

        Assert.True(result.Succeeded);

        return user.Id;
    }

    private TModel Page<TModel>(string userId)
        where TModel : ProfileModel
        => _host.CreatePageModel<TModel>(_host.PrincipalFor(userId));

    private static ProfileModel.ChangePasswordInput Form(
        string current,
        string newPassword,
        string confirm) => new()
        {
            CurrentPassword = current,
            NewPassword = newPassword,
            ConfirmPassword = confirm
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
    /// Същият модел, но с достъп до проверката на атрибутите. Платформата
    /// я прави сама преди OnPostAsync, а извън истинската страница тя няма
    /// нужните обекти, затова тук валидаторът се извиква директно и
    /// съобщенията се записват с ключовете на формите.
    /// </summary>
    private sealed class TestProfileModel : ProfileModel
    {
        public TestProfileModel(UserManager<IdentityUser> userManager)
            : base(userManager)
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

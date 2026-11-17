using System.ComponentModel.DataAnnotations;
using CivicEyeGD.Data;
using CivicEyeGD.Pages.Account;
using CivicEyeGD.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicEyeGD.Tests;

/// <summary>
/// Възстановяването на парола е единствената страница, която сменя парола,
/// без някой да я знае предварително, затова е и единственото място, където
/// грешка струва чужд профил. Тестовете минават през истинските handler-и
/// върху временна SQLite база и проверяват четири неща: че позналият
/// отговор влиза, че непозналият не променя нищо, че отказът звучи еднакво
/// във всички четири случая и че грешните опити се броят като грешни влизания.
/// </summary>
public sealed class PasswordRecoveryTests : IDisposable
{
    private const string OldPassword = "Starata!2026";
    private const string NewPassword = "Novata!2026";

    private const string QuestionKey = "first-pet";
    private const string Answer = "Карабайчо";

    private const string RefusalMessage = "Не можахме да те разпознаем";
    private const string LockoutMessage = "Акаунтът е временно заключен";

    private readonly TestHost _host = new();

    private UserManager<IdentityUser> Users
        => _host.Services.GetRequiredService<UserManager<IdentityUser>>();

    [Fact]
    public async Task TheRightAnswer_WritesANewPasswordAndSendsTheVisitorToLogin()
    {
        var userId = await CreateUserAsync();
        await SetQuestionAsync(userId, QuestionKey, Answer);

        var model = RecoveryPage();
        model.Input = Form("grazdanin1@civiceye.test", QuestionKey, Answer, NewPassword, NewPassword);

        var result = await model.OnPostAsync();

        // Посоката е входа, а не профила: новата парола все още не е
        // доказана с нищо друго, а влизането с нея е точно тази проверка.
        var redirect = Assert.IsType<RedirectToPageResult>(result);

        Assert.Equal("/Account/Login", redirect.PageName);
        Assert.True(redirect.RouteValues?["recovered"] is true);

        var user = await Users.FindByIdAsync(userId);

        Assert.NotNull(user);
        Assert.True(await Users.CheckPasswordAsync(user, NewPassword));
        Assert.False(await Users.CheckPasswordAsync(user, OldPassword));
    }

    [Fact]
    public async Task AWrongAnswer_ChangesNothingAndIsCountedLikeAFailedLogin()
    {
        var userId = await CreateUserAsync();
        await SetQuestionAsync(userId, QuestionKey, Answer);

        var model = RecoveryPage();
        model.Input = Form("grazdanin1@civiceye.test", QuestionKey, "Грешен отговор", NewPassword, NewPassword);

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains(RefusalMessage, ErrorsFor(model, string.Empty));

        var user = await Users.FindByIdAsync(userId);

        Assert.NotNull(user);
        Assert.True(await Users.CheckPasswordAsync(user, OldPassword));

        // Броячът е този на Identity: без него страницата би предложила
        // безкраен брой опити за отгатване, а входът си има праг от пет.
        Assert.Equal(1, user.AccessFailedCount);
    }

    [Fact]
    public async Task AnUnknownEmail_HearsTheSameWordsAsAWrongAnswer()
    {
        var userId = await CreateUserAsync();
        await SetQuestionAsync(userId, QuestionKey, Answer);

        var model = RecoveryPage();
        model.Input = Form("nqma.takav@civiceye.test", QuestionKey, Answer, NewPassword, NewPassword);

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);

        // Един текст за непознат имейл и за грешен отговор: различни
        // съобщения биха служили за проверяване кой има профил тук.
        Assert.Contains(RefusalMessage, ErrorsFor(model, string.Empty));
        Assert.Equal(1, await _host.Context.Users.CountAsync());
    }

    [Fact]
    public async Task AnAccountWithoutAQuestion_CannotBeRecovered()
    {
        var userId = await CreateUserAsync("bez.vupros");

        var model = RecoveryPage();
        model.Input = Form("bez.vupros@civiceye.test", QuestionKey, Answer, NewPassword, NewPassword);

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains(RefusalMessage, ErrorsFor(model, string.Empty));

        var user = await Users.FindByIdAsync(userId);

        Assert.NotNull(user);
        Assert.True(await Users.CheckPasswordAsync(user, OldPassword));
    }

    [Fact]
    public async Task TheRightAnswerToAnotherQuestion_StillRefuses()
    {
        var userId = await CreateUserAsync();
        await SetQuestionAsync(userId, QuestionKey, Answer);

        var model = RecoveryPage();
        model.Input = Form("grazdanin1@civiceye.test", "favorite-film", Answer, NewPassword, NewPassword);

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains(RefusalMessage, ErrorsFor(model, string.Empty));

        var user = await Users.FindByIdAsync(userId);

        Assert.NotNull(user);
        Assert.True(await Users.CheckPasswordAsync(user, OldPassword));
    }

    [Fact]
    public async Task TheAnswer_IsAcceptedWithoutRegardToCaseAndSpacing()
    {
        var userId = await CreateUserAsync();
        await SetQuestionAsync(userId, QuestionKey, Answer);

        var model = RecoveryPage();
        model.Input = Form("grazdanin1@civiceye.test", QuestionKey, "  КАРАБАЙЧО  ", NewPassword, NewPassword);

        var result = await model.OnPostAsync();

        // Хешът се смята върху нормализиран текст, защото човек помни
        // думата, а не коя буква е била главна в деня, в който я е записвал.
        Assert.IsType<RedirectToPageResult>(result);

        var user = await Users.FindByIdAsync(userId);

        Assert.NotNull(user);
        Assert.True(await Users.CheckPasswordAsync(user, NewPassword));
    }

    [Fact]
    public async Task FiveWrongAnswers_LockTheAccountAndTheRightOneNoLongerHelps()
    {
        var userId = await CreateUserAsync();
        await SetQuestionAsync(userId, QuestionKey, Answer);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var attemptModel = RecoveryPage();
            attemptModel.Input = Form("grazdanin1@civiceye.test", QuestionKey, "грешен" + attempt, NewPassword, NewPassword);

            await attemptModel.OnPostAsync();
        }

        var locked = await Users.FindByIdAsync(userId);

        Assert.NotNull(locked);
        Assert.True(locked.LockoutEnabled);
        Assert.True(locked.LockoutEnd > DateTimeOffset.UtcNow);

        // След прага дори вярният отговор не минава: иначе заключването
        // би било предупреждение, а не защита.
        var model = RecoveryPage();
        model.Input = Form("grazdanin1@civiceye.test", QuestionKey, Answer, NewPassword, NewPassword);

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains(LockoutMessage, ErrorsFor(model, string.Empty));
        Assert.True(await Users.CheckPasswordAsync(locked, OldPassword));
    }

    [Fact]
    public async Task TheRightAnswerAfterMistakes_ClearsTheCounterForTheLogin()
    {
        var userId = await CreateUserAsync();
        await SetQuestionAsync(userId, QuestionKey, Answer);

        // Два грешни отговора, които не стигат до прага: такъв е един
        // човек, който се е сети чак на третия път.
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var wrong = RecoveryPage();
            wrong.Input = Form("grazdanin1@civiceye.test", QuestionKey, "не този" + attempt, NewPassword, NewPassword);

            await wrong.OnPostAsync();
        }

        var before = await Users.FindByIdAsync(userId);
        Assert.NotNull(before);
        Assert.Equal(2, before.AccessFailedCount);

        var model = RecoveryPage();
        model.Input = Form("grazdanin1@civiceye.test", QuestionKey, Answer, NewPassword, NewPassword);

        Assert.IsType<RedirectToPageResult>(await model.OnPostAsync());

        var after = await Users.FindByIdAsync(userId);

        // Броячът тръгва отначало: иначе две сгреши в отговора биха
        // се изсипали във входа и следващото нормално влизане би
        // останало на косъм от заключване.
        Assert.NotNull(after);
        Assert.Equal(0, after.AccessFailedCount);
        Assert.True(await Users.CheckPasswordAsync(after, NewPassword));
    }

    [Fact]
    public async Task TheBase_HoldsAHashAndASaltInsteadOfTheAnswer()
    {
        var userId = await CreateUserAsync();
        await SetQuestionAsync(userId, QuestionKey, Answer);

        var row = await _host.Context.RecoveryQuestions.AsNoTracking().SingleAsync(q => q.UserId == userId);

        // Проверката е за текста: ако отговорът се чете в колоната, цялата
        // работа по хеширането е била на думи.
        Assert.DoesNotContain(Answer, row.AnswerHash, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Answer, row.AnswerSalt, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(44, row.AnswerHash.Length);
        Assert.Equal(24, row.AnswerSalt.Length);
        Assert.True(RecoveryQuestions.Verify(Answer, row.AnswerSalt, row.AnswerHash));
    }

    [Fact]
    public async Task TheSameAnswerTwice_IsStoredAsTwoDifferentHashes()
    {
        var first = await CreateUserAsync("perviq");
        var second = await CreateUserAsync("vtoriq");

        await SetQuestionAsync(first, QuestionKey, Answer);
        await SetQuestionAsync(second, QuestionKey, Answer);

        var hashes = await _host.Context.RecoveryQuestions
            .AsNoTracking()
            .OrderBy(q => q.UserId)
            .Select(q => q.AnswerHash)
            .ToListAsync();

        // Случайната сол прави това възможно: един и същ отговор при двама
        // потребители не бива да се разпознава по еднаквия ред.
        Assert.Equal(2, hashes.Count);
        Assert.NotEqual(hashes[0], hashes[1]);
    }

    [Fact]
    public async Task ACorruptedRecord_IsNeverTreatedAsAKnowledgeOfTheAnswer()
    {
        var userId = await CreateUserAsync();
        await SetQuestionAsync(userId, QuestionKey, Answer);

        var row = await _host.Context.RecoveryQuestions.SingleAsync(q => q.UserId == userId);

        // Ръчно наместен запис вместо изключение дава отказ: страницата не
        // трябва да пада, само защото някой е отворил базата с редактор.
        row.AnswerHash = "не е base64";

        Assert.False(RecoveryQuestions.Verify(Answer, row.AnswerSalt, row.AnswerHash));
    }

    [Fact]
    public async Task ANewPasswordBreakingTheRules_IsRefusedEvenAfterTheRightAnswer()
    {
        var userId = await CreateUserAsync();
        await SetQuestionAsync(userId, QuestionKey, Answer);

        var model = RecoveryPage();
        model.Input = Form("grazdanin1@civiceye.test", QuestionKey, Answer, "nova1234", "nova1234");

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);

        // Съобщението идва от BulgarianIdentityErrorDescriber, т.е. същото,
        // което гражданинът чете при регистрация.
        var user = await Users.FindByIdAsync(userId);

        Assert.NotNull(user);
        Assert.False(await Users.CheckPasswordAsync(user, "nova1234"));
    }

    [Fact]
    public async Task TheQuestionPage_ShowsWhatIsAlreadyChosen()
    {
        var userId = await CreateUserAsync();
        await SetQuestionAsync(userId, QuestionKey, Answer);

        var model = _host.CreatePageModel<SecurityQuestionModel>(_host.PrincipalFor(userId));

        await model.OnGetAsync();

        Assert.True(model.HasQuestion);
        Assert.Equal("Как се казваше първият ти домашен любимец?", model.CurrentQuestionText);

        // Отговорът не се връща никога: не съществува като текст, когото
        // да се върне.
        Assert.Equal(QuestionKey, model.Input.QuestionKey);
    }

    [Fact]
    public async Task TheQuestionPage_KeepsASingleRowWhenTheQuestionChanges()
    {
        var userId = await CreateUserAsync();

        var first = QuestionPage(userId);
        first.Input = QuestionForm(QuestionKey, Answer, OldPassword);

        Assert.IsType<RedirectToPageResult>(await first.OnPostAsync());

        var second = QuestionPage(userId);
        second.Input = QuestionForm("favorite-book", "Малкият принц", OldPassword);

        Assert.IsType<RedirectToPageResult>(await second.OnPostAsync());

        var rows = await _host.Context.RecoveryQuestions.AsNoTracking()
            .Where(q => q.UserId == userId)
            .ToListAsync();

        // Един ред, не два: ключът на профила е и първичният ключ, затова
        // вторият запис заменя първия вместо да се трупа до него.
        Assert.Single(rows);
        Assert.Equal("favorite-book", rows[0].QuestionKey);
        Assert.True(RecoveryQuestions.Verify("Малкият принц", rows[0].AnswerSalt, rows[0].AnswerHash));
    }

    [Fact]
    public async Task TheQuestionPage_RequiresTheCurrentPassword()
    {
        var userId = await CreateUserAsync();

        var model = QuestionPage(userId);
        model.Input = QuestionForm(QuestionKey, Answer, "Gresna!2026x");

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains("Невалидна парола.", ErrorsFor(model, CurrentPasswordKey));

        // Без сегашната парола страницата би била ключът към всеки оставен
        // отключен браузър: чужд човек би задал свой въпрос, би отговорил
        // на него и би сменил паролата.
        Assert.Equal(0, await _host.Context.RecoveryQuestions.CountAsync(q => q.UserId == userId));
    }

    [Fact]
    public async Task TheQuestionPage_RefusesAKeyThatIsNotOnTheList()
    {
        var userId = await CreateUserAsync();

        var model = QuestionPage(userId);
        model.Input = QuestionForm("na-koya-ulitsa-ziveesh", "Отговор", OldPassword);

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains("Избери един от посочените въпроси.", ErrorsFor(model, QuestionKeyField));
        Assert.Equal(0, await _host.Context.RecoveryQuestions.CountAsync(q => q.UserId == userId));
    }

    [Fact]
    public async Task TheQuestionPage_RefusesAnAnswerThatIsOnlySpaces()
    {
        var userId = await CreateUserAsync();

        var model = QuestionPage(userId);
        model.Input = QuestionForm(QuestionKey, "     ", OldPassword);

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains("поне 2 символа", ErrorsFor(model, AnswerField));
        Assert.Equal(0, await _host.Context.RecoveryQuestions.CountAsync(q => q.UserId == userId));
    }

    [Fact]
    public async Task TheQuestionPage_SendsAnExpiredCookieBackToLogin()
    {
        await CreateUserAsync();

        var model = _host.CreatePageModel<SecurityQuestionModel>();
        model.Input = QuestionForm(QuestionKey, Answer, OldPassword);

        var result = await model.OnPostAsync();

        Assert.Equal("/Account/Login", Assert.IsType<RedirectToPageResult>(result).PageName);
    }

    [Fact]
    public async Task DeletingTheAccount_TakesTheAnswerWithIt()
    {
        var userId = await CreateUserAsync("za.iztrivane");
        await SetQuestionAsync(userId, QuestionKey, Answer);

        var model = _host.CreatePageModel<DeleteModel>(_host.PrincipalFor(userId));
        model.Input = new DeleteModel.DeleteInput
        {
            Password = OldPassword,
            Confirmed = true
        };

        var result = await model.OnPostAsync();

        Assert.Equal("/Welcome", Assert.IsType<RedirectToPageResult>(result).PageName);
        Assert.Null(await Users.FindByIdAsync(userId));

        // Правото на забравяне не приключва, докато в базата стои ред,
        // пазиш доказателство за нещо, което човек е написал за себе си.
        Assert.Equal(0, await _host.Context.RecoveryQuestions.CountAsync());
    }

    [Fact]
    public async Task RegisteringWithAQuestion_SavesItsHashAndSignsTheVisitorIn()
    {
        await EnsureCitizenRoleAsync();

        var model = _host.CreatePageModel<RegisterModel>();
        model.Input = new RegisterModel.RegisterInput
        {
            Email = "nov.grazdanin@civiceye.test",
            Password = OldPassword,
            ConfirmPassword = OldPassword,
            RecoveryQuestionKey = QuestionKey,
            RecoveryAnswer = Answer
        };

        var result = await model.OnPostAsync();

        Assert.Equal("/Index", Assert.IsType<RedirectToPageResult>(result).PageName);

        var user = await Users.FindByEmailAsync("nov.grazdanin@civiceye.test");

        Assert.NotNull(user);

        var row = await _host.Context.RecoveryQuestions.AsNoTracking()
            .SingleAsync(q => q.UserId == user.Id);

        Assert.Equal(QuestionKey, row.QuestionKey);
        Assert.True(RecoveryQuestions.Verify(Answer, row.AnswerSalt, row.AnswerHash));
    }

    [Fact]
    public async Task RegisteringWithoutAQuestion_StillCreatesTheProfile()
    {
        await EnsureCitizenRoleAsync();

        var model = _host.CreatePageModel<RegisterModel>();
        model.Input = new RegisterModel.RegisterInput
        {
            Email = "bez.vapros@civiceye.test",
            Password = OldPassword,
            ConfirmPassword = OldPassword
        };

        var result = await model.OnPostAsync();

        // Въпросът е по желание: без това проверката би отказвала профили
        // на хора, които изобщо не са го попълнили.
        Assert.Equal("/Index", Assert.IsType<RedirectToPageResult>(result).PageName);
        Assert.Equal(0, await _host.Context.RecoveryQuestions.CountAsync());
    }

    [Fact]
    public async Task RegisteringWithAQuestionButNoAnswer_CreatesNothing()
    {
        var model = _host.CreatePageModel<RegisterModel>();
        model.Input = new RegisterModel.RegisterInput
        {
            Email = "polovichen@civiceye.test",
            Password = OldPassword,
            ConfirmPassword = OldPassword,
            RecoveryQuestionKey = QuestionKey
        };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains("Напиши отговор", ErrorsFor(model, RegisterAnswerField));

        // Половинчат запис не остава нито в таблицата с профилите, нито в
        // тази с въпросите: проверките са преди CreateAsync.
        Assert.Null(await Users.FindByEmailAsync("polovichen@civiceye.test"));
        Assert.Equal(0, await _host.Context.RecoveryQuestions.CountAsync());
    }

    [Fact]
    public async Task TheListOfQuestions_CarriesNoAddressOrBirthday()
    {
        // Списъкът е проверен, не вярван: в приложение, където гражданите
        // качват адреси на проблеми, въпрос за улица или за рождена дата би
        // бил подарък за някого, който иска да влезе в профила.
        Assert.All(RecoveryQuestions.Options, option =>
        {
            Assert.False(string.IsNullOrWhiteSpace(option.Key));
            Assert.False(string.IsNullOrWhiteSpace(option.Text));
            Assert.Matches("^[a-z-]+$", option.Key);

            foreach (var forbidden in new[] { "улица", "адрес", "рождена", " ЕГН" })
            {
                Assert.DoesNotContain(forbidden, option.Text, StringComparison.OrdinalIgnoreCase);
            }
        });

        Assert.Equal(6, RecoveryQuestions.Options.Length);
        Assert.Equal(
            RecoveryQuestions.Options.Select(option => option.Key).Distinct().Count(),
            RecoveryQuestions.Options.Length);
    }

    [Fact]
    public void NormalizingLeavesSingleSpacesAndNoCase()
    {
        Assert.Equal("малък принц", RecoveryQuestions.Normalize("  Малък   ПРИНЦ \t"));
        Assert.Equal(string.Empty, RecoveryQuestions.Normalize(null));
        Assert.Equal(string.Empty, RecoveryQuestions.Normalize("   "));
    }

    private const string CurrentPasswordKey = nameof(SecurityQuestionModel.Input) + "." + nameof(SecurityQuestionModel.SecurityQuestionInput.CurrentPassword);
    private const string QuestionKeyField = nameof(SecurityQuestionModel.Input) + "." + nameof(SecurityQuestionModel.SecurityQuestionInput.QuestionKey);
    private const string AnswerField = nameof(SecurityQuestionModel.Input) + "." + nameof(SecurityQuestionModel.SecurityQuestionInput.Answer);
    private const string RegisterAnswerField = nameof(RegisterModel.Input) + "." + nameof(RegisterModel.RegisterInput.RecoveryAnswer);

    private const string EmailField = nameof(ForgotPasswordModel.Input) + "." + nameof(ForgotPasswordModel.RecoveryInput.Email);
    private const string NewPasswordField = nameof(ForgotPasswordModel.Input) + "." + nameof(ForgotPasswordModel.RecoveryInput.NewPassword);
    private const string ConfirmPasswordField = nameof(ForgotPasswordModel.Input) + "." + nameof(ForgotPasswordModel.RecoveryInput.ConfirmPassword);
    private const string RecoveryAnswerField = nameof(ForgotPasswordModel.Input) + "." + nameof(ForgotPasswordModel.RecoveryInput.Answer);

    [Fact]
    public async Task TheRecoveryForm_SaysWhatIsWrongInBulgarian()
    {
        var model = _host.CreatePageModel<TestRecoveryModel>();
        model.Input = Form(string.Empty, QuestionKey, "а", "кова", "различна!2026");

        // Съобщенията са написани във формата, а не оставени на браузъра:
        // иначе един и същи отказ звучи на два езика, в зависимост от това
        // кой е пуснал страницата.
        Assert.False(model.ValidateInput());
        Assert.Contains("Въведи имейл адрес.", ErrorsFor(model, EmailField));
        Assert.Contains("Отговорът трябва да е между 2 и 60 символа.", ErrorsFor(model, RecoveryAnswerField));
        Assert.Contains("Паролата трябва да е поне 8 символа.", ErrorsFor(model, NewPasswordField));
        Assert.Contains("Паролите не съвпадат.", ErrorsFor(model, ConfirmPasswordField));

        var result = await model.OnPostAsync();

        // Невалидната форма спира преди базата да е видяла каквото и да е.
        Assert.IsType<PageResult>(result);
    }

    private static ForgotPasswordModel.RecoveryInput Form(
        string email,
        string questionKey,
        string answer,
        string newPassword,
        string confirmPassword) => new()
        {
            Email = email,
            QuestionKey = questionKey,
            Answer = answer,
            NewPassword = newPassword,
            ConfirmPassword = confirmPassword
        };

    private static SecurityQuestionModel.SecurityQuestionInput QuestionForm(
        string questionKey,
        string answer,
        string currentPassword) => new()
        {
            QuestionKey = questionKey,
            Answer = answer,
            ConfirmAnswer = answer,
            CurrentPassword = currentPassword
        };

    private ForgotPasswordModel RecoveryPage()
        => _host.CreatePageModel<ForgotPasswordModel>();

    private SecurityQuestionModel QuestionPage(string userId)
        => _host.CreatePageModel<SecurityQuestionModel>(_host.PrincipalFor(userId));

    private async Task EnsureCitizenRoleAsync()
    {
        // Ролята я има в истинското приложение — DbSeeder я създава при
        // старт. Тук базата е временна и празна, а RoleStore отказва, ако
        // ролята я няма, затова редът се добавя изрично.
        if (!await _host.Context.Roles.AnyAsync(role => role.Name == "Citizen"))
        {
            _host.Context.Roles.Add(new IdentityRole("Citizen") { NormalizedName = "CITIZEN" });
            await _host.Context.SaveChangesAsync();
        }
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

    private async Task SetQuestionAsync(string userId, string questionKey, string answer)
    {
        var salt = RecoveryQuestions.NewSalt();

        _host.Context.RecoveryQuestions.Add(new RecoveryQuestion
        {
            UserId = userId,
            QuestionKey = questionKey,
            AnswerSalt = salt,
            AnswerHash = RecoveryQuestions.Hash(answer, salt)
        });

        await _host.Context.SaveChangesAsync();
    }

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
    /// платформата прави сама преди handler-а: извън истинската страница
    /// тя се извиква директно, за да се види кое съобщение къде се появява.
    /// </summary>
    private sealed class TestRecoveryModel : ForgotPasswordModel
    {
        public TestRecoveryModel(UserManager<IdentityUser> userManager, ApplicationDbContext db)
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

using System.ComponentModel.DataAnnotations;
using CivicEyeGD.Data;
using CivicEyeGD.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace CivicEyeGD.Pages.Account;

// Изходът от задължителната отметка и от ограничението за честота са
// същите като на страницата за вход: тази форма е публична и е насочена
// точно срещу загубена парола, затова е първото място, което би избрал
// някой, решил да пробва насила чужди отговори.
[AllowAnonymous]
[EnableRateLimiting("forms")]
public class ForgotPasswordModel : PageModel
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ApplicationDbContext _db;

    public ForgotPasswordModel(
        UserManager<IdentityUser> userManager,
        ApplicationDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    // Един хеш, изчислен по същия начин както истинските, но записан за
    // никой. Когато имейлът не принадлежи на профил, проверката все пак
    // изразходва същото време: без това отговорът за непознат адрес би
    // идвал забележимо по-бързо и страницата би издала кой има профил
    // тук, без да е казала нищо с думи.
    private static readonly string DecoySalt = RecoveryQuestions.NewSalt();

    private static readonly string DecoyHash =
        RecoveryQuestions.Hash("няма такъв профил", DecoySalt);

    [BindProperty]
    public RecoveryInput Input { get; set; } = new();

    public (string Key, string Text)[] Options => RecoveryQuestions.Options;

    public class RecoveryInput
    {
        [Required(ErrorMessage = "Въведи имейл адрес.")]
        [EmailAddress(ErrorMessage = "Въведи валиден имейл адрес.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Избери въпрос.")]
        public string QuestionKey { get; set; } = string.Empty;

        [Required(ErrorMessage = "Напиши отговора.")]
        [StringLength(
            RecoveryQuestions.MaxAnswerLength,
            MinimumLength = RecoveryQuestions.MinAnswerLength,
            ErrorMessage = "Отговорът трябва да е между 2 и 60 символа.")]
        public string Answer { get; set; } = string.Empty;

        // Правилата са написани тук същите като при регистрация, за да се
        // види като човешко съобщение още във формата; истинската преценка
        // я прави Identity при записа.
        [Required(ErrorMessage = "Въведи нова парола.")]
        [StringLength(
            100,
            MinimumLength = 8,
            ErrorMessage = "Паролата трябва да е поне 8 символа.")]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Потвърди новата парола.")]
        [DataType(DataType.Password)]
        [Compare(
            nameof(NewPassword),
            ErrorMessage = "Паролите не съвпадат.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await _userManager.FindByEmailAsync(Input.Email);

        // Намирането е по ключ, не по имейл: ако профилът го няма,
        // резултатът е просто празен и никой ред не се чете.
        var question = user is null
            ? null
            : await _db.RecoveryQuestions.FindAsync(user.Id);

        // И двете проверки минават през същия бавен път, независимо дали
        // има какъв запис да се сравни.
        var answerMatches = RecoveryQuestions.Verify(
            Input.Answer,
            question?.AnswerSalt ?? DecoySalt,
            question?.AnswerHash ?? DecoyHash);

        var questionMatches = question is not null
            && question.QuestionKey == Input.QuestionKey;

        if (user is null || !questionMatches || !answerMatches)
        {
            if (user is not null)
            {
                // Грешният отговор се брои като грешно влизане: без това
                // страницата би оставила безкраен брой опити за отгатване,
                // а входът си има праг от пет. Заключен профил обаче не се
                // брои повторно — иначе всеки, който знае имейла, държи
                // човека заключен завинаги с по една грешка на всеки десет
                // минути.
                if (!await _userManager.IsLockedOutAsync(user))
                {
                    await _userManager.AccessFailedAsync(user);
                }
            }

            // Един текст за четирите различни отказа — непознат имейл,
            // профил без въпрос, грешен въпрос, грешен отговор. Различни
            // съобщения биха служили за проверяване кой какво има тук.
            ModelState.AddModelError(
                string.Empty,
                "Не можахме да те разпознаем. Провери имейла, въпроса и отговора.");

            return Page();
        }

        if (user.LockoutEnabled
            && user.LockoutEnd.HasValue
            && user.LockoutEnd.Value > DateTimeOffset.UtcNow)
        {
            // Същото съобщение, каквото дава и входът; тук то идва,
            // след някой вече е счупил прага на грешните отговори.
            ModelState.AddModelError(string.Empty, AccountLockout.Message);

            return Page();
        }

        // Токенът се ражда и се изразходва в една и съща заявка: той не
        // напуска сървъра, защото няма как да стигне до човека — поща в
        // проекта няма. Ползва се, за да мине записът на новата парола
        // през същия път, по който Identity я валидира и хешира.
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);

        var result = await _userManager.ResetPasswordAsync(
            user,
            token,
            Input.NewPassword);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        // Успехът изчиства брояча: човек, който е сгрешил два пъти в
        // отговора и на третия се е сети, не носи тези две грешки към
        // влизането. Без това прагът от пет би се пълнял през две форми
        // и следващият вход би бил заключен без човекът да е сгрешил
        // паролата си.
        await _userManager.ResetAccessFailedCountAsync(user);

        // Посоката е входът, а не профила: новата парола още не е
        // доказана с нищо освен с това, че току-що е написана, а
        // влизането с нея е точно тази проверка. ResetPasswordAsync
        // подновява подписа на профила, а Program.cs го препроверява
        // всяка минута — затова отворените преди това браузъри излизат
        // навън бързо, но не мигновено.
        return RedirectToPage("/Account/Login", new { recovered = true });
    }
}

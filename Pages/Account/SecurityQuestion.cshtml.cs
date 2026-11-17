using System.ComponentModel.DataAnnotations;
using CivicEyeGD.Data;
using CivicEyeGD.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace CivicEyeGD.Pages.Account;

// Страницата, в която човек сам си избира въпроса за сигурност. Тя е зад
// глобалния FallbackPolicy от Program.cs: съществува само за влезлия,
// защото променя нещо, което после се използва, за да се влезе.
//
// Ограничението „forms“ е същото като при входа, регистрацията и
// възстановяването: до 30 изпращания от един IP адрес за минута.
[EnableRateLimiting("forms")]
public class SecurityQuestionModel : PageModel
{
    private readonly UserManager<IdentityUser> _userManager;

    // Контекстът идва направо, а не през сервис: този запис не е част от
    // модела на Identity, затова нито UserManager, нито SignInManager го
    // познават.
    private readonly ApplicationDbContext _db;

    public SecurityQuestionModel(
        UserManager<IdentityUser> userManager,
        ApplicationDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    [BindProperty]
    public SecurityQuestionInput Input { get; set; } = new();

    // Отметката идва по адреса, а не от текста на страницата: след
    // изпращане на парола браузърът пита дали да преизпрати формата, а
    // преизпратеният отговор не трябва да се чете отново.
    [BindProperty(SupportsGet = true)]
    public bool Saved { get; set; }

    // Какво се вижда над формата: има ли вече избран въпрос и кой е той.
    public bool HasQuestion { get; private set; }

    public string CurrentQuestionText { get; private set; } = string.Empty;

    // Списъкът се подава директно от сервиса, за да не се поддържа второ
    // копие в отметките на .cshtml-а.
    public (string Key, string Text)[] Options => RecoveryQuestions.Options;

    public class SecurityQuestionInput
    {
        // Понеже отметките идват от списъка в кода, единствената нужна
        // проверка е стойността да е сред познатите ключове; тя се прави
        // в handler-а, защото атрибут не може да познава съдържанието на
        // списък.
        [Required(ErrorMessage = "Избери въпрос.")]
        public string QuestionKey { get; set; } = string.Empty;

        [Required(ErrorMessage = "Напиши отговора.")]
        [StringLength(
            RecoveryQuestions.MaxAnswerLength,
            MinimumLength = RecoveryQuestions.MinAnswerLength,
            ErrorMessage = "Отговорът трябва да е между 2 и 60 символа.")]
        public string Answer { get; set; } = string.Empty;

        [Required(ErrorMessage = "Потвърди отговора.")]
        [Compare(
            nameof(Answer),
            ErrorMessage = "Отговорите не съвпадат.")]
        public string ConfirmAnswer { get; set; } = string.Empty;

        // Сегашната парола се изисква, защото без нея тази страница би
        // била ключ към всеки оставен отключен браузър: чужд човек би
        // задал свой въпрос, би отговорил на него и би сменил паролата.
        [Required(ErrorMessage = "Въведи сегашната парола.")]
        [DataType(DataType.Password)]
        public string CurrentPassword { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);

        if (user is null)
        {
            return RedirectToPage("/Account/Login");
        }

        var question = await _db.RecoveryQuestions.FindAsync(user.Id);

        HasQuestion = question is not null;
        CurrentQuestionText = question is null ? string.Empty : RecoveryQuestions.TextFor(question.QuestionKey);

        if (question is not null)
        {
            // Въпросът се връща попълнен, за да е видно кой е сега;
            // отговорът никога не се попълва — той не се пази като текст.
            Input.QuestionKey = question.QuestionKey;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await _userManager.GetUserAsync(User);

        if (user is null)
        {
            return RedirectToPage("/Account/Login");
        }

        if (!RecoveryQuestions.IsKnown(Input.QuestionKey))
        {
            ModelState.AddModelError(
                $"{nameof(Input)}.{nameof(Input.QuestionKey)}",
                "Избери един от посочените въпроси.");

            return Page();
        }

        // Нормализацията е преди записа и преди проверката на дължината:
        // „   а   “ не е отговор, а само whitespace не бива да се крие
        // зад хеша, който после никой не може да повтори.
        var normalized = RecoveryQuestions.Normalize(Input.Answer);

        if (normalized.Length < RecoveryQuestions.MinAnswerLength)
        {
            ModelState.AddModelError(
                $"{nameof(Input)}.{nameof(Input.Answer)}",
                $"Отговорът трябва да е поне {RecoveryQuestions.MinAnswerLength} символа.");

            return Page();
        }

        if (!await _userManager.CheckPasswordAsync(user, Input.CurrentPassword))
        {
            ModelState.AddModelError(
                $"{nameof(Input)}.{nameof(Input.CurrentPassword)}",
                BulgarianIdentityErrorDescriber.InvalidPasswordMessage);

            return Page();
        }

        var salt = RecoveryQuestions.NewSalt();

        var question = await _db.RecoveryQuestions.FindAsync(user.Id);

        if (question is null)
        {
            question = new RecoveryQuestion { UserId = user.Id };
            _db.RecoveryQuestions.Add(question);
        }

        // Нова сол при всяка замяна: предишният отговор и новият не бива да
        // приличат един на друг в базата, дори да са написани еднакво.
        question.QuestionKey = Input.QuestionKey;
        question.AnswerSalt = salt;
        question.AnswerHash = RecoveryQuestions.Hash(normalized, salt);
        question.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return RedirectToPage(
            "/Account/SecurityQuestion",
            new { saved = true });
    }
}

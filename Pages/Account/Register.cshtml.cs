
using System.ComponentModel.DataAnnotations;
using CivicEyeGD.Data;
using CivicEyeGD.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace CivicEyeGD.Pages.Account;

// Регистрацията е анонимна по същата причина като входа: на този етап
// нямаше кой да бъде влязъл, а FallbackPolicy затваря всичко без
// отметка.
[AllowAnonymous]

// Тук ограничението е единствената защита срещу пълнене на базата с
// фалшиви профили: потвърждение по имейл няма (RequireConfirmedAccount
// е изключен в Program.cs), затова остава само политиката „forms“.
[EnableRateLimiting("forms")]
public class RegisterModel : PageModel
{
    // UserManager създава и валидира профила, SignInManager го вкарва
    // в сайта след това. И двата идват от контейнера, за да може тест
    // да сложи временна база вместо истинската.
    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;

    // Нужен е за допълнителния ред с въпроса за сигурност: той не е част
    // от модела на Identity, затова CreateAsync не би го записал.
    private readonly ApplicationDbContext _db;

    public RegisterModel(
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager,
        ApplicationDbContext db)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
    }

    // Полетата са в отделен вложен клас, за да не се бъркат със
    // свойствата на страницата; BindProperty ги връща попълнени, когато
    // формата се преизкара след грешка.
    [BindProperty]
    public RegisterInput Input { get; set; } = new();

    // Въпросите идват от сервиса, а не са преписани в отметките на
    // страницата: две копия на еднакви формулировки рано или късно стават
    // различни, а тогава човек вижда един въпрос и сървърът пази друг.
    public (string Key, string Text)[] Options => RecoveryQuestions.Options;

    public class RegisterInput
    {
        // Имейлът е едновременно и потребителското име, затова се
        // изисква веднъж и трябва да е валиден: проверките са и на
        // сървъра, не само в браузъра.
        [Required(ErrorMessage = "Въведи имейл адрес.")]
        [EmailAddress(ErrorMessage = "Въведи валиден имейл адрес.")]
        public string Email { get; set; } = string.Empty;

        // Минимумът от 8 символа е повторение на политиката от
        // Program.cs, за да се види като човешко съобщение още в
        // формата. Цифрата и малката буква тук ги няма — Identity ги
        // проверява при CreateAsync, а съобщението идва преведен от
        // BulgarianIdentityErrorDescriber. Горната граница пази от
        // случаи, в които някой изпрати цяла страница като парола.
        [Required(ErrorMessage = "Въведи парола.")]
        [StringLength(
            100,
            MinimumLength = 8,
            ErrorMessage = "Паролата трябва да е поне 8 символа.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        // Потвърждението се сравнява на сървъра с nameof(Password), а
        // не с изписано име: при преименуване на полето грешката се
        // вижда още при превеждането, а не когато някой се регистрира.
        [Required(ErrorMessage = "Потвърди паролата.")]
        [DataType(DataType.Password)]
        [Compare(
            nameof(Password),
            ErrorMessage = "Паролите не съвпадат.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        // Двете полета са по желание и затова нямат Required: човек, който
        // не ги попълни, пак получава профил. Липсата им само означава, че
        // загубената парола ще остане без път обратно — което формата казва
        // с текст, а не го крие.
        //
        // Писани са като nullable именно заради думата „по желание“: низ,
        // който не допуска null, връзката на модела тълкува като
        // задължително поле и връща английското „The RecoveryAnswer field
        // is required.“, когато човек е оставил полетата празни. Като null
        // се чете и истинският смисъл — отговор може и да няма — а
        // RecoveryQuestions приема пустотата навсякъде.
        public string? RecoveryQuestionKey { get; set; }

        [StringLength(
            RecoveryQuestions.MaxAnswerLength,
            ErrorMessage = "Отговорът не трябва да е по-дълъг от 60 символа.")]
        public string? RecoveryAnswer { get; set; }
    }

    // Празен метод: формата е неподвижна и няма какво да се зарежда.
    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Невалидна форма се връща веднага, преди базата да е видяла
        // каквото и да е: нищо не се записва на половина.
        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Въпросът е по желание, но половинчатият му вид не е: избран
        // въпрос без отговор би оставил профил, който обещава
        // възстановяване и не го дава, а ключ извън списъка би се записал
        // като текст, който никоя форма после не може да покаже.
        //
        // Двата реда са локални и невъзможни за null, защото полетата
        // навсякъде по-нататък се четат като готови: празният ключ не е
        // познат, а празният отговор е с дължина нула, затова и двете
        // проверки отдолу спират записа преди той да стигне до базата.
        var questionKey = Input.RecoveryQuestionKey ?? string.Empty;
        var normalizedAnswer = RecoveryQuestions.Normalize(Input.RecoveryAnswer);

        var wantsQuestion = !string.IsNullOrWhiteSpace(questionKey)
            || normalizedAnswer.Length > 0;

        if (wantsQuestion)
        {
            if (!RecoveryQuestions.IsKnown(questionKey))
            {
                ModelState.AddModelError(
                    $"{nameof(Input)}.{nameof(Input.RecoveryQuestionKey)}",
                    "Избери един от посочените въпроси.");

                return Page();
            }

            if (normalizedAnswer.Length < RecoveryQuestions.MinAnswerLength)
            {
                ModelState.AddModelError(
                    $"{nameof(Input)}.{nameof(Input.RecoveryAnswer)}",
                    "Напиши отговор от поне 2 символа или остави двете полета празни.");

                return Page();
            }
        }

        // Потребителското име е същият имейл: така остава едно
        // единствено нещо за запомняне при вход и няма второ име,
        // което да се сбъсква с чуждо.
        var user = new IdentityUser
        {
            UserName = Input.Email,
            Email = Input.Email
        };

        // CreateAsync проверява паролата по политиката от Program.cs
        // (8 знака, цифра, малка буква) и записва само нейния хеш, не
        // самата парола.
        var result = await _userManager.CreateAsync(
            user,
            Input.Password);

        if (!result.Succeeded)
        {
            // Всички грешки се изписват една по една, защото човекът
            // трябва да чуе всичко наведнъж и да поправи формата в един
            // опит. Текстът идва преведен от BulgarianIdentityErrorDescriber.
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(
                    string.Empty,
                    error.Description);
            }

            return Page();
        }

        // Ролята се задава от кода, не от формата. Тя е винаги
        // „Citizen“, затова никой не може да поиска администраторски
        // права, като промени едно поле в заявката; админите идват
        // единствено от DbSeeder.
        var roleResult = await _userManager.AddToRoleAsync(
            user,
            "Citizen");

        if (!roleResult.Succeeded)
        {
            // Половинчав профил се изтрива веднага: без роля човекът
            // остава недовършен, а заетото потребителско име би пречило
            // да се опита отново със същия имейл.
            await _userManager.DeleteAsync(user);

            foreach (var error in roleResult.Errors)
            {
                ModelState.AddModelError(
                    string.Empty,
                    error.Description);
            }

            return Page();
        }

        // Въпросът се записва чак сега, когато профилът вече съществува и
        // има роля: по-рано би оставил ред за акаунт, който току-що беше
        // изтрит при неуспех. В базата отива хешът, не отговорът.
        if (wantsQuestion)
        {
            var salt = RecoveryQuestions.NewSalt();

            _db.RecoveryQuestions.Add(new RecoveryQuestion
            {
                UserId = user.Id,
                QuestionKey = questionKey,
                AnswerSalt = salt,
                AnswerHash = RecoveryQuestions.Hash(normalizedAnswer, salt)
            });

            await _db.SaveChangesAsync();
        }

        // Човекът влиза веднага, без втора форма за вход: самата
        // регистрация вече доказа, че той знае паролата си.
        // isPersistent е false, за да не остава акаунтът отворен на
        // чужд или споделен компютър след затваряне на браузъра.
        await _signInManager.SignInAsync(
            user,
            isPersistent: false);

        // Началната страница е първото, от което човек има нужда сега:
        // там се виждат сигналите и бутонът за подаване на нов.
        return RedirectToPage("/Index");
    }
}

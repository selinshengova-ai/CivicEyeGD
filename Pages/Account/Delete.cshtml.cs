using System.ComponentModel.DataAnnotations;
using CivicEyeGD.Data;
using CivicEyeGD.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace CivicEyeGD.Pages.Account;

// Страницата е зад глобалния FallbackPolicy от Program.cs: съществува само
// за влезлия, чието право на забравяне обслужва. Гост няма какво да изтрие.
// Ограничението „forms“ е същото като при входа и регистрацията: до 30
// изпращания от един IP адрес за минута, за да не може чужд браузър,
// оставен отключен, да изпробва насила пароли една след друга.
[EnableRateLimiting("forms")]
public class DeleteModel : PageModel
{
    // UserManager взема профила от удостоверението, сравнява паролата с
    // хеша и изтрива реда. SignInManager не е нужен, защото разписването
    // на бисквитата става през схемата на Identity, а то идва с нея в
    // Program.cs.
    private readonly UserManager<IdentityUser> _userManager;

    // Контекстът е нужен за едно-единствено нещо: въпросът за сигурност не
    // е част от модела на Identity, затова UserManager не го познава и не
    // би го изтрил.
    private readonly ApplicationDbContext _db;

    public DeleteModel(
        UserManager<IdentityUser> userManager,
        ApplicationDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    [BindProperty]
    public DeleteInput Input { get; set; } = new();

    public class DeleteInput
    {
        // Паролата се иска отново, макар човекът да е влязъл: тя е
        // единственото доказателство, че заявката е от собственика, а не
        // от скрита форма на чужд сайт или от колега с отворен браузър.
        [Required(ErrorMessage = "Въведи паролата си.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        // Отметката не е валидирана с атрибут, а с код в handler-а:
        // отметнато поле идва като "true", а неотметнато изобщо не тръгва
        // с формата, затова проверката на ModelState преди това би казвала
        // „полето е задължително“ за човек, който просто не е съгласен.
        public bool Confirmed { get; set; }
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

        if (!Input.Confirmed)
        {
            // Ключът на грешката е същият, с който формата се връзва:
            // без префикса „Input.“ span-ът под отметката остава празен,
            // а съобщението не се вижда никъде от човека.
            ModelState.AddModelError(
                $"{nameof(Input)}.{nameof(Input.Confirmed)}",
                "Отбележи, че разбираш, че изтриването е необратимо.");

            return Page();
        }

        // Профилът се взема от влезлия, не от формата: в заявката няма поле
        // „изтрий потребител N“, затова не може да се посочи чужд акаунт.
        var user = await _userManager.GetUserAsync(User);

        if (user is null)
        {
            // Празно удостоверение се среща само ако бисквитата е изтекла,
            // докато страницата е стояла отворена.
            return RedirectToPage("/Account/Login");
        }

        // Сравнението е с хеша в базата, не с текст: грешна парола не
        // изтрива нищо. Текстът е общата форма за PasswordMismatch от
        // BulgarianIdentityErrorDescriber, за да не се учи нова формулировка
        // за вече казаното.
        if (!await _userManager.CheckPasswordAsync(user, Input.Password))
        {
            ModelState.AddModelError(
                $"{nameof(Input)}.{nameof(Input.Password)}",
                BulgarianIdentityErrorDescriber.InvalidPasswordMessage);

            return Page();
        }

        // Хешът на отговора се маха преди акаунта: правото на забравяне не
        // приключва, ако в базата остане ред, пазиш доказателство за нещо,
        // което човек е написал за себе си. Схемата го каскадира и сама, но
        // изтриването минава през UserManager, който не познава таблицата,
        // затова редът се маха изрично. SaveChanges-ът го записва заедно с
        // изтриването на профила в същата заявка към базата.
        var question = await _db.RecoveryQuestions.FindAsync(user.Id);

        if (question is not null)
        {
            _db.RecoveryQuestions.Remove(question);
        }

        var result = await _userManager.DeleteAsync(user);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(
                    string.Empty,
                    error.Description);
            }

            return Page();
        }

        // Бисквитата е последното, което остава от профила: без разписване
        // браузърът продължава да твърди, че е някой, когото базата вече не
        // познава, и носи ролите му, докато изтече. Разписва се точно
        // схемата, с която человекът влезе — тя е единствената, която сайтът
        // му слага.
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);

        // Посоката е рекламната страница: тя е публична и не изисква вход,
        // а човекът точно спря да бъде влязъл. Пренасочването, а не
        // PageResult, спира браузъра да пита дали да преизпрати формата с
        // паролата при опресняване.
        return RedirectToPage("/Welcome");
    }
}

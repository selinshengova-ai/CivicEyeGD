
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace CivicEyeGD.Pages.Account;

// Отметката AllowAnonymous е задължителна: глобалната FallbackPolicy
// важи за всяка страница без такава, а точната страница за вход не
// бива да изисква вход — иначе пренасочването се връща към себе си.
[AllowAnonymous]

// Политиката „forms“ е записана в Program.cs: 30 изпратени форми в
// минута за един IP адрес. Тя идва преди заключването на акаунта и
// спира масовото пробване на пароли, което иначе би товарило сървъра.
[EnableRateLimiting("forms")]
public class LoginModel : PageModel
{
    // Стига SignInManager: тук човекът вече е създаден, остава само да
    // се провери паролата и да се издаде бисквитата.
    private readonly SignInManager<IdentityUser> _signInManager;

    public LoginModel(SignInManager<IdentityUser> signInManager)
    {
        _signInManager = signInManager;
    }

    // Полетата на формата са в отделен вложен клас, за да не се бъркат
    // със свойствата на самата страница; BindProperty ги свързва с
    // имената на полетата и ги връща попълнени при грешка.
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    // Къде да пратим потребителя след вход. Идва като query параметър,
    // затова SupportsGet; без него вход от „влез, за да гласуваш“
    // връща човека към началната страница вместо към сигнала.
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public class LoginInput
    {
        // Съобщенията за грешка са изписани тук и на български, защото
        // иначе браузърът показва английските текстове на библиотеката
        // и формата говори на два езика. Проверката е и на сървъра, не
        // само в браузъра: директна заявка минава през тези атрибути.
        [Required(ErrorMessage = "Въведи имейл адрес.")]
        [EmailAddress(ErrorMessage = "Въведи валиден имейл адрес.")]
        public string Email { get; set; } = string.Empty;

        // DataType.Password кара браузъра да скрие написаното зад
        // точки, та паролата не се чете през рамото на човека.
        [Required(ErrorMessage = "Въведи паролата си.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        // Прехвърля се направо към PasswordSignInAsync: при true
        // бисквитата е трайна и човек остава влязъл след рестарт на
        // браузъра. При false излиза, когато затвори прозореца.
        public bool RememberMe { get; set; }
    }

    // Празен метод: за зареждане на формата не трябва нищо — няма данни
    // за четене и няма филтри.
    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Невалидна форма се спира още в началото: тя не трябва да
        // стига до проверката на паролата, защото би се броила за
        // опит и би приближила заключването на акаунта напразно.
        if (!ModelState.IsValid)
        {
            return Page();
        }

        // lockoutOnFailure: true е бутонът, без който броячът за
        // грешни опити не работи изобщо. Правилото му е в Program.cs —
        // 5 неуспешни влизания и 10 минути изчакване.
        var result = await _signInManager.PasswordSignInAsync(
            Input.Email,
            Input.Password,
            Input.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return RedirectToLocal(ReturnUrl);
        }

        if (result.IsLockedOut)
        {
            // Същото правило е записано и в документацията: 5 опита,
            // 10 минути изчакване.
            ModelState.AddModelError(
                string.Empty,
                "Акаунтът е временно заключен след 5 неуспешни опита. "
                + "Опитай отново след 10 минути.");

            return Page();
        }

        // Един и същ текст и за грешна парола, и за непознат имейл:
        // иначе страницата служи за проверка кой изобщо има акаунт тук.
        ModelState.AddModelError(
            string.Empty,
            "Входът е неуспешен. Провери имейла и паролата.");

        return Page();
    }

    // Пренасочва само към адрес от собствения сайт: отворен редирект
    // би позволил вход да изпрати потребителя към чужда страница.
    private IActionResult RedirectToLocal(string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToPage("/Index");
    }
}

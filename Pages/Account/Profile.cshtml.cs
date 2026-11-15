using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace CivicEyeGD.Pages.Account;

// Страницата е зад глобалния FallbackPolicy от Program.cs: тя е за влезли
// и не носи AllowAnonymous, затова гост не може да стигне до нея, а
// остава с него при входа с ReturnUrl.
// Ограничението „forms“ е същото като при входа и регистрацията: до 30
// изпращания от един IP адрес за минута. Без него би се пробвало насила
// да се отгатне сегашната парола на чужд профил, който някой е оставил
// отворен.
[EnableRateLimiting("forms")]
public class ProfileModel : PageModel
{
    // Само UserManager: той проверява сегашната парола, прилага
    // правилата от Program.cs и записва новия хеш. Входът не се
    // прекъсва и не се подновява оттук, затова SignInManager не е
    // нужен - и тестът може да конструира страницата със същите
    // услуги, с които живее и останалата бизнес логика.
    private readonly UserManager<IdentityUser> _userManager;

    public ProfileModel(UserManager<IdentityUser> userManager)
    {
        _userManager = userManager;
    }

    [BindProperty]
    public ChangePasswordInput Input { get; set; } = new();

    // Успехът се съобщава след преизкарането на страницата: при смяна
    // на парола няма къде друго да се отиде, без човек да загуби
    // контекста кой профил е променил.
    [BindProperty(SupportsGet = true)]
    public bool Changed { get; set; }

    public class ChangePasswordInput
    {
        // Сегашната парола се изисква задължително. Тя е единствената
        // причина смяната да не е возможна от чужд, но не заключен
        // браузър или от скрита форма, изпратена от друг сайт.
        [Required(ErrorMessage = "Въведи сегашната парола.")]
        [DataType(DataType.Password)]
        public string CurrentPassword { get; set; } = string.Empty;

        // Правилата са същите като при създаване на профил, за да не се
        // окаже, че човек може да смени паролата си с такава, с която
        // първоначално не би могъл да се регистрира.
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

        // Профилът се взема от влезлия потребител, не от формата:
        // в заявката няма къде да се напише „промени паролата на
        // потребител N“, затова единствената мишена е този, който е
        // влязъл.
        var user = await _userManager.GetUserAsync(User);

        if (user is null)
        {
            // Празно удостоверение се среща само ако бисквитата е
            // изтекла, докато формата е стояла отворена.
            return RedirectToPage("/Account/Login");
        }

        // NewPassword минава през политиката от Program.cs (8 знака,
        // цифра, малка буква), а CurrentPassword се сравнява с хеша,
        // не с текст: грешна сегашна парола дава PasswordMismatch,
        // преведен от BulgarianIdentityErrorDescriber.
        var result = await _userManager.ChangePasswordAsync(
            user,
            Input.CurrentPassword,
            Input.NewPassword);

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

        // Съобщението идва като адрес с отметка, а не като текст,
        // вписан в HTML-а: при опресняване браузърът пита дали да
        // преизпрати формата, а това е по-честният път от повторното
        // изпращане на паролите.
        return RedirectToPage("/Account/Profile", new { changed = true });
    }
}

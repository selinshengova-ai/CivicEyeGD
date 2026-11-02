
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CivicEyeGD.Pages.Account;

// Няма AllowAnonymous: изход има смисъл само за човек със сесия, а
// глобалната FallbackPolicy го пази и без друга отметка.
public class LogoutModel : PageModel
{
    // Единствената необходима услуга е тази, която затваря сесията;
    // базата данни тук не се пипа, затова я няма в конструктора.
    private readonly SignInManager<IdentityUser> _signInManager;

    public LogoutModel(SignInManager<IdentityUser> signInManager)
    {
        _signInManager = signInManager;
    }

    // Има само POST, няма OnGet: изход не бива да е обикновена връзка
    // или картинка от чужд сайт, защото така някой друг би те изкарал
    // от акаунта без твоя воля. Формата в изгледа носи предпазния
    // токен, затова заявката идва единствено от собствения сайт.
    public async Task<IActionResult> OnPostAsync()
    {
        // Премахва се бисквитата за вход. Акаунтът, сигналите и
        // паролата остават недокоснати — влизането пак е възможно.
        await _signInManager.SignOutAsync();

        // Посоката е рекламната страница, защото тя е публична и не
        // изисква вход; след изход човекът вече е анонимен.
        return RedirectToPage("/Welcome");
    }
}

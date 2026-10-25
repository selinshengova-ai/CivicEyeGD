
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace CivicEyeGD.Pages.Account;

[AllowAnonymous]
[EnableRateLimiting("forms")]
public class LoginModel : PageModel
{
    private readonly SignInManager<IdentityUser> _signInManager;

    public LoginModel(SignInManager<IdentityUser> signInManager)
    {
        _signInManager = signInManager;
    }

    [BindProperty]
    public LoginInput Input { get; set; } = new();

    // Къде да пратим потребителя след вход. Идва като query параметър,
    // затова SupportsGet; без него вход от „влез, за да гласуваш“
    // връща човека към началната страница вместо към сигнала.
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public class LoginInput
    {
        [Required(ErrorMessage = "Въведи имейл адрес.")]
        [EmailAddress(ErrorMessage = "Въведи валиден имейл адрес.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Въведи паролата си.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
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

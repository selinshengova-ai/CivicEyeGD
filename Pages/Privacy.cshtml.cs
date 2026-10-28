using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CivicEyeGD.Pages;

// Връзката „Поверителност" е във футъра и трябва да се отваря и от влезлия профил.
[Microsoft.AspNetCore.Authorization.AllowAnonymous]
public class PrivacyModel : PageModel
{
    public void OnGet()
    {
    }
}


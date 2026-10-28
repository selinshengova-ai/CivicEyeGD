
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CivicEyeGD.Pages;

public class WelcomeModel : PageModel
{
    // Рекламната страница е за хора без профил. Който вече е влязъл,
    // не трябва да се връща към нея всеки път при „/“.
    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToPage("/Index");
        }

        return Page();
    }
}

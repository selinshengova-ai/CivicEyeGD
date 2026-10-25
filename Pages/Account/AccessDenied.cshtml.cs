using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CivicEyeGD.Pages.Account;

// Identity насам препраща влезъл потребител, който поиска страница
// само за администратор. Без тази страница пренасочването се превръща
// в 404 и човек не разбира какво се е случило.
[AllowAnonymous]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class AccessDeniedModel : PageModel
{
    public void OnGet()
    {
    }
}

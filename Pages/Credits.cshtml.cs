using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CivicEyeGD.Pages;

// Връзката „Кредити“ е във футъра до „Поверителност“ и трябва да се
// отваря и от влезлия профил, и от непознатия браузър.
//
// Без атрибута страницата паднеше под глобалната FallbackPolicy: списъкът
// с чуждите библиотеки се виждаше чак след вход, а той е текстът, който
// се чете най-напред.
[Microsoft.AspNetCore.Authorization.AllowAnonymous]
public class CreditsModel : PageModel
{
    // Празен метод: съдържанието е неподвижен текст в изгледа, няма какво
    // да се зарежда и няма заявка към базата. Методът съществува само за да
    // отговаря маршрутът на GET.
    public void OnGet()
    {
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CivicEyeGD.Pages;

// Връзката „Поверителност“ е във футъра и трябва да се отваря и от влезлия профил.

// Атрибутът е изписан с пълното име на класа, защото файлът няма
// using за Microsoft.AspNetCore.Authorization.
//
// Без него страницата паднеше под глобалната FallbackPolicy и текстът
// за личните данни се виждаше чак след вход. Смисълът е обратният:
// човек чете как ще бъдат използвани данните му, преди да реши дали
// изобщо да създава акаунт.
[Microsoft.AspNetCore.Authorization.AllowAnonymous]
public class PrivacyModel : PageModel
{
    // Празен метод: съдържанието е неподвижен текст в изгледа, няма
    // какво да се зарежда и няма заявка към базата. Методът съществува
    // само за да отговаря маршрутът на GET.
    public void OnGet()
    {
    }
}


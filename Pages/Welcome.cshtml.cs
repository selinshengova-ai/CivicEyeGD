
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CivicEyeGD.Pages;

// Рекламната страница е първата, която вижда човек без акаунт: адресът
// „/“ е записан в Program.cs и пренасочва насам. Отметката
// AllowAnonymous не стои по класа, а в изгледа (Welcome.cshtml я
// обявява с @attribute), за да не остане публичната страница под
// глобалната FallbackPolicy.
public class WelcomeModel : PageModel
{
    // Рекламната страница е за хора без профил. Който вече е влязъл,
    // не трябва да се връща към нея всеки път при „/“.
    //
    // Методът връща IActionResult, а не void, защото тук се избира
    // между две различни действия; void би значело само „покажи
    // изгледа“. Проверката се чете през User.Identity?, защото при
    // анонимна заявка Identity може да липсва и директен достъп до
    // IsAuthenticated би гръмнал.
    //
    // RedirectToPage с водач „/“ е абсолютен път: без него пътът се
    // тълкува спрямо папката на текущата страница.
    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            // Влезлият отива към таблото: там са неговите сигнали и
            // картата, а рекламният текст би му губил времето.
            return RedirectToPage("/Index");
        }

        // Който не е влязъл, остава тук и вижда бутоните за вход и
        // за регистрация.
        return Page();
    }
}

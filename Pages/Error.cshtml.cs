using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CivicEyeGD.Pages;

[AllowAnonymous]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    public string? RequestId { get; set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    public int StatusCodeValue { get; private set; }

    public string Title { get; private set; } = "Неочаквана грешка";

    public string Description { get; private set; } =
        "Заявката не можа да бъде обработена. Опитай отново след малко.";

    public void OnGet(int? code)
    {
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        StatusCodeValue = code ?? HttpContext.Response.StatusCode;

        (Title, Description) = StatusCodeValue switch
        {
            404 => ("Страницата не е намерена",
                "Търсеният адрес не съществува в CivicEye GD."),
            403 => ("Нямаш достъп",
                "Тази част от системата е само за оторизирани потребители."),
            400 => ("Невалидна заявка",
                "Формата не е изпратена правилно. Опитай отново."),
            >= 500 => ("Грешка в сървъра",
                "Възникна проблем от наша страна. Той е записан в "
                + "дневника на сървъра, така че може да бъде проследен."),
            _ => (Title, Description)
        };
    }
}

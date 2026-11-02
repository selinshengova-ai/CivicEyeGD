
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace CivicEyeGD.Pages.Account;

// Регистрацията е анонимна по същата причина като входа: на този етап
// нямаше кой да бъде влязъл, а FallbackPolicy затваря всичко без
// отметка.
[AllowAnonymous]

// Тук ограничението е единствената защита срещу пълнене на базата с
// фалшиви профили: потвърждение по имейл няма (RequireConfirmedAccount
// е изключен в Program.cs), затова остава само политиката „forms“.
[EnableRateLimiting("forms")]
public class RegisterModel : PageModel
{
    // UserManager създава и валидира профила, SignInManager го вкарва
    // в сайта след това. И двата идват от контейнера, за да може тест
    // да сложи временна база вместо истинската.
    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;

    public RegisterModel(
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    // Полетата са в отделен вложен клас, за да не се бъркат със
    // свойствата на страницата; BindProperty ги връща попълнени, когато
    // формата се преизкара след грешка.
    [BindProperty]
    public RegisterInput Input { get; set; } = new();

    public class RegisterInput
    {
        // Имейлът е едновременно и потребителското име, затова се
        // изисква веднъж и трябва да е валиден: проверките са и на
        // сървъра, не само в браузъра.
        [Required(ErrorMessage = "Въведи имейл адрес.")]
        [EmailAddress(ErrorMessage = "Въведи валиден имейл адрес.")]
        public string Email { get; set; } = string.Empty;

        // Минимумът от 8 символа е повторение на политиката от
        // Program.cs, за да се види като човешко съобщение още в
        // формата. Цифрата и малката буква тук ги няма — Identity ги
        // проверява при CreateAsync, а съобщението идва преведен от
        // BulgarianIdentityErrorDescriber. Горната граница пази от
        // случаи, в които някой изпрати цяла страница като парола.
        [Required(ErrorMessage = "Въведи парола.")]
        [StringLength(
            100,
            MinimumLength = 8,
            ErrorMessage = "Паролата трябва да е поне 8 символа.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        // Потвърждението се сравнява на сървъра с nameof(Password), а
        // не с изписано име: при преименуване на полето грешката се
        // вижда още при превеждането, а не когато някой се регистрира.
        [Required(ErrorMessage = "Потвърди паролата.")]
        [DataType(DataType.Password)]
        [Compare(
            nameof(Password),
            ErrorMessage = "Паролите не съвпадат.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    // Празен метод: формата е неподвижна и няма какво да се зарежда.
    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Невалидна форма се връща веднага, преди базата да е видяла
        // каквото и да е: нищо не се записва на половина.
        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Потребителското име е същият имейл: така остава едно
        // единствено нещо за запомняне при вход и няма второ име,
        // което да се сбъсква с чуждо.
        var user = new IdentityUser
        {
            UserName = Input.Email,
            Email = Input.Email
        };

        // CreateAsync проверява паролата по политиката от Program.cs
        // (8 знака, цифра, малка буква) и записва само нейния хеш, не
        // самата парола.
        var result = await _userManager.CreateAsync(
            user,
            Input.Password);

        if (!result.Succeeded)
        {
            // Всички грешки се изписват една по една, защото човекът
            // трябва да чуе всичко наведнъж и да поправи формата в един
            // опит. Текстът идва преведен от BulgarianIdentityErrorDescriber.
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(
                    string.Empty,
                    error.Description);
            }

            return Page();
        }

        // Ролята се задава от кода, не от формата. Тя е винаги
        // „Citizen“, затова никой не може да поиска администраторски
        // права, като промени едно поле в заявката; админите идват
        // единствено от DbSeeder.
        var roleResult = await _userManager.AddToRoleAsync(
            user,
            "Citizen");

        if (!roleResult.Succeeded)
        {
            // Половинчав профил се изтрива веднага: без роля човекът
            // остава недовършен, а заетото потребителско име би пречило
            // да се опита отново със същия имейл.
            await _userManager.DeleteAsync(user);

            foreach (var error in roleResult.Errors)
            {
                ModelState.AddModelError(
                    string.Empty,
                    error.Description);
            }

            return Page();
        }

        // Човекът влиза веднага, без втора форма за вход: самата
        // регистрация вече доказа, че той знае паролата си.
        // isPersistent е false, за да не остава акаунтът отворен на
        // чужд или споделен компютър след затваряне на браузъра.
        await _signInManager.SignInAsync(
            user,
            isPersistent: false);

        // Началната страница е първото, от което човек има нужда сега:
        // там се виждат сигналите и бутонът за подаване на нов.
        return RedirectToPage("/Index");
    }
}

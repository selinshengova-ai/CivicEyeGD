using CivicEyeGD.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CivicEyeGD.Pages;

// Страницата е лична, затова изискването за вход е записано изрично с
// [Authorize], а не оставено само на глобалната FallbackPolicy: така
// остава в кода, дори политиката по подразбиране един ден да се смени.
// Атрибутът стои по класа, защото поставен върху обработващ метод на
// Razor Pages той не стига до крайната точка и не действа.
[Authorize]
public class MyReportsModel : PageModel
{
    // И двата сервиса идват през конструктора: контейнерът ги създава
    // за всяка заявка, а тестът може да сложи друга база вместо
    // истинската, без класът да знае за това.
    private readonly ApplicationDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;

    public MyReportsModel(ApplicationDbContext context, UserManager<IdentityUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    // Полето е празен списък, не null: изгледът брои резултатите и
    // показва „все още нямаш сигнали“, така че работи и когато
    // заявката не е върнала нищо. Променя се само вътре в класа
    // (private set) — кой какви сигнали вижда се решава от заявката.
    public List<Report> Reports { get; private set; } = new();

    public async Task OnGetAsync()
    {
        // Идентификаторът идва от бисквитата за вход, не от адреса на
        // страницата. Ако се четеше от параметър, всеки би написал
        // чужд номер и би получил нечий личен списък.
        var userId = _userManager.GetUserId(User);

        // Филтърът е единственият и е в базата: в списъка няма как да
        // влезе сигнал на друг човек.
        // AsNoTracking не следи за промени — страницата нищо не
        // записва, затова излишна памет не трябва да се харчи.
        // Редът е по CreatedAt надолу, защото човекът търси
        // най-новото; полето се записва в UTC, та подредбата не се
        // разваля при смяна на часовото време.
        // Целият списък идва наведнъж, без разделяне на страници:
        // сигналите на един човек в малък град са десетки, не хиляди.
        Reports = await _context.Reports
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }
}
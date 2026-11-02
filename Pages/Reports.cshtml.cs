using CivicEyeGD.Data;
using CivicEyeGD.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CivicEyeGD.Pages;

// Списъкът е публичен; създаването и „Моите сигнали“ си остават зад вход.

// Отметката е по класа, защото там се чете от Razor Pages; по метода
// тя не стига до крайната точка и глобалната FallbackPolicy остава
// изискването за вход. Публичният списък е витрината — през него
// гражданинът и журито преценяват дали си струва акаунт.
[AllowAnonymous]
public class ReportsModel : PageModel
{
    // Само базата данни и услугата за приоритет: страницата нищо не
    // записва и не зависи от профила, затова тук няма нито
    // SignInManager, нито проверка за роли.
    private readonly ApplicationDbContext _context;

    private readonly ReportPriorityService _priorityService;

    public ReportsModel(ApplicationDbContext context, ReportPriorityService priorityService)
    {
        _context = context;
        _priorityService = priorityService;
    }

    // Подредбата по подразбиране: най-спешното се вижда първо, защото
    // приоритетът е точно отговорът на въпроса „кое чака най-много“.
    public const string DefaultSort = "priority";

    // Три подредби, изброени с ръце, а не надуени от базата: всяка
    // носи различен въпрос, а четвърта би била шум.
    public static readonly (string Value, string Text)[] SortOptions =
    [
        ("priority", "По приоритет"),
        ("newest", "Най-новите първи"),
        ("oldest", "Най-старите първи"),
    ];

    public string Sort { get; private set; } = DefaultSort;

    // Името на приложената подредба: то стои над списъка, за да не се
    // налага човек да отваря кутията, за да разбере по какво е наредено.
    public string SortText =>
        SortOptions.FirstOrDefault(option => option.Value == Sort).Text
        ?? SortOptions[0].Text;

    // Всички полета се четат от изгледа, но не се променят от него
    // (private set): кой какви сигнали вижда се решава единствено в
    // OnGetAsync. Празните списъки в началото пазят изгледа от null,
    // ако заявката не е върнала нищо.
    public List<Report> Reports { get; private set; } = new();

    // Двата списъка пълнят кутиите за избор на филтъра.
    public List<string> Categories { get; private set; } = new();

    public List<string> Statuses { get; private set; } = new();

    // Трите полета са едновременно приложените условия и текстът,
    // който остава в кутиите след презареждането. Без тях формата би
    // се изчистила при всяко търсене и човек губи това, което е
    // написал.
    public string Search { get; private set; } = string.Empty;

    public string Category { get; private set; } = string.Empty;

    public string Status { get; private set; } = string.Empty;

    public async Task OnGetAsync(
        string? search,
        string? category,
        string? status,
        string? sort)
    {
        // Стойностите идват от адреса като query параметри и могат да
        // липсват или да са само интервали. Trim и празният низ ги
        // свеждат до едно състояние, за да не се тълкува „   “ като
        // търсена категория и списъкът да не остава пуст без причина.
        Search = search?.Trim() ?? string.Empty;
        Category = category?.Trim() ?? string.Empty;
        Status = status?.Trim() ?? string.Empty;

        // Подредбата се приема само ако е от изброените: измислен текст
        // в адреса връща подразбираната, вместо списък без подредба.
        Sort = SortOptions.Any(option => option.Value == sort) ? sort! : DefaultSort;

        // Кутиите за избор се пълнят от реалните сигнали, а не от
        // готовия списък на ReportCategories: остава само това, под
        // което има нещо за гледане, така филтър не води до пуст екран.
        // Distinct и OrderBy се изпълняват от базата, не в паметта.
        Categories = await _context.Reports
            .AsNoTracking()
            .Select(r => r.Category)
            .Where(c => c != null && c != "")
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();

        // Празната и null стойност се махат, защото иначе в падащите
        // списъци се появява празна опция, която не филтрира нищо.
        Statuses = await _context.Reports
            .AsNoTracking()
            .Select(r => r.Status)
            .Where(s => s != null && s != "")
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync();

        // Списъкът показва броя потвърждения и приоритета, затова и
        // двете колекции идват с реда. Без Include те са празни и
        // сигналът изглежда непотвърден и с минимален приоритет, макар
        // в базата да има гласове.
        var query = _context.Reports
            .Include(r => r.Confirmations)
            .Include(r => r.SeverityRatings)
            .AsNoTracking();

        // Условията се добавят само когато има какво да се филтрира;
        // празният филтър не трябва да се превръща в сравнение с
        // „празен низ“ и да крие сигналите.
        // Търсенето минава по заглавие, описание и адрес: човек помни
        // едно от трите, не номера на сигнала.
        if (!string.IsNullOrWhiteSpace(Search))
        {
            query = query.Where(r =>
                r.Title.Contains(Search) ||
                r.Description.Contains(Search) ||
                r.Address.Contains(Search));
        }

        // Точното съвпадение е умишлено: стойностите идват от кутиите,
        // не от написан от ръката текст, и свободното търсене тук само
        // би донесло изненадващи резултати.
        if (!string.IsNullOrWhiteSpace(Category))
        {
            query = query.Where(r => r.Category == Category);
        }

        // Статусът се филтрира по същия начин, за да може журито да
        // види например само сигналите, които все още чакат.
        if (!string.IsNullOrWhiteSpace(Status))
        {
            query = query.Where(r => r.Status == Status);
        }

        // Подредбата се случва в паметта, не в SQLite: приоритетът е
        // сума от три колекции, които базата не знае как се събират, а
        // втора формула в SQL би остаряла спрямо услугата.
        Reports = OrderReports(await query.ToListAsync());
    }

    // Приключилите сигнали са последни при всяка подредба: затвореният
    // проблем вече не чака нищо, макар някога да е бил най-спешен.
    private List<Report> OrderReports(List<Report> reports) => Sort switch
    {
        "newest" => reports
            .OrderBy(r => ReportStatuses.IsClosed(r.Status))
            .ThenByDescending(r => r.CreatedAt)
            .ToList(),
        "oldest" => reports
            .OrderBy(r => ReportStatuses.IsClosed(r.Status))
            .ThenBy(r => r.CreatedAt)
            .ToList(),
        _ => reports
            .OrderBy(r => ReportStatuses.IsClosed(r.Status))
            .ThenByDescending(ScoreOf)
            .ThenBy(r => r.Id)
            .ToList()
    };

    // Точният приоритет по формулата от услугата — същата като в
    // таблото и в страницата на сигнала, затова и трите места винаги
    // сочат едно и също число.
    public double ScoreOf(Report report) =>
        _priorityService.Calculate(
            report.IndependentAverageSeverity,
            report.IndependentConfirmationCount,
            report.AdminPriorityAdjustment);

    public string LabelOf(Report report) =>
        _priorityService.GetPriorityLabel(ScoreOf(report));

    // Червеното е само за активните: приключен с висок приоритет е
    // свършен факт, а не работа, която чака. Правилото живее в
    // услугата, за да съвпада с пулсацията по картата и с таблото.
    public bool IsUrgent(Report report) => _priorityService.IsUrgent(report);
}
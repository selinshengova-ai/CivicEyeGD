using System.Security.Claims;
using System.Text.Encodings.Web;
using CivicEyeGD.Data;
using CivicEyeGD.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CivicEyeGD.Tests;

/// <summary>
/// Изолирана среда за един тест: временна папка вместо корена на
/// приложението, временна SQLite база и реални сервиси през DI.
/// Нищо не пипа нито истинската App_Data база, нито диска на проекта.
/// </summary>
public sealed class TestHost : IDisposable
{
    private readonly string _rootDirectory;
    private readonly ServiceProvider _services;
    private readonly IServiceScope _scope;

    public TestHost()
    {
        _rootDirectory = Path.Combine(
            Path.GetTempPath(),
            "civiceye-tests-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_rootDirectory);

        var environment = new TempEnvironment(_rootDirectory);
        DatabaseFileName = Path.Combine(_rootDirectory, "test.db");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GoogleMapsApi:ApiKey"] = string.Empty
            })
            .Build();

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IWebHostEnvironment>(environment);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(new ReportImageStorage(environment));
        services.AddSingleton<ReportPriorityService>();
        services.AddSingleton<ReportExportService>();
        services.AddSingleton<ReportPdfService>();
        services.AddScoped<ReportOfficeService>();

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlite($"Data Source={DatabaseFileName}"));

        // Доставчикът на токени за възстановяване подписва стойности през
        // DataProtection. В уеб приложението идва с самото изграждане на
        // сайта, а тук услугите се сглобяват на ръка, затова се добавя
        // изрично — иначе генерирането на токена нямаше как да тръгне.
        services.AddDataProtection();

        // AddRoles е задължително, иначе UserManager.GetRolesAsync хвърля
        // NotSupportedException, а историята на статуса пита именно ролите.
        // AddDefaultTokenProviders е второто задължително: възстановяването
        // на парола минава през GeneratePasswordResetTokenAsync, а без
        // доставчиците на токени извикването спира с изключение. В Program.cs
        // същият ред съществува, затова тестове минават по същия път.
        services.AddIdentityCore<IdentityUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders()

            // Регистрацията вписва човека веднага след създаването, а
            // AddIdentityCore не добавя сам SignInManager — той идва с
            // AddIdentity. Без този ред PageModel-ът на регистрацията нямаше
            // как да бъде конструиран изобщо.
            .AddSignInManager<SignInManager<IdentityUser>>();

        // Изтриването на профил разписва и бисквитата за вход, а тук няма
        // браузър, на който бисквитата да се сложи или свали. Затова схемата
        // на Identity идва с обработчик, който не върши нищо: извикването
        // минава по същия път като в Program.cs, а проверките в тестовете
        // остават за базата, не за бисквитата.
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, SilentSignOutHandler>(
                IdentityConstants.ApplicationScheme,
                options => { });

        // Сървърът превежда грешките на Identity на български — същият
        // описвател е регистриран и в Program.cs. Тестовете за парола
        // твърдят нещо конкретно за текста, който гражданинът вижда,
        // затова и тук той е българският, а не английският по подразбиране.
        services.AddScoped<IdentityErrorDescriber, BulgarianIdentityErrorDescriber>();

        _services = services.BuildServiceProvider();
        _scope = _services.CreateScope();

        Context = _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Context.Database.EnsureCreated();
    }

    public ApplicationDbContext Context { get; }

    public string DatabaseFileName { get; }

    public string RootDirectory => _rootDirectory;

    public ReportImageStorage Storage
        => _scope.ServiceProvider.GetRequiredService<ReportImageStorage>();

    public ReportPriorityService Priority
        => _scope.ServiceProvider.GetRequiredService<ReportPriorityService>();

    /// <summary>
    /// Услуги от същия диапазон, в който живее и Context. Нужно е за
    /// seed-а, който сам си търси UserManager и DbContext.
    /// </summary>
    public IServiceProvider Services => _scope.ServiceProvider;

    /// <summary>Конфигурация само за един тест.</summary>
    public static IConfiguration Config(params (string Key, string? Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(item => item.Key, item => item.Value))
            .Build();

    /// <summary>Създава потребител директно в базата и връща Id-то му.</summary>
    public async Task<string> CreateUserAsync(string userName)
    {
        var user = new IdentityUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = userName,
            Email = userName + "@civiceye.test",
            EmailConfirmed = true
        };

        Context.Users.Add(user);
        await Context.SaveChangesAsync();

        return user.Id;
    }

    /// <summary>
    /// Потребител с ролята „Administrator“ в базата: историята на
    /// статуса пита точно това, за да покаже служителя като длъжност,
    /// а не като имейл.
    /// </summary>
    public async Task<string> CreateAdministratorAsync(string userName)
    {
        var userId = await CreateUserAsync(userName);

        var role = await Context.Roles.FirstOrDefaultAsync(r => r.Name == "Administrator")
                   ?? await AddAdministratorRoleAsync();

        Context.UserRoles.Add(new IdentityUserRole<string> { RoleId = role.Id, UserId = userId });
        await Context.SaveChangesAsync();

        return userId;
    }

    private async Task<IdentityRole> AddAdministratorRoleAsync()
    {
        var role = new IdentityRole("Administrator") { NormalizedName = "ADMINISTRATOR" };

        Context.Roles.Add(role);
        await Context.SaveChangesAsync();

        return role;
    }

    public ClaimsPrincipal PrincipalFor(string userId)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId)],
            authenticationType: "Test",
            nameType: ClaimTypes.Name,
            roleType: ClaimTypes.Role);

        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Връща PageModel от DI с готов PageContext, в който е записан
    /// влезлият потребител. Така handler-ите виждат същия контекст
    /// като при истинска заявка.
    /// </summary>
    public TModel CreatePageModel<TModel>(
        ClaimsPrincipal? user = null,
        Action<HttpContext>? configureRequest = null)
        where TModel : PageModel
    {
        var httpContext = new DefaultHttpContext
        {
            // Реалната заявка носи сервиси в контекста; без тях
            // резултати като RedirectToPage не могат да се създадат.
            RequestServices = _scope.ServiceProvider
        };

        if (user is not null)
        {
            httpContext.User = user;
        }

        configureRequest?.Invoke(httpContext);

        // SignInManager си търси контекста не през RequestServices, а през
        // IHttpContextAccessor, който при истинска заявка го слага самото
        // ASP.NET. Без този ред влизането след регистрация спира с
        // „HttpContext must not be null“ — тоест би показал грешка там,
        // където страницата е изправна.
        _scope.ServiceProvider
            .GetRequiredService<IHttpContextAccessor>()
            .HttpContext = httpContext;

        var model = ActivatorUtilities.CreateInstance<TModel>(_scope.ServiceProvider);

        var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary());

        model.PageContext = new PageContext
        {
            HttpContext = httpContext,
            RouteData = new RouteData(),
            ActionDescriptor = new CompiledPageActionDescriptor(),
            ViewData = viewData
        };

        return model;
    }

    /// <summary>Записва готов сигнал в базата и връща реда.</summary>
    public async Task<Report> AddReportAsync(
        string userId,
        Action<Report>? mutate = null)
    {
        var report = new Report
        {
            Title = "Пъпка на булеварда",
            Description = "Неравна настилка пред входа.",
            Category = ReportCategories.RoadSurface,
            Address = "бул. „Гоце Делчев“ 20",
            Status = ReportStatuses.New,
            CreatedAt = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc),
            Latitude = 41.5713,
            Longitude = 23.7361,
            UserId = userId
        };

        mutate?.Invoke(report);

        Context.Reports.Add(report);
        await Context.SaveChangesAsync();

        return report;
    }

    public void Dispose()
    {
        _scope.Dispose();
        _services.Dispose();
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
        catch (IOException)
        {
            // временната папка остава и системата я почистя при нужда
        }
    }

    /// <summary>
    /// Схема за вход без бисквита. Изтриването на профил разписва
    /// бисквитата след изтриването в базата, а регистрацията я слага,
    /// без никой да я иска: без регистрирана схема самото извикване спира
    /// с изключение — и тестът би показал грешка там, където нищо не е
    /// сгрешено. Обработчикът не върши нищо и точно това е неговата роля:
    /// да замени механизма, а не проверката.
    /// </summary>
    private sealed class SilentSignOutHandler :
        AuthenticationHandler<AuthenticationSchemeOptions>,
        IAuthenticationSignInHandler
    {
        public SilentSignOutHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());

        // Двете реализации са задължителни като интерфейсни, не само като
        // желания резултат: AuthenticationService търси точния интерфейс и
        // отказва с изключение, ако го няма — независимо че обработчикът
        // умее да не прави нищо. Входът е нужен на регистрацията, а
        // разписването на бисквитата — на изтриването на профила.
        Task IAuthenticationSignInHandler.SignInAsync(ClaimsPrincipal user, AuthenticationProperties? properties)
            => Task.CompletedTask;

        Task IAuthenticationSignOutHandler.SignOutAsync(AuthenticationProperties? properties)
            => Task.CompletedTask;
    }

    private sealed class TempEnvironment : IWebHostEnvironment
    {
        public TempEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            WebRootPath = Path.Combine(contentRootPath, "wwwroot");
            Directory.CreateDirectory(WebRootPath);
        }

        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "CivicEyeGD.Tests";

        public string ContentRootPath { get; set; }

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; }

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}

using CivicEyeGD.Data;
using CivicEyeGD.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// --- 1. МЯСТО ЗА БАЗАТА И КЛЮЧОВЕТЕ ---
// App_Data е извън wwwroot, затова файловете не се сервират директно.
// Папката на сайта е единственото, което се запазва при споделен хостинг,
// затова базата и ключовете са тук, а не в временната директория.
var appDataPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(appDataPath);

// Паролите на демо акаунтите не влизат в хранилището, а на споделен хост
// променливи на средата обикновено не се задават. Локално те идват от
// user-secrets, на сайта — от този файл: той е извън wwwroot, не се
// обслужва като статичен и е изключен от publish, затова не попада в
// архива за олимпиадата.
var seedSecretsFile = Path.Combine(appDataPath, "seed-accounts.json");
if (File.Exists(seedSecretsFile))
{
    builder.Configuration.AddJsonFile(seedSecretsFile, optional: true, reloadOnChange: false);
}

var databaseProvider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var connectionString = builder.Configuration.GetConnectionString(databaseProvider);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    switch (databaseProvider)
    {
        case "Postgres":
            options.UseNpgsql(connectionString);
            break;

        case "SqlServer":
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
            break;

        default:
            // Локална чанта: пътят се изчислява, за да не зависи от работната
            // директория на процеса, която при хостинг е различна.
            var sqliteSource = string.IsNullOrWhiteSpace(connectionString)
                ? $"Data Source={Path.Combine(appDataPath, "CivicEyeGD.db")}"
                : connectionString;
            options.UseSqlite(sqliteSource);
            options.AddInterceptors(new SqliteBusyTimeoutInterceptor());
            break;
    }
});

builder.Services
    .AddIdentity<IdentityUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = false;
        // Двете числа са от AccountLockout, защото същите стоят и в
        // текста, с който страниците обясняват заключването.
        options.Lockout.MaxFailedAccessAttempts = AccountLockout.FailedAttempts;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(AccountLockout.WaitMinutes);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// Входната бисквита идва с 14 дни живот по подразбиране и с „Remember me“
// наравно. На общ компютър това е отворена врата към чужд акаунт, затова
// сесията се скъсява до седмица с плъзгащо подновяване: човек, който
// работи със сайта всеки ден, не влиза наново, а изоставеният браузър
// спира да бъде ключ. HttpOnly и Lax са стойностите по подразбиране и са
// изписани изрично, за да се виждат при одит.
builder.Services.ConfigureApplicationCookie(options =>
{
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

    // Извън разработка сайтът се отваря само през HTTPS. Зад прокси на
    // споделения хост Request.IsHttps понякога остава false, защото
    // заглавката X-Forwarded-Proto идва от адрес, който не е вписан като
    // известен прокси, и тогава „SameAsRequest“ би пуснал бисквитата без
    // флага Secure. Изискването е поставено изрично; локално, където
    // сайтът се гледа като http://localhost, остава както досега.
    if (!builder.Environment.IsDevelopment())
    {
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    }
});

// Подновеният подпис на профила изхвърля отворените браузъри — но
// Identity ги проверява по график. По подразбиране графикът е 30 минути,
// затова страницата за възстановяване би обещала повече, отколкото
// върши: един влязъл браузър би останал жив половин час след смяната на
// паролата. Една минута е цената на едно допълнително четене на профила
// за заявка, за да излезе навън всеки, чиято парола току-що е сменена.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.FromMinutes(1));

// Identity иначе говори на английски за грешки в паролата и имейла,
// а останалите текстове в приложението са български.
builder.Services.AddScoped<IdentityErrorDescriber, BulgarianIdentityErrorDescriber>();

// Ключовете за защита подписват бисквитата за вход. Без трайно хранилище
// всеки рестарт на процес прави всички влезли потребители анонимни.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(appDataPath, "DataProtectionKeys")))
    .SetApplicationName("CivicEyeGD");

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddRazorPages();

// Колко често един адрес може да изпраща форми. Без това пълненето на
// базата с грешни опити за вход или със сигнали е въпрос на секунди.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Една кофа за писане и една за четене на страница. Четенето има
    // по-висок таван, защото проверката за близък сигнал се пуска при
    // всяко местене на маркера: с обща кофа човек, който търси точното
    // място, изяжда лимита за изпращане, преди да е попълнил формата.
    options.AddPolicy("forms", httpContext =>
    {
        var writing = IsWriteRequest(httpContext);
        var key = ClientKey(httpContext);

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: writing ? key : key + ":read",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = writing ? 30 : 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });

    // Гласуването се брои по човек, не по адрес: в училище или в общината
    // цялата сграда излиза през един адрес и общ лимит би спрял
    // гражданите, а не скрипта. Един глас на сигнал и без това спира
    // повторното натискане; това ограничение спира обхождането на
    // много сигнали подред с един акаунт.
    options.AddPolicy("votes", httpContext =>
    {
        var user = httpContext.User.Identity?.IsAuthenticated == true
            ? httpContext.User.Identity.Name
            : ClientKey(httpContext);
        var writing = IsWriteRequest(httpContext);

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: "vote:" + user + (writing ? string.Empty : ":read"),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = writing ? 20 : 90,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        await context.HttpContext.Response.WriteAsync(
            "Твърде много опити за една минута. Изчакай малко и опитай отново.",
            cancellationToken);
    };
});

builder.Services.AddSingleton<ReportImageStorage>();
builder.Services.AddSingleton<ReportPriorityService>();
builder.Services.AddSingleton<ReportExportService>();
builder.Services.AddSingleton<ReportPdfService>();
builder.Services.AddScoped<ReportOfficeService>();

var app = builder.Build();

// Зад обратен прокси (Azure App Service, nginx, IIS на споделен хост)
// заявките пристигат като HTTP, а адресът на клиента е в X-Forwarded-For.
// Без обработка приложение не разбира, че потребителят е на HTTPS, и
// всички заявки изглеждат от адреса на проксито — тоест една обща кофа
// за лимита. Доверяваме се само на изброените в настройките проксита:
// иначе всеки би могъл да подправи заглавката и да смени адреса си.
if (!app.Environment.IsDevelopment())
{
    var forwardedHeaders = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        // Само последният прокси пред нас се брои: верига от няколко
        // заглавки е начин да се подмени адресът.
        ForwardLimit = 1
    };

    foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
    {
        if (IPAddress.TryParse(proxy, out var address))
        {
            forwardedHeaders.KnownProxies.Add(address);
        }
    }

    // Празният списък е валиден само ако сайтът наистина не е зад прокси.
    // Затова липсата се записва в дневника: при споделен хост това е
    // причината лимитът да е общ, а не скрита подробност.
    if (forwardedHeaders.KnownProxies.Count == 0)
    {
        app.Logger.LogWarning(
            "Няма изброени доверени проксита (ForwardedHeaders:KnownProxies). "
            + "Ако сайтът е зад обратен прокси, адресът на клиента няма да се разпознава.");
    }

    app.UseForwardedHeaders(forwardedHeaders);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");

// Заглавките важат и за статичните файлове, затова са преди тях.
// 'unsafe-inline' за скриптове и стилове е нужен, защото страниците
// ползват вградени блокове и събития; външните източници са само
// картите (Leaflet, OpenStreetMap, Google) и затова са изброени.
const string contentSecurityPolicy =
    "default-src 'self'; "
    + "script-src 'self' 'unsafe-inline' https://unpkg.com https://cdn.jsdelivr.net "
    + "https://maps.googleapis.com https://maps.gstatic.com; "
    + "style-src 'self' 'unsafe-inline' https://unpkg.com https://cdn.jsdelivr.net "
    + "https://fonts.googleapis.com; "
    + "font-src 'self' data: https://fonts.gstatic.com; "
    + "img-src 'self' data: blob: https://*.tile.openstreetmap.org https://tile.openstreetmap.org "
    + "https://unpkg.com https://cdn.jsdelivr.net https://maps.googleapis.com https://maps.gstatic.com "
    + "https://streetviewpixels-pa.googleapis.com "
    + "https://*.ggpht.com https://*.googleusercontent.com; "
    + "connect-src 'self' https://nominatim.openstreetmap.org https://maps.googleapis.com; "
    + "frame-src 'self' https://www.google.com; "
    + "object-src 'none'; "
    + "base-uri 'self'; "
    + "form-action 'self'; "
    + "frame-ancestors 'none'";

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;

    headers["Content-Security-Policy"] = contentSecurityPolicy;
    headers["X-Frame-Options"] = "DENY";
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    // Геолокацията е разрешена само за собствения произход, а не забранена
    // с празни скоби: без това бутонът „Местоположението ми“ получава отказ
    // още преди браузърът да попита човека, и то с грешно обяснение.
    // Камерата, микрофонът и плащанията наистина не се ползват и остават
    // затворени.
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(self), payment=()";

    await next();
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// След проверката за самоличност: политиката „votes“ дели кофите по
// потребител, а той е известен едва след UseAuthentication.
app.UseRateLimiter();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    // WAL ползва споделена памет, която мрежовите дискове на хостовете не
    // поддържат, и записите започват да се губят. Оставяме го само за локална работа.
    if (databaseProvider == "Sqlite")
    {
        await dbContext.Database.OpenConnectionAsync();
        try
        {
            await ExecutePragmaAsync(dbContext, app.Environment.IsDevelopment()
                ? "PRAGMA journal_mode=WAL;"
                : "PRAGMA journal_mode=DELETE;");
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    // Споделените хостове вдигат нов процес, докато старият още държи файла
    // на базата. Без кратко изчакване сайтът би паднал при всеки рестарт.
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Startup");

    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await dbContext.Database.MigrateAsync();
            await DbSeeder.SeedAsync(scope.ServiceProvider, builder.Configuration);
            break;
        }
        catch (Exception ex) when (attempt < 5)
        {
            startupLogger.LogWarning(ex, "База данни: опит {Attempt} от 5 не минава, чакам 3 секунди.", attempt);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }

    // Снимка се записва на диска преди реда в базата. Ако процесът падне
    // между двете, файлът остава без собственик и никой не го трие —
    // затова при всяко стартиране се махат старите такива.
    var sweptImages = await ReportImageJanitor.SweepAsync(
        dbContext,
        scope.ServiceProvider.GetRequiredService<ReportImageStorage>(),
        DateTime.UtcNow,
        startupLogger);

    if (sweptImages > 0)
    {
        startupLogger.LogInformation("Изчистени {Count} осиротели снимки.", sweptImages);
    }
}

app.MapGet("/", (HttpContext context) =>
{
    context.Response.Redirect("/Welcome");
    return Task.CompletedTask;
}).AllowAnonymous();

app.MapGet("/report-images/{id:int}", async (
    int id,
    HttpContext context,
    ApplicationDbContext db,
    ReportImageStorage storage) =>
{
    var image = await db.ReportImages.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id);
    if (image is null) return Results.NotFound();
    if (!ReportImageStorage.IsAllowedContentType(image.ContentType)) return Results.NotFound();
    var path = storage.GetFullPath(image.StoredFileName);
    if (path is null) return Results.NotFound();

    // Снимките не се менят след записа, затова браузърът може да ги пази:
    // без това картата тегли всички отново при всяко презареждане.
    context.Response.Headers.CacheControl = "public,max-age=3600";
    return Results.File(path, image.ContentType);
    // Снимките са обществени, защото страниците, които ги показват,
    // също са: без това публичната карта идва със счупени изображения.
    // Заглавката срещу разгръщане по тип идва от глобалния филтър.
}).AllowAnonymous();

app.MapRazorPages().WithStaticAssets();
app.Run();

// Адресът, по който се дели лимитът. Зад прокси това е адресът от
// X-Forwarded-For, защото UseForwardedHeaders вече го е сложил в
// Connection.RemoteIpAddress.
static string ClientKey(HttpContext context) =>
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

// Писането е всичко, което променя състояние. Четенето взима отделна
// кофа: то не пълни базата и не бива да изяжда лимита на формите.
static bool IsWriteRequest(HttpContext context) =>
    HttpMethods.IsPost(context.Request.Method)
    || HttpMethods.IsPut(context.Request.Method)
    || HttpMethods.IsPatch(context.Request.Method)
    || HttpMethods.IsDelete(context.Request.Method);

static Task ExecutePragmaAsync(ApplicationDbContext dbContext, string pragma)
{
    var connection = dbContext.Database.GetDbConnection();
    using var command = connection.CreateCommand();
    command.CommandText = pragma;
    return command.ExecuteNonQueryAsync();
}

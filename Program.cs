using CivicEyeGD.Data;
using CivicEyeGD.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
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
// user-secrets, на сайта – от този файл: той е извън wwwroot, не се
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
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

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

    options.AddPolicy("forms", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

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

var app = builder.Build();

// Зад обратен прокси (Azure App Service, nginx) заявките пристигат като HTTP.
// Без това приложение не разбира, че потребителят е на HTTPS, и бисквитата
// за вход не се изпраща. Позволено е само извън режим за разработка.
if (!app.Environment.IsDevelopment())
{
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    });
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
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";

    await next();
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

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
}

app.MapGet("/", (HttpContext context) =>
{
    context.Response.Redirect("/Welcome");
    return Task.CompletedTask;
}).AllowAnonymous();

app.MapGet("/report-images/{id:int}", async (
    int id,
    HttpContext httpContext,
    ApplicationDbContext db,
    ReportImageStorage storage) =>
{
    var image = await db.ReportImages.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id);
    if (image is null) return Results.NotFound();
    if (!ReportImageStorage.IsAllowedContentType(image.ContentType)) return Results.NotFound();
    var path = storage.GetFullPath(image.StoredFileName);
    if (path is null) return Results.NotFound();
    httpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";
    return Results.File(path, image.ContentType);
    // Снимките са обществени, защото страниците, които ги показват,
    // също са: без това публичната карта идва със счупени изображения.
}).AllowAnonymous();

app.MapRazorPages().WithStaticAssets();
app.Run();

static Task ExecutePragmaAsync(ApplicationDbContext dbContext, string pragma)
{
    var connection = dbContext.Database.GetDbConnection();
    using var command = connection.CreateCommand();
    command.CommandText = pragma;
    return command.ExecuteNonQueryAsync();
}

using CivicEyeGD.Data;
using CivicEyeGD.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// --- 1. СИГУРНО МЯСТО ЗА БАЗАТА ДАННИ ---
var appDataPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(appDataPath);
var databasePath = Path.Combine(appDataPath, "CivicEyeGD.db");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite($"Data Source={databasePath}"));

builder.Services
    .AddIdentity<IdentityUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddRazorPages();

builder.Services.AddSingleton<ReportImageStorage>();
builder.Services.AddSingleton<ReportPriorityService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    var dbContext = services.GetRequiredService<ApplicationDbContext>();
    dbContext.Database.Migrate();

    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    string[] roles = { "Citizen", "Administrator" };

    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    if (app.Environment.IsDevelopment())
    {
        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();

        async Task CreateDemoUserAsync(string email, string password, string role)
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user == null)
            {
                user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
                await userManager.CreateAsync(user, password);
            }
            if (!await userManager.IsInRoleAsync(user, role))
            {
                await userManager.AddToRoleAsync(user, role);
            }
        }

        await CreateDemoUserAsync("citizen.demo@civiceye.test", "CitizenDev!2026", "Citizen");
        await CreateDemoUserAsync("admin.demo@civiceye.test", "AdminDev!2026", "Administrator");
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
}).RequireAuthorization();

app.MapRazorPages().WithStaticAssets();
app.Run();
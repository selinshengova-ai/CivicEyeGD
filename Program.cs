
using CivicEyeGD.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var databasePath = Path.Combine(
    builder.Environment.ContentRootPath,
    "CivicEyeGD.db");

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

var app = builder.Build();

// Обработка на грешки и HTTPS в Production.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Важно: статичните файлове се обслужват преди Authorization.
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Създаване на необходимите роли и демонстрационни акаунти.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    var roleManager =
        services.GetRequiredService<RoleManager<IdentityRole>>();

    string[] roles = { "Citizen", "Administrator" };

    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            var result =
                await roleManager.CreateAsync(new IdentityRole(role));

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Неуспешно създаване на роля {role}: " +
                    string.Join(
                        "; ",
                        result.Errors.Select(e => e.Description)));
            }
        }
    }

    // Демонстрационни акаунти само в Development.
    if (app.Environment.IsDevelopment())
    {
        var userManager =
            services.GetRequiredService<UserManager<IdentityUser>>();

        async Task CreateDemoUserAsync(
            string email,
            string password,
            string role)
        {
            var user = await userManager.FindByEmailAsync(email);

            if (user == null)
            {
                user = new IdentityUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true
                };

                var createResult =
                    await userManager.CreateAsync(user, password);

                if (!createResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Неуспешно създаване на {email}: " +
                        string.Join(
                            "; ",
                            createResult.Errors.Select(
                                e => e.Description)));
                }
            }

            if (!await userManager.IsInRoleAsync(user, role))
            {
                var roleResult =
                    await userManager.AddToRoleAsync(user, role);

                if (!roleResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Неуспешно задаване на роля за {email}: " +
                        string.Join(
                            "; ",
                            roleResult.Errors.Select(
                                e => e.Description)));
                }
            }
        }

        await CreateDemoUserAsync(
            "citizen.demo@civiceye.test",
            "CitizenDev!2026",
            "Citizen");

        await CreateDemoUserAsync(
            "admin.demo@civiceye.test",
            "AdminDev!2026",
            "Administrator");
    }
}

// Началният адрес води към Welcome или Index.
app.MapGet("/", (HttpContext context) =>
{
    context.Response.Redirect("/Welcome");
    return Task.CompletedTask;
}).AllowAnonymous();
app.MapRazorPages()
    .WithStaticAssets();

app.Run();

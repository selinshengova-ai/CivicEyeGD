using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CivicEyeGD.Data;

// Попълва ролите, тестовите акаунти и примерните сигнали.
// Извиква се при всеки старт, а не само в режим за разработка, защото
// регламентът изисква тестови акаунти за всяка рола и на живия сайт, а
// архивът за олимпиадата не носи файл с база данни.
public static class DbSeeder
{
    // Паролите не се пазят в кода и не се качват в хранилището: идват само
    // от конфигурация — dotnet user-secrets локално, променливи на средата
    // или файла App_Data/seed-accounts.json на хоста. Без тях акаунтите
    // просто не се създават, а сайтът тръгва и без тях.
    public static async Task SeedAsync(
        IServiceProvider services,
        IConfiguration configuration)
    {
        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DbSeeder).Name);
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        var context = services.GetRequiredService<ApplicationDbContext>();

        foreach (var role in new[] { "Citizen", "Administrator" })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        var author = await EnsureUserAsync(
            userManager, logger, configuration, "Citizen", "citizen.demo@civiceye.test", "Citizen");

        var office = await EnsureUserAsync(
            userManager, logger, configuration, "Admin", "admin.demo@civiceye.test", "Administrator");

        // Втори гражданин е нужен, защото и двете таблици за гласове имат
        // уникален индекс по сигнал и потребител: един потребител не може
        // да демонстрира повече от едно потвърждение.
        var neighbour = await EnsureUserAsync(
            userManager, logger, configuration, "SecondCitizen", "citizen.two@civiceye.test", "Citizen");

        // Примерните сигнали са за празна база — за демонстрация или за
        // архива. Който попълва истинския си град, изключва ключа
        // Seed:DemoReports и таблицата остава както я е оставил.
        if (!configuration.GetValue("Seed:DemoReports", true))
        {
            return;
        }

        if (author is null || neighbour is null)
        {
            // Примерните сигнали се държат от два акаунта: подателят не участва
            // в оценката на собствения си сигнал, затова е нужен и втори глас.
            logger.LogWarning("Примерните сигнали не се попълват, защото демо акаунтите липсват.");
            return;
        }

        if (!await context.Reports.AnyAsync())
        {
            await SeedReportsAsync(context, author, neighbour, office);
        }
    }

    private static async Task<IdentityUser?> EnsureUserAsync(
        UserManager<IdentityUser> userManager,
        ILogger logger,
        IConfiguration configuration,
        string name,
        string defaultEmail,
        string role)
    {
        var email = configuration[$"SeedAccounts:{name}Email"] ?? defaultEmail;
        var passwordKey = $"SeedAccounts:{name}Password";
        var password = configuration[passwordKey];

        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "Демо акаунтът {Email} не е създаден — липсва {Key}. "
                + "За локална работа го задай с dotnet user-secrets, "
                + "на хоста — с променлива на средата или с файла в App_Data.",
                email, passwordKey);
            return null;
        }

        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            user = new IdentityUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };

            var created = await userManager.CreateAsync(user, password);

            if (!created.Succeeded)
            {
                Warn(logger, "създаването", email, created);
                return null;
            }
        }
        else if (!await userManager.CheckPasswordAsync(user, password))
        {
            // Конфигурацията е променена — акаунтът я получава, за да може
            // подновената парола да замени тази, която е била в файл.
            await userManager.RemovePasswordAsync(user);
            Warn(logger, "подновяването", email, await userManager.AddPasswordAsync(user, password));
        }

        if (!await userManager.IsInRoleAsync(user, role))
        {
            await userManager.AddToRoleAsync(user, role);
        }

        return user;
    }

    private static void Warn(ILogger logger, string action, string email, IdentityResult result)
    {
        if (!result.Succeeded)
        {
            logger.LogWarning(
                "Не мина {Action} на демо акаунт {Email}: {Errors}. "
                + "Паролата трябва да е поне 8 знака, с цифра и малка буква.",
                action, email, string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    // Примерни сигнали по реални улици в Гоце Делчев. Координатите са в
    // границите на града, за да се виждат едновременно на картата и в списъка.
    private static async Task SeedReportsAsync(
        ApplicationDbContext context,
        IdentityUser author,
        IdentityUser neighbour,
        IdentityUser? office)
    {
        var samples = new Sample[]
        {
            new(
                "Липсва рампа на пешеходната пътека",
                ReportCategories.PedestrianPaths,
                "Пътеката пред читалище „Св. Климент Охридски“ няма рампа за колички. "
                + "Хора в инвалидна количка не могат да слязат от тротоара.",
                "бул. „Гоце Делчев“ 14",
                41.57390, 23.72960,
                ReportStatuses.New, 5, 12, NeighbourRating: 5),
            new(
                "Отделена плоча на тротоара",
                ReportCategories.Sidewalks,
                "Тротоарът е издълбан от дъждовете и една плоча се е мръднала. "
                + "През нощта минувачи си нараняват глезените.",
                "ул. „Васил Левски“ 3",
                41.57250, 23.73120,
                ReportStatuses.InProgress, 4, 9,
                "Извършен е оглед, плочата е в плана за есенна поддръжка."),
            new(
                "Ниско спуснат кабел пред вход",
                ReportCategories.Accessibility,
                "Кабел виси на височина над главата и е опасен за деца и за хора "
                + "с колички.",
                "ул. „Христо Ботев“ 8",
                41.57180, 23.72740,
                ReportStatuses.New, 5, 7, NeighbourRating: 5),
            new(
                "Течаща чешма до парк Кьосе",
                ReportCategories.Other,
                "От повредена чешма тече вода и през студено време по тротоара "
                + "се образува тънка ледена кора.",
                "парк „Кьосе“",
                41.57470, 23.73350,
                ReportStatuses.New, 3, 5),
            new(
                "Счупени лампи пред училище „Никола Вапцаров“",
                ReportCategories.StreetLighting,
                "Две лампи не светят и алеята към входа е напълно тъмна след 18 часа.",
                "ул. „Георги Сугарев“ 2",
                41.57090, 23.73010,
                ReportStatuses.Resolved, 4, 3,
                "Подменени са двете светещи тела и предпазителите."),
            new(
                "Надигнат асфалт при шахта",
                ReportCategories.RoadSurface,
                "Настилката е хлътнала около шахтата и автобусите я заобикалят "
                + "чрез насрещното платно.",
                "ул. „Цар Симеон“ 21",
                41.57560, 23.72680,
                ReportStatuses.InProgress, 4, 8,
                "Кръпката е в програмата за поддръжка на настилките."),
            new(
                "Зает тротоар от паркирани автомобили",
                ReportCategories.Sidewalks,
                "Тротоарът пред магазина е зает и пешеходците слизат на пътя.",
                "ул. „Панайот Волов“ 5",
                41.57310, 23.72520,
                ReportStatuses.New, 3, 6),
            new(
                "Липсват водачи за хора с увредено зрение",
                ReportCategories.Accessibility,
                "Пътеката към пощата няма вградени водачи и преходът е небезопасен "
                + "за хора с увредено зрение.",
                "ул. „Даме Груев“ 4",
                41.57440, 23.72870,
                ReportStatuses.Rejected, 5, 11,
                "Релсите са в проекта на друг инвеститор — сигналът е препратен.")
        };

        foreach (var sample in samples)
        {
            var report = new Report
            {
                Title = sample.Title,
                Category = sample.Category,
                Description = sample.Description,
                Address = sample.Address,
                Latitude = sample.Latitude,
                Longitude = sample.Longitude,
                Status = sample.Status,
                CreatedAt = DateTime.UtcNow.AddDays(-sample.DaysAgo),
                UserId = author.Id
            };

            context.Reports.Add(report);
            await context.SaveChangesAsync();

            context.SeverityRatings.Add(new SeverityRating
            {
                ReportId = report.Id,
                UserId = author.Id,
                Rating = sample.Severity,
                CreatedAt = report.CreatedAt.AddHours(1)
            });

            context.SeverityRatings.Add(new SeverityRating
            {
                ReportId = report.Id,
                UserId = neighbour.Id,
                // По принцип съседът е с една точка по-резервиран от
                // подателя. При двете най-тежки сигнала обаче и двамата
                // дават пълна сериозност: така приоритетът прескача прага
                // за спешност и демонстрацията най-после показва пулсиращия
                // маркер, който от обикновените демо данни не се ражда.
                Rating = sample.NeighbourRating ?? Math.Max(1, sample.Severity - 1),
                CreatedAt = report.CreatedAt.AddHours(3)
            });

            context.Confirmations.Add(new Confirmation
            {
                ReportId = report.Id,
                UserId = neighbour.Id,
                CreatedAt = report.CreatedAt.AddHours(2)
            });

            if (sample.Status != ReportStatuses.New)
            {
                context.StatusHistories.Add(new StatusHistory
                {
                    ReportId = report.Id,
                    OldStatus = ReportStatuses.New,
                    NewStatus = sample.Status,
                    // Преходите ги пише служителят с администраторска роля,
                    // защото и в таблицата, и на екрана ги прави само той.
                    ChangedByUserId = office?.Id,
                    ChangedAt = report.CreatedAt.AddDays(1),
                    Comment = sample.HistoryNote
                });
            }

            await context.SaveChangesAsync();
        }
    }

    private sealed record Sample(
        string Title,
        string Category,
        string Description,
        string Address,
        double Latitude,
        double Longitude,
        string Status,
        int Severity,
        int DaysAgo,
        string? HistoryNote = null,
        int? NeighbourRating = null);
}

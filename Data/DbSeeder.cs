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
    // границите на града, за да се виждат едновременно на картата и в
    // списъка, а адресите се повтарят по няколко сигнала, защото
    // „най-натоварените места“ иначе няма що да брои.
    //
    // Подредбата е от най-стария към най-новия. При равен приоритет
    // списъците решават по номера на сигнала с обещанието, че по-малкият
    // е подаден по-отдавна; объркан ред тук би го нарушил още в демо
    // данните. Дните също са относителни спрямо старта: колкото и късно
    // да се пусне приложението, най-новият сигнал си остава от вчера.
    private static async Task SeedReportsAsync(
        ApplicationDbContext context,
        IdentityUser author,
        IdentityUser neighbour,
        IdentityUser? office)
    {
        var samples = new Sample[]
        {
            new(
                "Повдигната бордюрна лента на автобусната спирка",
                ReportCategories.RoadSurface,
                "Бордюрът при спирката е повдигнат от мразта и автобусите "
                + "не могат да доближат до тротоара. Пътниците слизат на платното.",
                "бул. „Гоце Делчев“ 40",
                41.57500, 23.72720,
                ReportStatuses.Resolved, 2, 105,
                Steps:
                [
                    new(ReportStatuses.InProgress, 40,
                        "Спирката е огледана, подмяната на бордюра е в плана за "
                        + "асфалтиране до края на годината."),
                    new(ReportStatuses.Resolved, 80,
                        "Бордюрът е подновен и автобусите спират до тротоарната лента.")
                ]),
            new(
                "Няма рампа към входа на поликлиниката",
                ReportCategories.Accessibility,
                "Пред входа има високо стъпало, а рампа липсва, затова количките "
                + "не влизат сами. Болен човек чака на открито, докато някой го "
                + "придружи.",
                "ул. „Възраждане“ 2",
                41.57330, 23.72680,
                ReportStatuses.Rejected, 4, 90,
                Steps:
                [
                    new(ReportStatuses.InProgress, 14,
                        "Извършен е оглед на входа и на стъпалото пред него."),
                    new(ReportStatuses.Rejected, 52,
                        "Стъпалото е в частния имот на сградата, затова общината "
                        + "не може да го преустрои; сигналът е препратен на "
                        + "собственика.")
                ]),
            new(
                "Липсват водачи за хора с увредено зрение",
                ReportCategories.Accessibility,
                "Пътеката към пощата няма вградени водачи и преходът е небезопасен "
                + "за хора с увредено зрение.",
                "ул. „Даме Груев“ 4",
                41.57330, 23.72830,
                ReportStatuses.Rejected, 5, 74,
                NeighbourRating: 4,
                Steps:
                [
                    new(ReportStatuses.InProgress, 20,
                        "Участъкът е проверен на място, водачи са предвидени в "
                        + "одобрения проект за реновиране."),
                    new(ReportStatuses.Rejected, 41,
                        "Водачите са в проекта на друг инвеститор за същия участък "
                        + "— сигналът е препратен към него.")
                ]),
            new(
                "Счупени лампи пред училище „Никола Вапцаров“",
                ReportCategories.StreetLighting,
                "Две лампи не светят и алеята към входа е напълно тъмна след 18 часа.",
                "ул. „Георги Сугарев“ 2",
                41.57090, 23.73010,
                ReportStatuses.Resolved, 4, 58,
                Steps:
                [
                    new(ReportStatuses.InProgress, 6,
                        "Проверено е напрежението в таблото; двете светещи тела "
                        + "чакат подмяна от есенната поддръжка."),
                    new(ReportStatuses.Resolved, 33,
                        "Подменени са двете светещи тела и предпазителите, алеята "
                        + "осветява отново.")
                ]),
            new(
                "Избледняла маркировка на пешеходната пътека",
                ReportCategories.PedestrianPaths,
                "Бялата боя по пътеката е изтъркана и шофьорите не забелязват "
                + "прехода, особено когато вали. Маркировката е за цяло подновяване.",
                "ул. „Христо Ботев“ 24",
                41.57150, 23.72610,
                ReportStatuses.InProgress, 2, 48,
                Steps:
                [
                    new(ReportStatuses.InProgress, 20,
                        "Измерена е дължината на участъка, боята е поръчана и "
                        + "пътеката е в графика за обновяване на маркировките.")
                ]),
            new(
                "Отделена плоча на тротоара",
                ReportCategories.Sidewalks,
                "Тротоарът е издълбан от дъждовете и една плоча се е мръднала. "
                + "През нощта минувачи си нараняват глезените.",
                "ул. „Васил Левски“ 3",
                41.57250, 23.73120,
                ReportStatuses.InProgress, 4, 26,
                Steps:
                [
                    new(ReportStatuses.InProgress, 12,
                        "Извършен е оглед, плочата е в плана за есенна поддръжка.")
                ]),
            new(
                "Надигнат асфалт при шахта",
                ReportCategories.RoadSurface,
                "Настилката е хлътнала около шахтата и автобусите я заобикалят "
                + "чрез насрещното платно.",
                "ул. „Цар Симеон“ 21",
                41.57560, 23.72680,
                ReportStatuses.InProgress, 4, 19,
                Steps:
                [
                    new(ReportStatuses.InProgress, 5,
                        "Кръпката е в програмата за поддръжка на настилките.")
                ]),
            new(
                "Тъмна алея в парк „Кьосе“",
                ReportCategories.StreetLighting,
                "Двата стълба в края на алеята зад читалище „Св. Климент "
                + "Охридски“ не светят и мястото остава тъмно след залез. "
                + "Хората обикалят през тревата, за да не минават през него.",
                "парк „Кьосе“",
                41.57510, 23.73260,
                ReportStatuses.Resolved, 5, 9,
                NeighbourRating: 5,
                Steps:
                [
                    new(ReportStatuses.InProgress, 2,
                        "Установено е, че двата стълба са без напрежение заради "
                        + "повредена фотоклетка."),
                    new(ReportStatuses.Resolved, 6,
                        "Фотоклетката е подменена; лампите светят отново от девет "
                        + "часа вечерта.")
                ]),
            new(
                "Дупка по булеварда след дъждовете",
                ReportCategories.RoadSurface,
                "След дъждовете в настилката остана дълбока дупка, която се пълни "
                + "с вода. Моторедителите я заобикалят през насрещното платно.",
                "бул. „Гоце Делчев“ 22",
                41.57450, 23.72790,
                ReportStatuses.InProgress, 5, 11,
                NeighbourRating: 4,
                Steps:
                [
                    new(ReportStatuses.InProgress, 4,
                        "Дупката е измерена; кърпенето с гореща асфалтова смес "
                        + "чака топло време. Приоритетът е вдигнат с шест точки, "
                        + "защото до нея минават децата към училище.", 6)
                ]),
            new(
                "Течаща чешма до парк Кьосе",
                ReportCategories.Other,
                "От повредена чешма тече вода и през студено време по тротоара "
                + "се образува тънка ледена кора.",
                "парк „Кьосе“",
                41.57470, 23.73350,
                ReportStatuses.New, 3, 9),
            new(
                "Зает тротоар от паркирани автомобили",
                ReportCategories.Sidewalks,
                "Тротоарът пред магазина е зает и пешеходците слизат на пътя.",
                "ул. „Панайот Волов“ 5",
                41.57310, 23.72520,
                ReportStatuses.New, 3, 7),
            new(
                "Изхвърлен строителен отпадък до игрището",
                ReportCategories.Other,
                "До игрището са изхвърлени бетонни парчета и плоскости от ремонт. "
                + "Децата играят между тях, а мястото не е определено за "
                + "строителен отпадък.",
                "кв. „Отдоян“",
                41.56680, 23.73720,
                ReportStatuses.New, 4, 6),
            new(
                "Пътеката към църквата е затрупана с чакъл",
                ReportCategories.PedestrianPaths,
                "Пътеката към църквата „Св. Неделя“ е засипана с чакъл от "
                + "обновения двор и минувачите се подхлъзват по него.",
                "ул. „Пирин“ 12",
                41.57200, 23.72350,
                ReportStatuses.New, 3, 5),
            new(
                "Липсва рампа на пешеходната пътека",
                ReportCategories.PedestrianPaths,
                "Пътеката пред читалище „Св. Климент Охридски“ няма рампа за колички. "
                + "Хора в инвалидна количка не могат да слязат от тротоара.",
                "бул. „Гоце Делчев“ 14",
                41.57390, 23.72960,
                ReportStatuses.New, 5, 4, NeighbourRating: 5),
            new(
                "Корени на дърво повдигат тротоарните плочи",
                ReportCategories.Sidewalks,
                "Корените на старото дърво са повдигнали четири плочи една след "
                + "друга. Тротоарът е неравен и през нощта е опасен.",
                "ул. „Васил Левски“ 20",
                41.57280, 23.73050,
                ReportStatuses.New, 4, 3),
            new(
                "Ниско спуснат кабел пред вход",
                ReportCategories.Accessibility,
                "Кабел виси на височина над главата и е опасен за деца и за хора "
                + "с колички.",
                "ул. „Христо Ботев“ 8",
                41.57180, 23.72740,
                ReportStatuses.New, 5, 2, NeighbourRating: 5),
            new(
                "Трепкаща лампа до детската площадка",
                ReportCategories.StreetLighting,
                "Лампата до люлките трепти и загасва, а след девет часа "
                + "площадката остава празна и тъмна.",
                "ул. „Пирин“ 8",
                41.57230, 23.72430,
                ReportStatuses.New, 3, 1)
        };

        foreach (var sample in samples)
        {
            Stage[] steps = sample.Steps ?? [];

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
                // Корекцията живее в сигнала, а дневникът я описва в
                // стъпката. Вземам числото от последната стъпка, за да
                // съвпадат записът и следата, която той оставя.
                AdminPriorityAdjustment = steps.Length > 0 ? steps[^1].Adjustment : 0,
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

            // Дневникът се пише стъпка по стъпка, по същия начин, по
            // който го прави ReportOfficeService: всеки ред знае от какъв
            // статус идва, кой го е записал и кога. Междинните „В процес“
            // са там, където общината наистина би минала през такъв статус
            // преди решението, а датите са на три седмици разминаване —
            // така средното време за реакция има какво да усреднява.
            var previousStatus = ReportStatuses.New;
            var previousAdjustment = 0;

            foreach (var stage in steps)
            {
                // Двете числа за корекцията влизат само където числото
                // наистина е местено, както в сервисата: ред „6 → 6“ би
                // подсказвал намеса, каквато не е правена.
                var adjusted = stage.Adjustment != previousAdjustment;

                context.StatusHistories.Add(new StatusHistory
                {
                    ReportId = report.Id,
                    OldStatus = previousStatus,
                    NewStatus = stage.Status,
                    // Преходите ги пише служителят с администраторска роля,
                    // защото и в таблицата, и на екрана ги прави само той.
                    ChangedByUserId = office?.Id,
                    ChangedAt = report.CreatedAt.AddDays(stage.AfterDays),
                    Comment = stage.Note,
                    OldPriorityAdjustment = adjusted ? previousAdjustment : null,
                    NewPriorityAdjustment = adjusted ? stage.Adjustment : null
                });

                previousStatus = stage.Status;
                previousAdjustment = stage.Adjustment;
            }

            await context.SaveChangesAsync();
        }
    }

    // Една видима намеса на общината: статусът, който остава след нея,
    // колко дни след подаването е направена и каква бележка е оставена.
    // Полетата съвпадат с тези на ReportOfficeService, затова дневникът
    // от примерните данни се чете като истински, а не като нагласен.
    private sealed record Stage(
        string Status,
        int AfterDays,
        string? Note = null,
        int Adjustment = 0);

    private sealed record Sample(
        string Title,
        string Category,
        string Description,
        string Address,
        double Latitude,
        double Longitude,
        string Status,
        int Severity,
        // Дни назад от старта. Относителни са, за да не остаряват
        // примерните данни: колкото и късно да се пусне приложението,
        // сигналите от последните дни си остават пресни.
        int DaysAgo,
        int? NeighbourRating = null,
        // Празно за сигналите, които все още чакат: те нямат нито
        // стъпка от общината, нито дневник.
        Stage[]? Steps = null);
}

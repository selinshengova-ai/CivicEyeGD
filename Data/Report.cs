using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CivicEyeGD.Data;

// Централният елемент на приложението — сигнал от гражданина до общината.
// Един ред тук е цялото досие: описание, място, статус, снимки, гласове
// и история. Приоритетът НЕ е поле в базата, а се изчислява от тях
// (ReportPriorityService), за да не може числото да изостане след
// нова оценка или потвърждение.
public class Report
{
    public int Id { get; set; }

    // Атрибутите [Required] и [StringLength] се проверяват при връзването
    // на модела, т.е. и при директна POST заявка, а не само във формата —
    // заглавието е краткото, с което сигналът се разпознава в списъка.
    [Required]
    [StringLength(100)]
    public string Title { get; set; } = string.Empty;

    // 2000 знака стигат за подробно описание, но спират стените от
    // текст, които правят административния списък нечетим.
    [Required]
    [StringLength(2000)]
    public string Description { get; set; } = string.Empty;

    // Категорията е текст, а не външен ключ към таблица: списъкът е
    // малък и фиксиран в ReportCategories, проверява се от сървъра
    // преди запис, а за общински сайт не трябва референтна таблица
    // с собствени миграции.
    [Required]
    [StringLength(100)]
    public string Category { get; set; } = string.Empty;

    // Човешки четим адрес — координатите могат да липсват, а без адрес
    // служителят не знае къде да изпрати екипа си.
    [Required]
    [StringLength(200)]
    public string Address { get; set; } = string.Empty;

    // Новият сигнал тръгва от началния статус на ReportStatuses;
    // променя се само от административната част, която валидира
    // всяка стойност през ReportStatuses.IsValid.
    public string Status { get; set; } = "Подаден";

    // UTC, защото местният час скача при смяна на лятното часово време,
    // а сървър в друга зона би записал объркващи местни стойности;
    // на екрана времето се превръща към софийското чрез CivicTime.
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Географски координати
    // И двете са с разрешена липса: сигналът е валиден и без точка на
    // картата, а гражданин без GPS не бива да се спира да подаде. Двойката
    // се проверява заедно — или и двете липсват, или двете са в
    // границите на ReportGeography; едната сама по себе си е грешка.
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // Административна корекция на приоритета (0-10)
    // Това е A във формулата P = (20·S + 3·min(C,10) + A)/140 · 100.
    // Служителят може да добави до 10 точки, когато гражданските гласове
    // подценяват реалния риск; горницата е малка и се стяга принудително
    // в ReportPriorityService, за едно решение да не обръща йерархията.
    public int AdminPriorityAdjustment { get; set; } = 0;

    // Връзка към подателя
    // Разрешена липса: при изтрит акаунт DbContext-а слага null
    // (DeleteBehavior.SetNull), а не изтрива сигнала — той остава
    // видим за общината като анонимен.
    public string? UserId { get; set; }
    public IdentityUser? User { get; set; }

    // Снимки
    // Колекцията се инициализира с празен списък, за да не връща null
    // при нов сигнал — кодът я обхожда директно, без проверка за липса.
    public ICollection<ReportImage> Images { get; set; } = new List<ReportImage>();

    // Обратни навигации за оценките, потвържденията и историята на статуса.
    public ICollection<SeverityRating> SeverityRatings { get; set; } = new List<SeverityRating>();
    public ICollection<Confirmation> Confirmations { get; set; } = new List<Confirmation>();
    public ICollection<StatusHistory> StatusHistories { get; set; } = new List<StatusHistory>();

    // Оценките и потвържденията на подателя не участват в приоритета.
    // Без това правило един човек вдига собствения си сигнал до
    // „висок приоритет“ с едно щракване, а точността на йерархията е
    // целият смисъл на системата за общината.
    //
    // Самата проверка е в IndependentVotes — там, където е и SQL
    // вариантът ѝ за картата, за да не казват двете места различни неща.
    //
    // [NotMapped] е задължителен: EF Core иначе разбира
    // IndependentRatings като втора навигация към SeverityRatings и
    // създава сянка външен ключ ReportId1, което развали миграцията.
    [NotMapped]
    public IEnumerable<SeverityRating> IndependentRatings => IndependentVotes.RatingsOf(this);

    // Броят на независимите оценяващи — показва на гражданина колко
    // малка е извадката, докато средното все още няма тежест.
    [NotMapped]
    public int IndependentRaterCount => IndependentVotes.RaterCountOf(this);

    // Това е S във формулата за приоритет; закръгля се до два знака,
    // за да съвпада числото на екрана с това в изчисленията. При нула
    // оцени дава 0, а ReportPriorityService го тълкува като най-ниска
    // сериозност — сигналът не става спешен само от липса на гласове.
    [NotMapped]
    public double IndependentAverageSeverity => IndependentVotes.AverageSeverityOf(this);

    // Това е C във формулата; броят се само гласовете на други, защото
    // и потвърждението на подателя е от интерес. ReportPriorityService
    // го ограничава на 10 — след това потвържденията не добавят тежест,
    // за да не „удавят“ сериозността на масови, но леки проблеми.
    [NotMapped]
    public int IndependentConfirmationCount => IndependentVotes.ConfirmationsOf(this);

    // Проверката приема null и го тълкува като „не съвпада“, защото
    // страниците я викат и за непознат посетител — така извикателят
    // не дължи собствена проверка преди извикването.
    public bool WasSubmittedBy(string? userId) =>
        userId is not null && UserId == userId;
}
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CivicEyeGD.Data;

// Единната база от данни на приложението. Наследява IdentityDbContext,
// за да живеят акаунтите (AspNetUsers и дружките им) в един и същи
// SQLite файл със сигналите — една връзка, една миграция, едно копие.
public class ApplicationDbContext : IdentityDbContext<IdentityUser>
{
    // Опциите идват през внедряването, защото Program.cs решава през
    // конфигурацията кой доставчик да включи (SQLite, Postgres, SqlServer);
    // контекстът не трябва да знае нищо за този избор.
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // Всяка колекция дава достъп до един тип; имената са множествено
    // число, за да се чете заявката като изречение: Reports.Where(...).
    public DbSet<Report> Reports { get; set; }
    public DbSet<ReportImage> ReportImages { get; set; }
    public DbSet<SeverityRating> SeverityRatings { get; set; }
    public DbSet<Confirmation> Confirmations { get; set; }
    public DbSet<StatusHistory> StatusHistories { get; set; }

    // Тук се донастройват правила, които конвенциите не познават: каква
    // да е реакцията при изтриване и кои редове са уникални. Става с
    // код, не с атрибути, защото моделите са прости, а правилата са
    // на едно място и се четат заедно.
    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity таблиците (потребители, роли) се дефинират от базовия
        // клас; без този вик базата би нямала акаунти.
        base.OnModelCreating(builder);

        // Връзки за Report
        // SetNull: при изтрит акаунт сигналът остава — той е обществено
        // достояние и статистиката на общината не трябва да изчезва
        // заедно с автора; само връзката към потребителя се прекъсва.
        builder.Entity<Report>()
            .HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Cascade: снимката съществува само като част от сигнал; с неговото
        // изтриване редовете ѝ нямат смисъл и биха се трупали като сираци.
        builder.Entity<ReportImage>()
            .HasOne(i => i.Report)
            .WithMany(r => r.Images)
            .HasForeignKey(i => i.ReportId)
            .OnDelete(DeleteBehavior.Cascade);

        // Връзки за SeverityRating
        // Уникален индекс (сигнал, потребител): един гражданин дава една
        // оценка на един сигнал. Защита е в базата, а не само в кода,
        // защото две бързи щраквания или две устройства иначе минават през
        // проверката едновременно и вдигат изкуствено средната сериозност.
        builder.Entity<SeverityRating>()
            .HasIndex(sr => new { sr.ReportId, sr.UserId })
            .IsUnique();

        // Оценка без сигнал е безсмислена — изтрива се заедно с него,
        // за да не остава глас, който вече на никого не служи.
        builder.Entity<SeverityRating>()
            .HasOne(sr => sr.Report)
            .WithMany(r => r.SeverityRatings)
            .HasForeignKey(sr => sr.ReportId)
            .OnDelete(DeleteBehavior.Cascade);

        // Връзки за Confirmation
        // Същата защита като при оценките: един потребител потвърждава
        // веднъж, а уникалният индекс го налага и при състезателни заявки.
        builder.Entity<Confirmation>()
            .HasIndex(c => new { c.ReportId, c.UserId })
            .IsUnique();

        builder.Entity<Confirmation>()
            .HasOne(c => c.Report)
            .WithMany(r => r.Confirmations)
            .HasForeignKey(c => c.ReportId)
            .OnDelete(DeleteBehavior.Cascade);

        // Връзки за StatusHistory
        // Cascade само към сигнала: историята е част от дневника на
        // конкретния проблем и извън него няма значение; затова пък
        // връзката със служителя е с разрешена липса и не се каскадира
        // — редовете остават и след като акаунтът се изтрие.
        builder.Entity<StatusHistory>()
            .HasOne(sh => sh.Report)
            .WithMany(r => r.StatusHistories)
            .HasForeignKey(sh => sh.ReportId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
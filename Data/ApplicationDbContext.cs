using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CivicEyeGD.Data;

public class ApplicationDbContext : IdentityDbContext<IdentityUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Report> Reports { get; set; }
    public DbSet<ReportImage> ReportImages { get; set; }
    public DbSet<SeverityRating> SeverityRatings { get; set; }
    public DbSet<Confirmation> Confirmations { get; set; }
    public DbSet<StatusHistory> StatusHistories { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Връзки за Report
        builder.Entity<Report>()
            .HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<ReportImage>()
            .HasOne(i => i.Report)
            .WithMany(r => r.Images)
            .HasForeignKey(i => i.ReportId)
            .OnDelete(DeleteBehavior.Cascade);

        // Връзки за SeverityRating
        builder.Entity<SeverityRating>()
            .HasIndex(sr => new { sr.ReportId, sr.UserId })
            .IsUnique();

        builder.Entity<SeverityRating>()
            .HasOne(sr => sr.Report)
            .WithMany(r => r.SeverityRatings)
            .HasForeignKey(sr => sr.ReportId)
            .OnDelete(DeleteBehavior.Cascade);

        // Връзки за Confirmation
        builder.Entity<Confirmation>()
            .HasIndex(c => new { c.ReportId, c.UserId })
            .IsUnique();

        builder.Entity<Confirmation>()
            .HasOne(c => c.Report)
            .WithMany(r => r.Confirmations)
            .HasForeignKey(c => c.ReportId)
            .OnDelete(DeleteBehavior.Cascade);

        // Връзки за StatusHistory
        builder.Entity<StatusHistory>()
            .HasOne(sh => sh.Report)
            .WithMany(r => r.StatusHistories)
            .HasForeignKey(sh => sh.ReportId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
using Microsoft.EntityFrameworkCore;
using MpuTrainer.Models;

namespace MpuTrainer.Data;

/// <summary>
/// EF-Core-Kontext fuer die lokale SQLite-Datenbank. Speichert Projekte mit
/// eingebetteten Klientendaten und zugehoerigen Fragen.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ClientProject> Projects => Set<ClientProject>();
    public DbSet<TrainingQuestion> Questions => Set<TrainingQuestion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var project = modelBuilder.Entity<ClientProject>();

        // Klientendaten als Owned Entity (eigene Spalten in der Projekttabelle).
        project.OwnsOne(p => p.Client, c =>
        {
            c.Property(x => x.FirstName).HasColumnName("ClientFirstName");
            c.Property(x => x.LastName).HasColumnName("ClientLastName");
            c.Property(x => x.BirthDate).HasColumnName("ClientBirthDate");
        });

        // 1:n Projekt -> Fragen, Loeschen kaskadiert.
        project.HasMany(p => p.Questions)
               .WithOne()
               .HasForeignKey(q => q.ProjectId)
               .OnDelete(DeleteBehavior.Cascade);

        // Aufzaehlungen als Text speichern (lesbarer in der DB).
        modelBuilder.Entity<TrainingQuestion>()
                    .Property(q => q.Category).HasConversion<string>();
        modelBuilder.Entity<TrainingQuestion>()
                    .Property(q => q.Difficulty).HasConversion<string>();
        modelBuilder.Entity<TrainingQuestion>()
                    .Property(q => q.Status).HasConversion<string>();
        modelBuilder.Entity<ClientProject>()
                    .Property(p => p.FocusCategory).HasConversion<string>();

        base.OnModelCreating(modelBuilder);
    }
}

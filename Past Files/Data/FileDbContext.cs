// Data/FileTrackerContext.cs
using Microsoft.EntityFrameworkCore;
using Past_Files.Models;

namespace Past_Files.Data;

public class FileDbContext(string dbName) : DbContext
{
    public DbSet<FileRecord> FileRecords { get; set; } = null!;
    public DbSet<FileLocationsHistory> FileLocationsHistory { get; set; } = null!;
    public DbSet<FileNamesHistory> FileNamesHistory { get; set; } = null!;

    public DbSet<Metadata> Metadata { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options
            .UseSqlite($"Data Source={dbName}; Pooling=False");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // FileRecord configuration
        modelBuilder.Entity<FileRecord>(entity =>
        {
            entity.HasKey(e => e.FileRecordId);
            entity.Property(e => e.FileRecordId)
                  .ValueGeneratedOnAdd();

            entity.HasIndex(e => e.Hash);

            entity.HasMany(e => e.Locations)
                  .WithOne(l => l.FileRecord)
                  .HasForeignKey(l => l.FileRecordId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.NameHistory)
                  .WithOne(n => n.FileRecord)
                  .HasForeignKey(n => n.FileRecordId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // FileLocationsHistory configuration
        modelBuilder.Entity<FileLocationsHistory>(entity =>
        {
            entity.HasKey(e => e.FileLocationId);
            entity.Property(e => e.FileLocationId)
                  .ValueGeneratedOnAdd();

            // Configure Path as a value object stored as a string
            entity.Property(e => e.Path)
                  .HasConversion(
                      path => path.NormalizedPath, // Path object -> string (for DB)
                      value => new FilePath(value)     // string (from DB) -> Path object
                  )
                  .IsRequired(); // Ensure the path is not null
        });

        // FileNamesHistory configuration
        modelBuilder.Entity<FileNamesHistory>(entity =>
        {
            entity.HasKey(e => e.FileNameHistoryId);
            entity.Property(e => e.FileNameHistoryId)
                  .ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<Metadata>().HasData(new Metadata
        {
            Id = 1,
            LastScanStartTime = DateTime.UtcNow, // Default value
            LastScanCompleted = false
        });
    }
}

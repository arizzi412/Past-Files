// Data/FileTrackerContext.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using Past_Files.Models;

namespace Past_Files.Data;

public class FileTrackerContext(string dbName) : DbContext
{
    public DbSet<FileRecord> FileRecords { get; set; } = null!;
    public DbSet<FileLocationsHistory> FileLocationsHistory { get; set; } = null!;
    public DbSet<FileNamesHistory> FileNamesHistory { get; set; } = null!;

    public DbSet<Metadata> Metadata { get; set; }


    //public static readonly ILoggerFactory MyLoggerFactory
    //    = LoggerFactory.Create(builder =>
    //    {
    //        builder.AddSerilog(Log.Logger); // Pass the global Serilog logger
    //        builder.AddConsole(); // Requires Microsoft.Extensions.Logging.Console
    //    });

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options
            .UseSqlite($"Data Source={dbName}");

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
                      value => new Models.FilePath(value)     // string (from DB) -> Path object
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
            LastScanStartTime = DateTime.UtcNow, // Default value
            LastScanCompleted = false
        });
    }
}

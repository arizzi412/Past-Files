// Data/FileTrackerContext.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using Past_Files.Models;

namespace Past_Files.Data;

public class FileTrackerContext : DbContext
{
    public DbSet<FileRecord> FileRecords { get; set; } = null!;
    public DbSet<FileLocationHistory> FileLocations { get; set; } = null!;
    public DbSet<FileIdentity> FileIdentities { get; set; } = null!;
    public DbSet<FileNameHistory> FileNameHistories { get; set; } = null!;

    public static readonly ILoggerFactory MyLoggerFactory
        = LoggerFactory.Create(builder =>
        {
            builder.AddSerilog(Log.Logger); // Pass the global Serilog logger
            builder.AddConsole(); // Requires Microsoft.Extensions.Logging.Console
        });

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options
            .UseSqlite("Data Source=filetracker.db");
         //   .UseLoggerFactory(MyLoggerFactory) // Attach the logger
         //   .EnableSensitiveDataLogging(); // Enable sensitive data (for debugging only)

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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

            entity.HasMany(e => e.Identities)
                  .WithOne(i => i.FileRecord)
                  .HasForeignKey(i => i.FileRecordId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.NameHistories)
                  .WithOne(n => n.FileRecord)
                  .HasForeignKey(n => n.FileRecordId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FileIdentity>(entity =>
        {
            entity.HasKey(e => e.FileIdentityId);
            entity.Property(e => e.FileIdentityId)
                  .ValueGeneratedOnAdd();

            entity.HasIndex(e => new { e.VolumeSerialNumber, e.NTFSFileID })
                  .IsUnique();
        });

        modelBuilder.Entity<FileLocationHistory>(entity =>
        {
            entity.HasKey(e => e.FileLocationId);
            entity.Property(e => e.FileLocationId)
                  .ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<FileNameHistory>(entity =>
        {
            entity.HasKey(e => e.FileNameHistoryId);
            entity.Property(e => e.FileNameHistoryId)
                  .ValueGeneratedOnAdd();
        });
    }
}

// Data/FileDbContext.cs
using Microsoft.EntityFrameworkCore;
using Past_Files.Models; // Ensure this using is correct for your new models

namespace Past_Files.Data;

public class FileDbContext(string dbName) : DbContext
{
    // Remove old DbSet
    // public DbSet<FileRecord> FileRecords { get; set; } = null!;

    // Add new DbSets
    public DbSet<FileContent> FileContents { get; set; } = null!;
    public DbSet<FileInstance> FileInstances { get; set; } = null!;

    // Rename history DbSets
    public DbSet<FileLocationHistoryEntry> FileLocationHistoryEntries { get; set; } = null!;
    public DbSet<FileNameHistoryEntry> FileNameHistoryEntries { get; set; } = null!;

    public DbSet<Metadata> Metadata { get; set; } = null!;


    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options
            .UseSqlite($"Data Source={dbName}");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // FileContent configuration
        modelBuilder.Entity<FileContent>(entity =>
        {
            entity.HasKey(e => e.FileContentId);
            entity.Property(e => e.FileContentId).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.Hash).IsUnique(); // Hash must be unique for content
            // Relationship: One FileContent can have many FileInstances
            entity.HasMany(fc => fc.FileInstances)
                  .WithOne(fi => fi.FileContent)
                  .HasForeignKey(fi => fi.FileContentId)
                  .OnDelete(DeleteBehavior.Cascade); // Or Restrict, depending on desired behavior
        });

        // FileInstance configuration
        modelBuilder.Entity<FileInstance>(entity =>
        {
            entity.HasKey(e => e.FileInstanceId);
            entity.Property(e => e.FileInstanceId).ValueGeneratedOnAdd();

            // Unique constraint for a file instance on a specific volume
            entity.HasIndex(e => new { e.VolumeSerialNumber, e.NTFSFileID }).IsUnique();

            entity.Property(e => e.FilePath).IsRequired();
            entity.Property(e => e.CurrentFileName).IsRequired();

            // Relationship: One FileInstance has many FileLocationHistoryEntries
            entity.HasMany(fi => fi.LocationHistory)
                  .WithOne(lh => lh.FileInstance)
                  .HasForeignKey(lh => lh.FileInstanceId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Relationship: One FileInstance has many FileNameHistoryEntries
            entity.HasMany(fi => fi.NameHistory)
                  .WithOne(nh => nh.FileInstance)
                  .HasForeignKey(nh => nh.FileInstanceId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // FileLocationHistoryEntry configuration (was FileLocationsHistory)
        modelBuilder.Entity<FileLocationHistoryEntry>(entity =>
        {
            entity.HasKey(e => e.FileLocationHistoryEntryId);
            entity.Property(e => e.FileLocationHistoryEntryId).ValueGeneratedOnAdd();
            entity.Property(e => e.DirectoryPath).IsRequired();
            // Removed old Path value object conversion, DirectoryPath is now a string.
            // If you still use the FilePath class for normalization, apply it before setting the string.
        });

        // FileNameHistoryEntry configuration (was FileNamesHistory)
        modelBuilder.Entity<FileNameHistoryEntry>(entity =>
        {
            entity.HasKey(e => e.FileNameHistoryEntryId);
            entity.Property(e => e.FileNameHistoryEntryId).ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<Metadata>().HasData(new Metadata
        {
            Id = 1,
            LastScanStartTime = DateTime.UtcNow,
            LastScanCompleted = false
        });
    }
}
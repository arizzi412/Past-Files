// Past Files/Migration/DatabaseMigrator.cs
using Microsoft.EntityFrameworkCore;
using Past_Files.Data;
using Past_Files.Models;
using Past_Files.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ComponentModel.DataAnnotations; // <<< ADD THIS
using System.ComponentModel.DataAnnotations.Schema; // <<< ADD THIS (for DatabaseGenerated)

// Define a simple DbContext for the OLD schema
namespace Past_Files.Migration
{
    // --- Old Schema Model Definitions (Copied and simplified for migration context) ---
    public class OldFilePath
    {
        public string NormalizedPath { get; }
        public OldFilePath(string path) { NormalizedPath = path.Replace(System.IO.Path.DirectorySeparatorChar, '/'); }
        public override string ToString() => NormalizedPath;
    }

    public class OldFileLocationsHistory
    {
        [Key] // <<< ADD THIS
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)] // Assuming it was auto-generated
        public int FileLocationId { get; set; }

        public int FileRecordId { get; set; }
        public DateTime LocationChangeNoticedTime { get; set; }
        public OldFilePath? Path { get; set; }
        public OldFileRecord? FileRecord { get; set; }
    }

    public class OldFileNamesHistory
    {
        [Key] // <<< ADD THIS
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)] // Assuming it was auto-generated
        public int FileNameHistoryId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public DateTime NameChangeNoticedTime { get; set; }
        public int FileRecordId { get; set; }
        public OldFileRecord FileRecord { get; set; } = null!;
    }

    public class OldFileRecord
    {
        [Key] // <<< ADD THIS
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)] // Assuming it was auto-generated
        public int FileRecordId { get; set; }
        public string Hash { get; set; } = string.Empty;
        public string CurrentFileName { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime LastWriteTime { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public uint VolumeSerialNumber { get; set; }
        public ulong NTFSFileID { get; set; }
        public List<OldFileLocationsHistory> Locations { get; set; } = [];
        public List<OldFileNamesHistory> NameHistory { get; set; } = [];
    }

    public class OldDbMigrationContext : DbContext
    {
        private readonly string _dbPath;
        public DbSet<OldFileRecord> FileRecords { get; set; } = null!;
        public DbSet<OldFileLocationsHistory> FileLocationsHistory { get; set; } = null!;
        public DbSet<OldFileNamesHistory> FileNamesHistory { get; set; } = null!;

        public OldDbMigrationContext(string dbPath) { _dbPath = dbPath; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite($"Data Source={_dbPath}");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // No explicit .HasKey() needed here if [Key] attribute is used on the properties.
            modelBuilder.Entity<OldFileRecord>(entity => {
                // If you had specific configurations for the old schema, they'd go here.
                // For migration, often just letting EF infer from [Key] is enough.
            });

            modelBuilder.Entity<OldFileLocationsHistory>(entity =>
            {
                entity.Property(e => e.Path)
                      .HasConversion(
                          path => path!.NormalizedPath,
                          value => new OldFilePath(value)
                      );
                // No explicit .HasForeignKey() needed for FileRecordId -> OldFileRecord
                // if EF can infer it or if you don't rely on strict FK enforcement for reading.
                // For reading, includes work based on navigation properties.
            });

            modelBuilder.Entity<OldFileNamesHistory>(entity => {
                // Similar to above.
            });
        }
    }
    // --- End of Old Schema Definitions ---

    // ... rest of the DatabaseMigrator class remains the same
    public static class DatabaseMigrator
    {
        public static void Migrate(string oldDbPath, string newDbPath, IConcurrentLoggerService logger)
        {
            logger.Enqueue($"Starting migration from '{oldDbPath}' to '{newDbPath}'.");

            if (File.Exists(newDbPath))
            {
                logger.Enqueue($"Warning: New database '{newDbPath}' already exists. It will be overwritten.");
                File.Delete(newDbPath);
            }

            using var oldDbContext = new OldDbMigrationContext(oldDbPath);
            using var newDbContext = new FileDbContext(newDbPath); // Uses current (new) schema

            logger.Enqueue("Ensuring new database schema is created...");
            newDbContext.Database.EnsureCreated(); // Create new DB with new schema

            logger.Enqueue("Reading records from old database...");
            List<OldFileRecord> oldFileRecords;
            try
            {
                // Ensure relationships are correctly configured or includes work as expected
                oldFileRecords = oldDbContext.FileRecords
                    .Include(fr => fr.Locations)  // This relies on EF figuring out the relationship
                    .Include(fr => fr.NameHistory) // Same here
                    .AsNoTracking()
                    .ToList();
            }
            catch (Exception ex)
            {
                logger.Enqueue($"ERROR reading from old database: {ex.Message} - {ex.InnerException?.Message}. Migration aborted.");
                if (ex.ToString().Contains("no such table: FileRecords") || ex.ToString().Contains("no such table"))
                {
                    logger.Enqueue("The old database might not contain the expected tables or is not a valid SQLite DB with the defined old schema.");
                }
                return;
            }


            logger.Enqueue($"Found {oldFileRecords.Count} records in old database.");

            var contentMap = new Dictionary<string, FileContent>(); // Hash -> FileContent

            logger.Enqueue("Migrating FileContents...");
            int contentCounter = 0;
            // Group by Hash and Size to create unique FileContent entries.
            // If hash collisions with different sizes are possible (shouldn't be with good hashes),
            // then Hash alone might be okay if Size is considered an attribute of the content.
            foreach (var group in oldFileRecords.GroupBy(fr => fr.Hash))
            {
                if (string.IsNullOrEmpty(group.Key))
                {
                    logger.Enqueue($"Skipping records with empty hash (found {group.Count()} such records). First one example: ID {group.First().FileRecordId}");
                    continue;
                }

                var firstOfGroup = group.First();
                // It's possible for files with the same hash to have different FirstSeen/LastSeen times
                // if they were tracked independently in the old schema before realizing they were the same content.
                // The GlobalFirstSeen/LastSeen should be the min/max across all occurrences of that hash.
                var globalFirstSeen = group.Min(fr => fr.FirstSeen);
                var globalLastSeen = group.Max(fr => fr.LastSeen);
                // Assume Size is consistent for a given hash. If not, it's data corruption or hash collision.
                // Taking the size from the first record in the group is a common approach.
                long sizeForHash = firstOfGroup.Size;
                if (group.Any(fr => fr.Size != sizeForHash))
                {
                    logger.Enqueue($"[WARNING] Inconsistent sizes found for hash '{group.Key}'. Using size {sizeForHash}. Other sizes: {string.Join(", ", group.Select(fr => fr.Size).Distinct())}");
                }


                var newContent = new FileContent
                {
                    Hash = group.Key,
                    Size = sizeForHash,
                    GlobalFirstSeen = globalFirstSeen,
                    GlobalLastSeen = globalLastSeen
                };

                if (!contentMap.ContainsKey(newContent.Hash)) // Should be true due to GroupBy
                {
                    newDbContext.FileContents.Add(newContent);
                    contentMap[newContent.Hash] = newContent;
                    contentCounter++;
                    if (contentCounter % 1000 == 0)
                    {
                        try
                        {
                            newDbContext.SaveChanges();
                            logger.Enqueue($"Migrated {contentCounter} FileContents...");
                        }
                        catch (DbUpdateException duex)
                        {
                            logger.Enqueue($"Error saving FileContents batch: {duex.Message} - Inner: {duex.InnerException?.Message}");
                            // Potentially a unique constraint violation on Hash if logic is flawed.
                            // For now, rethrow or log and skip.
                            throw;
                        }
                    }
                }
            }
            try
            {
                newDbContext.SaveChanges();
            }
            catch (DbUpdateException duex)
            {
                logger.Enqueue($"Error saving final FileContents: {duex.Message} - Inner: {duex.InnerException?.Message}");
                throw;
            }
            logger.Enqueue($"Completed migrating {contentCounter} FileContents (unique hashes).");


            logger.Enqueue("Migrating FileInstances and their histories...");
            int instanceCounter = 0;
            foreach (var oldRec in oldFileRecords)
            {
                if (string.IsNullOrEmpty(oldRec.Hash) || !contentMap.TryGetValue(oldRec.Hash, out var parentContent))
                {
                    logger.Enqueue($"Skipping FileRecord ID {oldRec.FileRecordId} (File: '{oldRec.CurrentFileName}') due to missing hash or no parent content found.");
                    continue;
                }

                string fullFilePath = DetermineFullFilePath(oldRec);

                var newInstance = new FileInstance
                {
                    FileContentId = parentContent.FileContentId,
                    VolumeSerialNumber = oldRec.VolumeSerialNumber,
                    NTFSFileID = oldRec.NTFSFileID,
                    FilePath = fullFilePath,
                    CurrentFileName = oldRec.CurrentFileName,
                    InstanceLastWriteTime = oldRec.LastWriteTime,
                    InstanceFirstSeen = oldRec.FirstSeen,
                    InstanceLastSeen = oldRec.LastSeen
                };
                newDbContext.FileInstances.Add(newInstance);

                // Add history items; EF Core will fix up FKs (FileInstanceId) on SaveChanges.
                foreach (var oldNameHist in oldRec.NameHistory.OrderBy(n => n.NameChangeNoticedTime))
                {
                    var newNameHist = new FileNameHistoryEntry
                    {
                        FileInstance = newInstance, // Set navigation property
                        FileName = oldNameHist.FileName,
                        ChangeNoticedTime = oldNameHist.NameChangeNoticedTime
                    };
                    // newInstance.NameHistory.Add(newNameHist); // Let EF manage collection
                    newDbContext.FileNameHistoryEntries.Add(newNameHist);
                }

                foreach (var oldLocHist in oldRec.Locations.OrderBy(l => l.LocationChangeNoticedTime))
                {
                    if (oldLocHist.Path != null && !string.IsNullOrWhiteSpace(oldLocHist.Path.NormalizedPath))
                    {
                        var newLocHist = new FileLocationHistoryEntry
                        {
                            FileInstance = newInstance, // Set navigation property
                            DirectoryPath = oldLocHist.Path.NormalizedPath,
                            ChangeNoticedTime = oldLocHist.LocationChangeNoticedTime
                        };
                        // newInstance.LocationHistory.Add(newLocHist); // Let EF manage collection
                        newDbContext.FileLocationHistoryEntries.Add(newLocHist);
                    }
                    else
                    {
                        logger.Enqueue($"Skipping empty/null location history for OldFileRecordId: {oldRec.FileRecordId}, OldFileLocationId: {oldLocHist.FileLocationId}");
                    }
                }
                instanceCounter++;
                if (instanceCounter % 500 == 0)
                {
                    try
                    {
                        newDbContext.SaveChanges();
                        logger.Enqueue($"Migrated {instanceCounter} FileInstances...");
                    }
                    catch (DbUpdateException duex)
                    {
                        logger.Enqueue($"Error saving FileInstances batch (around instance for old ID {oldRec.FileRecordId}): {duex.Message} - Inner: {duex.InnerException?.Message}");
                        // Potential issue: UQ_FileInstance_Location (VolumeSerialNumber, NTFSFileID)
                        // This would happen if the old DB had duplicates for (VolumeSerialNumber, NTFSFileID) which is unlikely for FileRecordId PK.
                        // More likely an issue with FKs if not set up correctly, or other constraints.
                        throw;
                    }
                }
            }
            try
            {
                newDbContext.SaveChanges();
            }
            catch (DbUpdateException duex)
            {
                logger.Enqueue($"Error saving final FileInstances: {duex.Message} - Inner: {duex.InnerException?.Message}");
                throw;
            }
            logger.Enqueue($"Completed migrating {instanceCounter} FileInstances.");

            logger.Enqueue("Migration finished successfully.");
        }

        private static string DetermineFullFilePath(OldFileRecord oldRec)
        {
            // In the old schema:
            // FileRecord.CurrentFileName was just the name.
            // FileLocationsHistory.Path was the directory.
            var latestLocation = oldRec.Locations
                                      ?.Where(l => l.Path != null && !string.IsNullOrWhiteSpace(l.Path.NormalizedPath)) // Ensure path is valid
                                      .OrderByDescending(l => l.LocationChangeNoticedTime)
                                      .FirstOrDefault();

            string directory = latestLocation?.Path?.NormalizedPath ?? "";

            if (string.IsNullOrEmpty(oldRec.CurrentFileName))
            {
                // This would be problematic. Log it.
                // For now, try to make something up or skip.
                // This case should ideally not happen if data is clean.
                Console.WriteLine($"[WARNING] OldFileRecord ID {oldRec.FileRecordId} has empty CurrentFileName. Full path determination might be incorrect.");
                return directory; // Or throw, or return a placeholder
            }

            // Ensure proper path combination
            if (string.IsNullOrWhiteSpace(directory))
            {
                // If directory is empty, the path is just the filename (assuming it's in a "root" relative to the scan)
                return oldRec.CurrentFileName;
            }
            else
            {
                // Normalize directory to not end with a slash, and filename not to start with one, then join with one slash.
                string normalizedDir = directory.TrimEnd('/');
                string normalizedFileName = oldRec.CurrentFileName.TrimStart('/');
                return $"{normalizedDir}/{normalizedFileName}";
            }
        }
    }
}
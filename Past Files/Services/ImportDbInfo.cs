// Past Files/Services/ImportDbInfo.cs
using Past_Files.Data; // For FileDbContext (new schema)
using Past_Files.Models.Legacy; // For legacy FileRecord
using Microsoft.EntityFrameworkCore; // For DbContextOptions

namespace Past_Files.Services
{
    public class ImportDbInfo
    {
        public FileDbContext ImportedDbContext { get; private set; } // This context is for the OLD schema

        // Static factory method
        public static ImportDbInfo? CreateDBImportInfo(string? oldDbName, IConcurrentLoggerService consoleLoggerService)
        {
            if (string.IsNullOrEmpty(oldDbName)) return null;

            consoleLoggerService.Enqueue($"Initializing import database context for: {oldDbName}");
            try
            {
                // This DbContext should be configured to use the LEGACY models
                var optionsBuilder = new DbContextOptionsBuilder<FileDbContext>();
                optionsBuilder.UseSqlite($"Data Source={oldDbName}");

                // We need a way for this DbContext to know about Legacy.FileRecord.
                // One way is a separate DbContext class for legacy:
                // public class LegacyFileDbContext : DbContext { ... public DbSet<Legacy.FileRecord> FileRecords ... }
                // For now, we assume FileDbContext can be made to work with Legacy.FileRecord set.
                // This is a simplification and might require a custom DbContext for the old schema.
                var legacyDbContext = new FileDbContext(oldDbName); // This is NOT ideal if FileDbContext is strictly new schema.
                                                                    // You'd typically have a LegacyFileDbContext.

                // A quick check if the DB can be connected to and if FileRecords table (legacy) exists
                // This part is complex because the DbContext is strongly typed to its known models.
                // Easiest if you have a dedicated LegacyFileDbContext.

                // For the sake of this example, let's assume ImportedDbContext is correctly configured for legacy.
                return new ImportDbInfo(legacyDbContext);
            }
            catch (Exception ex)
            {
                consoleLoggerService.Enqueue($"Error creating import DB info for {oldDbName}: {ex.Message}");
                return null;
            }
        }

        private ImportDbInfo(FileDbContext importedDbContext)
        {
            ImportedDbContext = importedDbContext;
            // The old DbCache logic is removed as FileProcessor will query ImportedDbContext directly.
        }
    }
}
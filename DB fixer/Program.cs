using Microsoft.EntityFrameworkCore;
using Past_Files.Data;
using Past_Files.Models;
using Past_Files.Services;

DatabaseFixer.FixAbsolutePaths("filetracker.db", new ConsoleLoggerService());

public static class DatabaseFixer
{
    public static void FixAbsolutePaths(string dbPath, IConcurrentLoggerService logger)
    {
        logger.Enqueue($"Starting Database Fix for: {dbPath}");

        // Use a new context to ensure we have a clean state
        using var context = new FileDbContext(dbPath);
        context.Database.EnsureCreated();

        // 1. Get all FileRecordIds to process in batches
        // This prevents loading millions of records into memory at once
        var allRecordIds = context.FileRecords
            .Select(f => f.FileRecordId)
            .OrderBy(id => id)
            .ToList();

        int totalRecords = allRecordIds.Count;
        int batchSize = 2000; // Process 2000 file records at a time
        int processed = 0;
        int pathUpdatesCount = 0;
        int duplicatesRemovedCount = 0;

        logger.Enqueue($"Found {totalRecords} file records to check.");

        for (int i = 0; i < totalRecords; i += batchSize)
        {
            var batchIds = allRecordIds.Skip(i).Take(batchSize).ToList();

            // Load locations for this batch of file records
            var locations = context.FileLocationsHistory
                .Where(l => batchIds.Contains(l.FileRecordId))
                .ToList();

            // Group by FileRecord so we can process each file's history individually
            var groups = locations.GroupBy(l => l.FileRecordId);

            foreach (var group in groups)
            {
                // Sort by time to ensure we process history in order
                var history = group.OrderBy(h => h.LocationChangeNoticedTime).ToList();

                if (history.Count == 0) continue;

                // --- Step 1: Normalize and fix paths to relative ---
                foreach (var loc in history)
                {
                    if (loc.Path == null) continue;

                    string currentPath = loc.Path.NormalizedPath;
                    string relativePath = GetRelativePath(currentPath);

                    // Update if the path needs changing (e.g., stripping "E:/")
                    if (currentPath != relativePath)
                    {
                        loc.Path = new FilePath(relativePath);
                        pathUpdatesCount++;
                    }
                }

                // --- Step 2: Deduplicate consecutive entries ---
                FileLocationsHistory? lastKept = null;

                foreach (var loc in history)
                {
                    if (lastKept == null)
                    {
                        // Always keep the first entry
                        lastKept = loc;
                    }
                    else
                    {
                        // Compare the (now relative) paths
                        if (loc.Path!.NormalizedPath == lastKept.Path!.NormalizedPath)
                        {
                            // This entry is identical to the previous one (redundant).
                            // This happens when "E:/Game" and "F:/Game" both become "Game".
                            context.FileLocationsHistory.Remove(loc);
                            duplicatesRemovedCount++;
                        }
                        else
                        {
                            // The location actually changed (e.g., "Game" -> "Archive/Game")
                            lastKept = loc;
                        }
                    }
                }
            }

            // Save changes for this batch
            context.SaveChanges();

            // Clear tracker to free memory
            context.ChangeTracker.Clear();

            processed += batchIds.Count;
            logger.Enqueue($"Processed {processed}/{totalRecords}. Fixed: {pathUpdatesCount}, Deduped: {duplicatesRemovedCount}");
        }

        logger.Enqueue("Database fix completed.");
        logger.Enqueue($"Total Paths Converted: {pathUpdatesCount}");
        logger.Enqueue($"Total Redundant Entries Removed: {duplicatesRemovedCount}");
    }

    private static string GetRelativePath(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath)) return string.Empty;

        // Check if it's an absolute path (has a root like "C:\" or "\\Server")
        string? root = Path.GetPathRoot(fullPath);

        // If no root is found, it's already relative or invalid, return as is
        if (string.IsNullOrEmpty(root) || (root == "/" && !fullPath.StartsWith("//")))
        {
            return fullPath;
        }

        // Get path relative to the root (e.g., "E:\Games\Civ" -> "Games\Civ")
        string relative = Path.GetRelativePath(root, fullPath);

        // Path.GetRelativePath returns "." if the path is exactly the root
        if (relative == ".") return string.Empty;

        // Ensure we use forward slashes for consistency with the rest of the app
        return relative.Replace('\\', '/');
    }
}
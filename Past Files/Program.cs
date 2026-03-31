using Past_Files.Data;
using Past_Files.FileUtils;
using Past_Files.Services;
using System.Diagnostics;

namespace Past_Files;

public static class Program
{
    public static void Main(string[] args)
    {
        Stopwatch sw = Stopwatch.StartNew();

        using var logger = new ConsoleLoggerService();
        logger.Log($"Starting..");

        var rootScanDirectory = args.Length != 0 && PathHelpers.IsDirectoryValidAndExistant(args[0])
            ? args[0]
            : Environment.CurrentDirectory;

        string dbPath = args switch
        {
            [_, _] => PathHelpers.IsValidFilePath(args[1]) ? args[1] : "filetracker.db",
            [_] => Path.Combine(rootScanDirectory, "filetracker.db"),
            _ => "filetracker.db"
        };

        using (var repository = new EntityRepository(dbPath, logger))
        {
            logger.Log($"Backing up {rootScanDirectory}");

            string errorFilePath = Path.Combine(rootScanDirectory, "Scan Errors.txt");
            FileProcessor processor = new(repository, logger, FileHasherSHA256.ComputeHash, errorFilePath, saveIntervalInSeconds: 500);

            logger.Log("Starting scan...");

            repository.ScanStartUpdateMetadata();

            List<string> filePathsToSkip = [$"{dbPath}", $"{dbPath}-shm", $"{dbPath}-wal"];
            var filePaths = EnumerateAndFilterFiles(rootScanDirectory, filePathsToSkip)
                .Select(x => new ValidNormalizedFilePath(x));

            processor.ScanFiles(filePaths);

            sw.Stop();

            logger.Log($"Scan took {sw.ElapsedMilliseconds / 1000} seconds");

            repository.ScanEndUpdateMetadata();

            logger.Log("Scan completed. Database Updated.");
        }

        // Gracefully exit
        PromptExit();
    }

    private static IEnumerable<string> EnumerateAndFilterFiles(string rootDirectory, List<string> filePathsToSkip)
    {
        // Clone the target filenames into a local list so we can remove them as we find them.
        var remainingToSkip = new List<string>(filePathsToSkip);

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true
        };

        foreach (var path in Directory.EnumerateFiles(rootDirectory, "*", options))
        {
            // FAST PATH: We already found the files to skip. 
            if (remainingToSkip.Count == 0)
            {
                yield return path;
                continue;
            }

            // SLOW PATH: We are still hunting for the files to skip.
            var fileName = path.AsSpan();
            bool isFileToSkip = false;

            for (int i = 0; i < remainingToSkip.Count; i++)
            {
                if (fileName.Equals(remainingToSkip[i], StringComparison.OrdinalIgnoreCase))
                {
                    remainingToSkip.RemoveAt(i); // Remove it so we don't look for it again
                    isFileToSkip = true;
                    break;
                }
            }

            // If it wasn't one of our skipped files, return it to the processor
            if (!isFileToSkip)
            {
                yield return path;
            }
        }
    }
    private static void PromptExit()
    {
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
        Environment.Exit(0);
    }
}

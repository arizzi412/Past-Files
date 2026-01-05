using Past_Files.Data;
using Past_Files.Models;
using Past_Files.Services;
using System.Diagnostics;

namespace Past_Files;

public static class Program
{
    static readonly List<string> namesToskip = ["filetracker.db", "filetracker.db-shm", "filetracker.db-wal"];
    public static void Main(string[] args)
    {
        Stopwatch sw = Stopwatch.StartNew();

        var rootScanDirectory = (args.Length == 0 || string.IsNullOrEmpty(args[0]) || !PathHelpers.IsValidDirectoryAndExists(args[0])) ?  Environment.CurrentDirectory : args[0];

        string dbPath = args switch
        {
            [_, _] => PathHelpers.IsValidFilePath(args[1]) ? args[1] : "filetracker.db",
            [_] => Path.Combine(rootScanDirectory, "filetracker.db"),
            _ => "filetracker.db"
        };

        string errorFilePath = Path.Combine(rootScanDirectory, "Scan Errors.txt");

        using (var loggerService = new ConsoleLoggerService())
        using (var repository = new EntityRepository(dbPath, loggerService))
        {
            loggerService.Enqueue($"Backing up {rootScanDirectory}");
            FileProcessor processor = new(repository, loggerService, errorFilePath, saveIntervalInSeconds: 500);

            loggerService.Enqueue("Starting scan...");

            repository.ScanStartUpdateMetadata();

            ScanSingleThreaded(rootScanDirectory, processor);

            sw.Stop();

            loggerService.Enqueue($"Scan took {sw.ElapsedMilliseconds / 1000} seconds");

            repository.ScanEndUpdateMetadata();

            loggerService.Enqueue("Scan completed. Database Updated.");
        }

        // Gracefully exit
        PromptExit();
    }

    private static void ScanSingleThreaded(string rootDirectory, FileProcessor processor)
    {
        var filePaths = Directory.EnumerateFiles(rootDirectory, "*", new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true
        })
            .Where(x =>
            {
                var name = Path.GetFileName(x);
                return !namesToskip.Contains(name);
            })
            .Select(x => new ValidNormalizedFilePath(x));
            //.ToArray();

        processor.ScanFiles(filePaths);
    }

    private static void PromptExit()
    {
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
        Environment.Exit(0);
    }
}

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

        using var loggerService = new ConsoleLoggerService();

        var rootDirectory = (args.Length == 0 || string.IsNullOrEmpty(args[0]) || !FilePath.IsValidDirectoryAndExists(args[0])) ?  Environment.CurrentDirectory : args[0];
        loggerService.Enqueue($"Backing up {rootDirectory}");

        var repository = new EntityRepository(loggerService);

        FileProcessor processor = new(repository, loggerService, saveIntervalInSeconds: 500);

        loggerService.Enqueue("Starting scan...");

        repository.ScanStartUpdateMetadata();

        ScanSingleThreaded(rootDirectory, processor);

        sw.Stop();

        loggerService.Enqueue($"Scan took {sw.ElapsedMilliseconds / 1000} seconds");

        repository.ScanEndUpdateMetadata();

        loggerService.Enqueue("Scan completed. Database Updated.");

        // Gracefully exit
        PromptExit();
    }


    private static void ScanInParallel(string rootDirectory, FileProcessor processor, FileProcessor processor2)
    {
        var filePaths = GetSplitFilePaths(rootDirectory);

        var task1 = Task.Factory.StartNew(() => processor.ScanFiles(filePaths[0]), TaskCreationOptions.LongRunning);
        var task2 = Task.Factory.StartNew(() => processor2.ScanFiles(filePaths[1]), TaskCreationOptions.LongRunning);

        Task.WaitAll(task1, task2);
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
            .Select(x => new FilePath(x))
            .ToArray();

        processor.ScanFiles(filePaths);
    }

    private static List<FilePath[]> GetSplitFilePaths(string rootDirectory)
    {
        var files = Directory.EnumerateFiles(rootDirectory, "*", new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true
        }).ToArray();

        var sizeOfHalf = files.Length / 2 + 1;
        return [.. files.Select(x => new FilePath(x)).Chunk(sizeOfHalf)];
    }

    private static void PromptExit()
    {
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
        Environment.Exit(0);
    }
}

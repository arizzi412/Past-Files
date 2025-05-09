using CommandLine;
using Past_Files.Data;
using Past_Files.Models;
using Past_Files.Services;
using System.Diagnostics;

namespace Past_Files;

public static class Program
{
    public static void Main(string[] args)
    {
        Stopwatch sw = Stopwatch.StartNew();

        using var loggerService = new ConsoleLoggerService();

        var commandLineArguments = ParseArguments(args, loggerService);
        if (commandLineArguments == null) return;


        var rootDirectory = commandLineArguments.RootDirectory ?? Environment.CurrentDirectory;

        if (commandLineArguments.OldDatabasePath is not null
            && !IsImportDatabasePathValid(commandLineArguments.OldDatabasePath, loggerService))
        {
            return;
        }

        var dbPath = commandLineArguments.DatabasePath ?? "filetracker.db";
        using var dbContext = InitializeDatabase(dbPath);

        ImportDbInfo? importDbInfo = commandLineArguments.OldDatabasePath != null
            ? ImportDbInfo.CreateDBImportInfo(commandLineArguments.OldDatabasePath, loggerService)
            : null;

        var dbCache = DbCache.CreateCache(dbContext, loggerService);

        // Initialize file processors
        FileProcessor processor = new(dbContext, dbCache, loggerService, importDbInfo, saveIntervalInSeconds: 500);
        //using var dbContext2 = InitializeDatabase(dbPath);
        //FileProcessor processor2 = new(dbContext2, dbCache, loggerService, importDbInfo, saveIntervalInSeconds: 450);

        loggerService.Enqueue("Starting scan...");

        var dbMetadata = dbContext.Metadata.First();
        dbMetadata.LastScanStartTime = DateTime.Now;
        dbMetadata.LastScanCompleted = false;
        dbContext.SaveChanges();

        ScanSingleThreaded(rootDirectory, processor);

        sw.Stop();

        loggerService.Enqueue($"Scan took {sw.ElapsedMilliseconds / 1000} seconds");
        dbMetadata.LastScanCompleted = true;
        dbContext.SaveChanges();

        loggerService.Enqueue("Scan completed. Database Updated.");

        // Gracefully exit
        PromptExit();
    }

    private static Options? ParseArguments(string[] args, ConsoleLoggerService loggerService)
    {
        return Parser.Default.ParseArguments<Options>(args)
            .WithNotParsed(errors =>
            {
                foreach (var error in errors)
                {
                    loggerService.Enqueue($"Error parsing arguments: {error}");
                }
                loggerService.Enqueue("Malformed arguments. Exiting.");
                PromptExit();
            }).Value;
    }

    private static bool IsImportDatabasePathValid(string? oldDatabasePath, ConsoleLoggerService loggerService)
    {
        if (string.IsNullOrEmpty(oldDatabasePath))
        {
            loggerService.Enqueue("Empty old database path. Exiting.");
            PromptExit();
            return false;
        }

        if (!File.Exists(oldDatabasePath))
        {
            loggerService.Enqueue($"Old database doesn't exist: {oldDatabasePath}. Exiting.");
            PromptExit();
            return false;
        }

        return true;
    }

    private static FileDbContext InitializeDatabase(string dbPath)
    {
        var dbContext = new FileDbContext(dbPath);
        dbContext.Database.EnsureCreated();
        return dbContext;
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
        }).Select(x => new FilePath(x)).ToArray();

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

public class Options
{
    [Option('r', "root_directory", Required = false, HelpText = "Specifies the root directory.")]
    public string? RootDirectory { get; set; }

    [Option('d', "database_path", Required = false, HelpText = "Specifies the database path.")]
    public string? DatabasePath { get; set; }

    [Option('i', "import", Required = false, HelpText = "Specifies the old database path for import.")]
    public string? OldDatabasePath { get; set; }
}

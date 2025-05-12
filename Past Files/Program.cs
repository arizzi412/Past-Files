// Past Files/Program.cs
using CommandLine;
using Past_Files.Data;
using Past_Files.Models; // General models
using Past_Files.Services;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore; // For migration
using Past_Files.Migration; // Namespace for your migrator

namespace Past_Files;

public static class Program
{
    public class Options
    {
        [Option('r', "root_directory", Required = false, HelpText = "Specifies the root directory for scanning.")]
        public string? RootDirectory { get; set; }

        [Option('d', "database_path", Required = false, HelpText = "Specifies the main database path (new schema).")]
        public string? DatabasePath { get; set; }

        [Option('m', Required = false, HelpText = "Path to the OLD database to migrate to the new schema.")]
        public string? OldDatabasePathForMigration { get; set; }

        [Option('n', Required = false, HelpText = "Path for the NEW database that will be created by migration.")]
        public string? NewDatabasePathForMigration { get; set; }
    }

    public static void Main(string[] args)
    {
        Stopwatch sw = Stopwatch.StartNew();
        using var loggerService = new ConsoleLoggerService();

        var parsedArgs = Parser.Default.ParseArguments<Options>(args);

        parsedArgs.WithParsed(options =>
        {
            if (!string.IsNullOrEmpty(options.OldDatabasePathForMigration) &&
                !string.IsNullOrEmpty(options.NewDatabasePathForMigration))
            {
                loggerService.Enqueue("Starting database migration...");
                if (!File.Exists(options.OldDatabasePathForMigration))
                {
                    loggerService.Enqueue($"ERROR: Old database for migration not found: {options.OldDatabasePathForMigration}");
                    PromptExit();
                    return;
                }
                DatabaseMigrator.Migrate(options.OldDatabasePathForMigration, options.NewDatabasePathForMigration, loggerService);
                loggerService.Enqueue("Database migration completed.");
                PromptExit();
            }
            else if (!string.IsNullOrEmpty(options.RootDirectory))
            {
                RunScan(options, loggerService, sw);
            }
            else
            {
                loggerService.Enqueue("No operation specified. Use --root_directory to scan or --migrate_old_db to migrate.");
                parsedArgs.WithNotParsed(HandleParseError); // Show help
                PromptExit();
            }
        })
            .WithNotParsed(HandleParseError);
    }

    private static void HandleParseError(IEnumerable<Error> errs)
    {
        // Logger might not be initialized here if parsing fails early.
        Console.WriteLine("Error parsing arguments or missing required arguments.");
        foreach (var error in errs)
        {
            if (error is not HelpRequestedError && error is not VersionRequestedError)
                Console.WriteLine(error.ToString());
        }
        PromptExit(true);
    }


    private static void RunScan(Options commandLineArguments, ConsoleLoggerService loggerService, Stopwatch sw)
    {
        var rootDirectory = commandLineArguments.RootDirectory ?? Environment.CurrentDirectory;

        var dbPath = commandLineArguments.DatabasePath ?? "filetracker_new.db";
        loggerService.Enqueue($"Using database: {dbPath}");
        // IMPORTANT: Ensure this DB is either new or already migrated to the NEW schema.
        // EF Core's EnsureCreated will create it based on the NEW schema defined in FileDbContext.
        using var dbContext = InitializeDatabase(dbPath);

        var dbCache = DbCache.CreateCache(dbContext, loggerService);

        FileProcessor processor = new(dbContext, dbCache, loggerService, saveIntervalInSeconds: 300);

        loggerService.Enqueue("Starting scan...");
        var dbMetadata = dbContext.Metadata.FirstOrDefault();

        dbMetadata!.LastScanStartTime = DateTime.UtcNow;
        dbMetadata.LastScanCompleted = false;
        dbContext.SaveChanges();

        ScanSingleThreaded(rootDirectory, processor, loggerService);

        sw.Stop();
        loggerService.Enqueue($"Scan took {sw.ElapsedMilliseconds / 1000.0:F2} seconds");

        dbMetadata.LastScanCompleted = true;
        dbContext.SaveChanges();
        loggerService.Enqueue("Scan completed. Database Updated.");
        PromptExit();
    }

    private static FileDbContext InitializeDatabase(string dbPath)
    {
        var dbContext = new FileDbContext(dbPath);
        dbContext.Database.EnsureCreated(); // Creates DB based on current (new) model definitions
        return dbContext;
    }

    private static void ScanSingleThreaded(string rootDirectory, FileProcessor processor, IConcurrentLoggerService loggerService)
    {
        loggerService.Enqueue($"Scanning directory: {rootDirectory}");
        var filePaths = Directory.EnumerateFiles(rootDirectory, "*", new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true
        }).Select(x => new FilePath(x)).ToArray(); // Use your FilePath model

        processor.ScanFiles(filePaths);
    }

    private static void PromptExit(bool error = false)
    {
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
        Environment.Exit(error ? 1 : 0);
    }
}
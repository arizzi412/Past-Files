// Past Files/Program.cs
using CommandLine;
using Past_Files.Data;
using Past_Files.Models; // General models
using Past_Files.Services;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore; // For migration

namespace Past_Files;

public static class Program
{
    public static void Main(string[] args)
    {
        Stopwatch sw = Stopwatch.StartNew();
        using var loggerService = new ConsoleLoggerService();
        var rootDirectory = Environment.CurrentDirectory;
        var dbName = "filetracker.db";

        RunScan(rootDirectory, dbName, loggerService);

        sw.Stop();
        loggerService.Enqueue($"Scan took {sw.ElapsedMilliseconds / 1000.0:F2} seconds");
    }



    private static void RunScan(string directoryToSearch, string dbName, ConsoleLoggerService loggerService)
    {

        loggerService.Enqueue($"Using database: {dbName}");
        // IMPORTANT: Ensure this DB is either new or already migrated to the NEW schema.
        // EF Core's EnsureCreated will create it based on the NEW schema defined in FileDbContext.
        using var dbContext = InitializeDatabase(dbName);
        var repository = new EntityRepository(dbContext, loggerService);

        FileProcessor processor = new(repository, loggerService);

        loggerService.Enqueue("Starting scan...");
        repository.UpdateScanStartMetadata();

        ScanSingleThreaded(directoryToSearch, processor, loggerService);

        repository.UpdateScanEndMetadata();
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
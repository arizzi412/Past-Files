using Past_Files.PathHelper;
using Past_Files.Services;
using Past_Files.Models;
using System.Runtime.InteropServices;
using CommandLine;

namespace Past_Files;

public static class Program
{
    public static void Main(string[] args)
    {
        // Create the console logger service (optional to pass in explicitly)
        using var loggerService = new ConsoleLoggerService();

        Data.FileTrackerContext dbContext = null;
        FileProcessor processor = null;

        Parser.Default.ParseArguments<Options>(args)
            .WithParsed(options =>
            {
                if (options.DatabasePath is not null)
                {
                    dbContext = new Data.FileTrackerContext(options.DatabasePath);
                }
                else
                {
                    dbContext = new Data.FileTrackerContext("filetracker.db");
                }
                dbContext.Database.EnsureCreated();

                if (options.OldDatabasePath != null)
                {
                    var oldDBInfo = DBImportInfo.CreateDBImportInfo(options.OldDatabasePath, loggerService);
                    new FileProcessor(dbContext!, oldDBInfo, saveIntervalInSeconds: 20, logger: loggerService);
                }

                new FileProcessor(dbContext!, saveIntervalInSeconds: 20, logger: loggerService);
            });



        // Specify the directory to scan
        Models.Path rootDirectory = args.FirstOrDefault() ?? @"E:\Firaxis Games";

        loggerService.Enqueue("Starting scan...");
        processor.ScanDirectory(rootDirectory);
        loggerService.Enqueue("Scan completed. Database Updated.");

        // Example usage
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}

class Options
{
    [Option('d', "db", Required = false, HelpText = "Specifies the -db argument.")]
    public string? DatabasePath { get; set; }

    [Option('i', "import", Required = false, HelpText = "Specifies the -import argument.")]
    public string? OldDatabasePath { get; set; }
}
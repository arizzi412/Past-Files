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

        Models.Path rootDirectory = null; ;

        Parser.Default.ParseArguments<Options>(args)
            .WithParsed(options =>
            {
                rootDirectory = options.rootDirectory ?? @"E:\Firaxis Games";

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
                    processor = new FileProcessor(dbContext!, oldDBInfo, saveIntervalInSeconds: 20, logger: loggerService);
                }
                else
                {
                    processor = new FileProcessor(dbContext!, saveIntervalInSeconds: 20, logger: loggerService);
                }
            });



        loggerService.Enqueue("Starting scan...");
        processor!.ScanDirectory(rootDirectory);
        loggerService.Enqueue("Scan completed. Database Updated.");

        // Example usage
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}

class Options
{
    [Option('d', "directory", Required = false, HelpText = "Specifies the -directory argument.")]
    public string? rootDirectory { get; set; }


    [Option("db", Required = false, HelpText = "Specifies the -db argument.")]
    public string? DatabasePath { get; set; }

    [Option('i', "import", Required = false, HelpText = "Specifies the -import argument.")]
    public string? OldDatabasePath { get; set; }
}
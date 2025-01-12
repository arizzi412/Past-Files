using CommandLine;
using Past_Files.Models;
using Past_Files.Services;

namespace Past_Files
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            // Initialize the console logger service
            using var loggerService = new ConsoleLoggerService();


            // Parse command-line arguments
            var options = Parser.Default.ParseArguments<Options>(args)
                .WithNotParsed(errors =>
                {
                    // Handle parsing errors
                    foreach (var error in errors)
                    {
                        loggerService.Enqueue($"Error parsing arguments: {error}");
                    }
                    loggerService.Enqueue("Malformed arguments.  Exiting.");

                }).Value;



            var rootDirectory = options.RootDirectory ?? @"E:\Firaxis Games";
            var dbPath = options.DatabasePath ?? "filetracker.db";
            using var dbContext = new Data.FileTrackerContext(dbPath);
            dbContext.Database.EnsureCreated();

            DBImportInfo oldDBInfo = null;
            if (!string.IsNullOrWhiteSpace(options.OldDatabasePath))
            {
                oldDBInfo = DBImportInfo.CreateDBImportInfo(options.OldDatabasePath, loggerService);
            }

            var dataStore = DataStore.CreateDataStore(dbContext, loggerService);


            var filePaths = GetFilePaths(rootDirectory);

            loggerService.Enqueue($"Total files to process: {filePaths.Count}");

            FileProcessor processor = new FileProcessor(dbContext, dataStore, loggerService, oldDBInfo, saveIntervalInSeconds: 20);

            using var dbContext2 = new Data.FileTrackerContext(dbPath);
            FileProcessor processor2 = new FileProcessor(dbContext2, dataStore, loggerService, oldDBInfo, saveIntervalInSeconds: 20);


            // Start the scanning process
            loggerService.Enqueue("Starting scan...");


            var task1 = Task.Run(() => processor.ScanFiles(filePaths[0]));
            var task2 = Task.Run(() => processor2.ScanFiles(filePaths[1]));

            Task.WaitAll(task1, task2);

            loggerService.Enqueue("Scan completed. Database Updated.");


            // Prompt to exit
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        private static List<FilePath[]> GetFilePaths(string rootDirectory)
        {
            var files = Directory.EnumerateFiles(rootDirectory, "*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true
            }).ToArray();

            var sizeOfHalf = files.Count() / 2 + 1;

            return files.Select(x => new FilePath(x)).Chunk(sizeOfHalf).ToList();
        }
    }

    public class Options
    {
        [Option('d', "directory", Required = false, HelpText = "Specifies the root directory.")]
        public string? RootDirectory { get; set; }

        [Option("db", Required = false, HelpText = "Specifies the database path.")]
        public string? DatabasePath { get; set; }

        [Option('i', "import", Required = false, HelpText = "Specifies the old database path for import.")]
        public string? OldDatabasePath { get; set; }
    }
}

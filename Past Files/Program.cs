using CommandLine;
using Past_Files.Models;
using Past_Files.Services;
using System.Diagnostics;

namespace Past_Files
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            Stopwatch sw = Stopwatch.StartNew();

            // Initialize the console logger service
            using var loggerService = new ConsoleLoggerService();

            // Parse command-line arguments
            var options = ParseArguments(args, loggerService);
            if (options == null) return;

            var rootDirectory = options.RootDirectory ?? Environment.CurrentDirectory;

            // Validate database paths
            if (!ValidateDatabasePath(options.OldDatabasePath, loggerService)) return;

            var dbPath = options.DatabasePath ?? "filetracker.db";
            using var dbContext = InitializeDatabase(dbPath);

            DBImportInfo? dbImportInfo = options.OldDatabasePath != null
                ? DBImportInfo.CreateDBImportInfo(options.OldDatabasePath, loggerService)
                : null;

            var dataStore = DataStore.CreateDataStore(dbContext, loggerService);

            // Initialize file processors
            FileProcessor processor = new(dbContext, dataStore, loggerService, dbImportInfo, saveIntervalInSeconds: 500);
            using var dbContext2 = InitializeDatabase(dbPath);
            FileProcessor processor2 = new(dbContext2, dataStore, loggerService, dbImportInfo, saveIntervalInSeconds: 450);

            // Start the scanning process
            loggerService.Enqueue("Starting scan...");

            ScanInParallel(rootDirectory, processor, processor2);

            sw.Stop();
            loggerService.Enqueue($"Scan took {sw.ElapsedMilliseconds / 1000} seconds");
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

        private static bool ValidateDatabasePath(string? oldDatabasePath, ConsoleLoggerService loggerService)
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

        private static Data.FileTrackerContext InitializeDatabase(string dbPath)
        {
            var dbContext = new Data.FileTrackerContext(dbPath);
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
            return files.Select(x => new FilePath(x)).Chunk(sizeOfHalf).ToList();
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
        [Option('d', "directory", Required = false, HelpText = "Specifies the root directory.")]
        public string? RootDirectory { get; set; }

        [Option("db", Required = false, HelpText = "Specifies the database path.")]
        public string? DatabasePath { get; set; }

        [Option('i', "import", Required = false, HelpText = "Specifies the old database path for import.")]
        public string? OldDatabasePath { get; set; }
    }
}

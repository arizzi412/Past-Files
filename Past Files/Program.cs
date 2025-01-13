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
            var options = Parser.Default.ParseArguments<Options>(args)
                .WithNotParsed(errors =>
                {
                    // Handle parsing errors
                    foreach (var error in errors)
                    {
                        loggerService.Enqueue($"Error parsing arguments: {error}");
                    }
                    loggerService.Enqueue("Malformed arguments.  Exiting.");
                    return;

                }).Value;

            var rootDirectory = options.RootDirectory ?? Environment.CurrentDirectory;

            if (options.DatabasePath is not null)
            {
                ExitIfDBNotValid(options.DatabasePath, loggerService);
            }
            var dbPath = options.DatabasePath ?? "filetracker.db";
            using var dbContext = new Data.FileTrackerContext(dbPath);
            dbContext.Database.EnsureCreated();

            DBImportInfo? dbImportInfo = null;

            if (options.OldDatabasePath is not null)
            {
                ExitIfDBNotValid(options.OldDatabasePath, loggerService);

                dbImportInfo = DBImportInfo.CreateDBImportInfo(options.OldDatabasePath, loggerService);
            }


            var dataStore = DataStore.CreateDataStore(dbContext, loggerService);

            FileProcessor processor = new(dbContext, dataStore, loggerService, dbImportInfo, saveIntervalInSeconds: 500);

            using var dbContext2 = new Data.FileTrackerContext(dbPath);
            FileProcessor processor2 = new(dbContext2, dataStore, loggerService, dbImportInfo, saveIntervalInSeconds: 450);


            // Start the scanning process
            loggerService.Enqueue("Starting scan...");

            //ScanSingleThreaded(rootDirectory, processor);

            ScanInParallel(rootDirectory, processor, processor2);


            sw.Stop();
            loggerService.Enqueue($"Scan took {sw.ElapsedMilliseconds / 1000} seconds");

            loggerService.Enqueue("Scan completed. Database Updated.");

            Thread.Sleep(2000);

            // Prompt to exit
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        private static void ExitIfDBNotValid(string OldDatabasePath, ConsoleLoggerService loggerService)
        {
            if (OldDatabasePath == string.Empty)
            {
                loggerService.Enqueue($"Empty database: {OldDatabasePath}.  \nExiting.");
                loggerService.Dispose();
                return;
            }

            if (!File.Exists(OldDatabasePath))
            {
                loggerService.Enqueue($"Database doesn't exist: {OldDatabasePath}. \nExiting.");
                loggerService.Dispose();
                return;
            }
        }

        private static void ScanSingleThreaded(string rootDirectory, FileProcessor processor)
        {
            var filePaths = GetFilePaths(rootDirectory);
            processor.ScanFiles(filePaths);
        }

        private static void ScanInParallel(string rootDirectory, FileProcessor processor, FileProcessor processor2)
        {
            var filePaths = GetSplitFilePaths(rootDirectory);

            //loggerService.Enqueue($"Total files to process: {filePaths.Count}");

            var task1 = Task.Factory.StartNew(() => processor.ScanFiles(filePaths[0]), TaskCreationOptions.LongRunning);
            var task2 = Task.Factory.StartNew(() => processor2.ScanFiles(filePaths[1]), TaskCreationOptions.LongRunning);

            Task.WaitAll(task1, task2);
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

        private static FilePath[] GetFilePaths(string rootDirectory)
        {
            return Directory.EnumerateFiles(rootDirectory, "*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true
            }).Select(x => new FilePath(x)).ToArray();
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

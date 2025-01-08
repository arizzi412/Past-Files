using Past_Files.Services;

public static class Program
{
    public static void Main()
    {
        // Create the console logger service (optional to pass in explicitly)
        using var loggerService = new ConsoleLoggerService();

        // Create the FileProcessor with a 20-second autosave interval and pass in the logger
        using var processor = new FileProcessor(saveIntervalInSeconds: 20, logger: loggerService);

        // Specify the directory to scan
        string rootDirectory = @"E:\Firaxis Games";

        loggerService.Enqueue("Starting scan...");
        processor.ScanDirectory(rootDirectory);
        loggerService.Enqueue("Scan completed. Database Updated.");

        // Example usage
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}

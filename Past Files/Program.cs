// Program.cs
using Past_Files.Services;

var processor = new FileProcessor();

// Specify the directory to scan
// You can modify this to scan multiple directories if needed
string rootDirectory = @"C:\Users\arizz\Desktop\test program"; // Change this to the desired directory

Console.WriteLine("Starting scan...");
processor.ScanDirectory(rootDirectory);
Console.WriteLine("Scan completed.  Database Updated");

static void RetrieveInfoBasedOffFileName(FileProcessor processor)
{
    // Retrieve file information based on filename
    Console.WriteLine("\nEnter the name of the file to get its information (including extension):");
    string? inputFileName = Console.ReadLine();

    if (!string.IsNullOrWhiteSpace(inputFileName))
    {
        processor.GetFileInformation(inputFileName);
    }

    Console.WriteLine("\nPress any key to exit...");
    Console.ReadKey();
}

static void CheckIfFileExisted(FileProcessor processor)
{

    // Example usage of HasFileExistedBefore
    Console.WriteLine("\nEnter the full path of a file to check if it has existed before:");
    string? newFilePath = Console.ReadLine();

    if (!string.IsNullOrWhiteSpace(newFilePath))
    {
        if (processor.HasFileExistedBefore(newFilePath))
        {
            Console.WriteLine("This file has been on the hard drive before.");
        }
        else
        {
            Console.WriteLine("This is a new file.");
        }
    }
}

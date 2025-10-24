// Past Files/Services/FileProcessor.cs
using Past_Files.Data;
using Past_Files.FileUtils;
using Past_Files.Models;
using System;
using System.Diagnostics;
using System.IO; // Required for Path methods
using System.Linq; // Required for LINQ methods like MaxBy
using Microsoft.EntityFrameworkCore; // Required for EF Core operations

namespace Past_Files.Services;

public class FileProcessor(EntityRepository repository, IConcurrentLoggerService logger)
{
    private readonly int _saveIntervalInSeconds = 500;
    private readonly string errorFile = Environment.CurrentDirectory + @"\Scan errors.txt";

    public void ScanFiles(FilePath[] filePaths)
    {
        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            foreach (var filePathModel in filePaths)
            {
                logger.Enqueue($"Processing {filePathModel.NormalizedPath}");
                ProcessFile(filePathModel);
                if (stopwatch.ElapsedMilliseconds > _saveIntervalInSeconds * 1000)
                {
                    repository.SaveChangesCallback();
                    stopwatch.Restart();
                }
            }
            repository.SaveChangesCallback();
        }
        catch (Exception ex)
        {
            string errorMessage = $"Error during scanning: {ex.Message}. Inner exception: {ex.InnerException?.Message}\n";
            logger.Enqueue(errorMessage);
            File.AppendAllTextAsync(errorFile, errorMessage);
        }
    }

    public void ProcessFile(FilePath filePathModel)
    {
        string currentFilePath = filePathModel.NormalizedPath;
        try
        {
            var fileInfo = new FileInfo(currentFilePath);
            if (!fileInfo.Exists)
            {
                logger.Enqueue($"File not found: {currentFilePath}. Skipping.");
                return;
            }

            var fileIdentityKey = FileIdentifier.GetFileIdentityKey(currentFilePath);
            DateTime currentTime = DateTime.UtcNow;

            // Try to find existing instance in cache
            repository.dbCache.IdentityKeyToFileInstanceCache.TryGetValue(fileIdentityKey, out var existingInstance);

            if (existingInstance != null)
            {
                repository.UpdateExistingFileInstance(existingInstance, fileInfo, currentFilePath, currentTime);
            }
            else
            {
                repository.CreateNewFileInstance(fileInfo, fileIdentityKey, currentFilePath, currentTime);
            }
        }
        catch (IOException ioEx) when (ioEx.Message.Contains("being used by another process"))
        {
            logger.Enqueue($"Skipping locked file '{currentFilePath}': {ioEx.Message}");
        }
        catch (Exception ex)
        {
            string errorMessage = $"Error processing file '{currentFilePath}': {ex.Message}. Inner exception: {ex.InnerException?.Message}\n";
            logger.Enqueue(errorMessage);
            File.AppendAllTextAsync(errorFile, errorMessage);
        }
    }

}


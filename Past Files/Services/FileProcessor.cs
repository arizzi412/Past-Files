using Past_Files.Data;
using Past_Files.FileUtils;
using Past_Files.Models;
using System.Diagnostics;

namespace Past_Files.Services;

/// <summary>
/// Represents a method that computes a hash value for the specified file.
/// </summary>
/// <param name="filePath">The path to the file to hash. Cannot be null or empty.</param>
/// <returns>A string containing the computed hash value of the file.</returns>
public delegate string HashFileMethod(string filePath);

public class FileProcessor(EntityRepository repository, ConsoleLoggerService logger, HashFileMethod hashFileAlgorithm, string errorFile) : IDisposable
{
    public void ProcessFile(ValidNormalizedFilePath filePath)
    {
        try
        {
            var fileIdentityKey = FileIdentifier.GetFileIdentityKey(filePath);
            var fileInfo = new FileInfo(filePath);

            if (!fileInfo.Exists) return;

            bool RecordExistsInDB = repository.TryFindRecord(fileIdentityKey, out var fileRecord);

            if (RecordExistsInDB)
            {
                UpdateRecordIfHasChanges(fileRecord!, filePath, fileInfo);
            }
            else
            {
                fileRecord = repository.CreateNewFileRecordAndAddToDB(filePath, fileInfo, fileIdentityKey, hashFileAlgorithm(filePath));
            }

        }
        catch (Exception ex)
        {
            LogError($"Error processing file '{filePath}'", ex);
        }
    }

    private void UpdateRecordIfHasChanges(FileRecord fileRecord, ValidNormalizedFilePath filePath, FileInfo fileInfo)
    {
        FileLocationsHistory mostRecentLocationInDB = fileRecord.Locations.MaxBy(x => x.LocationChangeNoticedTime) ?? throw new Exception($"No FileLocationHistory entry in DB for {fileRecord.CurrentFileName}");

        var currentTime = DateTime.UtcNow;
        fileRecord.LastSeen = currentTime;

        if (fileRecord.LastWriteTime != fileInfo.LastWriteTimeUtc)
        {
            fileRecord.Size = fileInfo.Length;

            string newHash = hashFileAlgorithm(filePath);
            if (fileRecord.Hash != newHash)
            {
                fileRecord.Hash = newHash;
            }
            fileRecord.LastWriteTime = fileInfo.LastWriteTimeUtc;
        }

        var nameDifferent = !fileRecord.CurrentFileName.Equals(fileInfo.Name, StringComparison.OrdinalIgnoreCase);
        if (nameDifferent)
        {
            repository.UpdateName(fileInfo.Name, currentTime, fileRecord);
        }

        var incomingRelativePath = filePath.GetDirectoryRelativeToRoot();
        var pathInDB = mostRecentLocationInDB.Path!;

        var locationDifferent = !incomingRelativePath.Equals(pathInDB);

        if (locationDifferent)
        {
            repository.RecordNewFileRecordLocation(filePath, currentTime, fileRecord);
        }
    }
    private void LogError(string contextMessage, Exception ex)
    {
        string message = $"{contextMessage}: {ex.Message}. Inner Exception: {ex.InnerException?.Message}\n";
        logger.Log(message);
        File.AppendAllTextAsync(errorFile, message);
    }

    public void Dispose()
    {
            repository.SaveIfHasChanges();
    }
}

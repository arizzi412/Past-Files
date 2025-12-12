using Past_Files.Data;
using Past_Files.FileUtils;
using Past_Files.Models;
using System.Diagnostics;

namespace Past_Files.Services;

public class FileProcessor(EntityRepository repository, IConcurrentLoggerService logger, int saveIntervalInSeconds = 500) : IDisposable
{
    private readonly int _saveIntervalInSeconds = saveIntervalInSeconds > 0 ? saveIntervalInSeconds : 15;
    private readonly Lock _dbSaveLock = new();
    private readonly string errorFile = Environment.CurrentDirectory + @"\Scan errors.txt";

    private void SaveChangesCallback()
    {
        try
        {
            lock (_dbSaveLock)
            {
                repository.SaveIfHasChanges();
            }
        }
        catch (Exception ex)
        {
            string message = $"[TIMER ERROR] Failed to save changes: {ex.Message} Inner Exception: {ex.InnerException}\n";
            logger.Enqueue(message);
            File.AppendAllTextAsync(errorFile, message);
        }
    }

    public void ScanFiles(FilePath[] filePaths)
    {
        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            foreach (var filePath in filePaths)
            {
                logger.Enqueue($"Processing {filePath}");

                ProcessFile(filePath);
                if (stopwatch.ElapsedMilliseconds > _saveIntervalInSeconds * 1000)
                {
                    SaveChangesCallback();
                    stopwatch.Restart();
                }
            }

            SaveChangesCallback();

        }
        catch (Exception ex)
        {
            string errorMessage = $"Error during scanning: {ex.Message}.  Inner exception: {ex.InnerException}\n";
            logger.Enqueue(errorMessage);
            File.AppendAllTextAsync(errorFile, errorMessage);
        }
    }

    public void ProcessFile(FilePath filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var fileIdentityKey = FileIdentifier.GetFileIdentityKey(filePath);

            if (!fileInfo.Exists) return;

            DateTime currentTime = DateTime.UtcNow;

            bool RecordExistsInDB = repository.TryToFindRecordByFileIdentity(fileIdentityKey, out var fileRecord);

            if (RecordExistsInDB)
            {
                UpdateExistingRecord(filePath, fileInfo, currentTime, fileRecord!);
            }
            else
            {
                fileRecord = repository.CreateNewFileRecordAndAddToDB(filePath, fileInfo, fileIdentityKey, currentTime);
            }

            // note no database changes will be written until context.SaveChanges() is called.
        }
        catch (Exception ex)
        {
            string errorMessage = $"Error processing file '{filePath}': {ex.Message}.  Inner exception: {ex.InnerException}\n";
            logger.Enqueue(errorMessage);
            File.AppendAllTextAsync(errorFile, errorMessage);
        }
    }


    private void UpdateExistingRecord(FilePath filePath, FileInfo fileInfo, DateTime currentTime, FileRecord fileRecord)
    {
        fileRecord!.LastSeen = currentTime;

        if (fileRecord.LastWriteTime != fileInfo.LastWriteTimeUtc)
        {
            fileRecord.Size = fileInfo.Length;

            string newHash = FileHasher.ComputeFileHash(filePath);
            if (fileRecord.Hash != newHash)
            {
                fileRecord.Hash = newHash;
            }
            fileRecord.LastWriteTime = fileInfo.LastWriteTimeUtc;
        }

        var nameDifferent = !fileRecord.CurrentFileName.Equals(fileInfo.Name, StringComparison.OrdinalIgnoreCase);
        if (nameDifferent)
        {
            repository.UpdateFileRecordName(fileInfo, currentTime, fileRecord);
        }

        var mostRecentLocationInDB = fileRecord.Locations.MaxBy(x => x.LocationChangeNoticedTime);

        // The DB now contains (or will contain) relative paths"
        string incomingRelativePath = filePath.GetDirectoryRelativeToRoot();
        string dbPath = mostRecentLocationInDB.Path!.NormalizedPath;

        // Simple string comparison
        var locationDifferent = !incomingRelativePath.Equals(dbPath, StringComparison.OrdinalIgnoreCase);

        if (locationDifferent)
        {
            repository.UpdateFileRecordLocation(filePath, currentTime, fileRecord);
        }
        repository.UpdateFileRecord(fileRecord);
    }


    public void Dispose()
    {
        logger.Dispose();

        lock (_dbSaveLock)
        {
            repository.SaveIfHasChanges();
        }
        repository.Dispose();
    }

}

public record struct FileIdentityKey(ulong NTFSFileID, uint VolumeSerialNumber);
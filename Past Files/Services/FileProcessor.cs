using Past_Files.Data;
using Past_Files.FileUtils;
using Past_Files.Models;
using System.Diagnostics;

namespace Past_Files.Services;

public class FileProcessor(EntityRepository repository, ConsoleLoggerService logger, string errorFile, int saveIntervalInSeconds = 500) : IDisposable
{
    private readonly int _saveIntervalInSeconds = saveIntervalInSeconds > 0 ? saveIntervalInSeconds : 15;
    private readonly Lock _dbSaveLock = new();
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

    public void ScanFiles(IEnumerable<ValidNormalizedFilePath> filePaths)
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

    public void ProcessFile(ValidNormalizedFilePath filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var fileIdentityKey = FileIdentifier.GetFileIdentityKey(filePath);

            if (!fileInfo.Exists) return;

            DateTime currentTime = DateTime.UtcNow;

            bool RecordExistsInDB = repository.TryFindRecord(fileIdentityKey, out var fileRecord);

            if (RecordExistsInDB)
            {
                UpdateRecordIfHasChanges(fileRecord!, filePath, fileInfo, currentTime);
            }
            else
            {
                fileRecord = repository.CreateNewFileRecordAndAddToDB(filePath, fileInfo, fileIdentityKey, currentTime, FileHasher.ComputeFileHash(filePath));
            }

        }
        catch (Exception ex)
        {
            string errorMessage = $"Error processing file '{filePath}': {ex.Message}.  Inner exception: {ex.InnerException}\n";
            logger.Enqueue(errorMessage);
            File.AppendAllTextAsync(errorFile, errorMessage);
        }
    }


    private void UpdateRecordIfHasChanges(FileRecord fileRecord, ValidNormalizedFilePath filePath, FileInfo fileInfo, DateTime currentTime)
    {
        FileLocationsHistory mostRecentLocationInDB = fileRecord.Locations.MaxBy(x => x.LocationChangeNoticedTime) ?? throw new Exception($"No FileLocationHistory entry in DB for {fileRecord.CurrentFileName}");

        fileRecord.LastSeen = currentTime;

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

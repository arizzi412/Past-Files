using Microsoft.EntityFrameworkCore;
using Past_Files.Data;
using Past_Files.FileUtils;
using Past_Files.Models;
using System.Diagnostics;

namespace Past_Files.Services;

public class FileProcessor(FileTrackerContext context, IDataStore dataStore, IConcurrentLoggerService logger, DBImportInfo? oldDBHashImportInfo = null, int saveIntervalInSeconds = 500) : IDisposable
{
    private readonly int _saveIntervalInSeconds = saveIntervalInSeconds > 0 ? saveIntervalInSeconds : 500;
    private readonly Lock _dbSaveLock = new();
    private readonly string errorFile = Environment.CurrentDirectory + @"\errors.txt";

    private void SaveChangesCallback()
    {
        try
        {
            lock (_dbSaveLock)
            {
                if (context.ChangeTracker.HasChanges())
                {
                    context.SaveChanges();
                    logger.Enqueue($"[TIMER] Auto-saved changes at {DateTime.UtcNow:u}");
                }
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
            int processedCount = 0;

            foreach (var filePath in filePaths)
            {
                logger.Enqueue($"Processing {filePath}");

                ProcessFile(filePath);

                processedCount++;
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
        FileInformation info = null;
        try
        {
            info = FileInformation.CreateFileInformation(filePath);
            
            if (!info.Info.Exists) return;

            FileIdentityKey fileIdentityKey = info.IdentityKey;

            DateTime currentTime = DateTime.UtcNow;
            bool isNewRecord = false;

            FileRecord? fileRecord = TryToFindRecordByFileIdentity(fileIdentityKey, dataStore);

            if (fileRecord == null)
            {
                isNewRecord = true;

                fileRecord = CreateNewFileRecordWithNameHistoryAndIdentity(info.Info, fileIdentityKey, currentTime);
            }
            else
            {
                UpdateFileRecordIfDifferencesInNameOrContentFound(info.Path, info.Info, currentTime, fileRecord);
            }

            UpdateFileLocationIfMoved(info.Path, currentTime, fileRecord);

            if (!isNewRecord)
            {
                context.FileRecords.Update(fileRecord);
            }
        }
        catch (Exception ex)
        {
            string errorMessage = $"Error processing file '{info?.Path}': {ex.Message}.  Inner exception: {ex.InnerException}\n";
            logger.Enqueue(errorMessage);
            File.AppendAllTextAsync(errorFile, errorMessage);
        }
    }

    private void UpdateFileLocationIfMoved(FilePath filePath, DateTime currentTime, FileRecord fileRecord)
    {
        var mostRecentLocationInDB = fileRecord.Locations.Count != 0 ? fileRecord.Locations.MaxBy(x => x.LocationChangeNoticedTime) : null;
        // if mostRecentLocationInDB is null then there are no locations in db
        if (mostRecentLocationInDB is null
            || !System.IO.Path.GetDirectoryName(filePath.NormalizedPath.AsSpan()).SequenceEqual(mostRecentLocationInDB.Path!.NormalizedPath.AsSpan()))
        {
            var newLocation = new FileLocationsHistory
            {
                Path = Path.GetDirectoryName(filePath) ?? string.Empty,
                FileRecordId = fileRecord.FileRecordId,
                LocationChangeNoticedTime = currentTime
            };
            context.FileLocationsHistory.Add(newLocation);
            fileRecord.Locations.Add(newLocation);
        }
    }

    private void UpdateFileRecordIfDifferencesInNameOrContentFound(FilePath filePath, FileInfo fileInfo, DateTime currentTime, FileRecord fileRecord)
    {
        fileRecord.LastSeen = currentTime;
        fileRecord.Size = fileInfo.Length;

        if (fileRecord.LastWriteTime != fileInfo.LastWriteTimeUtc || fileRecord.Size != fileInfo.Length)
        {
            string newHash = FileHasher.ComputeFileHash(filePath);
            if (fileRecord.Hash != newHash)
            {
                fileRecord.Hash = newHash;
            }
            fileRecord.LastWriteTime = fileInfo.LastWriteTimeUtc;
        }

        if (!fileRecord.CurrentFileName.Equals(fileInfo.Name, StringComparison.OrdinalIgnoreCase))
        {
            fileRecord.CurrentFileName = fileInfo.Name;

            var nameHistory = new FileNamesHistory
            {
                FileName = fileInfo.Name,
                NameChangeNoticedTime = currentTime,
                FileRecordId = fileRecord.FileRecordId
            };
            context.FileNamesHistory.Add(nameHistory);
            fileRecord.NameHistory.Add(nameHistory);
        }
    }

    private FileRecord CreateNewFileRecordWithNameHistoryAndIdentity(FileInfo fileInfo, FileIdentityKey fileIdentityKey, DateTime currentTime)
    {
        FileRecord fileRecord = new()
        {
            Hash = ComputeHashOrImportFromOldDb(fileInfo.FullName, fileIdentityKey),
            CurrentFileName = fileInfo.Name,
            Size = fileInfo.Length,
            LastWriteTime = fileInfo.LastWriteTimeUtc,
            FirstSeen = currentTime,
            LastSeen = currentTime,
            NTFSFileID = fileIdentityKey.NTFSFileID,
            VolumeSerialNumber = fileIdentityKey.VolumeSerialNumber,
            Locations = [],
            NameHistory = []
        };

        context.FileRecords.Add(fileRecord);
        dataStore.IdentityKeyToFileRecord[fileIdentityKey] = fileRecord;

        var initialNameHistory = new FileNamesHistory
        {
            FileName = fileInfo.Name,
            NameChangeNoticedTime = currentTime,
            FileRecordId = fileRecord.FileRecordId
        };
        context.FileNamesHistory.Add(initialNameHistory);
        fileRecord.NameHistory.Add(initialNameHistory);
        return fileRecord;
    }
    private string ComputeHashOrImportFromOldDb(string filePath, FileIdentityKey fileIdentityKey)
    {
        if (oldDBHashImportInfo is not null)
        {
            return TryToFindRecordByFileIdentity(fileIdentityKey, oldDBHashImportInfo.dataStore)?.Hash ?? FileHasher.ComputeFileHash(filePath);
        }

        return FileHasher.ComputeFileHash(filePath);
    }

    private static FileRecord? TryToFindRecordByFileIdentity(FileIdentityKey fileIdentityKey, IDataStore dataStore)
    {
        dataStore.IdentityKeyToFileRecord.TryGetValue(fileIdentityKey, out var fileRecord);
        return fileRecord;
    }

    public void Dispose()
    {
        logger.Dispose();

        lock (_dbSaveLock)
        {
            if (context.ChangeTracker.HasChanges())
            {
                context.SaveChanges();
            }
        }
        context.Dispose();
    }
}

public record struct FileIdentityKey(ulong NTFSFileID, uint VolumeSerialNumber);
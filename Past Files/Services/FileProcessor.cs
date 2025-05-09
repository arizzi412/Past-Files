using Past_Files.Data;
using Past_Files.FileUtils;
using Past_Files.Models;
using System.Diagnostics;

namespace Past_Files.Services;

public class FileProcessor(FileDbContext context, IDbCache dbCache, IConcurrentLoggerService logger, ImportDbInfo? oldDBHashImportInfo = null, int saveIntervalInSeconds = 500) : IDisposable
{
    private readonly int _saveIntervalInSeconds = saveIntervalInSeconds > 0 ? saveIntervalInSeconds : 500;
    private readonly Lock _dbSaveLock = new();
    private readonly string errorFile = Environment.CurrentDirectory + @"\Scan errors.txt";

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

            bool RecordExistsInDB = TryToFindRecordByFileIdentity(fileIdentityKey, dbCache, out var fileRecord);

            if (RecordExistsInDB)
            {
                UpdateExistingRecord(filePath, fileInfo, currentTime, fileRecord!);
            }
            else
            {
                fileRecord = CreateNewFileRecordAndAddToDB(filePath, fileInfo, fileIdentityKey, currentTime);
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
            UpdateName(fileInfo, currentTime, fileRecord);
        }

        var mostRecentLocationInDB = fileRecord.Locations.MaxBy(x => x.LocationChangeNoticedTime);
        var locationDifferent = !Path.GetDirectoryName(filePath.NormalizedPath.AsSpan()).SequenceEqual(mostRecentLocationInDB.Path!.NormalizedPath.AsSpan());

        if (locationDifferent)
        {
            UpdateLocation(filePath, currentTime, fileRecord);
        }
        context.FileRecords.Update(fileRecord);
    }

    private void UpdateLocation(FilePath filePath, DateTime currentTime, FileRecord fileRecord)
    {
        var newLocation = new FileLocationsHistory
        {
            Path = Path.GetDirectoryName(filePath.NormalizedPath) ?? string.Empty,
            FileRecordId = fileRecord.FileRecordId,
            LocationChangeNoticedTime = currentTime
        };
        context.FileLocationsHistory.Add(newLocation);
        fileRecord.Locations.Add(newLocation);
    }

    private void UpdateName(FileInfo fileInfo, DateTime currentTime, FileRecord fileRecord)
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

    private FileRecord CreateNewFileRecordAndAddToDB(FilePath filePath, FileInfo fileInfo, FileIdentityKey fileIdentityKey, DateTime currentTime)
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
        dbCache.IdentityKeyToFileRecord[fileIdentityKey] = fileRecord;

        var initialNameHistory = new FileNamesHistory
        {
            FileName = fileInfo.Name,
            NameChangeNoticedTime = currentTime,
            FileRecordId = fileRecord.FileRecordId
        };
        context.FileNamesHistory.Add(initialNameHistory);
        fileRecord.NameHistory.Add(initialNameHistory);


        var newLocation = new FileLocationsHistory
        {
            Path = Path.GetDirectoryName(filePath.NormalizedPath) ?? string.Empty,
            FileRecordId = fileRecord.FileRecordId,
            LocationChangeNoticedTime = currentTime
        };
        context.FileLocationsHistory.Add(newLocation);
        fileRecord.Locations.Add(newLocation);

        return fileRecord;
    }
    private string ComputeHashOrImportFromOldDb(string filePath, FileIdentityKey fileIdentityKey)
    {
        if (oldDBHashImportInfo is not null)
        {
            if (TryToFindRecordByFileIdentity(fileIdentityKey, oldDBHashImportInfo.dataStore, out var fileRecord))
            {
                return fileRecord?.Hash ?? FileHasher.ComputeFileHash(filePath);
            }
        }
        return FileHasher.ComputeFileHash(filePath);
    }

    private static bool TryToFindRecordByFileIdentity(FileIdentityKey fileIdentityKey, IDbCache dbCache, out FileRecord? fileRecord)
    {
        return dbCache.IdentityKeyToFileRecord.TryGetValue(fileIdentityKey, out fileRecord);
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
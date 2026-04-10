using Past_Files.FileUtils;
using Past_Files.Models;
using Past_Files.Services;

namespace Past_Files.Data;

public class EntityRepository: IDisposable
{
    private readonly FileDbContext dbContext;
    private readonly DbCache dbCache;
    private readonly Metadata dbMetadata;
    private readonly ConsoleLoggerService loggerService;

    public EntityRepository(string dbPath, ConsoleLoggerService loggerService)
    {
        this.loggerService = loggerService;
        dbContext = InitializeandCreateDatabase(dbPath);
        dbCache = DbCache.CreateCache(dbContext, loggerService);
        dbMetadata = dbContext.Metadata.First();
    }

    private static FileDbContext InitializeandCreateDatabase(string dbPath)
    {
        var dbContext = new FileDbContext(dbPath);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    public void ScanStartUpdateMetadata()
    {

        dbMetadata.LastScanStartTime = DateTime.UtcNow;
        dbMetadata.LastScanCompleted = false;
        dbContext.SaveChanges();
    }

    public void ScanEndUpdateMetadata()
    {
        dbMetadata.LastScanCompleted = true;
        dbContext.SaveChanges();
    }

    public FileRecord CreateNewFileRecordAndAddToDB(ValidNormalizedFilePath filePath, FileInfo fileInfo, FileIdentityKey fileIdentityKey, string hash)
    {
        var currentTime = DateTime.UtcNow;
        FileRecord fileRecord = new()
        {
            Hash = hash,
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

        dbContext.FileRecords.Add(fileRecord);
        dbCache.IdentityKeyToFileRecord[fileIdentityKey] = fileRecord;

        RecordNewFileName(fileInfo.Name, currentTime, fileRecord);

        RecordNewFileRecordLocation(filePath, currentTime, fileRecord);

        return fileRecord;
    }

    public void UpdateName(string fileName, DateTime currentTime, FileRecord fileRecord)
    {
        fileRecord.CurrentFileName = fileName;
        RecordNewFileName(fileName, currentTime, fileRecord);
    }

    private void RecordNewFileName(string fileName, DateTime currentTime, FileRecord fileRecord)
    {
        var newNameHistoryEntry = new FileNamesHistory
        {
            FileName = fileName,
            NameChangeNoticedTime = currentTime,
            FileRecordId = fileRecord.FileRecordId
        };
        dbContext.FileNamesHistory.Add(newNameHistoryEntry);
        fileRecord.NameHistory.Add(newNameHistoryEntry);
    }

    public void RecordNewFileRecordLocation(ValidNormalizedFilePath filePath, DateTime currentTime, FileRecord fileRecord)
    {
        var newLocation = new FileLocationsHistory
        {
            Path = PathHelpers.GetDirectoryRelativeToRoot(filePath),
            FileRecordId = fileRecord.FileRecordId,
            LocationChangeNoticedTime = currentTime
        };
        dbContext.FileLocationsHistory.Add(newLocation);
        fileRecord.Locations.Add(newLocation);
    }

    public bool TryFindRecord(FileIdentityKey fileIdentityKey, out FileRecord? fileRecord)
    {
        return dbCache.IdentityKeyToFileRecord.TryGetValue(fileIdentityKey, out fileRecord);
    }

    public void SaveIfHasChanges()
    {
        try
        {
            if (dbContext.ChangeTracker.HasChanges())
            {
                dbContext.SaveChanges();
                loggerService.Log("Database changes saved.");
            }
        }
        catch (Exception ex)
        {
            loggerService.Log($"[TIMER ERROR] Failed to save changes.  Exception: {ex}");
        }
    }

    public void Dispose()
    {
        dbContext.Dispose();
    }
}
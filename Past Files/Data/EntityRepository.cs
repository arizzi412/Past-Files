using Past_Files.FileUtils;
using Past_Files.Models;
using Past_Files.Services;

namespace Past_Files.Data;

public class EntityRepository: IDisposable
{
    private string dbPath;
    private FileDbContext dbContext;
    private DbCache dbCache;
    private Metadata dbMetadata;
    private IConcurrentLoggerService loggerService;

    public EntityRepository(IConcurrentLoggerService loggerService)
    {
        this.loggerService = loggerService;
        dbPath = "filetracker.db";
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

        dbMetadata.LastScanStartTime = DateTime.Now;
        dbMetadata.LastScanCompleted = false;
        dbContext.SaveChanges();
    }

    public void ScanEndUpdateMetadata()
    {
        dbMetadata.LastScanCompleted = true;
        dbContext.SaveChanges();
    }

    public FileRecord CreateNewFileRecordAndAddToDB(FilePath filePath, FileInfo fileInfo, FileIdentityKey fileIdentityKey, DateTime currentTime)
    {
        FileRecord fileRecord = new()
        {
            Hash = FileHasher.ComputeFileHash(filePath),
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

        var initialNameHistory = new FileNamesHistory
        {
            FileName = fileInfo.Name,
            NameChangeNoticedTime = currentTime,
            FileRecordId = fileRecord.FileRecordId
        };
        dbContext.FileNamesHistory.Add(initialNameHistory);
        fileRecord.NameHistory.Add(initialNameHistory);


        var newLocation = new FileLocationsHistory
        {
            Path = Path.GetDirectoryName(filePath.NormalizedPath) ?? string.Empty,
            FileRecordId = fileRecord.FileRecordId,
            LocationChangeNoticedTime = currentTime
        };
        dbContext.FileLocationsHistory.Add(newLocation);
        fileRecord.Locations.Add(newLocation);

        return fileRecord;
    }

    public void UpdateFileRecordName(FileInfo fileInfo, DateTime currentTime, FileRecord fileRecord)
    {
        fileRecord.CurrentFileName = fileInfo.Name;

        var nameHistory = new FileNamesHistory
        {
            FileName = fileInfo.Name,
            NameChangeNoticedTime = currentTime,
            FileRecordId = fileRecord.FileRecordId
        };
        dbContext.FileNamesHistory.Add(nameHistory);
        fileRecord.NameHistory.Add(nameHistory);
    }

    public void UpdateFileRecordLocation(FilePath filePath, DateTime currentTime, FileRecord fileRecord)
    {
        var newLocation = new FileLocationsHistory
        {
            Path = Path.GetDirectoryName(filePath.NormalizedPath) ?? string.Empty,
            FileRecordId = fileRecord.FileRecordId,
            LocationChangeNoticedTime = currentTime
        };
        dbContext.FileLocationsHistory.Add(newLocation);
        fileRecord.Locations.Add(newLocation);
    }
    public void UpdateFileRecord(FileRecord fileRecord)
    {
        dbContext.FileRecords.Update(fileRecord);
    }

    public bool TryToFindRecordByFileIdentity(FileIdentityKey fileIdentityKey, out FileRecord? fileRecord)
    {
        return dbCache.IdentityKeyToFileRecord.TryGetValue(fileIdentityKey, out fileRecord);
    }


    public void SaveIfHasChanges()
    {
        if (dbContext.ChangeTracker.HasChanges())
        {
            dbContext.SaveChanges();
            loggerService.Enqueue("Database changes saved.");
        }
    }

    public void Dispose()
    {
        dbContext.Dispose();
    }
}

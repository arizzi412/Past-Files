using Microsoft.EntityFrameworkCore;
using Past_Files.Data;
using Past_Files.FileUtils;
using Past_Files.Models;
using System.Diagnostics;

namespace Past_Files.Services;

public class FileProcessor : IDisposable
{

    private readonly IDataStore _dataStore;
    private readonly DBImportInfo? OldDBImportInfo;
    private readonly FileTrackerContext _context;

    private readonly IConcurrentLoggerService _logger;
    private readonly int _saveIntervalInSeconds;
    private readonly Lock _dbSaveLock = new();
    private readonly string errorFile = Environment.CurrentDirectory + @"\errors.txt";

    public FileProcessor(FileTrackerContext context, IDataStore dataStore, IConcurrentLoggerService logger, DBImportInfo? oldDBHashImportInfo = null, int saveIntervalInSeconds = 20)
    {
        _saveIntervalInSeconds = saveIntervalInSeconds > 0 ? saveIntervalInSeconds : 20;
        _logger = logger;
        _context = context;
        _dataStore = dataStore;

        OldDBImportInfo = oldDBHashImportInfo;
    }

    private void SaveChangesCallback()
    {
        try
        {
            lock (_dbSaveLock)
            {
                if (_context.ChangeTracker.HasChanges())
                {
                    _context.SaveChanges();
                    _logger.Enqueue($"[TIMER] Auto-saved changes at {DateTime.UtcNow:u}");
                }
            }
        }
        catch (Exception ex)
        {
            string message = $"[TIMER ERROR] Failed to save changes: {ex.Message} Inner Exception: {ex.InnerException}\n";
            _logger.Enqueue(message);
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
                _logger.Enqueue($"Processing {filePath}");

                ProcessFile(filePath);

                processedCount++;
                if (stopwatch.ElapsedMilliseconds > _saveIntervalInSeconds * 1000)
                {
                    SaveChangesCallback();
                    stopwatch.Restart();
                }
            }

            _logger.Enqueue($"Scan complete. Processed {processedCount} files.");
        }
        catch (Exception ex)
        {
            string errorMessage = $"Error during scanning: {ex.Message}.  Inner exception: {ex.InnerException}\n";
            _logger.Enqueue(errorMessage);
            File.AppendAllTextAsync(errorFile, errorMessage);
        }
    }

    public void ProcessFile(Models.FilePath filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists) return;

            var (ntfsFileID, volumeSerialNumber) = FileIdentifier.GetNTFSFileID(filePath);
            FileIdentityKey fileIdentityKey = new(ntfsFileID, volumeSerialNumber);

            DateTime currentTime = DateTime.UtcNow;
            bool isNewRecord = false;

            FileRecord? fileRecord = TryToFindRecordByFileIdentity(fileIdentityKey, _dataStore);

            string? fileHash = null;
            if (fileRecord == null)
            {
                fileHash = ComputeHashOrImportFromOldDb(filePath, fileIdentityKey);

                fileRecord = GetFileRecordByHashIfExists(fileHash);

                if (FoundFileRecordByHashButDifferentIdentity(fileIdentityKey, fileRecord))
                {
                    var newIdentity = new FileIdentity
                    {
                        VolumeSerialNumber = fileIdentityKey.VolumeSerialNumber,
                        NTFSFileID = fileIdentityKey.NTFSFileID,
                        FileRecordId = fileRecord!.FileRecordId
                    };
                    _context.FileIdentities.Add(newIdentity);
                    _dataStore.IdentityMap[fileIdentityKey] = newIdentity;
                }

            }

            if (fileRecord == null)
            {
                isNewRecord = true;

                fileHash ??= FileHasher.ComputeFileHash(filePath);

                fileRecord = CreateNewFileRecordWithNameHistoryAndIdentity(fileInfo, fileIdentityKey, currentTime, fileHash);
            }
            else
            {
                UpdateFileRecordIfDifferencesInNameOrContentFound(filePath, fileInfo, currentTime, fileRecord);
            }

            UpdateFileLocationIfMoved(filePath, currentTime, fileRecord);

            if (!isNewRecord)
            {
                _context.FileRecords.Update(fileRecord);
            }
        }
        catch (Exception ex)
        {
            string errorMessage = $"Error processing file '{filePath}': {ex.Message}.  Inner exception: {ex.InnerException}\n";
            _logger.Enqueue(errorMessage);
            File.AppendAllTextAsync(errorFile, errorMessage);
        }
    }

    private void UpdateFileLocationIfMoved(Models.FilePath filePath, DateTime currentTime, FileRecord fileRecord)
    {
        var mostRecentLocationInDB = fileRecord.Locations.Count != 0 ? fileRecord.Locations.MaxBy(x => x.LocationChangeNoticedTime) : null;
        // if mostRecentLocationInDB is null then there are no locations in db
        if (mostRecentLocationInDB is null
            || !System.IO.Path.GetDirectoryName(filePath.NormalizedPath.AsSpan()).SequenceEqual(mostRecentLocationInDB.Path.NormalizedPath.AsSpan()))
        {
            var newLocation = new FileLocationsHistory
            {
                Path = System.IO.Path.GetDirectoryName(filePath) ?? string.Empty,
                FileRecordId = fileRecord.FileRecordId,
                LocationChangeNoticedTime = currentTime
            };
            _context.FileLocationsHistory.Add(newLocation);
            fileRecord.Locations.Add(newLocation);
        }
    }

    private void UpdateFileRecordIfDifferencesInNameOrContentFound(Models.FilePath filePath, FileInfo fileInfo, DateTime currentTime, FileRecord fileRecord)
    {
        fileRecord.LastSeen = currentTime;
        fileRecord.Size = fileInfo.Length;

        if (fileRecord.LastWriteTime != fileInfo.LastWriteTimeUtc || fileRecord.Size != fileInfo.Length)
        {
            string newHash = FileHasher.ComputeFileHash(filePath);
            if (fileRecord.Hash != newHash)
            {
                fileRecord.Hash = newHash;
                if (!_dataStore.HashToFileRecord.ContainsKey(newHash))
                {
                    _dataStore.HashToFileRecord[newHash] = fileRecord;
                }
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
            _context.FileNamesHistory.Add(nameHistory);
            fileRecord.NameHistories.Add(nameHistory);
        }
    }

    private FileRecord CreateNewFileRecordWithNameHistoryAndIdentity(FileInfo fileInfo, FileIdentityKey fileIdentityKey, DateTime currentTime, string fileHash)
    {
        FileRecord fileRecord = new()
        {
            Hash = fileHash,
            CurrentFileName = fileInfo.Name,
            Size = fileInfo.Length,
            LastWriteTime = fileInfo.LastWriteTimeUtc,
            FirstSeen = currentTime,
            LastSeen = currentTime,
            Locations = [],
            Identities = [],
            NameHistories = []
        };

        FileIdentity newIdentity = new()
        {
            VolumeSerialNumber = fileIdentityKey.VolumeSerialNumber,
            NTFSFileID = fileIdentityKey.NTFSFileID,
            FileRecord = fileRecord
        };

        fileRecord.Identities.Add(newIdentity);
        _context.FileRecords.Add(fileRecord);
        _context.FileIdentities.Add(newIdentity);
        _dataStore.IdentityMap[fileIdentityKey] = newIdentity;

        if (!_dataStore.HashToFileRecord.ContainsKey(fileHash))
        {
            _dataStore.HashToFileRecord[fileHash] = fileRecord;
        }

        var initialNameHistory = new FileNamesHistory
        {
            FileName = fileInfo.Name,
            NameChangeNoticedTime = currentTime,
            FileRecordId = fileRecord.FileRecordId
        };
        _context.FileNamesHistory.Add(initialNameHistory);
        fileRecord.NameHistories.Add(initialNameHistory);
        return fileRecord;
    }

    private FileRecord? GetFileRecordByHashIfExists(string fileHash)
    {
        if (_dataStore.HashToFileRecord.TryGetValue(fileHash, out var existingRecord))
        {
            return _context.FileRecords
                .Include(f => f.Locations)
                .Include(f => f.Identities)
                .Include(f => f.NameHistories)
                .FirstOrDefault(f => f.FileRecordId == existingRecord.FileRecordId);
        }
        return null;
    }

    private bool FoundFileRecordByHashButDifferentIdentity(FileIdentityKey fileIdentityKey, FileRecord? fileRecord)
    {
        return fileRecord is not null && !_dataStore.IdentityMap.ContainsKey(fileIdentityKey);
    }

    private string ComputeHashOrImportFromOldDb(Models.FilePath filePath, FileIdentityKey fileIdentityKey)
    {
        if (OldDBImportInfo is not null)
        {
            return TryToFindRecordByFileIdentity(fileIdentityKey, OldDBImportInfo.dataStore)?.Hash ?? FileHasher.ComputeFileHash(filePath);
        }

        return FileHasher.ComputeFileHash(filePath);
    }

    private static FileRecord? TryToFindRecordByFileIdentity(FileIdentityKey fileIdentityKey, IDataStore dataStore)
    {
        dataStore.IdentityMap.TryGetValue(fileIdentityKey, out var identity);
        return identity?.FileRecord;
    }

    public void Dispose()
    {
        _logger.Dispose();

        lock (_dbSaveLock)
        {
            if (_context.ChangeTracker.HasChanges())
            {
                _context.SaveChanges();
            }
        }
        _context.Dispose();
    }
}

public record struct FileIdentityKey(ulong NTFSFileID, uint VolumeSerialNumber);
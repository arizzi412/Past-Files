using Microsoft.EntityFrameworkCore;
using Past_Files.Data;
using Past_Files.FileUtils;
using Past_Files.Models;
using Serilog;

namespace Past_Files.Services;

public class FileProcessor : IDisposable
{

    private readonly DataStore _dataStore;
    private readonly DBImportInfo? OldDBImportInfo;
    private readonly FileTrackerContext _context;

    private readonly ConsoleLoggerService _consoleLogger;
    private readonly int _saveIntervalInSeconds;
    private readonly Timer _autoSaveTimer;
    private readonly Lock _dbSaveLock = new();

    public FileProcessor(FileTrackerContext context, DBImportInfo? oldDBHashImportInfo = null, int saveIntervalInSeconds = 20, ConsoleLoggerService? logger = null)
    {
        _saveIntervalInSeconds = saveIntervalInSeconds > 0 ? saveIntervalInSeconds : 20;
        _consoleLogger = logger ?? new ConsoleLoggerService();

        _autoSaveTimer = new Timer(SaveChangesCallback, null,
            TimeSpan.FromSeconds(_saveIntervalInSeconds),
            TimeSpan.FromSeconds(_saveIntervalInSeconds));


        _context = context;
        _dataStore = new DataStore(_consoleLogger);

        OldDBImportInfo = oldDBHashImportInfo;

    }

    private void SaveChangesCallback(object? state)
    {
        try
        {
            lock (_dbSaveLock)
            {
                if (_context.ChangeTracker.HasChanges())
                {
                    _context.SaveChanges();
                    _consoleLogger.Enqueue($"[TIMER] Auto-saved changes at {DateTime.UtcNow:u}");
                }
            }
        }
        catch (Exception ex)
        {
            _consoleLogger.Enqueue($"[TIMER ERROR] Failed to save changes: {ex.Message}");
        }
    }

    public void ScanDirectory(string directoryPath)
    {
        try
        {
            Models.Path[] allFiles = Directory.EnumerateFiles(directoryPath, "*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true
            }).Select(x => new Models.Path(x)).ToArray();

            _consoleLogger.Enqueue($"Total files to process: {allFiles.Length}");

            _dataStore.LoadRecords(_context);

            int processedCount = 0;
            foreach (var filePath in allFiles)
            {
                _consoleLogger.Enqueue($"Processing {filePath}");
                ProcessFile(filePath);

                processedCount++;
            }

            lock (_dbSaveLock)
            {
                _context.SaveChanges();
            }

            _consoleLogger.Enqueue($"Scan complete. Processed {processedCount} files.");
        }
        catch (Exception ex)
        {
            _consoleLogger.Enqueue($"Error scanning directory '{directoryPath}': {ex.Message}");
            Log.Error(ex, $"Error scanning directory '{directoryPath}'");
        }
    }

    public void ProcessFile(Models.Path filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists) return;

            var (ntfsFileID, volumeSerialNumber) = FileIdentifier.GetNTFSFileID(filePath);
            FileIdentityKey fileIdentityKey = new (ntfsFileID, volumeSerialNumber);

            DateTime currentTime = DateTime.UtcNow;
            bool isNewRecord = false;

            FileRecord? fileRecord = TryToFindRecordByFileIdentity(fileIdentityKey, _dataStore);

            string? fileHash = null;
            if (fileRecord == null)
            {
                fileHash = ComputeHashOrImportFromOldDb(filePath, fileIdentityKey);

                if (_dataStore.HashMap.TryGetValue(fileHash, out var existingRecord))
                {
                    fileRecord = _context.FileRecords
                        .Include(f => f.Locations)
                        .Include(f => f.Identities)
                        .Include(f => f.NameHistories)
                        .FirstOrDefault(f => f.FileRecordId == existingRecord.FileRecordId);

                    if (FileHasSameHashInDbButDifferentIdentity(fileIdentityKey, fileRecord))
                    {
                        var newIdentity = new FileIdentity
                        {
                            VolumeSerialNumber = fileIdentityKey.VolumeSerialNumber,
                            NTFSFileID = fileIdentityKey.NTFSFileID,
                            FileRecordId = fileRecord.FileRecordId
                        };
                        _context.FileIdentities.Add(newIdentity);
                        _dataStore.IdentityMap[fileIdentityKey] = newIdentity;
                    }
                }
            }

            if (fileRecord == null)
            {
                fileHash ??= FileHasher.ComputeFileHash(filePath);

                fileRecord = new FileRecord
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

                var newIdentity = new FileIdentity
                {
                    VolumeSerialNumber = fileIdentityKey.VolumeSerialNumber,
                    NTFSFileID = fileIdentityKey.NTFSFileID,
                    FileRecord = fileRecord
                };

                fileRecord.Identities.Add(newIdentity);
                _context.FileRecords.Add(fileRecord);
                _context.FileIdentities.Add(newIdentity);
                _dataStore.IdentityMap[fileIdentityKey] = newIdentity;

                if (!_dataStore.HashMap.ContainsKey(fileHash))
                {
                    _dataStore.HashMap[fileHash] = fileRecord;
                }

                isNewRecord = true;

                var initialNameHistory = new FileNamesHistory
                {
                    FileName = fileInfo.Name,
                    NameChangeNoticedTime = currentTime,
                    FileRecordId = fileRecord.FileRecordId
                };
                _context.FileNamesHistory.Add(initialNameHistory);
                fileRecord.NameHistories.Add(initialNameHistory);
            }
            else
            {
                fileRecord.LastSeen = currentTime;
                fileRecord.Size = fileInfo.Length;

                if (fileRecord.LastWriteTime != fileInfo.LastWriteTimeUtc || fileRecord.Size != fileInfo.Length)
                {
                    string newHash = FileHasher.ComputeFileHash(filePath);
                    if (fileRecord.Hash != newHash)
                    {
                        fileRecord.Hash = newHash;
                        if (!_dataStore.HashMap.ContainsKey(newHash))
                        {
                            _dataStore.HashMap[newHash] = fileRecord;
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

            if (!fileRecord.Locations.Any(l => l.Path.Equals(filePath)))
            {
                var newLocation = new FileLocationsHistory
                {
                    Path = filePath,
                    FileRecordId = fileRecord.FileRecordId,
                    LocationChangeNoticedTime = currentTime
                };
                _context.FileLocationsHistory.Add(newLocation);
                fileRecord.Locations.Add(newLocation);
            }

            if (!isNewRecord)
            {
                _context.FileRecords.Update(fileRecord);
            }
        }
        catch (Exception ex)
        {
            _consoleLogger.Enqueue($"Error processing file '{filePath}': {ex.Message}");
            Log.Error(ex, $"Error processing file '{filePath}'");
        }
    }

    private bool FileHasSameHashInDbButDifferentIdentity(FileIdentityKey fileIdentityKey, FileRecord? fileRecord)
    {
        return fileRecord != null && !_dataStore.IdentityMap.ContainsKey(fileIdentityKey);
    }

    private string ComputeHashOrImportFromOldDb(Models.Path filePath, FileIdentityKey fileIdentityKey)
    {
        if (OldDBImportInfo is not null)
        {
            return TryToFindRecordByFileIdentity(fileIdentityKey, OldDBImportInfo.dataStore)?.Hash ?? FileHasher.ComputeFileHash(filePath);
        }

        return FileHasher.ComputeFileHash(filePath);
    }

    private static FileRecord? TryToFindRecordByFileIdentity(FileIdentityKey fileIdentityKey, DataStore dataStore)
    {
        if (dataStore.IdentityMap.TryGetValue(fileIdentityKey, out var identity))
        {
            return identity.FileRecord;
        }

        return null;
    }

    public void Dispose()
    {
        _autoSaveTimer.Dispose();
        _consoleLogger.Dispose();

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
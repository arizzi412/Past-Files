using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Past_Files.Data;
using Past_Files.FileUtils;
using Past_Files.Models;
using Serilog;

namespace Past_Files.Services
{
    public class FileProcessor : IDisposable
    {
        private readonly FileTrackerContext _context;
        private Dictionary<(uint VolumeSerialNumber, ulong FileID), FileIdentity> _identityMap = [];
        private Dictionary<string, FileRecord> _hashMap = [];

        private readonly ConsoleLoggerService _consoleLogger;
        private readonly int _saveIntervalInSeconds;
        private readonly Timer _autoSaveTimer;
        private readonly Lock _dbSaveLock = new();

        public FileProcessor(int saveIntervalInSeconds = 20, ConsoleLoggerService? logger = null)
        {
            _context = new FileTrackerContext();
            _context.Database.EnsureCreated();

            _saveIntervalInSeconds = saveIntervalInSeconds > 0 ? saveIntervalInSeconds : 20;
            _consoleLogger = logger ?? new ConsoleLoggerService();

            _autoSaveTimer = new Timer(SaveChangesCallback, null,
                TimeSpan.FromSeconds(_saveIntervalInSeconds),
                TimeSpan.FromSeconds(_saveIntervalInSeconds));
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

        private void LoadRecordsIntoMemory()
        {
            _consoleLogger.Enqueue("Loading database into memory");
            try
            {
                var identities = _context.FileIdentities
                    .Include(i => i.FileRecord)
                    .AsNoTracking()
                    .ToList();

                _identityMap = identities.ToDictionary(
                    i => (i.VolumeSerialNumber, i.NTFSFileID),
                    i => i
                );

                var fileRecords = _context.FileRecords
                    .AsNoTracking()
                    .Include(f => f.Locations)
                    .Include(f => f.Identities)
                    .Include(f => f.NameHistories)
                    .Where(f => !string.IsNullOrEmpty(f.Hash))
                    .AsSplitQuery()
                    .ToList();

                _hashMap = fileRecords.ToDictionary(f => f.Hash, f => f);
            }
            catch (Exception ex)
            {
                _consoleLogger.Enqueue($"Error loading records into memory: {ex.Message}");
                Log.Error(ex, "Error loading records into memory");
            }
        }

        public void ScanDirectory(string directoryPath)
        {
            try
            {
                var allFiles = Directory.GetFiles(directoryPath, "*", new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true
                });

                _consoleLogger.Enqueue($"Total files to process: {allFiles.Length}");

                LoadRecordsIntoMemory();

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

        public void ProcessFile(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists) return;

                var (ntfsFileID, volumeSerialNumber) = FileIdentifier.GetNTFSFileID(filePath);
                var fileIdentityKey = (VolumeSerialNumber: volumeSerialNumber, NTFSFileID: ntfsFileID);

                DateTime currentTime = DateTime.UtcNow;
                FileRecord? fileRecord = null;
                bool isNewRecord = false;

                if (_identityMap.TryGetValue(fileIdentityKey, out var identity))
                {
                    fileRecord = identity.FileRecord;
                }

                string? fileHash = null;
                if (fileRecord == null)
                {
                    fileHash = FileHasher.ComputeFileHash(filePath);

                    if (_hashMap.TryGetValue(fileHash, out var existingRecord))
                    {
                        fileRecord = _context.FileRecords
                            .Include(f => f.Locations)
                            .Include(f => f.Identities)
                            .Include(f => f.NameHistories)
                            .FirstOrDefault(f => f.FileRecordId == existingRecord.FileRecordId);

                        if (fileRecord != null && !_identityMap.ContainsKey(fileIdentityKey))
                        {
                            var newIdentity = new FileIdentity
                            {
                                VolumeSerialNumber = fileIdentityKey.VolumeSerialNumber,
                                NTFSFileID = fileIdentityKey.NTFSFileID,
                                FileRecordId = fileRecord.FileRecordId
                            };
                            _context.FileIdentities.Add(newIdentity);
                            _identityMap[fileIdentityKey] = newIdentity;
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
                    _identityMap[fileIdentityKey] = newIdentity;

                    if (!_hashMap.ContainsKey(fileHash))
                    {
                        _hashMap[fileHash] = fileRecord;
                    }

                    isNewRecord = true;

                    var initialNameHistory = new FileNameHistory
                    {
                        FileName = fileInfo.Name,
                        NameChangeNoticedTime = currentTime,
                        FileRecordId = fileRecord.FileRecordId
                    };
                    _context.FileNameHistories.Add(initialNameHistory);
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
                            if (!_hashMap.ContainsKey(newHash))
                            {
                                _hashMap[newHash] = fileRecord;
                            }
                        }
                        fileRecord.LastWriteTime = fileInfo.LastWriteTimeUtc;
                    }

                    if (!fileRecord.CurrentFileName.Equals(fileInfo.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        fileRecord.CurrentFileName = fileInfo.Name;

                        var nameHistory = new FileNameHistory
                        {
                            FileName = fileInfo.Name,
                            NameChangeNoticedTime = currentTime,
                            FileRecordId = fileRecord.FileRecordId
                        };
                        _context.FileNameHistories.Add(nameHistory);
                        fileRecord.NameHistories.Add(nameHistory);
                    }
                }

                if (!fileRecord.Locations.Any(l => l.Path.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                {
                    var newLocation = new FileLocationHistory
                    {
                        Path = filePath,
                        FileRecordId = fileRecord.FileRecordId,
                        LocationChangeNoticedTime = currentTime
                    };
                    _context.FileLocations.Add(newLocation);
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
}

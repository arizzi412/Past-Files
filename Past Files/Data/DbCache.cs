using Microsoft.EntityFrameworkCore;
using Past_Files.Models;
using Past_Files.Services;
using System.Collections.Concurrent;

namespace Past_Files.Data
{
    public class DbCache
    {
        private readonly ConsoleLoggerService _consoleLogger;

        public ConcurrentDictionary<FileIdentityKey, FileRecord> IdentityKeyToFileRecord { get; private set; }

        public static DbCache CreateCache(FileDbContext fileTrackerContext, ConsoleLoggerService consoleLoggerService)
        {
            var ds = new DbCache(consoleLoggerService);
            ds.LoadDbRecords(fileTrackerContext);
            return ds;
        }

        private DbCache(ConsoleLoggerService consoleLoggerService)
        {
            _consoleLogger = consoleLoggerService;
            IdentityKeyToFileRecord = new ConcurrentDictionary<FileIdentityKey, FileRecord>();
        }

        private void LoadDbRecords(FileDbContext context)
        {
            _consoleLogger.Log("Loading database into memory");
            try
            {
                var fileRecords = context.FileRecords
                    .Include(f => f.Locations)
                    .Include(f => f.NameHistory)
                    .Where(f => !string.IsNullOrEmpty(f.Hash))
                    .AsSplitQuery()
                    .ToList();

                var identityKeyToFileRecordKVPs = fileRecords.Select(fileRecord =>
                    new KeyValuePair<FileIdentityKey, FileRecord>(
                        new FileIdentityKey(fileRecord.NTFSFileID, fileRecord.VolumeSerialNumber),
                        fileRecord));

                IdentityKeyToFileRecord = new ConcurrentDictionary<FileIdentityKey, FileRecord>(identityKeyToFileRecordKVPs);
            }
            catch (Exception ex)
            {
                _consoleLogger.Log($"Error loading records into memory: {ex.Message}");
            }
        }
    }
}

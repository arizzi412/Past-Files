using Microsoft.EntityFrameworkCore;
using Past_Files.Models;
using Past_Files.Services;
using System.Collections.Concurrent;

namespace Past_Files.Data
{
    public class DbCache
    {
        private readonly ConsoleLoggerService _consoleLogger;

        public IDictionary<FileIdentityKey, FileRecord> IdentityKeyToFileRecord { get; private set; }

        public static DbCache CreateCache(FileDbContext fileTrackerContext, ConsoleLoggerService consoleLoggerService)
        {
            var cache = new DbCache(consoleLoggerService);
            cache.LoadDbRecords(fileTrackerContext);
            return cache;
        }

        private DbCache(ConsoleLoggerService consoleLoggerService)
        {
            _consoleLogger = consoleLoggerService;
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

                IdentityKeyToFileRecord = fileRecords.ToDictionary(
                    fr => new FileIdentityKey(fr.NTFSFileID, fr.VolumeSerialNumber),
                    fr => fr);
            }
            catch (Exception ex)
            {
                _consoleLogger.Log($"Error loading records into memory: {ex.Message}");
            }
        }
    }
}

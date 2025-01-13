using Microsoft.EntityFrameworkCore;
using Past_Files.Data;
using Past_Files.Models;
using Past_Files.Services;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Past_Files
{
    public class DataStore : IDataStore
    {
        private readonly IConcurrentLoggerService _consoleLogger;

        public ConcurrentDictionary<FileIdentityKey, FileRecord> IdentityKeyToFileRecord { get; private set; }

        public static DataStore CreateDataStore(FileTrackerContext fileTrackerContext, IConcurrentLoggerService consoleLoggerService)
        {
            var ds = new DataStore(consoleLoggerService);
            ds.LoadRecords(fileTrackerContext);
            return ds;
        }

        private DataStore(IConcurrentLoggerService consoleLoggerService)
        {
            _consoleLogger = consoleLoggerService;
            IdentityKeyToFileRecord = new ConcurrentDictionary<FileIdentityKey, FileRecord>();
        }

        private void LoadRecords(FileTrackerContext context)
        {
            _consoleLogger.Enqueue("Loading database into memory");
            try
            {
                var fileRecords = context.FileRecords
                    .AsNoTracking()
                    .Include(f => f.Locations)
                    .Include(f => f.NameHistory)
                    .Where(f => !string.IsNullOrEmpty(f.Hash))
                    .AsSplitQuery()
                    .ToList();

                var identityKeyToFileRecordKVPs = fileRecords.Select(fileRecord =>
                    new KeyValuePair<FileIdentityKey, FileRecord>(
                        new FileIdentityKey(fileRecord.NTFSFileID, fileRecord.VolumeSerialNumber),
                        fileRecord));

                var distinct = identityKeyToFileRecordKVPs.DistinctBy(x => x.Key.NTFSFileID).ToList();

                var except = identityKeyToFileRecordKVPs.Except(distinct).ToList();

                IdentityKeyToFileRecord = new ConcurrentDictionary<FileIdentityKey, FileRecord>(identityKeyToFileRecordKVPs);
            }
            catch (Exception ex)
            {
                _consoleLogger.Enqueue($"Error loading records into memory: {ex.Message}");
                Log.Error(ex, "Error loading records into memory");
            }
        }
    }
}

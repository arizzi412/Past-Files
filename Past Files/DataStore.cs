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

        public ConcurrentDictionary<FileIdentityKey, FileIdentity> IdentityMap { get; private set; }
        public ConcurrentDictionary<string, FileRecord> HashToFileRecord { get; private set; }

        public static DataStore CreateDataStore(FileTrackerContext fileTrackerContext, IConcurrentLoggerService consoleLoggerService)
        {
            var ds = new DataStore(consoleLoggerService);
            ds.LoadRecords(fileTrackerContext);
            return ds;
        }


        private DataStore(IConcurrentLoggerService consoleLoggerService)
        {
            _consoleLogger = consoleLoggerService;
            IdentityMap = new ConcurrentDictionary<FileIdentityKey, FileIdentity>();
            HashToFileRecord = new ConcurrentDictionary<string, FileRecord>();
        }

        private void LoadRecords(FileTrackerContext context)
        {
            _consoleLogger.Enqueue("Loading database into memory");
            try
            {
                // Load and populate IdentityMap
                var identities = context.FileIdentities
                    .Include(i => i.FileRecord)
                    .AsNoTracking()
                    .ToList();

                IdentityMap = new ConcurrentDictionary<FileIdentityKey, FileIdentity>(
                    identities.Select(i => new KeyValuePair<FileIdentityKey, FileIdentity>(
                        new FileIdentityKey(i.NTFSFileID, i.VolumeSerialNumber),
                        i
                    ))
                );

                // Load and populate HashToFileRecord
                var fileRecords = context.FileRecords
                    .AsNoTracking()
                    .Include(f => f.Locations)
                    .Include(f => f.Identities)
                    .Include(f => f.NameHistories)
                    .Where(f => !string.IsNullOrEmpty(f.Hash))
                    .AsSplitQuery()
                    .ToList();

                HashToFileRecord = new ConcurrentDictionary<string, FileRecord>(
                    fileRecords.Select(f => new KeyValuePair<string, FileRecord>(f.Hash, f))
                );
            }
            catch (Exception ex)
            {
                _consoleLogger.Enqueue($"Error loading records into memory: {ex.Message}");
                Log.Error(ex, "Error loading records into memory");
            }
        }
    }
}

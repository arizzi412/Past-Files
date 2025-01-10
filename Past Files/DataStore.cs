using Microsoft.EntityFrameworkCore;
using Past_Files.Data;
using Past_Files.Models;
using Past_Files.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Past_Files
{
    public class DataStore(ConsoleLoggerService consoleLoggerService)
    {
        private readonly ConsoleLoggerService _consoleLogger = consoleLoggerService;


        public Dictionary<FileIdentityKey, FileIdentity> IdentityMap { get; set; } = [];
        public Dictionary<string, FileRecord> HashMap { get; set; } = [];


        public void LoadRecords(FileTrackerContext _context)
        {
            _consoleLogger.Enqueue("Loading database into memory");
            try
            {
                var identities = _context.FileIdentities
                    .Include(i => i.FileRecord)
                    .AsNoTracking()
                    .ToList();

                IdentityMap = identities.ToDictionary(
                    i => new FileIdentityKey(i.NTFSFileID, i.VolumeSerialNumber),
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

                HashMap = fileRecords.ToDictionary(f => f.Hash, f => f);
            }
            catch (Exception ex)
            {
                _consoleLogger.Enqueue($"Error loading records into memory: {ex.Message}");
                Log.Error(ex, "Error loading records into memory");
            }
        }
    }

           
    }

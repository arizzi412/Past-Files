// Past Files/DbCache.cs
using Microsoft.EntityFrameworkCore;
using Past_Files.Data;
using Past_Files.Models;
using Past_Files.Services; // For FileIdentityKey
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Past_Files
{
    public class DbCache : IDbCache
    {
        private readonly IConcurrentLoggerService _consoleLogger;

        public ConcurrentDictionary<FileIdentityKey, FileInstance> IdentityKeyToFileInstanceCache { get; private set; }
        public ConcurrentDictionary<string, FileContent> HashToFileContentCache { get; private set; }

        public static DbCache CreateCache(FileDbContext fileTrackerContext, IConcurrentLoggerService consoleLoggerService)
        {
            var ds = new DbCache(consoleLoggerService);
            ds.LoadDbRecords(fileTrackerContext);
            return ds;
        }

        private DbCache(IConcurrentLoggerService consoleLoggerService)
        {
            _consoleLogger = consoleLoggerService;
            IdentityKeyToFileInstanceCache = new ConcurrentDictionary<FileIdentityKey, FileInstance>();
            HashToFileContentCache = new ConcurrentDictionary<string, FileContent>();
        }

        private void LoadDbRecords(FileDbContext context)
        {
            _consoleLogger.Enqueue("Loading database into memory...");
            try
            {
                _consoleLogger.Enqueue("Loading FileContents...");
                var fileContents = context.FileContents
                    .AsNoTracking()
                    .ToList();
                HashToFileContentCache = new ConcurrentDictionary<string, FileContent>(
                    fileContents.ToDictionary(fc => fc.Hash, fc => fc)
                );
                _consoleLogger.Enqueue($"Loaded {HashToFileContentCache.Count} FileContents into cache.");

                _consoleLogger.Enqueue("Loading FileInstances...");
                var fileInstances = context.FileInstances
                    .AsNoTracking()
                    .Include(fi => fi.FileContent) // Eager load FileContent
                    .Include(fi => fi.LocationHistory)
                    .Include(fi => fi.NameHistory)
                    .AsSplitQuery()
                    .ToList();

                IdentityKeyToFileInstanceCache = new ConcurrentDictionary<FileIdentityKey, FileInstance>(
                    fileInstances.Select(fi => new KeyValuePair<FileIdentityKey, FileInstance>(
                        new FileIdentityKey(fi.NTFSFileID, fi.VolumeSerialNumber),
                        fi
                    ))
                );
                _consoleLogger.Enqueue($"Loaded {IdentityKeyToFileInstanceCache.Count} FileInstances into cache.");
            }
            catch (Exception ex)
            {
                _consoleLogger.Enqueue($"Error loading records into memory: {ex.Message} {ex.InnerException}");
                Log.Error(ex, "Error loading records into memory");
            }
            _consoleLogger.Enqueue("Finished loading database into memory.");
        }

        public void AddOrUpdateInstance(FileIdentityKey key, FileInstance instance)
        {
            IdentityKeyToFileInstanceCache[key] = instance;
        }

        public void AddOrUpdateContent(string hash, FileContent content)
        {
            HashToFileContentCache[hash] = content;
        }
    }
}
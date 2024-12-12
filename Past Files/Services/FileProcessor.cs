// Services/FileProcessor.cs
using Microsoft.EntityFrameworkCore;
using Past_Files.Data;
using Past_Files.FileUtils;
using Past_Files.Models;
using Serilog;

namespace Past_Files.Services;

public class FileProcessor
{
    private readonly FileTrackerContext _context;
    private Dictionary<(uint VolumeSerialNumber, ulong FileID), FileIdentity> _identityMap = [];
    private Dictionary<string, FileRecord> _hashMap = [];

    public FileProcessor()
    {
        _context = new FileTrackerContext();
        _context.Database.EnsureCreated();
    }

    /// <summary>
    /// Loads existing FileIdentities and FileRecords into memory for quick access.
    /// </summary>
    private void LoadRecordsIntoMemory()
    {
        try
        {
            // Load FileIdentities into a dictionary
            var identities = _context.FileIdentities
                .Include(i => i.FileRecord)
                .AsNoTracking()
                .ToList();

            _identityMap = identities.ToDictionary(
                i => (i.VolumeSerialNumber, i.NTFSFileID),
                i => i
            );

            // Load FileRecords with their Hash into a dictionary for quick hash lookup
            var fileRecords = _context.FileRecords
                .Include(f => f.Locations)
                .Include(f => f.Identities)
                .Include(f => f.NameHistories)
                .AsNoTracking()
                .Where(f => !string.IsNullOrEmpty(f.Hash))
                .ToList();

            _hashMap = fileRecords.ToDictionary(f => f.Hash, f => f);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading records into memory: {ex.Message}");
            Log.Error(ex, "Error loading records into memory");
        }
    }

    /// <summary>
    /// Scans the specified directory and processes all files within it.
    /// </summary>
    /// <param name="directoryPath">The root directory to scan.</param>
    public void ScanDirectory(string directoryPath)
    {
        try
        {
            var allFiles = Directory.GetFiles(directoryPath, "*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true
            });

            Console.WriteLine($"Total files to process: {allFiles.Length}");

            LoadRecordsIntoMemory();
            int counter = 0;
            foreach (var filePath in allFiles)
            {
                if (counter >10) _context.SaveChanges();
                Console.WriteLine($"Processing {filePath}");
                ProcessFile(filePath);
                counter++;
            }

            // After processing all files, save changes to the database
            _context.SaveChanges();

            // Optionally, mark deleted files
            MarkDeletedFiles(DateTime.Now);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error scanning directory '{directoryPath}': {ex.Message}");
            Log.Error(ex, $"Error scanning directory '{directoryPath}'");
        }
    }

    /// <summary>
    /// Processes a single file: identifies it, updates records, and tracks history.
    /// </summary>
    /// <param name="filePath">The full path of the file to process.</param>
    public void ProcessFile(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
            {
                // File might have been deleted during scanning
                return;
            }

            // Attempt to retrieve FileID and VolumeSerialNumber
            (var ntfsFileID, var volumeSerialNumber) = FileIdentifier.GetNTFSFileID(filePath);
            var fileIdentityKey = (VolumeSerialNumber: volumeSerialNumber, NTFSFileID: ntfsFileID);

            DateTime currentTime = DateTime.Now;
            FileRecord? fileRecord = null;
            bool isNewRecord = false;

            // Step 1: Try lookup by FileIdentity
            if (_identityMap.TryGetValue(fileIdentityKey, out var identity))
            {
                fileRecord = identity.FileRecord;
            }

            // Step 2: If no match by identity, try lookup by hash
            string? fileHash = null;
            if (fileRecord == null)
            {
                // Compute hash only if necessary
                fileHash = FileHasher.ComputeFileHash(filePath);

                if (_hashMap.TryGetValue(fileHash, out var existingRecord))
                {
                    // Retrieve the tracked FileRecord
                    fileRecord = _context.FileRecords
                        .Include(f => f.Locations)
                        .Include(f => f.Identities)
                        .Include(f => f.NameHistories)
                        .FirstOrDefault(f => f.FileRecordId == existingRecord.FileRecordId);

                    if (fileRecord != null)
                    {
                        // Create and add new FileIdentity
                        if (!_identityMap.ContainsKey(fileIdentityKey))
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
            }

            // Step 3: If still no record found, create a new one
            if (fileRecord == null)
            {
                fileRecord = new FileRecord
                {
                    Hash = fileHash ??= FileHasher.ComputeFileHash(filePath),
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

                // Add initial FileNameHistory entry
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
                // Update existing record
                fileRecord.LastSeen = currentTime;
                fileRecord.Size = fileInfo.Length;

                // Detect content changes
                if (fileRecord.LastWriteTime != fileInfo.LastWriteTimeUtc || fileRecord.Size != fileInfo.Length)
                {
                    // Potential content change: re-hash
                    string newHash = FileHasher.ComputeFileHash(filePath);
                    if (fileRecord.Hash != newHash)
                    {
                        fileRecord.Hash = newHash;

                        // Update hash map
                        if (!_hashMap.ContainsKey(newHash))
                        {
                            _hashMap[newHash] = fileRecord;
                        }
                    }

                    fileRecord.LastWriteTime = fileInfo.LastWriteTimeUtc;
                }

                // Check if the filename has changed
                if (!fileRecord.CurrentFileName.Equals(fileInfo.Name, StringComparison.OrdinalIgnoreCase))
                {
                    // Update the current filename
                    fileRecord.CurrentFileName = fileInfo.Name;

                    // Add to FileNameHistory with timestamp
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

            // Step 4: Update FileLocation
            if (!_context.FileLocations.Any(l => l.Path == filePath && l.FileRecordId == fileRecord.FileRecordId))
            {
                var newLocation = new FileLocation
                {
                    Path = filePath,
                    FileRecordId = fileRecord.FileRecordId,
                    LocationChangeNoticedTime = currentTime
                };
                _context.FileLocations.Add(newLocation);
                fileRecord.Locations.Add(newLocation);
            }

            // No additional step needed for FileNameHistory since it's handled above

            // Mark the record as modified only if it's an existing record
            if (!isNewRecord)
            {
                _context.FileRecords.Update(fileRecord);
            }
            // For new records, EF Core already tracks them as Added, no need to call Update
        }
        catch
        {

        }
    }
    /// <summary>
    /// Marks files that were not seen in the latest scan as possibly deleted or moved.
    /// </summary>
    /// <param name="scanTime">The time when the scan started.</param>
    public void MarkDeletedFiles(DateTime scanTime)
    {
        try
        {
            var possiblyDeletedFiles = _context.FileRecords
                .Include(f => f.Locations)
                .Where(f => f.LastSeen < scanTime)
                .ToList();

            foreach (var file in possiblyDeletedFiles)
            {
                Console.WriteLine($"File possibly deleted or moved: {file.CurrentFileName}");
                foreach (var location in file.Locations.Where(l => l.Path != null))
                {
                    Console.WriteLine($"\tPath: {location.Path}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error marking deleted files: {ex.Message}");
            Log.Error(ex, "Error marking deleted files");
        }
    }

    /// <summary>
    /// Checks if a file has existed before based on its content.
    /// </summary>
    /// <param name="filePath">The path of the file to check.</param>
    /// <returns>True if the file has existed before; otherwise, false.</returns>
    public bool HasFileExistedBefore(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"File '{filePath}' does not exist.");
                return false;
            }

            string fileHash = FileHasher.ComputeFileHash(filePath);
            return _context.FileRecords.Any(f => f.Hash == fileHash);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking file '{filePath}': {ex.Message}");
            Log.Error(ex, $"Error checking file '{filePath}'");
            return false;
        }
    }

    /// <summary>
    /// Retrieves and displays information about a file based on its name.
    /// </summary>
    /// <param name="fileName">The name of the file to retrieve information for.</param>
    public void GetFileInformation(string fileName)
    {
        try
        {
            var files = _context.FileRecords
                .Include(f => f.Locations)
                .Include(f => f.Identities)
                .Include(f => f.NameHistories)
                .Where(f => f.CurrentFileName.Equals(fileName, StringComparison.OrdinalIgnoreCase) ||
                            f.NameHistories.Any(n => n.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (files.Count == 0)
            {
                Console.WriteLine($"No records found for file name: {fileName}");
                return;
            }

            foreach (var file in files)
            {
                Console.WriteLine($"--- File Record ID: {file.FileRecordId} ---");
                Console.WriteLine($"Current File Name: {file.CurrentFileName}");
                Console.WriteLine($"Size: {file.Size} bytes");
                Console.WriteLine($"First Seen: {file.FirstSeen}");
                Console.WriteLine($"Last Seen: {file.LastSeen}");
                Console.WriteLine($"Hash: {file.Hash}");
                Console.WriteLine($"Last Write Time: {file.LastWriteTime}");

                Console.WriteLine("Locations:");
                foreach (var location in file.Locations)
                {
                    Console.WriteLine($"\t{location.Path}");
                }

                Console.WriteLine("Filename History:");
                foreach (var nameHistory in file.NameHistories.OrderBy(n => n.NameChangeNoticedTime))
                {
                    Console.WriteLine($"\t{nameHistory.FileName} (Change Noticed Time: {nameHistory.NameChangeNoticedTime})");
                }

                Console.WriteLine("Identities:");
                foreach (var identity in file.Identities)
                {
                    Console.WriteLine($"\tVolume Serial Number: {identity.VolumeSerialNumber}, File ID: {identity.NTFSFileID}");
                }

                Console.WriteLine();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving information for file '{fileName}': {ex.Message}");
            Log.Error(ex, $"Error retrieving information for file '{fileName}'");
        }
    }
}


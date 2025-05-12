// Past Files/Services/FileProcessor.cs
using Past_Files.Data;
using Past_Files.FileUtils;
using Past_Files.Models;
using System;
using System.Diagnostics;
using System.IO; // Required for Path methods
using System.Linq; // Required for LINQ methods like MaxBy
using Microsoft.EntityFrameworkCore; // Required for EF Core operations

namespace Past_Files.Services;

public class FileProcessor(FileDbContext context, IDbCache dbCache, IConcurrentLoggerService logger, int saveIntervalInSeconds = 500) : IDisposable
{
    private readonly int _saveIntervalInSeconds = saveIntervalInSeconds > 0 ? saveIntervalInSeconds : 500;
    private readonly Lock _dbSaveLock = new();
    private readonly string errorFile = Environment.CurrentDirectory + @"\Scan errors.txt";

    private void SaveChangesCallback()
    {
        try
        {
            lock (_dbSaveLock)
            {
                if (context.ChangeTracker.HasChanges())
                {
                    context.SaveChanges();
                   // dbCache.LoadDbRecords(context); // Reload cache after saving
                    logger.Enqueue($"[TIMER] Auto-saved changes and reloaded cache at {DateTime.UtcNow:u}");
                }
            }
        }
        catch (Exception ex)
        {
            string message = $"[TIMER ERROR] Failed to save changes: {ex.Message} Inner Exception: {ex.InnerException?.Message}\n";
            logger.Enqueue(message);
            File.AppendAllTextAsync(errorFile, message);
        }
    }

    public void ScanFiles(FilePath[] filePaths) // Models.FilePath is your custom class
    {
        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            foreach (var filePathModel in filePaths)
            {
                logger.Enqueue($"Processing {filePathModel.NormalizedPath}");
                ProcessFile(filePathModel);
                if (stopwatch.ElapsedMilliseconds > _saveIntervalInSeconds * 1000)
                {
                    SaveChangesCallback();
                    stopwatch.Restart();
                }
            }
            SaveChangesCallback();
        }
        catch (Exception ex)
        {
            string errorMessage = $"Error during scanning: {ex.Message}. Inner exception: {ex.InnerException?.Message}\n";
            logger.Enqueue(errorMessage);
            File.AppendAllTextAsync(errorFile, errorMessage);
        }
    }

    public void ProcessFile(FilePath filePathModel) // filePathModel is your custom FilePath
    {
        string currentFilePath = filePathModel.NormalizedPath;
        try
        {
            var fileInfo = new FileInfo(currentFilePath);
            if (!fileInfo.Exists)
            {
                logger.Enqueue($"File not found: {currentFilePath}. Skipping.");
                return;
            }

            var fileIdentityKey = FileIdentifier.GetFileIdentityKey(currentFilePath);
            DateTime currentTime = DateTime.UtcNow;

            // Try to find existing instance in cache
            dbCache.IdentityKeyToFileInstanceCache.TryGetValue(fileIdentityKey, out var existingInstance);

            if (existingInstance != null)
            {
                UpdateExistingFileInstance(existingInstance, fileInfo, currentFilePath, currentTime);
            }
            else
            {
                CreateNewFileInstance(fileInfo, fileIdentityKey, currentFilePath, currentTime);
            }
        }
        catch (IOException ioEx) when (ioEx.Message.Contains("being used by another process"))
        {
            logger.Enqueue($"Skipping locked file '{currentFilePath}': {ioEx.Message}");
        }
        catch (Exception ex)
        {
            string errorMessage = $"Error processing file '{currentFilePath}': {ex.Message}. Inner exception: {ex.InnerException?.Message}\n";
            logger.Enqueue(errorMessage);
            File.AppendAllTextAsync(errorFile, errorMessage);
        }
    }

    private FileContent GetOrCreateFileContent(string hash, long size, DateTime currentTime)
    {
        if (dbCache.HashToFileContentCache.TryGetValue(hash, out var existingContent) && existingContent != null)
        {
            if (existingContent.GlobalLastSeen < currentTime) // Update if current scan is later
            {
                existingContent.GlobalLastSeen = currentTime;
                context.FileContents.Update(existingContent);
                // dbCache.AddOrUpdateContent(hash, existingContent); // Cache will be reloaded on save
            }
            // If Size differs for the same hash, it's an anomaly or hash collision.
            if (existingContent.Size != size)
            {
                logger.Enqueue($"[WARNING] Hash collision or size mismatch for hash {hash}. DB size: {existingContent.Size}, File size: {size}. Keeping existing DB record.");
                // Decide on a strategy: update size, log error, etc. For now, we keep existing.
            }
            return existingContent;
        }
        else
        {
            var newContent = new FileContent
            {
                Hash = hash,
                Size = size,
                GlobalFirstSeen = currentTime,
                GlobalLastSeen = currentTime
            };
            context.FileContents.Add(newContent);
            // dbCache.AddOrUpdateContent(hash, newContent); // Cache will be reloaded on save
            return newContent;
        }
    }

    private void UpdateExistingFileInstance(FileInstance instance, FileInfo fileInfo, string currentNormalizedFilePath, DateTime currentTime)
    {
        instance.InstanceLastSeen = currentTime;

        // Check for path/name changes
        string newFileName = fileInfo.Name;
        string newDirectoryPath = Path.GetDirectoryName(currentNormalizedFilePath) ?? string.Empty;
        newDirectoryPath = new FilePath(newDirectoryPath).NormalizedPath; // Normalize
        bool changed = false;

        if (instance.FilePath != currentNormalizedFilePath)
        {
            // Path has changed (file moved or renamed with path)
            instance.FilePath = currentNormalizedFilePath;
            changed = true;

            // Update CurrentFileName if it also changed as part of the path change
            if (instance.CurrentFileName != newFileName)
            {
                instance.CurrentFileName = newFileName;
                AddFileNameHistory(instance, newFileName, currentTime); // Log new name
            }

            var latestLocation = instance.LocationHistory.MaxBy(lh => lh.ChangeNoticedTime);
            if (latestLocation == null || latestLocation.DirectoryPath != newDirectoryPath)
            {
                AddLocationHistory(instance, newDirectoryPath, currentTime);
            }
        }

        // Check for content changes (LastWriteTime or Hash)
        if (instance.InstanceLastWriteTime != fileInfo.LastWriteTimeUtc)
        {
            changed = true;
            instance.InstanceLastWriteTime = fileInfo.LastWriteTimeUtc;
            // Size is on FileContent, hash identifies content
            string newHash = FileHasher.ComputeFileHash(currentNormalizedFilePath);

            if (instance.FileContent == null || instance.FileContent.Hash != newHash)
            {
                logger.Enqueue($"Hash changed for {currentNormalizedFilePath}. Old: {instance.FileContent?.Hash}, New: {newHash}");
                var newFileContent = GetOrCreateFileContent(newHash, fileInfo.Length, currentTime);
                instance.FileContentId = newFileContent.FileContentId;
                instance.FileContent = newFileContent; // Associate with new or existing content
            }
            else if (instance.FileContent.Size != fileInfo.Length)
            {
                // Hash is the same, but size changed. This is unusual.
                // Update size on FileContent.
                logger.Enqueue($"[INFO] Size changed for {currentNormalizedFilePath} but hash {instance.FileContent.Hash} is the same. Updating size from {instance.FileContent.Size} to {fileInfo.Length}.");
                instance.FileContent.Size = fileInfo.Length;
                context.FileContents.Update(instance.FileContent);
            }
        }

        if (changed)
        {
            context.FileInstances.Update(instance);
            dbCache.AddOrUpdateInstance(new FileIdentityKey(instance.NTFSFileID, instance.VolumeSerialNumber), instance); // Cache will be reloaded
        }
    }

    private FileInstance CreateNewFileInstance(FileInfo fileInfo, FileIdentityKey fileIdentityKey, string currentNormalizedFilePath, DateTime currentTime)
    {
        string hash = FileHasher.ComputeFileHash(currentNormalizedFilePath);
        var fileContent = GetOrCreateFileContent(hash, fileInfo.Length, currentTime);

        var newInstance = new FileInstance
        {
            FileContentId = fileContent.FileContentId,
            FileContent = fileContent,
            VolumeSerialNumber = fileIdentityKey.VolumeSerialNumber,
            NTFSFileID = fileIdentityKey.NTFSFileId,
            FilePath = currentNormalizedFilePath,
            CurrentFileName = fileInfo.Name,
            InstanceLastWriteTime = fileInfo.LastWriteTimeUtc,
            InstanceFirstSeen = currentTime,
            InstanceLastSeen = currentTime
        };

        context.FileInstances.Add(newInstance);
        // Need to save to get newInstance.FileInstanceId for history entries
        // This is suboptimal during a batch. Consider deferring history or a different strategy.
        // For now, let's assume IDs are available after Add and before SaveChanges if using in-memory PK generation or similar.
        // A better approach: Add history items and EF Core will fix up FKs on SaveChanges.

        // Add initial history entries
        AddFileNameHistory(newInstance, newInstance.CurrentFileName, currentTime);
        string directoryPath = Path.GetDirectoryName(currentNormalizedFilePath) ?? string.Empty;
        directoryPath = new FilePath(directoryPath).NormalizedPath; // Normalize
        AddLocationHistory(newInstance, directoryPath, currentTime);

        // dbCache.AddOrUpdateInstance(fileIdentityKey, newInstance); // Cache will be reloaded on save

        logger.Enqueue($"New file instance created for: {currentNormalizedFilePath} with hash {hash}");
        return newInstance;
    }

    private void AddFileNameHistory(FileInstance instance, string fileName, DateTime noticedTime)
    {
        var nameHistory = new FileNameHistoryEntry
        {
            FileInstance = instance, // Link to instance
            // FileInstanceId will be set by EF if instance is tracked or upon saving
            FileName = fileName,
            ChangeNoticedTime = noticedTime
        };
        context.FileNameHistoryEntries.Add(nameHistory);
        if (instance.FileInstanceId > 0)
        { // If instance is already saved and has an ID
            instance.NameHistory ??= [];
            instance.NameHistory.Add(nameHistory);
        }
    }

    private void AddLocationHistory(FileInstance instance, string directoryPath, DateTime noticedTime)
    {
        var locationHistory = new FileLocationHistoryEntry
        {
            FileInstance = instance, // Link to instance
            DirectoryPath = directoryPath,
            ChangeNoticedTime = noticedTime
        };
        context.FileLocationHistoryEntries.Add(locationHistory);
        if (instance.FileInstanceId > 0)
        { // If instance is already saved and has an ID
            instance.LocationHistory ??= [];
            instance.LocationHistory.Add(locationHistory);
        }
    }

    public void Dispose()
    {
        SaveChangesCallback(); // Final save
        logger.Dispose();
        context.Dispose();
    }
}


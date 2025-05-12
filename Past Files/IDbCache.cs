// Past Files/IDbCache.cs
using Past_Files.Data;
using Past_Files.Models;
using Past_Files.Services; // For FileIdentityKey
using System.Collections.Concurrent;

namespace Past_Files
{
    public interface IDbCache
    {
        // Changed from FileRecord to FileInstance
        ConcurrentDictionary<FileIdentityKey, FileInstance> IdentityKeyToFileInstanceCache { get; }
        ConcurrentDictionary<string, FileContent> HashToFileContentCache { get; }

        // Method to add/update instance in cache
        void AddOrUpdateInstance(FileIdentityKey key, FileInstance instance);
        // Method to add/update content in cache
        void AddOrUpdateContent(string hash, FileContent content);
    }
}
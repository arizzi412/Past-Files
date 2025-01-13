using Past_Files.Models;
using Past_Files.Services;
using System.Collections.Concurrent;

namespace Past_Files
{
    public interface IDataStore
    {
        ConcurrentDictionary<FileIdentityKey, FileRecord> IdentityKeyToFileRecord { get; }
    }
}
using Past_Files.FileUtils;
using Past_Files.Models;
using System.IO;

namespace Past_Files.Services
{
    public class FileInformation
    {
        private FileInformation()
        {
        }

        public static FileInformation CreateFileInformation(FilePath filePath) => new()
        {
            Path = filePath,
            IdentityKey = FileIdentifier.GetFileIdentityKey(filePath),
            Info = new FileInfo(filePath)
        };
        public required FileInfo Info { get; init; }
        public required FilePath Path { get; init; }
        public required FileIdentityKey IdentityKey { get; init; }
    }
}
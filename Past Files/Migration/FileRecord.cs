// Add a dummy legacy FileRecord model if needed for _oldDBHashImportInfo, or define it properly
namespace Past_Files.Models.Legacy
{
    public class FileRecord
    {
        public int FileRecordId { get; set; }
        public string Hash { get; set; } = string.Empty;
        public uint VolumeSerialNumber { get; set; }
        public ulong NTFSFileID { get; set; }
        public DateTime LastWriteTime { get; set; }
        // Add other properties if needed by the import logic
    }
}
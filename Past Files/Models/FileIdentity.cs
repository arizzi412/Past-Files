// Models/FileIdentity.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FileTrackerApp.Models
{
    public class FileIdentity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int FileIdentityId { get; set; }

        public uint VolumeSerialNumber { get; set; }
        public ulong FileID { get; set; }

        public int FileRecordId { get; set; }
        public FileRecord FileRecord { get; set; } = null!;
    }
}

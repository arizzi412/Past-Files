// Past Files/Models/FileInstance.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO; // Required for Path.GetFileName

namespace Past_Files.Models
{
    public class FileInstance
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int FileInstanceId { get; set; }

        public int FileContentId { get; set; }
        public FileContent FileContent { get; set; } = null!;

        public uint VolumeSerialNumber { get; set; }
        public ulong NTFSFileID { get; set; }

        [Required]
        public string FilePath { get; set; } = string.Empty; // Full normalized path

        [Required]
        public string CurrentFileName { get; set; } = string.Empty; // Just the file name

        public DateTime InstanceLastWriteTime { get; set; }
        public DateTime InstanceFirstSeen { get; set; }
        public DateTime InstanceLastSeen { get; set; }

        public ICollection<FileLocationHistoryEntry> LocationHistory { get; set; } = new List<FileLocationHistoryEntry>();
        public ICollection<FileNameHistoryEntry> NameHistory { get; set; } = new List<FileNameHistoryEntry>();

        // Optional: Non-mapped property to derive filename if not storing it explicitly
        // [NotMapped]
        // public string DerivedFileName => System.IO.Path.GetFileName(FilePath);
    }
}
// Models/FileRecord.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FileTrackerApp.Models
{
    public class FileRecord
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int FileRecordId { get; set; }

        public string Hash { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime LastWriteTime { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }

        public List<FileLocation> Locations { get; set; } = [];
        public List<FileIdentity> Identities { get; set; } = [];
        public List<FileNameHistory> NameHistories { get; set; } = [];
    }
}

// Models/FileNameHistory.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FileTrackerApp.Models
{
    public class FileNameHistory
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int FileNameHistoryId { get; set; }

        public string FileName { get; set; } = string.Empty;

        public DateTime ChangeNoticedTime { get; set; } // Timestamp of the name change

        public int FileRecordId { get; set; }
        public FileRecord FileRecord { get; set; } = null!;
    }
}

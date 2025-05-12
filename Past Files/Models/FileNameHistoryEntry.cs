// Past Files/Models/FileNameHistoryEntry.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Past_Files.Models
{
    public class FileNameHistoryEntry
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int FileNameHistoryEntryId { get; set; } // Renamed PK

        [Required]
        public string FileName { get; set; } = string.Empty;

        public DateTime ChangeNoticedTime { get; set; } // Was NameChangeNoticedTime

        public int FileInstanceId { get; set; } // Changed FK
        public FileInstance FileInstance { get; set; } = null!; // Navigation property
    }
}
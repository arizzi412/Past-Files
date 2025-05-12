// Past Files/Models/FileLocationHistoryEntry.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Past_Files.Models
{
    public class FileLocationHistoryEntry
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int FileLocationHistoryEntryId { get; set; } // Renamed PK

        public int FileInstanceId { get; set; } // Changed FK
        public FileInstance FileInstance { get; set; } = null!; // Navigation property

        [Required]
        public string DirectoryPath { get; set; } = string.Empty; // Was Path, now string for directory

        public DateTime ChangeNoticedTime { get; set; } // Was LocationChangeNoticedTime
    }
}
// Models/FileRecord.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Past_Files.Models;

public class FileRecord
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int FileRecordId { get; set; }

    public string Hash { get; set; } = string.Empty;
    public string CurrentFileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime LastWriteTime { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }

    public List<FileLocationHistory> Locations { get; set; } = [];
    public List<FileIdentity> Identities { get; set; } = [];
    public List<FileNameHistory> NameHistories { get; set; } = [];
}

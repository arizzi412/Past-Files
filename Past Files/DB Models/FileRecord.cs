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
    public uint VolumeSerialNumber { get; set; }
    public ulong NTFSFileID { get; set; }

    public List<FileLocationsHistory> Locations { get; set; } = [];
    public List<FileNamesHistory> NameHistory { get; set; } = [];
}

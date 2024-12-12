// Models/FileLocation.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Past_Files.Models;

public class FileLocation
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int FileLocationId { get; set; }

    public string Path { get; set; } = string.Empty;

    public int FileRecordId { get; set; }

    public DateTime LocationChangeNoticedTime { get; set; }
    public FileRecord FileRecord { get; set; } = null!;
}

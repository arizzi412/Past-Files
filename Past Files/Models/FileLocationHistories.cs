using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Past_Files.Models;

public class FileLocationsHistory
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int FileLocationId { get; set; }

    public int FileRecordId { get; set; }

    public DateTime LocationChangeNoticedTime { get; set; }

    // Path is a value object
    public FilePath? Path { get; set; }

    // Navigation property to FileRecord
    public FileRecord? FileRecord { get; set; }
}

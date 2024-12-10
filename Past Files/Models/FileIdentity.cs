// Models/FileIdentity.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Past_Files.Models;

public class FileIdentity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int FileIdentityId { get; set; }

    public uint VolumeSerialNumber { get; set; }
    public ulong NTFSFileID { get; set; }
    public int FileRecordId { get; set; }
    public FileRecord FileRecord { get; set; } = null!;
}

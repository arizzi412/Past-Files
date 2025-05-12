// Past Files/Models/FileContent.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Past_Files.Models
{
    public class FileContent
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int FileContentId { get; set; }

        [Required]
        public string Hash { get; set; } = string.Empty;

        public long Size { get; set; }

        public DateTime GlobalFirstSeen { get; set; }
        public DateTime GlobalLastSeen { get; set; }

        public ICollection<FileInstance> FileInstances { get; set; } = new List<FileInstance>();
    }
}
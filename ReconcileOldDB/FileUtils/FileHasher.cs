// Utils/FileHasher.cs
using System.Security.Cryptography;
using System.IO;

namespace Past_Files.FileUtils;

public static class FileHasher
{
    public static string ComputeFileHash(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        byte[] hashBytes = sha256.ComputeHash(stream);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }
}

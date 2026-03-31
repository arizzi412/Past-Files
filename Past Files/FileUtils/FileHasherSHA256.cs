using Past_Files.Services;
using System.Security.Cryptography;

namespace Past_Files.FileUtils;

public class FileHasherSHA256
{
    private const int BufferSize = 64 * 1024;
    public static string ComputeHash(string filePath)
    {
        using var stream = new FileStream(
                  filePath,
                  FileMode.Open,
                  FileAccess.Read,
                  FileShare.Read,
                  BufferSize,
                  FileOptions.SequentialScan); // Hint to OS to pre-fetch data

        byte[] hashBytes = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hashBytes);
    }
}

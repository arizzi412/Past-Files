using System;
using System.IO;

namespace Past_Files;

public readonly record struct ValidNormalizedFilePath : IEquatable<ValidNormalizedFilePath>
{
    public string NormalizedPath { get; }

    public ValidNormalizedFilePath(string path)
    {
        NormalizedPath = path.Contains('\\')
            ? path.Replace('\\', '/')
            : path;
    }
    public override string ToString() => NormalizedPath;

    public bool Equals(ValidNormalizedFilePath other)
    {
        return string.Equals(NormalizedPath, other.NormalizedPath, StringComparison.OrdinalIgnoreCase);
    }
    public override int GetHashCode() => NormalizedPath.GetHashCode();

    public static implicit operator string(ValidNormalizedFilePath v) => v.NormalizedPath;

    /// <summary>
    /// Returns the directory path relative to the drive/volume root.
    /// E.g., "C:/Users/Name/File.txt" -> "Users/Name"
    /// E.g., "/var/log/nginx/error.log" -> "var/log/nginx"
    /// </summary>
    public ValidNormalizedFilePath GetDirectoryRelativeToRoot()
    {
        ReadOnlySpan<char> pathSpan = NormalizedPath.AsSpan();

        // 1. Find the end of the file name (last slash)
        int lastSlashIndex = pathSpan.LastIndexOf('/');

        // If no slash, it's just a filename (e.g. "file.txt"), so no directory.
        if (lastSlashIndex < 0) return new(string.Empty);

        int rootEndIndex = 0;

        int colonIndex = pathSpan.IndexOf(':');
        if (colonIndex >= 0)
        {
            // Root is "C:/" so end is colon + 2
            rootEndIndex = colonIndex + 2;
        }
        else if (pathSpan.Length > 0 && pathSpan[0] == '/')
        {
            // Unix Root is "/" so end is 1
            rootEndIndex = 1;
        }

        // 3. Calculate length of the target directory section
        // Example: C:/Users/File.txt
        // Indices: 012345678...
        // RootEnd: 3 ("C:/")
        // LastSlash: 8 (After "Users")
        // Length: 8 - 3 = 5 ("Users")

        int length = lastSlashIndex - rootEndIndex;

        if (length <= 0) return new(string.Empty);

        return new(pathSpan.Slice(rootEndIndex, length).ToString());
    }
}
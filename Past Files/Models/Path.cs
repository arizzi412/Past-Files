// Models/Path.cs
using System;
using System.IO;

namespace Past_Files.Models;

public class FilePath(string path)
{
    public string NormalizedPath { get; } = NormalizePath(path);

    private static string NormalizePath(string path) => path.Replace(Path.DirectorySeparatorChar, '/');

    public override string ToString() => NormalizedPath;

    public override bool Equals(object? obj)
    {
        if (obj is FilePath other)
        {
            return NormalizedPath.Equals(other.NormalizedPath);
        }
        return false;
    }

    public override int GetHashCode() => NormalizedPath.GetHashCode();

    public static implicit operator FilePath(string v)
    {
        return new FilePath(v);
    }

    public static implicit operator string(FilePath v)
    {
        return v.NormalizedPath;
    }

    public static bool IsValidDirectoryAndExists(string path)
    {
        // 1. Basic null or empty check
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        // 2. Check for invalid characters in the path
        if (path.Any(c => Path.GetInvalidPathChars().Contains(c)))
        {
            return false;
        }

        // 3. Check if the directory actually exists
        // Directory.Exists handles some other errors (like unmapped drives) by returning false
        return Directory.Exists(path);
    }
}

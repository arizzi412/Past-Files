// Models/Path.cs
using System;
using System.IO;

namespace Past_Files;

public class ValidNormalizedFilePath(string path)
{
    public string NormalizedPath { get; } = NormalizePath(path);

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    public override string ToString() => NormalizedPath;

    public override bool Equals(object? obj)
    {
        if (obj is ValidNormalizedFilePath other)
        {
            return NormalizedPath.Equals(other.NormalizedPath);
        }
        return false;
    }

    public override int GetHashCode() => NormalizedPath.GetHashCode();

    public static implicit operator ValidNormalizedFilePath(string v)
    {
        return new ValidNormalizedFilePath(v);
    }

    public static implicit operator string(ValidNormalizedFilePath v)
    {
        return v.NormalizedPath;
    }

    /// <summary>
    /// Returns the directory path relative to the drive/volume root.
    /// E.g., "C:/Users/Name/File.txt" -> "Users/Name"
    /// E.g., "E:/Games/Game.exe" -> "Games"
    /// </summary>
    public string GetDirectoryRelativeToRoot()
    {
        // Get the parent directory of the file
        string? directory = Path.GetDirectoryName(NormalizedPath);
        if (string.IsNullOrEmpty(directory)) return string.Empty;

        // Get the root (e.g., "C:\")
        string? root = Path.GetPathRoot(directory);

        // If no root is found, it's already relative or invalid
        if (string.IsNullOrEmpty(root)) return NormalizePath(directory);

        // Get relative path (e.g., "Users\Name")
        string relative = Path.GetRelativePath(root, directory);

        // Handle case where file is at root (returns ".")
        if (relative == ".") return string.Empty;

        // Ensure we store it with forward slashes for consistency
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }
}
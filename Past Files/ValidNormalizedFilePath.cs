using System;
using System.IO;

namespace Past_Files;

public readonly record struct ValidNormalizedFilePath : IEquatable<ValidNormalizedFilePath>
{
    public string NormalizedPath { get; }

    public ValidNormalizedFilePath(string path)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException($"Path cannot be null or empty: {path}");
        
        NormalizedPath = path.Contains('\\')
            ? path.Replace('\\', '/')
            : path;
    }

    public bool Equals(ValidNormalizedFilePath other)
    {
        return string.Equals(NormalizedPath, other.NormalizedPath, StringComparison.OrdinalIgnoreCase);
    }
    public override int GetHashCode() => NormalizedPath.GetHashCode();

    public override string ToString() => NormalizedPath;

    public static implicit operator string(ValidNormalizedFilePath v) => v.NormalizedPath;
}
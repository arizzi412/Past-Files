// Models/Path.cs
using System;
using System.IO;

namespace Past_Files.Models;

public class FilePath
{
    private string _normalizedPath; // Backing field for the property
    public string NormalizedPath
    {
        get => _normalizedPath;
        private set => _normalizedPath = NormalizePath(value);
    }

    private FilePath() { } // Required for EF Core materialization

    public FilePath(string path)
    {
        NormalizedPath = path;
    }

    private static string NormalizePath(string path) => path.Replace(System.IO.Path.DirectorySeparatorChar, '/');

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
}

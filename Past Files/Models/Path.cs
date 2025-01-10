// Models/Path.cs
using System;
using System.IO;

namespace Past_Files.Models;

public class Path
{
    private string _normalizedPath; // Backing field for the property
    public string NormalizedPath
    {
        get => _normalizedPath;
        private set => _normalizedPath = NormalizePath(value);
    }

    private Path() { } // Required for EF Core materialization

    public Path(string path)
    {
        NormalizedPath = path;
    }

    private static string NormalizePath(string path) => path.Replace(System.IO.Path.DirectorySeparatorChar, '/');

    public override string ToString() => NormalizedPath;

    public override bool Equals(object? obj)
    {
        if (obj is Path other)
        {
            return NormalizedPath.Equals(other.NormalizedPath);
        }
        return false;
    }

    public override int GetHashCode() => NormalizedPath.GetHashCode();

    public static implicit operator Path(string v)
    {
        return new Path(v);
    }

    public static implicit operator string(Path v)
    {
        return v.NormalizedPath;
    }
}

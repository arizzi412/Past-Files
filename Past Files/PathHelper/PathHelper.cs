using System;
using System.IO;

namespace Past_Files.PathHelper;

public static class PathHelper
{
    public static string NormalizePath(string path) => path.Replace(Path.DirectorySeparatorChar, '/');

}
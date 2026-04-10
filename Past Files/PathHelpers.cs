using System;
using System.Collections.Generic;
using System.Text;

namespace Past_Files
{
    internal static class PathHelpers
    {
        public static bool IsDirectoryValidAndExistant(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
            return Directory.Exists(path);
        }

        public static bool IsValidFilePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
          
            try
            {
                // Path.GetFullPath will throw specific exceptions if the format is invalid,
                // the path is too long, or it contains a colon in an invalid position (Windows).
                string fullPath = Path.GetFullPath(path);

                // 4. Check for invalid file name characters specifically
                // (sometimes needed because GetFullPath allows some things in the directory part 
                // that aren't allowed in the file name part)
                var fileName = Path.GetFileName(path.AsSpan());

                if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;

                return true;
            }

            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Returns the directory path relative to the drive/volume root.
        /// E.g., "C:/Users/Name/File.txt" -> "Users/Name"
        /// E.g., "/var/log/nginx/error.log" -> "var/log/nginx"
        /// </summary>
        public static ValidNormalizedFilePath GetDirectoryRelativeToRoot(this ValidNormalizedFilePath filePath)
        {
            ReadOnlySpan<char> pathSpan = filePath.NormalizedPath.AsSpan();

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
}

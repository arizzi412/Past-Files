using System;
using System.Collections.Generic;
using System.Text;

namespace Past_Files
{
    internal class PathHelpers
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
    }
}

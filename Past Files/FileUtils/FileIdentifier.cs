// Utils/FileIdentifier.cs
using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Past_Files.FileUtils;

public static partial class FileIdentifier
{
    // Constants for CreateFile
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint FILE_SHARE_DELETE = 0x00000004;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000; // Allows opening directories

    // We use StringMarshalling.Utf16 to target CreateFileW, which supports long paths (>260 chars)
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle hFile, out BY_HANDLE_FILE_INFORMATION lpFileInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    /// <summary>
    /// Ensures the path is formatted for Windows Long Path support (\\?\)
    /// </summary>
    private static string EnsureLongPath(string path)
    {
        // 1. Resolve to absolute path (handles relative paths, dots, etc.)
        // This is crucial because \\?\ paths must be absolute.
        string fullPath = Path.GetFullPath(path);

        // 2. If it already starts with \\?\, it is already formatted.
        if (fullPath.StartsWith(@"\\?\")) return fullPath;

        // 3. Handle UNC paths (e.g., \\Server\Share)
        // They need to become \\?\UNC\Server\Share
        if (fullPath.StartsWith(@"\\"))
        {
            return string.Concat(@"\\?\UNC\", fullPath.AsSpan(2));
        }

        // 4. Handle standard Drive paths (e.g., C:\Folder)
        // They need to become \\?\C:\Folder
        return @"\\?\" + fullPath;
    }

    /// <summary>
    /// Retrieves the NTFS File ID and Volume Serial Number for a given file.
    /// Optimized to avoid opening the file data stream and handles Long Paths.
    /// </summary>
    /// <param name="filePath">The full or relative path of the file.</param>
    /// <returns>A tuple containing the FileID and VolumeSerialNumber.</returns>
    public static FileIdentityKey GetFileIdentityKey(string filePath)
    {
        // Normalize the path string to support >260 characters
        string longPath = EnsureLongPath(filePath);

        using SafeFileHandle handle = CreateFile(
            longPath,
            0, // No read/write access requested (Metadata only)
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        }

        if (GetFileInformationByHandle(handle, out BY_HANDLE_FILE_INFORMATION fileInfo))
        {
            ulong fileID = ((ulong)fileInfo.FileIndexHigh << 32) | fileInfo.FileIndexLow;
            return new FileIdentityKey(fileID, fileInfo.VolumeSerialNumber);
        }
        else
        {
            throw new IOException($"Unable to get file information for {filePath}.", Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
        }
    }
}
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Past_Files.Services; // Required for FileIdentityKey (implied by context)

namespace Past_Files.FileUtils;

public static partial class FileIdentifier
{
    // Constants for CreateFile
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint FILE_SHARE_DELETE = 0x00000004;
    private const uint OPEN_EXISTING = 3;

    // FILE_FLAG_BACKUP_SEMANTICS (0x02000000) allows opening directories as well as files.
    // We combine it with FILE_ATTRIBUTE_NORMAL (0x80)
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

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
    /// Retrieves the NTFS File ID and Volume Serial Number for a given file.
    /// Optimized to avoid opening the file data stream.
    /// </summary>
    /// <param name="filePath">The full path of the file.</param>
    /// <returns>A tuple containing the FileID and VolumeSerialNumber.</returns>
    public static FileIdentityKey GetFileIdentityKey(string filePath)
    {
        // 1. Use CreateFile with 0 as dwDesiredAccess. 
        // This requests a handle for metadata queries only, avoiding the overhead 
        // of preparing the data stream (and triggering AV scans for read access).
        using SafeFileHandle handle = CreateFile(
            filePath,
            0, // No read/write access requested
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, // Allow others to do anything
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS, // Allows opening directories if needed
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            // If we fail to open it (e.g. exclusive lock by another process or access denied),
            // throw appropriate exception based on LastWin32Error.
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        }

        // 2. Query the information using the lightweight handle
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
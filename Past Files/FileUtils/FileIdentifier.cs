// Utils/FileIdentifier.cs
using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Past_Files.Services;

namespace Past_Files.FileUtils;

public static partial class FileIdentifier
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle hFile, out BY_HANDLE_FILE_INFORMATION lpFileInformation);

    [StructLayout(LayoutKind.Sequential)]
    struct BY_HANDLE_FILE_INFORMATION
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
    /// </summary>
    /// <param name="filePath">The full path of the file.</param>
    /// <returns>A tuple containing the FileID and VolumeSerialNumber.</returns>
    public static FileIdentityKey GetFileIdentityKey(string filePath)
    {
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var handle = fileStream.SafeFileHandle;
        if (GetFileInformationByHandle(handle, out BY_HANDLE_FILE_INFORMATION fileInfo))
        {
            ulong fileID = (ulong)fileInfo.FileIndexHigh << 32 | fileInfo.FileIndexLow;
            return new(fileID, fileInfo.VolumeSerialNumber);
        }
        else
        {
            throw new IOException("Unable to get file information.", Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
        }
    }
}
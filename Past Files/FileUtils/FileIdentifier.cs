// Utils/FileIdentifier.cs
using Microsoft.Win32.SafeHandles;
using Past_Files.Services;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

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


    // P/Invoke for opening a file handle efficiently using LibraryImport
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFile(
        string lpFileName,
        FileAccess dwDesiredAccess,
        FileShare dwShareMode,
        IntPtr lpSecurityAttributes,
        FileMode dwCreationDisposition,
        FileAttributes dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    /// <summary>
    /// Retrieves the NTFS File ID and Volume Serial Number for a given file.
    /// </summary>
    /// <param name="filePath">The full path of the file.</param>
    /// <returns>A tuple containing the FileID and VolumeSerialNumber.</returns>
    public static FileIdentityKey GetFileIdentityKey(string filePath)
    {
        using (SafeFileHandle handle = CreateFile(filePath, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, FileMode.Open, FileAttributes.Normal, IntPtr.Zero))
        {
            if (handle.IsInvalid)
            {
                throw new IOException("Unable to get file handle.", new Win32Exception(Marshal.GetLastWin32Error()));
            }

            if (GetFileInformationByHandle(handle, out BY_HANDLE_FILE_INFORMATION fileInfo))
            {
                ulong fileID = ((ulong)fileInfo.FileIndexHigh << 32) | fileInfo.FileIndexLow;
                return new(fileID, fileInfo.VolumeSerialNumber);
            }
            else
            {
                throw new IOException("Unable to get file information.", new Win32Exception(Marshal.GetLastWin32Error()));
            }
        }
}
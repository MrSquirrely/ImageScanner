using System.Runtime.InteropServices;

namespace ImageScanner.Plugins.Duplicates;

/// <summary>
/// Moves files to the Recycle Bin (never deletes permanently), so a wrong pick can be restored from there.
/// </summary>
internal static partial class RecycleBin {
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT operation);

    /// <summary>Returns true when the file is gone from its folder (it's in the Recycle Bin, or was already missing).</summary>
    public static bool Send(string path) {
        if (!File.Exists(path)) {
            return true;
        }

        SHFILEOPSTRUCT operation = new() {
            wFunc = FO_DELETE,
            pFrom = Path.GetFullPath(path) + "\0\0",
            // The plugin already asked; no shell confirmation or progress UI, but keep it recoverable
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI
        };

        return SHFileOperation(ref operation) == 0 && !operation.fAnyOperationsAborted && !File.Exists(path);
    }
}

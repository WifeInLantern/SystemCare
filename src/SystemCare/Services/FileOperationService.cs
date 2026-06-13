using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SystemCare.Services;

public interface IFileOperationService
{
    /// <summary>Sends a file to the Recycle Bin. Returns false when the delete failed (in use, gone, access denied).</summary>
    bool SendToRecycleBin(string path);

    void OpenInExplorer(string path);
}

public class FileOperationService : IFileOperationService
{
    // SHFileOperation rather than File.Delete so user-content deletions are recoverable.
    // x64-only layout (no Pack=1) — the app is published win-x64.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOERRORUI = 0x0400;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    public bool SendToRecycleBin(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return false;
            var op = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = path + '\0', // marshalled string adds the second terminator
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
            };
            return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void OpenInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // explorer failed to launch — nothing sensible to do
        }
    }
}

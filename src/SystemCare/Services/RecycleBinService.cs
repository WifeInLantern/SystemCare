using SystemCare.Native;

namespace SystemCare.Services;

/// <summary>
/// Thin seam over the shell Recycle Bin APIs so consumers (dashboard quick-action, scheduled
/// maintenance) stay unit-testable — the P/Invoke calls themselves can't be mocked.
/// </summary>
public interface IRecycleBinService
{
    /// <summary>Total size and item count across all drives' Recycle Bins.</summary>
    (long Bytes, long Items) Query();
    /// <summary>Empties all Recycle Bins without confirmation, progress UI, or sound.</summary>
    void Empty();
}

public class RecycleBinService : IRecycleBinService
{
    public (long Bytes, long Items) Query() => NativeMethods.QueryRecycleBin();

    public void Empty() => NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null,
        NativeMethods.SHERB_NOCONFIRMATION | NativeMethods.SHERB_NOPROGRESSUI | NativeMethods.SHERB_NOSOUND);
}

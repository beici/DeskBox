using System.Runtime.InteropServices;

namespace DeskBox.Platform;

/// <summary>
/// shell32/propsys/ole32 entry points used by the Jump List AUMID
/// registration that used to declare them privately. Signatures moved
/// verbatim.
/// </summary>
internal static partial class JumpListNativeMethods
{
    // R19-S9-01: the entry point returns an HRESULT. Marshalling it as a bool
    // read S_OK (0) as false and every failure code (negative HRESULTs) as
    // true, so callers could never tell success from failure; keep the raw
    // HRESULT and let the caller check it.
    // R20-S9-10: this entry point reports through its HRESULT only — it never
    // sets the thread's last error, so `SetLastError = true` promised a value
    // that does not exist (and invited a future caller to read stale state).
    [DllImport("shell32.dll", EntryPoint = "SetCurrentProcessExplicitAppUserModelID",
        CharSet = CharSet.Unicode)]
    internal static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHGetPropertyStoreFromParsingName(
        string pszPath, IntPtr pbc, uint flags, ref Guid riid, out IntPtr ppv);

    [DllImport("propsys.dll", CharSet = CharSet.Unicode)]
    internal static extern int PropVariantFromString(string psz, IntPtr ppropvar);

    [DllImport("ole32.dll")]
    internal static extern int PropVariantClear(IntPtr ppropvar);
}

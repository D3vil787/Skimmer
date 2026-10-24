using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Skimmer;

/// <summary>Deletes files the way Explorer does (Recycle Bin, Windows' own dialogs).</summary>
static class ShellOps
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    const uint FO_DELETE = 3;
    const ushort FOF_NOCONFIRMATION = 0x0010;
    const ushort FOF_ALLOWUNDO = 0x0040;
    const ushort FOF_WANTNUKEWARNING = 0x4000;

    /// <summary>
    /// permanent = false: to the Recycle Bin without a question (it can be undone); Windows still warns
    /// if a file cannot go to the Recycle Bin (e.g. on a network drive).
    /// permanent = true: Skimmer asks first ("No" is the default button).
    /// Returns true when the operation was not cancelled.
    /// </summary>
    public static bool Delete(IWin32Window owner, IReadOnlyCollection<string> paths, bool permanent)
    {
        if (paths.Count == 0) return false;

        // Brand book ("Кнопки"): a dangerous action is confirmed by a separate question, "No" by default.
        // The Recycle Bin can be undone, so only the permanent delete asks.
        if (permanent && MessageBox.Show(owner, Lang.Format("del.confirm", paths.Count), Lang.Get("app.title"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return false;

        var op = new SHFILEOPSTRUCT
        {
            hwnd = owner?.Handle ?? IntPtr.Zero,
            wFunc = FO_DELETE,
            pFrom = string.Join("\0", paths) + "\0\0",
            fFlags = permanent ? FOF_NOCONFIRMATION : (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING)
        };
        int rc = SHFileOperation(ref op);
        return rc == 0 && !op.fAnyOperationsAborted;
    }
}

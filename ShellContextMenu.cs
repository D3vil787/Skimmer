using System.Runtime.InteropServices;

namespace Skimmer;

/// <summary>
/// The real Explorer context menu (IContextMenu) for one or more files / folders of the same folder,
/// with Skimmer's own items on top. Submenus such as "Open with" and "Send to" are owner-drawn by the
/// shell, so the window that owns the menu forwards a few messages here (see <see cref="HandleMenuMessage"/>).
/// </summary>
static class ShellContextMenu
{
    public sealed record Item(string Text, Action Run, bool Bold = false);   // null Text = separator

    const uint FirstShellId = 1000;
    const uint LastShellId = 0x7FFF;

    static IContextMenu2 active2;
    static IContextMenu3 active3;

    /// <summary>Forward WM_INITMENUPOPUP / DRAWITEM / MEASUREITEM / MENUCHAR to the shell while its menu is open.</summary>
    public static bool HandleMenuMessage(ref Message m)
    {
        if (active2 == null && active3 == null) return false;
        if (m.Msg is not (WM_INITMENUPOPUP or WM_DRAWITEM or WM_MEASUREITEM or WM_MENUCHAR)) return false;
        try
        {
            if (active3 != null && active3.HandleMenuMsg2((uint)m.Msg, m.WParam, m.LParam, out IntPtr result) == 0)
            {
                m.Result = result;
                return true;
            }
            if (active2 != null && active2.HandleMenuMsg((uint)m.Msg, m.WParam, m.LParam) == 0)
            {
                m.Result = IntPtr.Zero;
                return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>Shows the menu at a screen point. <paramref name="owner"/> must forward menu messages (HandleMenuMessage).</summary>
    public static void Show(Control owner, IReadOnlyList<string> paths, Point screen, IReadOnlyList<Item> ownItems)
    {
        if (paths.Count == 0) return;

        var absolute = new List<IntPtr>();
        var children = new List<IntPtr>();
        IntPtr parentPtr = IntPtr.Zero;
        IntPtr menuPtr = IntPtr.Zero;
        IntPtr hmenu = IntPtr.Zero;
        IContextMenu cm = null;
        try
        {
            Guid iidFolder = typeof(IShellFolder).GUID;
            foreach (string p in paths)
            {
                if (SHParseDisplayName(p, IntPtr.Zero, out IntPtr pidl, 0, out _) != 0 || pidl == IntPtr.Zero) continue;
                absolute.Add(pidl);
                if (SHBindToParent(pidl, ref iidFolder, out IntPtr folder, out IntPtr last) != 0) continue;
                if (parentPtr == IntPtr.Zero) parentPtr = folder;
                else Marshal.Release(folder);   // all items live in the same folder
                children.Add(last);              // points inside the absolute pidl, freed with it
            }
            if (parentPtr == IntPtr.Zero || children.Count == 0) { ShowOwnOnly(owner, screen, ownItems); return; }

            var parent = (IShellFolder)Marshal.GetObjectForIUnknown(parentPtr);
            Guid iidMenu = typeof(IContextMenu).GUID;
            parent.GetUIObjectOf(owner.Handle, (uint)children.Count, children.ToArray(), ref iidMenu, IntPtr.Zero, out menuPtr);
            cm = (IContextMenu)Marshal.GetObjectForIUnknown(menuPtr);

            hmenu = CreatePopupMenu();
            uint pos = 0;
            for (int i = 0; i < ownItems.Count; i++)
            {
                var it = ownItems[i];
                if (it.Text == null) InsertMenu(hmenu, pos++, MF_BYPOSITION | MF_SEPARATOR, UIntPtr.Zero, null);
                else InsertMenu(hmenu, pos++, MF_BYPOSITION | MF_STRING, (UIntPtr)(uint)(i + 1), it.Text);
            }
            if (ownItems.Count > 0) InsertMenu(hmenu, pos++, MF_BYPOSITION | MF_SEPARATOR, UIntPtr.Zero, null);
            int boldOwn = ownItems.ToList().FindIndex(x => x.Bold && x.Text != null);
            if (boldOwn >= 0) SetMenuDefaultItem(hmenu, (uint)(boldOwn + 1), 0);

            uint flags = CMF_NORMAL | CMF_EXPLORE;
            if ((Control.ModifierKeys & Keys.Shift) != 0) flags |= CMF_EXTENDEDVERBS;
            cm.QueryContextMenu(hmenu, pos, FirstShellId, LastShellId, flags);
            // the shell marks its own default ("Open") - ours stays the default when there is one
            if (boldOwn >= 0) SetMenuDefaultItem(hmenu, (uint)(boldOwn + 1), 0);

            active2 = cm as IContextMenu2;
            active3 = cm as IContextMenu3;
            int cmd;
            try
            {
                cmd = TrackPopupMenuEx(hmenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, screen.X, screen.Y, owner.Handle, IntPtr.Zero);
            }
            finally
            {
                active2 = null;
                active3 = null;
            }

            if (cmd >= 1 && cmd <= ownItems.Count)
            {
                ownItems[cmd - 1].Run?.Invoke();
            }
            else if (cmd >= FirstShellId)
            {
                var info = new CMINVOKECOMMANDINFOEX
                {
                    cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                    fMask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE,
                    hwnd = owner.Handle,
                    lpVerb = (IntPtr)(cmd - FirstShellId),
                    lpVerbW = (IntPtr)(cmd - FirstShellId),
                    nShow = SW_SHOWNORMAL,
                    ptInvoke = new POINT { X = screen.X, Y = screen.Y }
                };
                if ((Control.ModifierKeys & Keys.Control) != 0) info.fMask |= CMIC_MASK_CONTROL_DOWN;
                if ((Control.ModifierKeys & Keys.Shift) != 0) info.fMask |= CMIC_MASK_SHIFT_DOWN;
                cm.InvokeCommand(ref info);
            }
        }
        catch
        {
            // shell extensions can fail in many ways; never take the app down because of them
        }
        finally
        {
            if (hmenu != IntPtr.Zero) DestroyMenu(hmenu);
            if (cm != null) Marshal.ReleaseComObject(cm);
            if (menuPtr != IntPtr.Zero) Marshal.Release(menuPtr);
            if (parentPtr != IntPtr.Zero) Marshal.Release(parentPtr);
            foreach (var p in absolute) Marshal.FreeCoTaskMem(p);
        }
    }

    /// <summary>Fallback when the shell cannot build a menu (e.g. the file is already gone).</summary>
    static void ShowOwnOnly(Control owner, Point screen, IReadOnlyList<Item> ownItems)
    {
        if (ownItems.Count == 0) return;
        IntPtr hmenu = CreatePopupMenu();
        try
        {
            for (int i = 0; i < ownItems.Count; i++)
            {
                var it = ownItems[i];
                if (it.Text == null) InsertMenu(hmenu, (uint)i, MF_BYPOSITION | MF_SEPARATOR, UIntPtr.Zero, null);
                else InsertMenu(hmenu, (uint)i, MF_BYPOSITION | MF_STRING, (UIntPtr)(uint)(i + 1), it.Text);
            }
            int cmd = TrackPopupMenuEx(hmenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, screen.X, screen.Y, owner.Handle, IntPtr.Zero);
            if (cmd >= 1 && cmd <= ownItems.Count) ownItems[cmd - 1].Run?.Invoke();
        }
        finally { DestroyMenu(hmenu); }
    }

    // ------------------------------------------------------------ interop

    const int WM_INITMENUPOPUP = 0x0117, WM_DRAWITEM = 0x002B, WM_MEASUREITEM = 0x002C, WM_MENUCHAR = 0x0120;
    const uint MF_STRING = 0x0000, MF_SEPARATOR = 0x0800, MF_BYPOSITION = 0x0400;
    const uint TPM_RETURNCMD = 0x0100, TPM_RIGHTBUTTON = 0x0002;
    const uint CMF_NORMAL = 0x0000, CMF_EXPLORE = 0x0004, CMF_EXTENDEDVERBS = 0x0100;
    const uint CMIC_MASK_UNICODE = 0x00004000, CMIC_MASK_PTINVOKE = 0x20000000,
               CMIC_MASK_SHIFT_DOWN = 0x10000000, CMIC_MASK_CONTROL_DOWN = 0x40000000;
    const int SW_SHOWNORMAL = 1;

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr lpTitle;
        public IntPtr lpVerbW;
        public IntPtr lpParametersW;
        public IntPtr lpDirectoryW;
        public IntPtr lpTitleW;
        public POINT ptInvoke;
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214E6-0000-0000-C000-000000000046")]
    interface IShellFolder
    {
        void ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr pchEaten, out IntPtr ppidl, IntPtr pdwAttributes);
        void EnumObjects(IntPtr hwnd, int flags, out IntPtr ppenum);
        void BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        void BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        void CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
        void GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint rgfInOut);
        void GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);
        void GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr pName);
        void SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IntPtr ppidlOut);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214E4-0000-0000-C000-000000000046")]
    interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F4-0000-0000-C000-000000000046")]
    interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
        [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
    interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
        [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr ppidlLast);

    [DllImport("user32.dll")]
    static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool InsertMenu(IntPtr hMenu, uint uPosition, uint uFlags, UIntPtr uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    static extern bool SetMenuDefaultItem(IntPtr hMenu, uint uItem, uint fByPos);

    [DllImport("user32.dll")]
    static extern int TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);
}

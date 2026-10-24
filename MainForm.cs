using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Skimmer;

sealed class MainForm : Form
{
    const string DummyTag = "\0dummy";

    readonly ToolTip tips = new();
    readonly SplitContainer split = new();
    readonly TreeView tree = new();
    readonly ThumbGrid grid = new();
    readonly TextBox addr = new();
    readonly ComboBox cmbSort = new();
    readonly TrackBar trk = new();
    readonly CheckBox chkNames = new();
    readonly StatusStrip status = new();
    readonly ToolStripStatusLabel stLeft = new();
    readonly ToolStripStatusLabel stMid = new();
    readonly ToolStripStatusLabel stRight = new();
    readonly ToolStripStatusLabel stUpdate = new();
    readonly ContextMenuStrip ctx = new();
    readonly System.Windows.Forms.Timer uiTimer = new() { Interval = 400 };
    readonly System.Windows.Forms.Timer updTimer = new() { Interval = 6 * 60 * 60 * 1000 };
    readonly BackgroundIndexer indexer;
    readonly string startArg;
    readonly string startupLastFolder;   // read before any UI event can overwrite it

    Button btnBack, btnFwd, btnUp, btnRefresh, btnDesc, btnSettings;

    volatile FolderSession session;
    string currentPath;
    readonly List<string> back = new();
    readonly List<string> fwd = new();
    CancellationTokenSource openCts;
    int openTicket;
    bool suppressTreeSelect;
    string pendingOpenFile;
    string pendingSelectName;   // select this item after the next folder load
    int pendingSelectGrid = -1; // ...or this grid index if the name is gone
    UpdateInfo pendingUpdate;
    bool splitReady;            // splitter restored; safe to save its position
    bool userDragsSplitter;     // set by SplitterMoving, which fires only for a user drag
    int treeWidth96 = -1;       // tree width the user chose, in 96-dpi units
    int ctxIndex = -1;
    bool cacheCleared;
    DateTime lastClean = DateTime.MinValue;

    public MainForm(string startArg)
    {
        this.startArg = startArg;
        var s = AppSettings.Current;
        startupLastFolder = s.LastFolder;

        Text = L.T("app.title");
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);
        MinimumSize = new Size(640, 420);
        AllowDrop = true;
        KeyPreview = false;
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath); } catch { }

        RestoreWindowBounds(s);
        BuildUi(s);

        indexer = new BackgroundIndexer(() => session?.IsBusy ?? false);

        DragEnter += OnDragEnterFiles;
        DragDrop += OnDropFiles;

        uiTimer.Tick += (_, _) => OnUiTimer();
        uiTimer.Start();
    }

    // ================================================================ UI construction

    void RestoreWindowBounds(AppSettings s)
    {
        if (s.WindowX != int.MinValue)
        {
            var r = new Rectangle(s.WindowX, s.WindowY, s.WindowW, s.WindowH);
            if (Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(r)))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = r;
            }
        }
        else
        {
            Size = new Size(s.WindowW, s.WindowH);
            StartPosition = FormStartPosition.CenterScreen;
        }
        if (s.Maximized) WindowState = FormWindowState.Maximized;
    }

    void BuildUi(AppSettings s)
    {
        // ---- top bar
        var bar = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = Theme.Bar };
        var tl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Theme.Bar,
            Margin = Padding.Empty
        };
        tl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var left = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Dock = DockStyle.Fill, BackColor = Theme.Bar };
        btnBack = Theme.FlatButton("◀", L.T("tip.back"), tips, (_, _) => GoBack());
        btnFwd = Theme.FlatButton("▶", L.T("tip.forward"), tips, (_, _) => GoForward());
        btnUp = Theme.FlatButton("▲", L.T("tip.up"), tips, (_, _) => GoUp());
        btnRefresh = Theme.FlatButton("⟳", L.T("tip.refresh"), tips, (_, _) => Reload());
        left.Controls.AddRange(new Control[] { btnBack, btnFwd, btnUp, btnRefresh });

        addr.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        addr.Margin = new Padding(8, 10, 8, 0);
        addr.BorderStyle = BorderStyle.FixedSingle;
        addr.BackColor = Theme.Input;
        addr.ForeColor = Theme.Text;
        addr.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                OpenFolder(addr.Text, true);
                grid.Focus();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                addr.Text = currentPath ?? "";
                grid.Focus();
            }
        };

        var right = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Dock = DockStyle.Fill, BackColor = Theme.Bar };
        cmbSort.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbSort.FlatStyle = FlatStyle.Flat;
        cmbSort.BackColor = Theme.Input;
        cmbSort.ForeColor = Theme.Text;
        cmbSort.Width = 110;
        cmbSort.Margin = new Padding(4, 8, 2, 0);
        cmbSort.Items.AddRange(new object[] { L.T("sort.name"), L.T("sort.date"), L.T("sort.size") });
        cmbSort.SelectedIndex = Math.Clamp(s.SortMode, 0, 2);
        cmbSort.SelectedIndexChanged += (_, _) =>
        {
            AppSettings.Current.SortMode = cmbSort.SelectedIndex;
            Reload();
        };

        btnDesc = Theme.FlatButton(s.SortDescending ? "▼" : "▲", L.T("tip.desc"), tips, (_, _) =>
        {
            var st = AppSettings.Current;
            st.SortDescending = !st.SortDescending;
            btnDesc.Text = st.SortDescending ? "▼" : "▲";
            Reload();
        }, 32);
        btnDesc.Font = new Font("Segoe UI Symbol", 9f);

        trk.AutoSize = false;
        trk.Width = 120;
        trk.Height = 30;
        trk.Margin = new Padding(6, 8, 4, 0);
        trk.Minimum = 96;
        trk.Maximum = 256;
        trk.TickStyle = TickStyle.None;
        trk.SmallChange = 16;
        trk.LargeChange = 32;
        trk.Value = Math.Clamp(s.TileSize, 96, 256);
        trk.BackColor = Theme.Bar;
        tips.SetToolTip(trk, L.T("tip.size"));
        trk.ValueChanged += (_, _) =>
        {
            grid.TileSize = trk.Value;
            AppSettings.Current.TileSize = trk.Value;
        };

        chkNames.Text = L.T("chk.names");
        chkNames.AutoSize = true;
        chkNames.Margin = new Padding(6, 12, 6, 0);
        chkNames.ForeColor = Theme.Text;
        chkNames.Checked = s.ShowNames;
        chkNames.CheckedChanged += (_, _) =>
        {
            grid.ShowNames = chkNames.Checked;
            AppSettings.Current.ShowNames = chkNames.Checked;
        };

        btnSettings = Theme.FlatButton("⚙", L.T("tip.settings"), tips, (_, _) => ShowSettings());
        right.Controls.AddRange(new Control[] { cmbSort, btnDesc, trk, chkNames, btnSettings });

        tl.Controls.Add(left, 0, 0);
        tl.Controls.Add(addr, 1, 0);
        tl.Controls.Add(right, 2, 0);
        bar.Controls.Add(tl);

        // ---- status strip
        status.Renderer = new DarkRenderer();
        status.BackColor = Theme.Bar;
        status.ForeColor = Theme.Text;
        status.SizingGrip = false;
        foreach (var l in new[] { stLeft, stMid, stRight })
        {
            l.ForeColor = Theme.Text;
            l.TextAlign = ContentAlignment.MiddleLeft;
        }
        stMid.Spring = true;
        stUpdate.IsLink = true;
        stUpdate.LinkBehavior = LinkBehavior.HoverUnderline;
        stUpdate.LinkColor = Color.FromArgb(110, 180, 255);
        stUpdate.ActiveLinkColor = Color.FromArgb(150, 205, 255);
        stUpdate.VisitedLinkColor = Color.FromArgb(110, 180, 255);
        stUpdate.Visible = false;
        stUpdate.Click += (_, _) => ShowUpdateDialog(pendingUpdate);
        status.Items.AddRange(new ToolStripItem[] { stLeft, stMid, stUpdate, stRight });

        // ---- tree
        tree.Dock = DockStyle.Fill;
        tree.BackColor = Theme.Back;
        tree.ForeColor = Theme.Text;
        tree.BorderStyle = BorderStyle.None;
        tree.HideSelection = false;
        tree.ShowLines = false;
        tree.FullRowSelect = true;
        tree.ItemHeight = 22;
        tree.HotTracking = true;
        tree.ImageList = MakeIcons();
        tree.AllowDrop = true;
        tree.DragEnter += OnDragEnterFiles;
        tree.DragDrop += OnDropFiles;
        tree.BeforeExpand += (_, e) => LoadChildren(e.Node);
        tree.AfterSelect += (_, e) =>
        {
            // Only real user clicks / keys. Windows auto-selects the first node when an empty-selection
            // tree gets focus (Action = Unknown) - that used to open "Desktop" and overwrite the last folder.
            if (suppressTreeSelect) return;
            if (e.Action != TreeViewAction.ByMouse && e.Action != TreeViewAction.ByKeyboard) return;
            if (e.Node.Tag is string p && p != DummyTag && Directory.Exists(p)) OpenFolder(p, true);
        };
        tree.HandleCreated += (_, _) => Theme.DarkControl(tree);

        // ---- grid
        grid.Dock = DockStyle.Fill;
        grid.TileSize = trk.Value;
        grid.ShowNames = s.ShowNames;
        grid.AllowDrop = true;
        grid.DragEnter += OnDragEnterFiles;
        grid.DragDrop += OnDropFiles;
        grid.ItemActivated += OnItemActivated;
        grid.SelectionChanged += OnSelectionChanged;
        grid.ItemContext += ShowItemMenu;
        grid.DeleteRequested += DeleteSelected;

        // ---- split
        split.Dock = DockStyle.Fill;
        split.Orientation = Orientation.Vertical;
        split.BackColor = Theme.Border;
        split.FixedPanel = FixedPanel.Panel1;
        split.SplitterWidth = 3;
        split.Panel1.BackColor = Theme.Back;
        split.Panel2.BackColor = Theme.GridBack;
        split.Panel1.Controls.Add(tree);
        split.Panel2.Controls.Add(grid);

        // Remember only widths the user sets by dragging. Programmatic changes (initial layout,
        // clamping while the window is minimized or shrunk) must not overwrite the saved width.
        split.SplitterMoving += (_, _) => userDragsSplitter = true;
        split.SplitterMoved += (_, _) =>
        {
            if (!splitReady || !userDragsSplitter) return;
            userDragsSplitter = false;
            treeWidth96 = Math.Max(60, (int)Math.Round(split.SplitterDistance * 96f / DeviceDpi));
        };

        // docking order: fill first, then bottom, then top
        Controls.Add(split);
        Controls.Add(status);
        Controls.Add(bar);

        BuildContextMenu();
        BuildTree();
        ActiveControl = grid;
    }

    static ImageList MakeIcons()
    {
        var il = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(16, 16) };

        Bitmap Draw(Action<Graphics> paint)
        {
            var b = new Bitmap(16, 16);
            using var g = Graphics.FromImage(b);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            paint(g);
            return b;
        }

        il.Images.Add("folder", Draw(g =>
        {
            using var tab = new SolidBrush(Color.FromArgb(205, 160, 45));
            using var body = new SolidBrush(Color.FromArgb(245, 205, 85));
            g.FillRectangle(tab, 1, 3, 6, 4);
            g.FillRectangle(body, 1, 5, 14, 9);
        }));
        il.Images.Add("drive", Draw(g =>
        {
            using var body = new SolidBrush(Color.FromArgb(170, 175, 185));
            using var led = new SolidBrush(Color.FromArgb(80, 200, 100));
            g.FillRectangle(body, 1, 6, 14, 7);
            g.FillRectangle(led, 11, 10, 2, 2);
        }));
        il.Images.Add("star", Draw(g =>
        {
            using var f = new Font("Segoe UI Symbol", 10f);
            using var br = new SolidBrush(Color.FromArgb(255, 200, 60));
            g.DrawString("★", f, br, -1, 0);
        }));
        return il;
    }

    void BuildContextMenu()
    {
        ctx.Renderer = new DarkRenderer();
        ctx.BackColor = Theme.Bar;
        ctx.ForeColor = Theme.Text;

        var open = new ToolStripMenuItem(L.T("ctx.open")) { ForeColor = Theme.Text };
        open.Click += (_, _) => { if (ctxIndex >= 0) OnItemActivated(ctxIndex); };
        var explorer = new ToolStripMenuItem(L.T("ctx.explorer")) { ForeColor = Theme.Text };
        explorer.Click += (_, _) => ShowInExplorer(ctxIndex);
        var copyPath = new ToolStripMenuItem(L.T("ctx.copypath")) { ForeColor = Theme.Text };
        copyPath.Click += (_, _) => CopyPaths();
        var copyFile = new ToolStripMenuItem(L.T("ctx.copyfile")) { ForeColor = Theme.Text };
        copyFile.Click += (_, _) => CopyFiles();
        var delete = new ToolStripMenuItem(L.T("ctx.delete")) { ForeColor = Theme.Text, ShortcutKeyDisplayString = "Del" };
        delete.Click += (_, _) => DeleteSelected(false);
        var refresh = new ToolStripMenuItem(L.T("ctx.refresh")) { ForeColor = Theme.Text, ShortcutKeyDisplayString = "F5" };
        refresh.Click += (_, _) => Reload();

        ctx.Items.AddRange(new ToolStripItem[]
        {
            open, new ToolStripSeparator(), explorer, copyPath, copyFile, new ToolStripSeparator(),
            delete, new ToolStripSeparator(), refresh
        });
        ctx.Opening += (_, _) =>
        {
            var s = session;
            delete.Enabled = s != null && grid.SelectedIndices.Any(g => !s.IsFolder(g));
        };
    }

    // ================================================================ folder tree

    void BuildTree()
    {
        var s = AppSettings.Current;
        tree.BeginUpdate();
        tree.Nodes.Clear();

        if (s.IndexFolders.Count > 0)
        {
            var fav = new TreeNode(L.T("tree.indexed")) { ImageKey = "star", SelectedImageKey = "star" };
            foreach (var f in s.IndexFolders)
                if (Directory.Exists(f)) fav.Nodes.Add(MakeFolderNode(f, f));
            if (fav.Nodes.Count > 0) { tree.Nodes.Add(fav); fav.Expand(); }
        }

        AddSpecial(Environment.SpecialFolder.Desktop, "tree.desktop");
        AddSpecial(Environment.SpecialFolder.MyPictures, "tree.pictures");
        AddSpecial(Environment.SpecialFolder.MyDocuments, "tree.documents");
        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloads)) tree.Nodes.Add(MakeFolderNode(downloads, L.T("tree.downloads")));

        foreach (var d in DriveInfo.GetDrives())
        {
            string label = "";
            try
            {
                if (d.DriveType == DriveType.Network) label = L.T("tree.network");
                else if (d.IsReady) label = d.VolumeLabel;
            }
            catch { }
            string root = d.Name;
            string name = root.TrimEnd('\\');
            var node = new TreeNode(string.IsNullOrEmpty(label) ? name : $"{name} ({label})")
            {
                Tag = root,
                ImageKey = "drive",
                SelectedImageKey = "drive"
            };
            node.Nodes.Add(new TreeNode("…") { Tag = DummyTag });
            tree.Nodes.Add(node);
        }
        tree.EndUpdate();
    }

    void AddSpecial(Environment.SpecialFolder sf, string key)
    {
        string p = Environment.GetFolderPath(sf);
        if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) tree.Nodes.Add(MakeFolderNode(p, L.T(key)));
    }

    static TreeNode MakeFolderNode(string path, string text = null)
    {
        string name = text ?? Path.GetFileName(path.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(name)) name = path;
        var n = new TreeNode(name) { Tag = path, ImageKey = "folder", SelectedImageKey = "folder" };
        n.Nodes.Add(new TreeNode("…") { Tag = DummyTag });
        return n;
    }

    static void LoadChildren(TreeNode node)
    {
        if (node.Nodes.Count != 1 || !(node.Nodes[0].Tag is string t && t == DummyTag)) return;
        node.Nodes.Clear();
        if (node.Tag is not string path) return;
        try
        {
            var opts = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
            };
            var names = new DirectoryInfo(path).EnumerateDirectories("*", opts).Select(d => d.Name).ToList();
            names.Sort(NaturalComparer.Instance);
            foreach (var n in names) node.Nodes.Add(MakeFolderNode(Path.Combine(path, n), n));
        }
        catch { }
    }

    /// <summary>Expands the tree down to the given folder without re-opening it.</summary>
    void SyncTree(string path)
    {
        suppressTreeSelect = true;
        try
        {
            string full = Path.GetFullPath(path);
            string root = Path.GetPathRoot(full) ?? "";
            TreeNode cur = null;
            foreach (TreeNode n in tree.Nodes)
                if (n.Tag is string t && string.Equals(t, root, StringComparison.OrdinalIgnoreCase)) { cur = n; break; }
            if (cur == null) return;

            string rest = full.Substring(root.Length);
            foreach (var seg in rest.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                cur.Expand();
                TreeNode next = null;
                foreach (TreeNode c in cur.Nodes)
                    if (string.Equals(c.Text, seg, StringComparison.OrdinalIgnoreCase)) { next = c; break; }
                if (next == null) break;
                cur = next;
            }
            tree.SelectedNode = cur;
            cur.EnsureVisible();
        }
        catch { }
        finally { suppressTreeSelect = false; }
    }

    // ================================================================ opening folders

    void OpenFolder(string path, bool push) => _ = OpenFolderAsync(path, push);

    void Reload()
    {
        if (currentPath != null) OpenFolder(currentPath, false);
    }

    async Task OpenFolderAsync(string path, bool push)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { path = Path.GetFullPath(path.Trim().Trim('"')); }
        catch { return; }

        if (!Directory.Exists(path))
        {
            stLeft.Text = L.T("err.nofolder");
            return;
        }

        if (push && currentPath != null && !string.Equals(currentPath, path, StringComparison.OrdinalIgnoreCase))
        {
            back.Add(currentPath);
            fwd.Clear();
        }
        currentPath = path;
        addr.Text = path;
        if (!string.Equals(AppSettings.Current.LastFolder, path, StringComparison.OrdinalIgnoreCase))
        {
            AppSettings.Current.LastFolder = path;
            AppSettings.Current.Save();
        }
        UpdateNav();

        string selectName = pendingSelectName;
        int selectGrid = pendingSelectGrid;
        pendingSelectName = null;
        pendingSelectGrid = -1;
        SyncTree(path);

        var old = session;
        session = null;
        grid.SetSession(null);
        old?.Dispose();

        openCts?.Cancel();
        openCts = new CancellationTokenSource();
        var ct = openCts.Token;
        int ticket = ++openTicket;
        stLeft.Text = L.T("status.loading");
        stMid.Text = "";
        stRight.Text = "";

        var st = AppSettings.Current;
        int mode = st.SortMode;
        bool desc = st.SortDescending;

        FolderSession ns;
        try
        {
            ns = await Task.Run(() => FolderSession.Open(path, mode, desc, ct), ct);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            if (ticket == openTicket) stLeft.Text = L.F("status.error", ex.Message);
            return;
        }

        if (ticket != openTicket || IsDisposed)
        {
            ns.Dispose();
            return;
        }

        session = ns;
        ns.Start();
        grid.SetSession(ns);
        stLeft.Text = L.F("status.files", ns.Files.Length, Fmt.Size(ns.Files.Sum(f => f.Size)));
        OnUiTimer();
        grid.Focus();

        int sel = -1;
        if (selectName != null)
            for (int g = 0; g < ns.ItemCount; g++)
                if (string.Equals(ns.ItemName(g), selectName, StringComparison.OrdinalIgnoreCase)) { sel = g; break; }
        if (sel < 0 && selectGrid >= 0 && ns.ItemCount > 0) sel = Math.Min(selectGrid, ns.ItemCount - 1);
        if (sel >= 0) grid.SelectIndex(sel);

        if (pendingOpenFile != null)
        {
            string name = pendingOpenFile;
            pendingOpenFile = null;
            int idx = Array.FindIndex(ns.Files, f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                grid.SelectIndex(ns.GridIndexOfFile(idx));
                OpenViewer(idx);
            }
        }
    }

    void GoBack()
    {
        if (back.Count == 0) return;
        if (currentPath != null) fwd.Add(currentPath);
        string p = back[^1];
        back.RemoveAt(back.Count - 1);
        OpenFolder(p, false);
    }

    void GoForward()
    {
        if (fwd.Count == 0) return;
        if (currentPath != null) back.Add(currentPath);
        string p = fwd[^1];
        fwd.RemoveAt(fwd.Count - 1);
        OpenFolder(p, false);
    }

    void GoUp()
    {
        if (currentPath == null) return;
        var parent = Directory.GetParent(currentPath);
        if (parent == null) return;
        pendingSelectName = Path.GetFileName(currentPath.TrimEnd('\\', '/'));
        OpenFolder(parent.FullName, true);
    }

    void UpdateNav()
    {
        btnBack.Enabled = back.Count > 0;
        btnFwd.Enabled = fwd.Count > 0;
        btnUp.Enabled = currentPath != null && Directory.GetParent(currentPath) != null;
    }

    // ================================================================ viewer & item actions

    void OnItemActivated(int g)
    {
        var s = session;
        if (s == null || g < 0 || g >= s.ItemCount) return;
        if (s.IsFolder(g)) OpenFolder(s.Folders[g].FullPath, true);
        else OpenViewer(s.FileIndex(g));
    }

    void OpenViewer(int fileIndex)
    {
        var s = session;
        if (s == null || s.Files.Length == 0) return;
        using var v = new ViewerForm(s, fileIndex, Screen.FromControl(this));
        v.ShowDialog(this);

        if (v.AnyDeleted)
        {
            // indices shift after a deletion: reload and select the photo the viewer ended on
            pendingSelectName = s.Files[v.CurrentIndex].Name;
            pendingSelectGrid = s.GridIndexOfFile(v.CurrentIndex);
            Reload();
            return;
        }
        grid.SelectIndex(s.GridIndexOfFile(v.CurrentIndex));
        grid.Focus();
    }

    void OnSelectionChanged()
    {
        var s = session;
        int n = grid.SelectedCount;
        int i = grid.FocusedIndex;
        string prefix = n > 1 ? L.F("status.selected", n) + "   ·   " : "";
        if (s == null || i < 0 || i >= s.ItemCount)
        {
            stRight.Text = n > 1 ? L.F("status.selected", n) : "";
            return;
        }
        if (s.IsFolder(i))
        {
            stRight.Text = prefix + s.Folders[i].Name;
            return;
        }
        var f = s.Files[s.FileIndex(i)];
        stRight.Text = prefix + $"{f.Name}   ·   {Fmt.Size(f.Size)}   ·   {Fmt.Date(f.Modified)}";
    }

    void ShowItemMenu(int index, Point screenPos)
    {
        ctxIndex = index;
        ctx.Show(screenPos);
    }

    List<string> SelectedPaths()
    {
        var s = session;
        if (s == null) return new List<string>();
        return grid.SelectedIndices.Where(g => g >= 0 && g < s.ItemCount).Select(s.ItemPath).ToList();
    }

    void ShowInExplorer(int g)
    {
        var s = session;
        if (s == null || g < 0 || g >= s.ItemCount) return;
        string p = s.ItemPath(g);
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{p}\"") { UseShellExecute = true }); } catch { }
    }

    void CopyPaths()
    {
        var paths = SelectedPaths();
        if (paths.Count == 0) return;
        try { Clipboard.SetText(string.Join(Environment.NewLine, paths)); } catch { }
    }

    void CopyFiles()
    {
        var paths = SelectedPaths();
        if (paths.Count == 0) return;
        try
        {
            var sc = new System.Collections.Specialized.StringCollection();
            sc.AddRange(paths.ToArray());
            Clipboard.SetFileDropList(sc);
        }
        catch { }
    }

    /// <summary>Del = Recycle Bin, Shift+Del = permanently (Windows asks). Folders in the selection are ignored.</summary>
    void DeleteSelected(bool permanent)
    {
        var s = session;
        if (s == null) return;
        var sel = grid.SelectedIndices.Where(g => g >= 0 && g < s.ItemCount && !s.IsFolder(g)).ToList();
        if (sel.Count == 0) return;

        // keep the cursor where it was: the first item after the deleted block, else the one before it
        var set = new HashSet<int>(sel);
        int keep = -1;
        for (int g = sel[0] + 1; g < s.ItemCount; g++) if (!set.Contains(g)) { keep = g; break; }
        if (keep < 0) for (int g = sel[0] - 1; g >= 0; g--) if (!set.Contains(g)) { keep = g; break; }

        ShellOps.Delete(this, sel.Select(s.ItemPath).ToList(), permanent);

        pendingSelectName = keep >= 0 ? s.ItemName(keep) : null;
        pendingSelectGrid = sel[0];
        Reload();
    }

    // ================================================================ settings

    void ShowSettings()
    {
        cacheCleared = false;
        using var dlg = new SettingsForm(ClearCacheNow, () => UpdateService.CheckAsync());
        var result = dlg.ShowDialog(this);

        if (result == DialogResult.OK)
        {
            BuildTree();
            if (currentPath != null) SyncTree(currentPath);
            indexer.Reconfigure();
            Task.Run(() => CacheCleaner.Clean(AppSettings.Current));
        }
        if (cacheCleared) Reload();

        if (dlg.FoundUpdate != null)
        {
            ShowUpdateAvailable(dlg.FoundUpdate);
            ShowUpdateDialog(dlg.FoundUpdate);
        }
    }

    // ================================================================ updates

    async Task CheckUpdatesBackgroundAsync(int delayMs)
    {
        try
        {
            if (delayMs > 0) await Task.Delay(delayMs);
            if (IsDisposed || !AppSettings.Current.AutoUpdateCheck) return;
            var info = await UpdateService.CheckAsync();
            if (IsDisposed || info == null) return;
            if (string.Equals(info.Tag, AppSettings.Current.SkippedVersion, StringComparison.OrdinalIgnoreCase)) return;
            ShowUpdateAvailable(info);
        }
        catch { }   // offline, GitHub limit etc. - try again later
    }

    void ShowUpdateAvailable(UpdateInfo info)
    {
        pendingUpdate = info;
        stUpdate.Text = L.F("upd.available", info.VersionText);
        stUpdate.Visible = true;
    }

    void ShowUpdateDialog(UpdateInfo info)
    {
        if (info == null) return;
        using var dlg = new UpdateForm(info);
        var r = dlg.ShowDialog(this);

        if (dlg.SkipRequested)
        {
            AppSettings.Current.SkippedVersion = info.Tag;
            AppSettings.Current.Save();
            stUpdate.Visible = false;
            pendingUpdate = null;
            return;
        }
        if (r != DialogResult.OK || dlg.DownloadedFile == null) return;

        try
        {
            UpdateService.Install(dlg.DownloadedFile);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L.F("upd.failed", ex.Message), L.T("upd.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Close();   // Program starts the new version after the window is closed
    }

    /// <summary>Releases the cache files held by the open folder so they can be deleted.</summary>
    void ClearCacheNow()
    {
        cacheCleared = true;
        var s = session;
        session = null;
        grid.SetSession(null);
        s?.DisposeSync();
    }

    // ================================================================ timers / status

    void OnUiTimer()
    {
        var s = session;
        string mid = "";
        if (s != null && s.MissingAtOpen > 0 && s.Done < s.MissingAtOpen)
        {
            mid = L.F("status.index", Math.Min(s.Done, s.MissingAtOpen), s.MissingAtOpen);
        }
        else if (indexer.Active)
        {
            string folder = indexer.CurrentFolder ?? "";
            string name = Path.GetFileName(folder.TrimEnd('\\', '/'));
            mid = L.F("status.bg", string.IsNullOrEmpty(name) ? folder : name);
        }
        else if (s != null && s.MissingAtOpen > 0)
        {
            mid = L.T("status.ready");
        }
        if (stMid.Text != mid) stMid.Text = mid;

        if ((DateTime.UtcNow - lastClean).TotalHours >= 6)
        {
            lastClean = DateTime.UtcNow;
            Task.Run(() => { Thread.Sleep(4000); CacheCleaner.Clean(AppSettings.Current); });
        }
    }

    // ================================================================ drag & drop

    void OnDragEnterFiles(object sender, DragEventArgs e)
    {
        e.Effect = e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
    }

    void OnDropFiles(object sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        string p = paths[0];
        if (Directory.Exists(p)) OpenFolder(p, true);
        else if (File.Exists(p))
        {
            pendingOpenFile = Path.GetFileName(p);
            OpenFolder(Path.GetDirectoryName(p), true);
        }
    }

    // ================================================================ keys, lifetime

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F5: Reload(); return true;
            case Keys.Alt | Keys.Left: GoBack(); return true;
            case Keys.Alt | Keys.Right: GoForward(); return true;
            case Keys.Alt | Keys.Up: GoUp(); return true;
            case Keys.Control | Keys.L:
            case Keys.F4:
                addr.Focus();
                addr.SelectAll();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(this);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var s = AppSettings.Current;
        // Min sizes are set only now, when the container already has its real width
        // (setting them earlier throws "SplitterDistance must be between ...").
        try
        {
            int w = split.Width;
            split.Panel1MinSize = Math.Min(140, Math.Max(25, w / 4));
            split.Panel2MinSize = Math.Min(300, Math.Max(25, w / 3));
            int max = Math.Max(split.Panel1MinSize, w - split.Panel2MinSize - split.SplitterWidth);
            int want = (int)Math.Round(s.TreeWidth * DeviceDpi / 96f);   // stored in 96-dpi units
            split.SplitterDistance = Math.Clamp(want, split.Panel1MinSize, max);
            treeWidth96 = s.TreeWidth;   // keep the saved value even if the window is too small to show it fully
            splitReady = true;
        }
        catch { }

        string start = null;
        if (!string.IsNullOrEmpty(startArg))
        {
            try
            {
                string full = Path.GetFullPath(startArg.Trim('"'));
                if (Directory.Exists(full)) start = full;
                else if (File.Exists(full))
                {
                    start = Path.GetDirectoryName(full);
                    pendingOpenFile = Path.GetFileName(full);
                }
            }
            catch { }
        }
        if (start == null)
        {
            if (!string.IsNullOrEmpty(startupLastFolder) && Directory.Exists(startupLastFolder)) start = startupLastFolder;
            else
            {
                start = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (!Directory.Exists(start)) start = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
        }
        OpenFolder(start, false);
        indexer.Start();

        _ = CheckUpdatesBackgroundAsync(8000);
        updTimer.Tick += (_, _) => _ = CheckUpdatesBackgroundAsync(0);
        updTimer.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        var s = AppSettings.Current;
        s.Maximized = WindowState == FormWindowState.Maximized;
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        s.WindowX = b.X; s.WindowY = b.Y; s.WindowW = b.Width; s.WindowH = b.Height;
        if (treeWidth96 > 0) s.TreeWidth = treeWidth96;
        s.Save();

        uiTimer.Stop();
        updTimer.Stop();
        openCts?.Cancel();
        var sess = session;
        session = null;
        sess?.DisposeSync();   // flushes the cache index to disk
        indexer.Stop();
    }
}

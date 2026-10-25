using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Skimmer;

sealed class MainForm : Form, IThemeAware
{
    const string DummyTag = "\0dummy";

    readonly ToolTip tips = new();
    readonly SplitContainer split = new();
    readonly TreeView tree = new();
    readonly ThumbGrid grid = new();
    readonly BreadcrumbBar addr = new();
    readonly ComboBox cmbSort = new();
    readonly TrackBar trk = new();
    readonly CheckBox chkNames = new();
    readonly StatusStrip status = new();
    readonly ToolStripStatusLabel stState = new();   // indexing / loading / errors
    readonly ToolStripStatusLabel stInfo = new();    // selected item
    readonly ToolStripStatusLabel stCount = new();   // files and size
    readonly ToolStripStatusLabel stUpdate = new();  // update zone (right)
    readonly UpdateBanner banner = new();
    readonly SystemThemeWatcher systemTheme = new();
    readonly ContextMenuStrip ctx = new();
    readonly System.Windows.Forms.Timer uiTimer = new() { Interval = 400 };
    readonly System.Windows.Forms.Timer updTimer = new() { Interval = 60 * 60 * 1000 };   // hourly tick, checks once a day
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
    bool stateFromTimer;        // the state zone currently shows indexing progress
    bool splitReady;            // splitter restored; safe to save its position

    // live updates of the open folder
    FileSystemWatcher watcher;
    readonly System.Windows.Forms.Timer fsTimer = new() { Interval = 1000 };
    volatile bool fsDirty;
    bool refreshing;
    bool viewerOpen;
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

        Text = Lang.Get("app.title");
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Font = UiTheme.Default;
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
        fsTimer.Tick += (_, _) => OnFsTimer();
        uiTimer.Start();

        // Brand book: one theme system for every window, switched on the fly.
        UiStyle.Attach(this);
        systemTheme.Changed += (_, _) =>
        {
            try { BeginInvoke(new Action(() => ThemeService.SetSystemTheme(systemTheme.IsLight))); } catch { }
        };
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
        // ---- toolbar: 42 px, icon buttons 36 x 30 (brand book: "Панель инструментов")
        var bar = UiStyle.Role(new Panel { Dock = DockStyle.Top, Height = 42 }, UiRole.Toolbar);
        var tl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty
        };
        tl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var left = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Dock = DockStyle.Fill };
        btnBack = UiStyle.IconButton("◀", Lang.Get("tip.back"), tips, (_, _) => GoBack());
        btnFwd = UiStyle.IconButton("▶", Lang.Get("tip.forward"), tips, (_, _) => GoForward());
        btnUp = UiStyle.IconButton("▲", Lang.Get("tip.up"), tips, (_, _) => GoUp());
        btnRefresh = UiStyle.IconButton("⟳", Lang.Get("tip.refresh"), tips, (_, _) => Reload());
        left.Controls.AddRange(new Control[] { btnBack, btnFwd, btnUp, btnRefresh });

        addr.Dock = DockStyle.Fill;
        addr.Margin = new Padding(8, 6, 8, 6);
        addr.Navigate += p =>
        {
            OpenFolder(p, true);
            grid.Focus();
        };
        addr.EditFinished += () => grid.Focus();

        var right = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Dock = DockStyle.Fill };
        cmbSort.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbSort.FlatStyle = FlatStyle.Flat;
        cmbSort.Width = 130;
        cmbSort.Margin = new Padding(4, 8, 2, 0);
        cmbSort.Items.AddRange(new object[] { Lang.Get("sort.name"), Lang.Get("sort.date"), Lang.Get("sort.size") });
        cmbSort.SelectedIndex = Math.Clamp(s.SortMode, 0, 2);
        cmbSort.SelectedIndexChanged += (_, _) =>
        {
            AppSettings.Current.SortMode = cmbSort.SelectedIndex;
            Reload();
        };

        btnDesc = UiStyle.IconButton(s.SortDescending ? "▼" : "▲", Lang.Get("tip.desc"), tips, (_, _) =>
        {
            var st = AppSettings.Current;
            st.SortDescending = !st.SortDescending;
            btnDesc.Text = st.SortDescending ? "▼" : "▲";
            Reload();
        });

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
        tips.SetToolTip(trk, Lang.Get("tip.size"));
        trk.ValueChanged += (_, _) =>
        {
            grid.TileSize = trk.Value;
            AppSettings.Current.TileSize = trk.Value;
        };

        chkNames.Text = Lang.Get("chk.names");
        chkNames.AutoSize = true;
        chkNames.Margin = new Padding(6, 12, 6, 0);
        chkNames.Checked = s.ShowNames;
        chkNames.CheckedChanged += (_, _) =>
        {
            grid.ShowNames = chkNames.Checked;
            AppSettings.Current.ShowNames = chkNames.Checked;
        };

        btnSettings = UiStyle.IconButton("⚙", Lang.Get("tip.settings"), tips, (_, _) => ShowSettings());
        right.Controls.AddRange(new Control[] { cmbSort, btnDesc, trk, chkNames, btnSettings });

        tl.Controls.Add(left, 0, 0);
        tl.Controls.Add(addr, 1, 0);
        tl.Controls.Add(right, 2, 0);
        bar.Controls.Add(tl);

        // ---- update banner (brand book: accent strip right under the toolbar)
        banner.UpdateClicked += () => ShowUpdateDialog(banner.Info);
        banner.WhatsNewClicked += () => OpenUrl(banner.Info?.PageUrl);
        banner.Dismissed += () =>
        {
            if (banner.Info != null)
            {
                UpdateState.Current.DismissedVersion = banner.Info.Tag;
                UpdateState.Current.Save();
            }
            banner.HideBanner();
        };

        // ---- status strip: state, explanation, counter, update zone on the right
        status.SizingGrip = false;
        foreach (var l in new[] { stState, stInfo, stCount })
            l.TextAlign = ContentAlignment.MiddleLeft;
        stInfo.Spring = true;
        stUpdate.IsLink = true;
        stUpdate.LinkBehavior = LinkBehavior.HoverUnderline;
        stUpdate.Visible = false;
        stUpdate.Click += (_, _) => ShowUpdateDialog(pendingUpdate);
        status.Items.AddRange(new ToolStripItem[] { stState, stInfo, stCount, stUpdate });

        // ---- tree
        tree.Dock = DockStyle.Fill;
        tree.BorderStyle = BorderStyle.None;
        tree.HideSelection = false;
        tree.ShowLines = false;
        tree.FullRowSelect = true;
        tree.HotTracking = true;
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
        grid.BackgroundContext += ShowBackgroundMenu;
        grid.DeleteRequested += DeleteSelected;

        // ---- split
        split.Dock = DockStyle.Fill;
        split.Orientation = Orientation.Vertical;
        split.FixedPanel = FixedPanel.Panel1;
        split.SplitterWidth = 3;
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

        // docking order: fill first, then bottom, then the banner, then the toolbar on top
        Controls.Add(split);
        Controls.Add(status);
        Controls.Add(banner);
        Controls.Add(bar);

        BuildContextMenu();
        tree.ImageList = MakeIcons();
        BuildTree();
        ActiveControl = grid;
    }

    /// <summary>Colours that the generic theme pass does not know about (IThemeAware, called after every theme change).</summary>
    public void ApplyTheme()
    {
        var p = ThemeService.Palette;
        split.BackColor = p.Border;
        split.Panel1.BackColor = p.SidebarBack;
        split.Panel2.BackColor = p.PanelBack;

        tree.Font = UiTheme.Chrome;
        tree.BackColor = p.SidebarBack;
        tree.ForeColor = p.Fore;
        tree.ItemHeight = (int)Math.Round(ThemeService.Density.RowHeight() * DeviceDpi / 96f);
        var oldIcons = tree.ImageList;
        tree.ImageList = MakeIcons();
        oldIcons?.Dispose();

        ThemeService.Apply(ctx);
    }

    /// <summary>Tree icons in the palette colours (folder, drive, indexed-folders star).</summary>
    static ImageList MakeIcons()
    {
        var p = ThemeService.Palette;
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
            using var tab = new SolidBrush(ThemeDraw.Blend(p.FolderIcon, p.Fore, 0.25f));
            using var body = new SolidBrush(p.FolderIcon);
            g.FillRectangle(tab, 1, 3, 6, 4);
            g.FillRectangle(body, 1, 5, 14, 9);
        }));
        il.Images.Add("drive", Draw(g =>
        {
            using var body = new SolidBrush(p.Muted);
            using var led = new SolidBrush(UiTheme.Success);
            g.FillRectangle(body, 1, 6, 14, 7);
            g.FillRectangle(led, 11, 10, 2, 2);
        }));
        il.Images.Add("star", Draw(g =>
        {
            using var f = new Font("Segoe UI Symbol", 10f);
            using var br = new SolidBrush(p.Accent);
            g.DrawString("★", f, br, -1, 0);
        }));
        return il;
    }

    /// <summary>Menu for the empty part of the grid (the item menu is Explorer's own, see ShowItemMenu).</summary>
    void BuildContextMenu()
    {
        var refresh = new ToolStripMenuItem(Lang.Get("ctx.refresh")) { ShortcutKeyDisplayString = "F5" };
        refresh.Click += (_, _) => Reload();
        var explorer = new ToolStripMenuItem(Lang.Get("ctx.folderexplorer"));
        explorer.Click += (_, _) =>
        {
            if (currentPath == null) return;
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{currentPath}\"") { UseShellExecute = true }); } catch { }
        };
        ctx.Items.AddRange(new ToolStripItem[] { refresh, new ToolStripSeparator(), explorer });
        ThemeService.Apply(ctx);
    }

    void SetState(string text)
    {
        stState.Text = text;
        stateFromTimer = false;
    }

    static void OpenUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    // ================================================================ folder tree

    void BuildTree()
    {
        var s = AppSettings.Current;
        tree.BeginUpdate();
        tree.Nodes.Clear();

        if (s.IndexFolders.Count > 0)
        {
            var fav = new TreeNode(Lang.Get("tree.indexed")) { ImageKey = "star", SelectedImageKey = "star" };
            foreach (var f in s.IndexFolders)
                if (Directory.Exists(f)) fav.Nodes.Add(MakeFolderNode(f, f));
            if (fav.Nodes.Count > 0) { tree.Nodes.Add(fav); fav.Expand(); }
        }

        AddSpecial(Environment.SpecialFolder.Desktop, "tree.desktop");
        AddSpecial(Environment.SpecialFolder.MyPictures, "tree.pictures");
        AddSpecial(Environment.SpecialFolder.MyDocuments, "tree.documents");
        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloads)) tree.Nodes.Add(MakeFolderNode(downloads, Lang.Get("tree.downloads")));

        foreach (var d in DriveInfo.GetDrives())
        {
            string label = "";
            try
            {
                if (d.DriveType == DriveType.Network) label = Lang.Get("tree.network");
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
        if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) tree.Nodes.Add(MakeFolderNode(p, Lang.Get(key)));
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
            SetState(Lang.Get("err.nofolder"));
            return;
        }

        if (push && currentPath != null && !string.Equals(currentPath, path, StringComparison.OrdinalIgnoreCase))
        {
            back.Add(currentPath);
            fwd.Clear();
        }
        currentPath = path;
        addr.PathText = path;
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
        SetState(Lang.Get("status.loading"));
        stInfo.Text = "";
        stCount.Text = "";

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
            if (ticket == openTicket) SetState(Lang.Format("status.error", ex.Message));
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
        Watch(ns.Path);
        SetState("");
        stCount.Text = Lang.Format("status.files", ns.Files.Length, Fmt.Size(ns.Files.Sum(f => f.Size)));
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
        viewerOpen = true;
        try { v.ShowDialog(this); }
        finally { viewerOpen = false; }

        grid.SelectIndex(s.GridIndexOfFile(v.CurrentIndex));
        grid.Focus();
        if (v.AnyDeleted)
        {
            // indices shift after a deletion: re-read the folder, keep the photo the viewer ended on
            _ = SoftRefreshAsync(s.Files[v.CurrentIndex].Name);
        }
        else if (fsDirty)
        {
            fsTimer.Start();   // changes that arrived while the viewer was open
        }
    }

    void OnSelectionChanged()
    {
        var s = session;
        int n = grid.SelectedCount;
        int i = grid.FocusedIndex;
        string prefix = n > 1 ? Lang.Format("status.selected", n) + "   ·   " : "";
        if (s == null || i < 0 || i >= s.ItemCount)
        {
            stInfo.Text = n > 1 ? Lang.Format("status.selected", n) : "";
            return;
        }
        if (s.IsFolder(i))
        {
            stInfo.Text = prefix + s.Folders[i].Name;
            return;
        }
        var f = s.Files[s.FileIndex(i)];
        stInfo.Text = prefix + $"{f.Name}   ·   {Fmt.Size(f.Size)}   ·   {Fmt.Date(f.Modified)}";
    }

    /// <summary>
    /// Right click on a tile: the standard Explorer menu (Open with, Send to, Copy, Delete, Properties...)
    /// for the whole selection, with Skimmer's own "View" and "Copy path" on top.
    /// Deletes and renames made from it show up by themselves through the folder watcher.
    /// </summary>
    void ShowItemMenu(int index, Point screenPos)
    {
        var s = session;
        if (s == null || index < 0 || index >= s.ItemCount) return;
        ctxIndex = index;
        var paths = SelectedPaths();
        if (paths.Count == 0) paths.Add(s.ItemPath(index));

        bool folder = s.IsFolder(index);
        var own = new List<ShellContextMenu.Item>
        {
            new(Lang.Get(folder ? "ctx.openfolder" : "ctx.view"), () => OnItemActivated(index), Bold: true),
            new(Lang.Get("ctx.copypath"), CopyPaths),
        };
        ShellContextMenu.Show(grid, paths, screenPos, own);
    }

    void ShowBackgroundMenu(Point screenPos)
    {
        if (currentPath == null) return;
        ThemeService.Apply(ctx);
        ctx.Show(screenPos);
    }

    List<string> SelectedPaths()
    {
        var s = session;
        if (s == null) return new List<string>();
        return grid.SelectedIndices.Where(g => g >= 0 && g < s.ItemCount).Select(s.ItemPath).ToList();
    }

    void CopyPaths()
    {
        var paths = SelectedPaths();
        if (paths.Count == 0) return;
        try { Clipboard.SetText(string.Join(Environment.NewLine, paths)); } catch { }
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
        _ = SoftRefreshAsync(keep >= 0 ? s.ItemName(keep) : null);
    }

    // ================================================================ live folder updates

    void Watch(string path)
    {
        watcher?.Dispose();
        watcher = null;
        fsDirty = false;
        fsTimer.Stop();
        try
        {
            var w = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024
            };
            w.Created += (_, e) => OnFsEvent(e.FullPath, false);
            w.Deleted += (_, e) => OnFsEvent(e.FullPath, false);
            w.Changed += (_, e) => OnFsEvent(e.FullPath, true);
            w.Renamed += (_, e) => { OnFsEvent(e.OldFullPath, false); OnFsEvent(e.FullPath, false); };
            w.Error += (_, _) => OnFsEvent(null, false);   // buffer overflow etc.: just re-read
            w.EnableRaisingEvents = true;
            watcher = w;
        }
        catch { }   // some network shares do not support change notifications
    }

    /// <summary>Called on a thread-pool thread.</summary>
    void OnFsEvent(string fullPath, bool isChange)
    {
        if (fullPath != null)
        {
            string name = Path.GetFileName(fullPath);
            bool image = ImageLoader.IsImage(name);
            // content changes of subfolders do not affect this view; created/deleted/renamed folders do
            if (!image && (isChange || File.Exists(fullPath))) return;
        }
        fsDirty = true;
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (!fsTimer.Enabled) fsTimer.Start();   // batch: one refresh per interval, not one per file
            }));
        }
        catch { }
    }

    void OnFsTimer()
    {
        fsTimer.Stop();
        if (!fsDirty) return;
        if (viewerOpen || session == null) return;   // applied after the viewer closes / on the next open
        if (refreshing) { fsTimer.Start(); return; }
        _ = SoftRefreshAsync(null);
    }

    /// <summary>Re-reads the open folder in the background and swaps it in without losing scroll / selection.</summary>
    async Task SoftRefreshAsync(string focusName)
    {
        var old = session;
        if (old == null) return;
        if (refreshing) { fsDirty = true; fsTimer.Start(); return; }

        refreshing = true;
        fsDirty = false;
        int ticket = openTicket;
        var st = AppSettings.Current;
        int mode = st.SortMode;
        bool desc = st.SortDescending;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        FolderSession ns = null;
        try
        {
            ns = await Task.Run(() => FolderSession.Open(old.Path, mode, desc, CancellationToken.None));
        }
        catch { }
        finally { refreshing = false; }
        if (ns == null) return;

        // the user navigated away, the viewer opened meanwhile, or the window is closing
        if (ticket != openTicket || session != old || IsDisposed || viewerOpen)
        {
            ns.Dispose();
            if (viewerOpen && session == old) fsDirty = true;
            return;
        }

        session = ns;
        ns.Start();
        grid.SwapSession(ns, focusName);
        old.Dispose();
        stCount.Text = Lang.Format("status.files", ns.Files.Length, Fmt.Size(ns.Files.Sum(f => f.Size)));
        OnUiTimer();

        // big folders take longer to list: refresh them less often
        fsTimer.Interval = (int)Math.Clamp(sw.ElapsedMilliseconds * 3, 1000, 15000);
        if (fsDirty) fsTimer.Start();
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
            OfferUpdate(dlg.FoundUpdate, manual: true);
            ShowUpdateDialog(dlg.FoundUpdate);
        }
    }

    // ================================================================ updates
    // Brand book: check at start and then once a day; the banner never takes the focus;
    // "Later" / "×" hide it for this version, "Skip this version" in the dialog hides it for good.

    async Task CheckUpdatesBackgroundAsync(int delayMs, bool force)
    {
        try
        {
            if (delayMs > 0) await Task.Delay(delayMs);
            if (IsDisposed || !AppSettings.Current.AutoUpdateCheck) return;
            var state = UpdateState.Current;
            if (!force && DateTime.UtcNow - state.LastCheckUtc < TimeSpan.FromHours(24)) return;

            var info = await UpdateService.CheckAsync();
            state.LastCheckUtc = DateTime.UtcNow;
            state.Save();
            if (IsDisposed || info == null) return;
            OfferUpdate(info, manual: false);
        }
        catch { }   // offline, GitHub limit etc.: stay silent, try again later
    }

    void OfferUpdate(UpdateInfo info, bool manual)
    {
        var state = UpdateState.Current;
        if (!manual && string.Equals(info.Tag, state.SkippedVersion, StringComparison.OrdinalIgnoreCase)) return;

        pendingUpdate = info;
        stUpdate.Text = Lang.Format("upd.available", info.VersionText);
        stUpdate.Visible = true;
        if (manual || !state.IsSuppressed(info.Tag)) banner.ShowFor(info);
    }

    void ShowUpdateDialog(UpdateInfo info)
    {
        if (info == null) return;
        using var dlg = new UpdateForm(info);
        var r = dlg.ShowDialog(this);

        if (dlg.SkipRequested)
        {
            UpdateState.Current.SkippedVersion = info.Tag;
            UpdateState.Current.Save();
            banner.HideBanner();
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
            MessageBox.Show(this, Lang.Format("upd.failed", ex.Message), Lang.Get("app.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            mid = Lang.Format("status.index", Math.Min(s.Done, s.MissingAtOpen), s.MissingAtOpen);
        }
        else if (indexer.Active)
        {
            string folder = indexer.CurrentFolder ?? "";
            string name = Path.GetFileName(folder.TrimEnd('\\', '/'));
            mid = Lang.Format("status.bg", string.IsNullOrEmpty(name) ? folder : name);
        }
        else if (s != null && s.MissingAtOpen > 0)
        {
            mid = Lang.Get("status.ready");
        }
        // the timer owns the state zone only while it has something to say; errors stay until the next folder
        if (mid.Length > 0) { if (stState.Text != mid) stState.Text = mid; stateFromTimer = true; }
        else if (stateFromTimer) { stState.Text = ""; stateFromTimer = false; }

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
                addr.BeginEdit();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
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

        _ = CheckUpdatesBackgroundAsync(8000, force: true);
        updTimer.Tick += (_, _) => _ = CheckUpdatesBackgroundAsync(0, force: false);
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
        fsTimer.Stop();
        systemTheme.Dispose();
        watcher?.Dispose();
        watcher = null;
        openCts?.Cancel();
        var sess = session;
        session = null;
        sess?.DisposeSync();   // flushes the cache index to disk
        indexer.Stop();
    }
}

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Skimmer;

/// <summary>
/// Owner-drawn, fully virtual thumbnail grid. Only the tiles on screen are painted,
/// so a folder with tens of thousands of photos costs nothing extra.
/// Items: subfolders first, then images (see FolderSession). Explorer-style selection:
/// click, Ctrl+click, Shift+click, Shift+arrows, Ctrl+A.
/// </summary>
sealed class ThumbGrid : Control
{
    sealed class LruItem { public int Index; public Bitmap Bmp; }

    const int LruCapacity = 1500;
    const int PrefetchRows = 3;

    readonly VScrollBar vsb;
    readonly System.Windows.Forms.Timer pump;
    readonly LinkedList<LruItem> lruList = new();
    readonly Dictionary<int, LinkedListNode<LruItem>> lru = new();
    readonly HashSet<int> failed = new();
    readonly HashSet<int> selection = new();
    readonly SolidBrush tileBrush = new(Theme.Tile);
    readonly SolidBrush selBrush = new(Theme.TileSelected);
    readonly SolidBrush folderBack = new(Color.FromArgb(200, 160, 60));
    readonly SolidBrush folderFront = new(Color.FromArgb(235, 195, 85));
    readonly Pen focusPen = new(Theme.Accent, 2f);

    FolderSession session;
    int tileSize = 160;
    bool showNames = true;
    int cellW, cellH, cols, offX, maxScroll, total;
    int scroll;
    int focus = -1;      // "current" item (keyboard focus)
    int anchor = -1;     // start of Shift-range
    bool updatingScroll;

    public event Action<int> ItemActivated;
    public event Action SelectionChanged;
    public event Action<int, Point> ItemContext;
    public event Action<bool> DeleteRequested;   // true = permanent (Shift+Del)

    public ThumbGrid()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
        TabStop = true;
        BackColor = Theme.GridBack;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);

        vsb = new VScrollBar { Dock = DockStyle.Right };
        vsb.ValueChanged += (s, e) =>
        {
            if (updatingScroll) return;
            scroll = vsb.Value;
            Invalidate();
        };
        Controls.Add(vsb);

        pump = new System.Windows.Forms.Timer { Interval = 30 };
        pump.Tick += (s, e) => DrainResults();
        pump.Start();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkControl(vsb);
    }

    // ------------------------------------------------------------ public API

    public int Count => session?.ItemCount ?? 0;

    /// <summary>The focused item (grid index) or -1.</summary>
    public int FocusedIndex => focus;

    /// <summary>All selected grid indices in ascending order.</summary>
    public List<int> SelectedIndices
    {
        get
        {
            var l = selection.ToList();
            l.Sort();
            return l;
        }
    }

    public int SelectedCount => selection.Count;

    public int TileSize
    {
        get => tileSize;
        set
        {
            tileSize = Math.Clamp(value, 64, 320);
            RecalcLayout();
            if (focus >= 0) EnsureVisible(focus);
            Invalidate();
        }
    }

    public bool ShowNames
    {
        get => showNames;
        set
        {
            showNames = value;
            RecalcLayout();
            if (focus >= 0) EnsureVisible(focus);
            Invalidate();
        }
    }

    public void SetSession(FolderSession s)
    {
        ClearCache();
        session = s;
        selection.Clear();
        focus = anchor = -1;
        scroll = 0;
        RecalcLayout();
        if (s != null && s.ItemCount > 0)
        {
            focus = anchor = 0;
            selection.Add(0);
        }
        Invalidate();
        SelectionChanged?.Invoke();
    }

    static string Key(FolderSession s, int g) => (s.IsFolder(g) ? "D|" : "F|") + s.ItemName(g);

    /// <summary>Same file and unchanged on disk, so its thumbnail (or "cannot decode" mark) is still valid.</summary>
    static bool SameContent(FolderSession a, int ga, FolderSession b, int gb)
    {
        if (a.IsFolder(ga) || b.IsFolder(gb)) return a.IsFolder(ga) && b.IsFolder(gb);
        var fa = a.Files[a.FileIndex(ga)];
        var fb = b.Files[b.FileIndex(gb)];
        return fa.Mtime == fb.Mtime && fa.Size == fb.Size;
    }

    /// <summary>
    /// Replaces the folder contents after a change on disk without visual disruption:
    /// keeps scroll position, selection and already decoded thumbnails (matched by name).
    /// preferFocusName: select this item instead (e.g. the neighbour of deleted files).
    /// </summary>
    public void SwapSession(FolderSession ns, string preferFocusName = null)
    {
        var old = session;
        if (old == null || ns == null)
        {
            SetSession(ns);
            return;
        }

        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int g = 0; g < ns.ItemCount; g++) index[Key(ns, g)] = g;

        int Map(int gOld) =>
            gOld >= 0 && gOld < old.ItemCount && index.TryGetValue(Key(old, gOld), out var g) ? g : -1;

        // thumbnails
        var items = lruList.ToList();
        lruList.Clear();
        lru.Clear();
        foreach (var it in items)
        {
            int g = Map(it.Index);
            if (g >= 0 && SameContent(old, it.Index, ns, g))
            {
                it.Index = g;
                lru[g] = lruList.AddLast(it);
            }
            else it.Bmp.Dispose();
        }
        var oldFailed = failed.ToList();
        failed.Clear();
        foreach (int f in oldFailed)
        {
            int g = Map(f);
            if (g >= 0 && SameContent(old, f, ns, g)) failed.Add(g);
        }

        // selection
        int oldFocus = focus;
        var newSel = selection.Select(Map).Where(g => g >= 0).ToList();
        int newFocus = Map(focus);
        int newAnchor = Map(anchor);

        session = ns;
        selection.Clear();
        if (preferFocusName != null)
        {
            int g = -1;
            for (int i = 0; i < ns.ItemCount; i++)
                if (string.Equals(ns.ItemName(i), preferFocusName, StringComparison.OrdinalIgnoreCase)) { g = i; break; }
            newFocus = newAnchor = g;
            newSel.Clear();
            if (g >= 0) newSel.Add(g);
        }
        if (newFocus < 0 && ns.ItemCount > 0 && oldFocus >= 0)
        {
            newFocus = Math.Min(oldFocus, ns.ItemCount - 1);    // focused item disappeared: stay at the same place
            newSel.Add(newFocus);
        }
        foreach (int g in newSel) selection.Add(g);
        focus = newFocus;
        anchor = newAnchor >= 0 ? newAnchor : newFocus;

        RecalcLayout();          // keeps the scroll offset (clamped to the new size)
        Invalidate();
        SelectionChanged?.Invoke();
    }

    /// <summary>Selects exactly one item.</summary>
    public void SelectIndex(int i, bool ensureVisible = true)
    {
        if (session == null || session.ItemCount == 0) return;
        i = Math.Clamp(i, 0, session.ItemCount - 1);
        selection.Clear();
        selection.Add(i);
        focus = anchor = i;
        if (ensureVisible) EnsureVisible(i);
        Invalidate();
        SelectionChanged?.Invoke();
    }

    public void SelectAll()
    {
        if (session == null || session.ItemCount == 0) return;
        selection.Clear();
        for (int i = 0; i < session.ItemCount; i++) selection.Add(i);
        Invalidate();
        SelectionChanged?.Invoke();
    }

    void SelectRange(int from, int to, bool add)
    {
        if (!add) selection.Clear();
        int a = Math.Min(from, to), b = Math.Max(from, to);
        for (int i = a; i <= b; i++) selection.Add(i);
    }

    // ------------------------------------------------------------ layout

    float Dpi => DeviceDpi / 96f;

    void RecalcLayout()
    {
        int tile = (int)(tileSize * Dpi);
        int pad = (int)(16 * Dpi);
        cellW = tile + pad;
        cellH = tile + pad + (showNames ? (int)(22 * Dpi) : 0);

        int avail = Math.Max(1, ClientSize.Width - vsb.Width);
        cols = Math.Max(1, avail / cellW);
        offX = Math.Max(0, (avail - cols * cellW) / 2);

        int count = Count;
        int rows = (count + cols - 1) / cols;
        total = rows * cellH;
        int page = Math.Max(1, ClientSize.Height);
        maxScroll = Math.Max(0, total - page);

        updatingScroll = true;
        try
        {
            vsb.Enabled = maxScroll > 0;
            vsb.Minimum = 0;
            vsb.Maximum = Math.Max(total, 1);
            vsb.LargeChange = page;
            vsb.SmallChange = Math.Max(1, cellH / 3);
            scroll = Math.Clamp(scroll, 0, maxScroll);
            vsb.Value = Math.Min(scroll, Math.Max(0, vsb.Maximum - vsb.LargeChange + 1));
        }
        finally { updatingScroll = false; }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RecalcLayout();
    }

    void SetScroll(int v)
    {
        v = Math.Clamp(v, 0, maxScroll);
        if (v == scroll) return;
        scroll = v;
        updatingScroll = true;
        try { vsb.Value = Math.Min(v, Math.Max(0, vsb.Maximum - vsb.LargeChange + 1)); }
        finally { updatingScroll = false; }
        Invalidate();
    }

    void EnsureVisible(int i)
    {
        if (i < 0 || cols < 1) return;
        int top = (i / cols) * cellH;
        if (top < scroll) SetScroll(top);
        else if (top + cellH > scroll + ClientSize.Height) SetScroll(top + cellH - ClientSize.Height);
    }

    // ------------------------------------------------------------ results / cache

    void DrainResults()
    {
        if (session == null) return;
        bool any = false;
        int n = 0;
        while (n < 200 && session.TryTakeResult(out var r))
        {
            n++;
            any = true;
            if (r.Failed || r.Bmp == null) { failed.Add(r.GridIndex); r.Bmp?.Dispose(); }
            else AddToCache(r.GridIndex, r.Bmp);
        }
        if (any) Invalidate();
    }

    void AddToCache(int index, Bitmap bmp)
    {
        if (lru.TryGetValue(index, out var node))
        {
            node.Value.Bmp.Dispose();
            node.Value.Bmp = bmp;
            lruList.Remove(node);
            lruList.AddFirst(node);
            return;
        }
        lru[index] = lruList.AddFirst(new LruItem { Index = index, Bmp = bmp });
        while (lruList.Count > LruCapacity)
        {
            var last = lruList.Last;
            lruList.RemoveLast();
            lru.Remove(last.Value.Index);
            last.Value.Bmp.Dispose();
        }
    }

    Bitmap GetCached(int index)
    {
        if (!lru.TryGetValue(index, out var node)) return null;
        if (node != lruList.First)
        {
            lruList.Remove(node);
            lruList.AddFirst(node);
        }
        return node.Value.Bmp;
    }

    void ClearCache()
    {
        foreach (var it in lruList) it.Bmp.Dispose();
        lruList.Clear();
        lru.Clear();
        failed.Clear();
    }

    bool NeedsThumb(int i) => !lru.ContainsKey(i) && !failed.Contains(i);

    // ------------------------------------------------------------ painting

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (session == null) return;
        int count = session.ItemCount;
        if (count == 0)
        {
            var area = new Rectangle(0, 0, Math.Max(1, Width - vsb.Width), Height);
            TextRenderer.DrawText(g, L.T("grid.empty"), Font, area, Theme.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            return;
        }

        g.InterpolationMode = InterpolationMode.Bilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        int tile = (int)(tileSize * Dpi);
        int firstRow = scroll / cellH;
        int lastRow = (scroll + Math.Max(1, ClientSize.Height) - 1) / cellH;
        int first = firstRow * cols;
        int last = Math.Min(count - 1, (lastRow + 1) * cols - 1);

        var wanted = new List<int>(last - first + 1);
        for (int i = first; i <= last; i++)
        {
            int row = i / cols, col = i % cols;
            int x = offX + col * cellW;
            int y = row * cellH - scroll;
            var cell = new Rectangle(x + 4, y + 4, cellW - 8, cellH - 8);
            if (!cell.IntersectsWith(e.ClipRectangle)) { if (NeedsThumb(i)) wanted.Add(i); continue; }

            bool sel = selection.Contains(i);
            g.FillRectangle(sel ? selBrush : tileBrush, cell);

            var imgArea = new Rectangle(cell.X + 4, cell.Y + 4, cell.Width - 8, tile);
            var bmp = GetCached(i);

            if (session.IsFolder(i))
            {
                DrawFolder(g, imgArea, bmp);
                if (bmp == null && !failed.Contains(i)) wanted.Add(i);
            }
            else if (bmp != null)
            {
                DrawFit(g, bmp, imgArea);
            }
            else if (failed.Contains(i))
            {
                TextRenderer.DrawText(g, L.T("grid.broken"), Font, imgArea, Theme.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            else wanted.Add(i);

            if (showNames)
            {
                var nameArea = new Rectangle(cell.X + 2, imgArea.Bottom + 2, cell.Width - 4, cell.Bottom - imgArea.Bottom - 4);
                TextRenderer.DrawText(g, session.ItemName(i), Font, nameArea, Theme.Text,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            }

            if (i == focus && Focused)
                g.DrawRectangle(focusPen, cell.X + 1, cell.Y + 1, cell.Width - 2, cell.Height - 2);
        }

        // Ask the workers for what is missing: visible tiles first, then rows above/below.
        int pf = PrefetchRows * cols;
        int rangeFirst = Math.Max(0, first - pf);
        int rangeLast = Math.Min(count - 1, last + pf);
        session.SetVisibleRange(rangeFirst, rangeLast);

        for (int i = last + 1; i <= rangeLast; i++) if (NeedsThumb(i)) wanted.Add(i);
        for (int i = first - 1; i >= rangeFirst; i--) if (NeedsThumb(i)) wanted.Add(i);
        if (wanted.Count > 0) session.Request(wanted);
    }

    static void DrawFit(Graphics g, Bitmap bmp, Rectangle area)
    {
        float k = Math.Min((float)area.Width / bmp.Width, (float)area.Height / bmp.Height);
        int w = Math.Max(1, (int)(bmp.Width * k)), h = Math.Max(1, (int)(bmp.Height * k));
        g.DrawImage(bmp, area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
    }

    /// <summary>Folder shape; the preview mosaic (if any) sits inside the folder's front panel.</summary>
    void DrawFolder(Graphics g, Rectangle area, Bitmap preview)
    {
        int s = Math.Min(area.Width, area.Height);
        int w = (int)(s * 0.92), h = (int)(s * 0.74);
        int x = area.X + (area.Width - w) / 2;
        int y = area.Y + (area.Height - h) / 2;
        int tabW = w * 2 / 5, tabH = Math.Max(4, h / 9);
        int r = Math.Max(2, s / 40);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var back = RoundRect(new Rectangle(x, y, w, h), r))
            g.FillPath(folderBack, back);
        using (var tab = RoundRect(new Rectangle(x, y - tabH + r, tabW, tabH + r), r))
            g.FillPath(folderBack, tab);

        var front = new Rectangle(x, y + tabH, w, h - tabH);
        using (var fp = RoundRect(front, r))
            g.FillPath(folderFront, fp);
        g.SmoothingMode = SmoothingMode.None;

        if (preview != null)
        {
            int inset = Math.Max(3, s / 22);
            var pr = Rectangle.Inflate(front, -inset, -inset);
            DrawFit(g, preview, pr);
        }
    }

    static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // ------------------------------------------------------------ input

    int HitTest(Point p)
    {
        if (session == null || cols < 1 || p.X >= ClientSize.Width - vsb.Width) return -1;
        int col = (p.X - offX) / cellW;
        if (p.X < offX || col < 0 || col >= cols) return -1;
        int row = (p.Y + scroll) / cellH;
        int i = row * cols + col;
        return i >= 0 && i < session.ItemCount ? i : -1;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        int i = HitTest(e.Location);
        bool ctrl = (ModifierKeys & Keys.Control) != 0;
        bool shift = (ModifierKeys & Keys.Shift) != 0;

        if (i < 0)
        {
            if (e.Button == MouseButtons.Left && !ctrl && !shift && selection.Count > 0)
            {
                selection.Clear();
                Invalidate();
                SelectionChanged?.Invoke();
            }
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            if (shift)
            {
                if (anchor < 0) anchor = i;
                SelectRange(anchor, i, ctrl);
                focus = i;
            }
            else if (ctrl)
            {
                if (!selection.Remove(i)) selection.Add(i);
                focus = anchor = i;
            }
            else
            {
                selection.Clear();
                selection.Add(i);
                focus = anchor = i;
            }
            Invalidate();
            SelectionChanged?.Invoke();
        }
        else if (e.Button == MouseButtons.Right)
        {
            if (!selection.Contains(i))
            {
                selection.Clear();
                selection.Add(i);
                anchor = i;
            }
            focus = i;
            Invalidate();
            SelectionChanged?.Invoke();
            ItemContext?.Invoke(i, PointToScreen(e.Location));
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button != MouseButtons.Left || (ModifierKeys & (Keys.Control | Keys.Shift)) != 0) return;
        int i = HitTest(e.Location);
        if (i >= 0) ItemActivated?.Invoke(i);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        SetScroll(scroll - e.Delta * cellH / 240);
    }

    protected override bool IsInputKey(Keys keyData)
    {
        switch (keyData & Keys.KeyCode)
        {
            case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
            case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End:
            case Keys.Enter: case Keys.Delete: case Keys.Space:
                return true;
        }
        return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (session == null || session.ItemCount == 0) return;
        int count = session.ItemCount;
        int cur = focus < 0 ? 0 : focus;
        int pageRows = Math.Max(1, ClientSize.Height / cellH - 1);
        int next;

        switch (e.KeyCode)
        {
            case Keys.Left: next = cur - 1; break;
            case Keys.Right: next = cur + 1; break;
            case Keys.Up: next = cur - cols; break;
            case Keys.Down: next = cur + cols; break;
            case Keys.PageUp: next = cur - pageRows * cols; break;
            case Keys.PageDown: next = cur + pageRows * cols; break;
            case Keys.Home: next = 0; break;
            case Keys.End: next = count - 1; break;
            case Keys.Enter:
                ItemActivated?.Invoke(cur);
                e.Handled = true;
                return;
            case Keys.Delete:
                DeleteRequested?.Invoke(e.Shift);
                e.Handled = true;
                return;
            case Keys.A when e.Control:
                SelectAll();
                e.Handled = true;
                return;
            case Keys.Space when e.Control:
                if (!selection.Remove(cur)) selection.Add(cur);
                anchor = cur;
                Invalidate();
                SelectionChanged?.Invoke();
                e.Handled = true;
                return;
            default:
                return;
        }

        e.Handled = true;
        next = Math.Clamp(next, 0, count - 1);
        if (e.Shift)
        {
            if (anchor < 0) anchor = cur;
            SelectRange(anchor, next, e.Control);
            focus = next;
        }
        else if (e.Control)
        {
            focus = next;      // move focus only; Ctrl+Space toggles
        }
        else
        {
            selection.Clear();
            selection.Add(next);
            focus = anchor = next;
        }
        EnsureVisible(focus);
        Invalidate();
        SelectionChanged?.Invoke();
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            pump.Stop();
            pump.Dispose();
            ClearCache();
            tileBrush.Dispose();
            selBrush.Dispose();
            folderBack.Dispose();
            folderFront.Dispose();
            focusPen.Dispose();
        }
        base.Dispose(disposing);
    }
}

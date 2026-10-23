using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Skimmer;

/// <summary>
/// Owner-drawn, fully virtual thumbnail grid. Only the tiles on screen are painted,
/// so a folder with tens of thousands of photos costs nothing extra.
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
    readonly SolidBrush tileBrush = new(Theme.Tile);
    readonly SolidBrush selBrush = new(Theme.TileSelected);
    readonly Pen focusPen = new(Theme.Accent, 2f);

    FolderSession session;
    int tileSize = 160;
    bool showNames = true;
    int cellW, cellH, cols, offX, maxScroll, total;
    int scroll;
    int selected = -1;
    bool updatingScroll;

    public event Action<int> ItemActivated;
    public event Action<int> SelectionChanged;
    public event Action<int, Point> ItemContext;

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

    public int Count => session?.Files.Length ?? 0;
    public int SelectedIndex => selected;

    public int TileSize
    {
        get => tileSize;
        set
        {
            tileSize = Math.Clamp(value, 64, 320);
            RecalcLayout();
            if (selected >= 0) EnsureVisible(selected);
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
            if (selected >= 0) EnsureVisible(selected);
            Invalidate();
        }
    }

    public void SetSession(FolderSession s)
    {
        ClearCache();
        session = s;
        selected = -1;
        scroll = 0;
        RecalcLayout();
        if (s != null && s.Files.Length > 0) selected = 0;
        Invalidate();
        SelectionChanged?.Invoke(selected);
    }

    public void SelectIndex(int i, bool ensureVisible = true)
    {
        if (session == null || session.Files.Length == 0) return;
        i = Math.Clamp(i, 0, session.Files.Length - 1);
        bool changed = i != selected;
        selected = i;
        if (ensureVisible) EnsureVisible(i);
        Invalidate();
        if (changed) SelectionChanged?.Invoke(i);
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
            if (r.Failed || r.Bmp == null) { failed.Add(r.Index); r.Bmp?.Dispose(); }
            else AddToCache(r.Index, r.Bmp);
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

    // ------------------------------------------------------------ painting

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (session == null) return;
        int count = session.Files.Length;
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
            if (!cell.IntersectsWith(e.ClipRectangle)) { if (!lru.ContainsKey(i) && !failed.Contains(i)) wanted.Add(i); continue; }

            bool sel = i == selected;
            g.FillRectangle(sel ? selBrush : tileBrush, cell);

            var imgArea = new Rectangle(cell.X + 4, cell.Y + 4, cell.Width - 8, tile);
            var bmp = GetCached(i);
            if (bmp != null)
            {
                float k = Math.Min((float)imgArea.Width / bmp.Width, (float)imgArea.Height / bmp.Height);
                int w = Math.Max(1, (int)(bmp.Width * k)), h = Math.Max(1, (int)(bmp.Height * k));
                g.DrawImage(bmp, imgArea.X + (imgArea.Width - w) / 2, imgArea.Y + (imgArea.Height - h) / 2, w, h);
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
                TextRenderer.DrawText(g, session.Files[i].Name, Font, nameArea, Theme.Text,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            }

            if (sel && Focused)
                g.DrawRectangle(focusPen, cell.X + 1, cell.Y + 1, cell.Width - 2, cell.Height - 2);
        }

        // Ask the workers for what is missing: visible tiles first, then rows above/below.
        int pf = PrefetchRows * cols;
        int rangeFirst = Math.Max(0, first - pf);
        int rangeLast = Math.Min(count - 1, last + pf);
        session.VisFirst = rangeFirst;
        session.VisLast = rangeLast;

        for (int i = last + 1; i <= rangeLast; i++) if (!lru.ContainsKey(i) && !failed.Contains(i)) wanted.Add(i);
        for (int i = first - 1; i >= rangeFirst; i--) if (!lru.ContainsKey(i) && !failed.Contains(i)) wanted.Add(i);
        if (wanted.Count > 0) session.Request(wanted);
    }

    // ------------------------------------------------------------ input

    int HitTest(Point p)
    {
        if (session == null || cols < 1 || p.X >= ClientSize.Width - vsb.Width) return -1;
        int col = (p.X - offX) / cellW;
        if (p.X < offX || col < 0 || col >= cols) return -1;
        int row = (p.Y + scroll) / cellH;
        int i = row * cols + col;
        return i >= 0 && i < session.Files.Length ? i : -1;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        int i = HitTest(e.Location);
        if (i >= 0)
        {
            SelectIndex(i, false);
            if (e.Button == MouseButtons.Right) ItemContext?.Invoke(i, PointToScreen(e.Location));
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button != MouseButtons.Left) return;
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
            case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End: case Keys.Enter:
                return true;
        }
        return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (session == null || session.Files.Length == 0) return;
        int cur = selected < 0 ? 0 : selected;
        int pageRows = Math.Max(1, ClientSize.Height / cellH - 1);
        int next = cur;
        switch (e.KeyCode)
        {
            case Keys.Left: next = cur - 1; break;
            case Keys.Right: next = cur + 1; break;
            case Keys.Up: next = cur - cols; break;
            case Keys.Down: next = cur + cols; break;
            case Keys.PageUp: next = cur - pageRows * cols; break;
            case Keys.PageDown: next = cur + pageRows * cols; break;
            case Keys.Home: next = 0; break;
            case Keys.End: next = session.Files.Length - 1; break;
            case Keys.Enter: ItemActivated?.Invoke(cur); e.Handled = true; return;
            default: return;
        }
        e.Handled = true;
        SelectIndex(Math.Clamp(next, 0, session.Files.Length - 1));
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
            focusPen.Dispose();
        }
        base.Dispose(disposing);
    }
}

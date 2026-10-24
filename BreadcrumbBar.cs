using System.Drawing;
using System.Windows.Forms;

namespace Skimmer;

/// <summary>
/// Explorer / ACDSee style address bar: every folder of the path is a button,
/// the ▸ arrow after a folder lists its subfolders. Clicking the empty part of the bar,
/// Ctrl+L or F4 switches to a plain text box (Enter = go, Esc = back to the crumbs).
/// When the path does not fit, the leading folders collapse into a « menu.
/// </summary>
sealed class BreadcrumbBar : Control
{
    enum PartKind { Segment, Arrow, Overflow }

    sealed class Part
    {
        public PartKind Kind;
        public Rectangle Rect;
        public int Seg;       // segment index (Segment / Arrow)
    }

    readonly TextBox edit = new();
    readonly ContextMenuStrip menu = new();
    readonly List<(string Name, string Path)> segs = new();
    readonly List<Part> parts = new();
    int firstShown;           // segments before this one are collapsed into «
    Part hot;
    Part pressed;
    string path = "";

    public event Action<string> Navigate;
    public event Action EditFinished;

    public BreadcrumbBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Input;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9.5f);
        Cursor = Cursors.Default;

        edit.BorderStyle = BorderStyle.None;
        edit.BackColor = Theme.Input;
        edit.ForeColor = Theme.Text;
        edit.Visible = false;
        edit.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                string text = edit.Text;
                EndEdit();
                Navigate?.Invoke(text);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                EndEdit();
            }
        };
        edit.LostFocus += (_, _) => { if (edit.Visible) EndEdit(false); };
        Controls.Add(edit);

        menu.Renderer = new DarkRenderer();
        menu.BackColor = Theme.Bar;
        menu.ForeColor = Theme.Text;
        menu.ShowImageMargin = false;
        menu.Closed += (_, _) => { pressed = null; Invalidate(); };
    }

    // ------------------------------------------------------------ public API

    public string PathText
    {
        get => path;
        set
        {
            path = value ?? "";
            Split();
            if (!edit.Visible) edit.Text = path;
            LayoutParts();
            Invalidate();
        }
    }

    public bool Editing => edit.Visible;

    public void BeginEdit()
    {
        edit.Text = path;
        PlaceEdit();
        edit.Visible = true;
        edit.Focus();
        edit.SelectAll();
        Invalidate();
    }

    void EndEdit(bool notify = true)
    {
        if (!edit.Visible) return;
        edit.Visible = false;
        edit.Text = path;
        Invalidate();
        if (notify) EditFinished?.Invoke();
    }

    // ------------------------------------------------------------ segments

    void Split()
    {
        segs.Clear();
        if (string.IsNullOrEmpty(path)) return;
        string full;
        try { full = System.IO.Path.GetFullPath(path); } catch { full = path; }

        string root = System.IO.Path.GetPathRoot(full) ?? "";
        if (root.Length == 0) { segs.Add((full, full)); return; }

        // "G:\" -> "G:", "\\server\share" -> "\\server\share"
        segs.Add((root.TrimEnd('\\', '/'), root));
        string cur = root;
        foreach (var part in full.Substring(root.Length).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            cur = System.IO.Path.Combine(cur, part);
            segs.Add((part, cur));
        }
    }

    float Dpi => DeviceDpi / 96f;

    int ArrowW => (int)(16 * Dpi);
    int PadX => (int)(6 * Dpi);

    void LayoutParts()
    {
        parts.Clear();
        if (segs.Count == 0) return;

        var widths = segs.Select(s => TextRenderer.MeasureText(s.Name, Font, Size.Empty, TextFormatFlags.NoPrefix).Width + PadX * 2).ToList();
        int avail = ClientSize.Width - (int)(24 * Dpi);   // keep a little empty space to click for text editing
        int overflowW = (int)(22 * Dpi);

        // drop leading segments (keep at least the last one) until the rest fits
        firstShown = 0;
        int Total(int from) => (from > 0 ? overflowW : 0) + Enumerable.Range(from, segs.Count - from).Sum(i => widths[i] + ArrowW);
        while (firstShown < segs.Count - 1 && Total(firstShown) > avail) firstShown++;

        int x = 2, h = ClientSize.Height - 4;
        if (firstShown > 0)
        {
            parts.Add(new Part { Kind = PartKind.Overflow, Rect = new Rectangle(x, 2, overflowW, h) });
            x += overflowW;
        }
        for (int i = firstShown; i < segs.Count; i++)
        {
            parts.Add(new Part { Kind = PartKind.Segment, Seg = i, Rect = new Rectangle(x, 2, widths[i], h) });
            x += widths[i];
            parts.Add(new Part { Kind = PartKind.Arrow, Seg = i, Rect = new Rectangle(x, 2, ArrowW, h) });
            x += ArrowW;
        }
    }

    void PlaceEdit()
    {
        int eh = edit.PreferredHeight;
        edit.SetBounds(6, Math.Max(0, (ClientSize.Height - eh) / 2), Math.Max(10, ClientSize.Width - 12), eh);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutParts();
        PlaceEdit();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        edit.Font = Font;
        LayoutParts();
    }

    // ------------------------------------------------------------ painting

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        using (var border = new Pen(Theme.Border))
            g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        if (edit.Visible) return;

        foreach (var p in parts)
        {
            bool isHot = p == hot || p == pressed;
            if (isHot)
            {
                using var b = new SolidBrush(p == pressed ? Color.FromArgb(80, 80, 80) : Color.FromArgb(66, 66, 66));
                g.FillRectangle(b, p.Rect);
            }

            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            switch (p.Kind)
            {
                case PartKind.Segment:
                    TextRenderer.DrawText(g, segs[p.Seg].Name, Font, p.Rect, Theme.Text, flags);
                    break;
                case PartKind.Arrow:
                    TextRenderer.DrawText(g, p == pressed ? "▾" : "›", Font, p.Rect, Theme.TextDim, flags);
                    break;
                case PartKind.Overflow:
                    TextRenderer.DrawText(g, "«", Font, p.Rect, Theme.TextDim, flags);
                    break;
            }
        }
    }

    // ------------------------------------------------------------ mouse

    Part HitTest(Point pt) => parts.FirstOrDefault(p => p.Rect.Contains(pt));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = HitTest(e.Location);
        if (p != hot) { hot = p; Invalidate(); }
        Cursor = p == null && !edit.Visible ? Cursors.IBeam : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hot != null) { hot = null; Invalidate(); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || edit.Visible) return;
        var p = HitTest(e.Location);
        if (p == null)
        {
            BeginEdit();
            return;
        }
        switch (p.Kind)
        {
            case PartKind.Segment:
                Navigate?.Invoke(segs[p.Seg].Path);
                break;
            case PartKind.Arrow:
                ShowSubfolders(p);
                break;
            case PartKind.Overflow:
                ShowCollapsed(p);
                break;
        }
    }

    void ShowSubfolders(Part p)
    {
        string dir = segs[p.Seg].Path;
        string next = p.Seg + 1 < segs.Count ? segs[p.Seg + 1].Name : null;
        List<string> names;
        try
        {
            var opts = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
            names = new DirectoryInfo(dir).EnumerateDirectories("*", opts).Select(d => d.Name).ToList();
        }
        catch { names = new List<string>(); }
        names.Sort(NaturalComparer.Instance);

        var items = names.Select(n => (Text: n, Path: System.IO.Path.Combine(dir, n), Bold: string.Equals(n, next, StringComparison.OrdinalIgnoreCase)));
        ShowMenu(p, items);
    }

    void ShowCollapsed(Part p)
    {
        // nearest ancestor first, like Explorer
        var items = Enumerable.Range(0, firstShown).Reverse().Select(i => (Text: segs[i].Name, Path: segs[i].Path, Bold: false));
        ShowMenu(p, items);
    }

    void ShowMenu(Part p, IEnumerable<(string Text, string Path, bool Bold)> items)
    {
        menu.Items.Clear();
        foreach (var it in items.Take(500))
        {
            string target = it.Path;
            var mi = new ToolStripMenuItem(it.Text) { ForeColor = Theme.Text };
            if (it.Bold) mi.Font = new Font(menu.Font, FontStyle.Bold);
            mi.Click += (_, _) => Navigate?.Invoke(target);
            menu.Items.Add(mi);
        }
        if (menu.Items.Count == 0)
            menu.Items.Add(new ToolStripMenuItem("—") { Enabled = false });

        pressed = p;
        Invalidate();
        menu.Show(this, new Point(p.Rect.Left, Height));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) menu.Dispose();
        base.Dispose(disposing);
    }
}

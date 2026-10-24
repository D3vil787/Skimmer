using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Skimmer;

/// <summary>
/// Full-screen viewer. A cached thumbnail is shown instantly as a placeholder, the real
/// picture (decoded to screen size) replaces it as soon as it is ready, and the neighbours
/// are pre-decoded in the direction of travel so that paging feels immediate.
/// Keys: Right/Down/PageDown/Space = next, Left/Up/PageUp/Backspace = previous,
/// Home/End, Z = 100%, I = info bar, Del = to Recycle Bin, Shift+Del = delete permanently,
/// Esc/Enter/double click = close.
/// </summary>
sealed class ViewerForm : Form
{
    const int CacheSize = 9;
    const int PrefetchAhead = 3;
    const int PrefetchBehind = 1;

    readonly FolderSession session;
    readonly Dictionary<int, LoadedImage> cache = new();
    readonly HashSet<int> loading = new();
    readonly HashSet<int> failed = new();
    readonly HashSet<int> removed = new();   // deleted while the viewer was open
    readonly SemaphoreSlim gate = new(3);
    readonly Rectangle screen;

    int index;
    int direction = 1;
    Bitmap preview;
    int previewIndex = -1;

    bool zoom100;
    LoadedImage full;
    int fullIndex = -1;
    bool fullLoading;
    int panX, panY;
    Point dragStart;
    bool dragging;
    int dragPanX, dragPanY;

    bool overlay;
    volatile bool closing;

    public int CurrentIndex => index;
    public bool AnyDeleted => removed.Count > 0;

    public ViewerForm(FolderSession session, int startIndex, Screen onScreen)
    {
        this.session = session;
        screen = onScreen.Bounds;
        index = Math.Clamp(startIndex, 0, session.Files.Length - 1);
        overlay = AppSettings.Current.ShowOverlay;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = screen;
        BackColor = ViewerColors.Back;
        ShowInTaskbar = false;
        KeyPreview = true;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Text = Lang.Get("app.title");
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        ShowImage(index, force: true);
    }

    // ------------------------------------------------------------ navigation

    void ShowImage(int i, bool force = false)
    {
        i = Math.Clamp(i, 0, session.Files.Length - 1);
        if (i == index && !force) return;

        direction = i >= index ? 1 : -1;
        index = i;
        zoom100 = false;
        panX = panY = 0;
        DisposeFull();

        DisposePreview();
        if (!cache.ContainsKey(i))
        {
            preview = session.LoadThumbBitmap(i);
            previewIndex = preview != null ? i : -1;
        }

        RequestLoad(i);
        for (int k = 1; k <= PrefetchAhead; k++) RequestLoad(i + direction * k);
        for (int k = 1; k <= PrefetchBehind; k++) RequestLoad(i - direction * k);
        Invalidate();
    }

    void RequestLoad(int i)
    {
        if (i < 0 || i >= session.Files.Length) return;
        if (cache.ContainsKey(i) || loading.Contains(i) || failed.Contains(i) || removed.Contains(i)) return;
        loading.Add(i);
        string path = session.Files[i].FullPath;
        int w = screen.Width, h = screen.Height;

        Task.Run(() =>
        {
            LoadedImage img = null;
            gate.Wait();
            try
            {
                // user already moved far away: skip the expensive decode
                if (!closing && Math.Abs(i - index) <= PrefetchAhead + 2)
                    img = ImageLoader.LoadFull(path, w, h);
            }
            catch { img = null; }
            finally { gate.Release(); }

            bool skipped = img == null && Math.Abs(i - index) > PrefetchAhead + 2;
            Post(() => OnLoaded(i, img, skipped));
        });
    }

    void OnLoaded(int i, LoadedImage img, bool skipped)
    {
        loading.Remove(i);
        if (closing) { img?.Bmp.Dispose(); return; }

        if (img == null)
        {
            if (!skipped) failed.Add(i);
            if (i == index)
            {
                if (skipped) RequestLoad(i);
                Invalidate();
            }
            return;
        }

        cache[i] = img;
        TrimCache();
        if (i == index)
        {
            DisposePreview();
            Invalidate();
        }
    }

    void TrimCache()
    {
        while (cache.Count > CacheSize)
        {
            int victim = cache.Keys.OrderByDescending(k => Math.Abs(k - index)).First();
            if (victim == index) break;
            cache[victim].Bmp.Dispose();
            cache.Remove(victim);
        }
    }

    void Post(Action a)
    {
        try
        {
            if (!IsDisposed && IsHandleCreated) BeginInvoke(a);
        }
        catch { }
    }

    void DisposePreview()
    {
        preview?.Dispose();
        preview = null;
        previewIndex = -1;
    }

    void DisposeFull()
    {
        full?.Bmp.Dispose();
        full = null;
        fullIndex = -1;
    }

    /// <summary>Next index in the given direction that was not deleted, or -1.</summary>
    int Step(int from, int dir)
    {
        for (int j = from + dir; j >= 0 && j < session.Files.Length; j += dir)
            if (!removed.Contains(j)) return j;
        return -1;
    }

    void Go(int from, int dir)
    {
        int j = Step(from, dir);
        if (j >= 0) ShowImage(j);
    }

    void DeleteCurrent(bool permanent)
    {
        string path = session.Files[index].FullPath;
        if (!ShellOps.Delete(this, new[] { path }, permanent)) return;
        if (File.Exists(path)) return;   // cancelled or failed

        removed.Add(index);
        if (cache.Remove(index, out var li)) li.Bmp.Dispose();
        DisposePreview();
        DisposeFull();

        int dir = direction >= 0 ? 1 : -1;
        int j = Step(index, dir);
        if (j < 0) j = Step(index, -dir);
        if (j < 0) { Close(); return; }
        ShowImage(j);
    }

    // ------------------------------------------------------------ zoom 100%

    void ToggleZoom()
    {
        if (zoom100) { zoom100 = false; Invalidate(); return; }
        zoom100 = true;
        panX = panY = 0;
        if (fullIndex == index || fullLoading) { Invalidate(); return; }

        int i = index;
        string path = session.Files[i].FullPath;
        fullLoading = true;
        Task.Run(() =>
        {
            LoadedImage img = null;
            try { img = ImageLoader.LoadFull(path, 0, 0); } catch { }
            Post(() =>
            {
                fullLoading = false;
                if (closing || i != index || !zoom100) { img?.Bmp.Dispose(); return; }
                DisposeFull();
                full = img;
                fullIndex = img != null ? i : -1;
                Invalidate();
            });
        });
        Invalidate();
    }

    // ------------------------------------------------------------ painting

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(ViewerColors.Back);

        int ow = 0, oh = 0;
        bool shown = false;

        if (zoom100 && full != null && fullIndex == index)
        {
            var bmp = full.Bmp;
            ow = full.OrigW; oh = full.OrigH;
            int maxX = Math.Max(0, (bmp.Width - ClientSize.Width) / 2);
            int maxY = Math.Max(0, (bmp.Height - ClientSize.Height) / 2);
            panX = Math.Clamp(panX, -maxX, maxX);
            panY = Math.Clamp(panY, -maxY, maxY);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(bmp, (ClientSize.Width - bmp.Width) / 2 + panX, (ClientSize.Height - bmp.Height) / 2 + panY, bmp.Width, bmp.Height);
            shown = true;
        }
        else if (cache.TryGetValue(index, out var li))
        {
            DrawFit(g, li.Bmp, false);
            ow = li.OrigW; oh = li.OrigH;
            shown = true;
        }
        else if (preview != null && previewIndex == index)
        {
            DrawFit(g, preview, true);
            shown = true;
        }

        if (!shown && failed.Contains(index))
        {
            TextRenderer.DrawText(g, Lang.Get("viewer.error"), UiTheme.Subtitle, ClientRectangle, ViewerColors.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        if (overlay) DrawOverlay(g, ow, oh);
    }

    void DrawFit(Graphics g, Bitmap bmp, bool smooth)
    {
        float k = Math.Min((float)ClientSize.Width / bmp.Width, (float)ClientSize.Height / bmp.Height);
        int w = Math.Max(1, (int)Math.Round(bmp.Width * k));
        int h = Math.Max(1, (int)Math.Round(bmp.Height * k));
        g.InterpolationMode = Math.Abs(k - 1f) < 0.02f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(bmp, (ClientSize.Width - w) / 2, (ClientSize.Height - h) / 2, w, h);
    }

    void DrawOverlay(Graphics g, int ow, int oh)
    {
        var f = session.Files[index];
        string dims = ow > 0 ? $"   {ow}×{oh}" : "";
        int pos = index + 1 - removed.Count(r => r < index);
        int total = session.Files.Length - removed.Count;
        string text = $"{pos} / {total}   {f.Name}{dims}   {Fmt.Size(f.Size)}   {Fmt.Date(f.Modified)}";
        if (zoom100) text += "   100%";

        var font = UiTheme.Default;
        var size = TextRenderer.MeasureText(g, text, font);
        var bar = new Rectangle(0, ClientSize.Height - size.Height - 14, ClientSize.Width, size.Height + 14);
        using var br = new SolidBrush(ViewerColors.InfoBar);
        g.FillRectangle(br, bar);
        TextRenderer.DrawText(g, text, font, new Rectangle(bar.X + 14, bar.Y, bar.Width - 28, bar.Height), ViewerColors.InfoText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    // ------------------------------------------------------------ input

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Right: case Keys.Down: case Keys.PageDown: case Keys.Space:
                Go(index, 1); return true;
            case Keys.Left: case Keys.Up: case Keys.PageUp: case Keys.Back:
                Go(index, -1); return true;
            case Keys.Home: Go(-1, 1); return true;
            case Keys.End: Go(session.Files.Length, -1); return true;
            case Keys.Delete: DeleteCurrent(false); return true;
            case Keys.Shift | Keys.Delete: DeleteCurrent(true); return true;
            case Keys.Z: ToggleZoom(); return true;
            case Keys.I:
                overlay = !overlay;
                AppSettings.Current.ShowOverlay = overlay;
                Invalidate();
                return true;
            case Keys.Escape: case Keys.Enter:
                Close(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Delta > 0) Go(index, -1);
        else if (e.Delta < 0) Go(index, 1);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left) Close();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Middle) { ToggleZoom(); return; }
        if (e.Button == MouseButtons.Left && zoom100)
        {
            dragging = true;
            dragStart = e.Location;
            dragPanX = panX;
            dragPanY = panY;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging) return;
        panX = dragPanX + e.X - dragStart.X;
        panY = dragPanY + e.Y - dragStart.Y;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        dragging = false;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        closing = true;
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            closing = true;
            foreach (var li in cache.Values) li.Bmp.Dispose();
            cache.Clear();
            DisposePreview();
            DisposeFull();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// The full-screen viewer stays black in both themes: a photo is judged against a neutral dark surround.
/// Not in BRANDBOOK.md yet - proposed as an addition ("Полноэкранный просмотр"); kept in one place until agreed.
/// </summary>
static class ViewerColors
{
    public static readonly Color Back = Color.Black;
    public static readonly Color InfoBar = Color.FromArgb(150, 0, 0, 0);
    public static readonly Color InfoText = ColorTranslator.FromHtml("#E6E6E6");
    public static readonly Color Muted = ColorTranslator.FromHtml("#8791A3");   // Graphite Dark "muted"
}

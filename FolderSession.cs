using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Skimmer;

sealed class FileEntry
{
    public string Name;
    public string FullPath;
    public long Size;
    public long Mtime;   // LastWriteTimeUtc ticks
    public DateTime Modified => new DateTime(Mtime, DateTimeKind.Utc).ToLocalTime();
}

sealed class DirEntry
{
    public string Name;
    public string FullPath;
}

struct ThumbResult
{
    public int GridIndex;
    public Bitmap Bmp;     // null when Failed
    public bool Failed;
}

/// <summary>
/// The folder currently shown. Grid items are the subfolders first, then the images:
///   grid index g &lt; FolderCount  -> Folders[g]
///   otherwise                     -> Files[g - FolderCount]
/// A pool of worker threads (1) loads / generates thumbnails for what is on screen first,
/// (2) builds folder previews from the first photos inside each subfolder and
/// (3) then quietly indexes the rest of the folder.
/// </summary>
sealed class FolderSession : IDisposable
{
    const int FolderPreviewImages = 4;

    public readonly string Path;
    public readonly DirEntry[] Folders;
    public readonly FileEntry[] Files;
    public readonly FolderStore Store;
    public readonly int MissingAtOpen;

    readonly CancellationTokenSource cts = new();
    readonly object reqLock = new();
    readonly Stack<int> requests = new();      // file index >= 0, folder k encoded as -(k+1)
    readonly HashSet<int> inflight = new();
    readonly SemaphoreSlim wake = new(0);
    readonly ConcurrentQueue<ThumbResult> ready = new();
    Thread[] workers;
    int bgCursor = -1;
    volatile bool bgFinished;
    int done;
    int disposed;

    // File-index range (with prefetch margin) that the grid currently cares about.
    volatile int visFirst;
    volatile int visLast = -1;

    public int Done => Volatile.Read(ref done);
    public int FolderCount => Folders.Length;
    public int ItemCount => Folders.Length + Files.Length;
    public bool IsFolder(int g) => g >= 0 && g < Folders.Length;
    public int FileIndex(int g) => g - Folders.Length;
    public int GridIndexOfFile(int f) => f + Folders.Length;

    public string ItemName(int g) => IsFolder(g) ? Folders[g].Name : Files[g - Folders.Length].Name;
    public string ItemPath(int g) => IsFolder(g) ? Folders[g].FullPath : Files[g - Folders.Length].FullPath;

    /// <summary>True while thumbnails of this folder are still being produced.</summary>
    public bool IsBusy
    {
        get
        {
            if (!bgFinished) return true;
            lock (reqLock) return requests.Count > 0 || inflight.Count > 0;
        }
    }

    FolderSession(string path, DirEntry[] folders, FileEntry[] files, FolderStore store, int missing)
    {
        Path = path;
        Folders = folders;
        Files = files;
        Store = store;
        MissingAtOpen = missing;
        bgFinished = files.Length == 0 || missing == 0;
    }

    // ------------------------------------------------------------ opening

    public static FileEntry[] ListFiles(string path, CancellationToken ct = default)
    {
        var list = new List<FileEntry>();
        var di = new DirectoryInfo(path);
        var opts = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false };
        foreach (var fi in di.EnumerateFiles("*", opts))
        {
            ct.ThrowIfCancellationRequested();
            string n = fi.Name;
            if (n.StartsWith("._", StringComparison.Ordinal) || !ImageLoader.IsImage(n)) continue;
            list.Add(new FileEntry { Name = n, FullPath = fi.FullName, Size = fi.Length, Mtime = fi.LastWriteTimeUtc.Ticks });
        }
        return list.ToArray();
    }

    static DirEntry[] ListFolders(string path, CancellationToken ct)
    {
        var list = new List<DirEntry>();
        var opts = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
        };
        try
        {
            foreach (var d in new DirectoryInfo(path).EnumerateDirectories("*", opts))
            {
                ct.ThrowIfCancellationRequested();
                list.Add(new DirEntry { Name = d.Name, FullPath = d.FullName });
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }
        list.Sort((a, b) => Native.StrCmpLogicalW(a.Name, b.Name));
        return list.ToArray();
    }

    static void Sort(FileEntry[] files, int mode, bool desc)
    {
        Comparison<FileEntry> cmp = mode switch
        {
            1 => (a, b) => a.Mtime != b.Mtime ? a.Mtime.CompareTo(b.Mtime) : Native.StrCmpLogicalW(a.Name, b.Name),
            2 => (a, b) => a.Size != b.Size ? a.Size.CompareTo(b.Size) : Native.StrCmpLogicalW(a.Name, b.Name),
            _ => (a, b) => Native.StrCmpLogicalW(a.Name, b.Name),
        };
        Array.Sort(files, cmp);
        if (desc) Array.Reverse(files);
    }

    /// <summary>Runs on a background thread: list, sort, open cache, count missing thumbnails.</summary>
    public static FolderSession Open(string path, int sortMode, bool desc, CancellationToken ct)
    {
        var folders = ListFolders(path, ct);
        var files = ListFiles(path, ct);
        Sort(files, sortMode, desc);
        ct.ThrowIfCancellationRequested();

        var store = StoreManager.Acquire(path);
        try
        {
            var live = new HashSet<string>(files.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            StoreManager.TryCompact(store, live);
            int missing = 0;
            foreach (var f in files)
                if (!store.Has(f.Name, f.Mtime, f.Size)) missing++;
            ct.ThrowIfCancellationRequested();
            return new FolderSession(path, folders, files, store, missing);
        }
        catch
        {
            StoreManager.Release(store);
            throw;
        }
    }

    public void Start()
    {
        int n = Math.Clamp(Environment.ProcessorCount - 1, 2, 8);
        workers = new Thread[n];
        for (int i = 0; i < n; i++)
        {
            workers[i] = new Thread(Worker)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "Skimmer-thumb-" + i
            };
            workers[i].Start();
        }
    }

    // ------------------------------------------------------------ requests from the UI

    /// <summary>Grid-index range the grid currently shows (with prefetch margin).</summary>
    public void SetVisibleRange(int gFirst, int gLast)
    {
        visFirst = gFirst - Folders.Length;
        visLast = gLast - Folders.Length;
    }

    /// <summary>Replaces the pending request list. Grid indices are given in the order they should be served.</summary>
    public void Request(IList<int> gridIndicesInPriorityOrder)
    {
        lock (reqLock)
        {
            requests.Clear();
            for (int k = gridIndicesInPriorityOrder.Count - 1; k >= 0; k--)
            {
                int g = gridIndicesInPriorityOrder[k];
                int key = IsFolder(g) ? -(g + 1) : g - Folders.Length;
                if (!inflight.Contains(key)) requests.Push(key);
            }
        }
        if (workers != null && wake.CurrentCount < workers.Length) wake.Release(workers.Length);
    }

    public bool TryTakeResult(out ThumbResult r) => ready.TryDequeue(out r);

    /// <summary>Decoded cached thumbnail of a file (used by the viewer as an instant placeholder).</summary>
    public Bitmap LoadThumbBitmap(int fileIndex)
    {
        var f = Files[fileIndex];
        try
        {
            if (Store.TryGet(f.Name, f.Mtime, f.Size, out var d) && d.Length > 0)
                return ImageLoader.BitmapFromJpeg(d);
        }
        catch { }
        return null;
    }

    // ------------------------------------------------------------ workers

    void Worker()
    {
        var ct = cts.Token;
        while (!ct.IsCancellationRequested)
        {
            int key = int.MinValue;
            bool requested = false;
            lock (reqLock)
            {
                while (requests.Count > 0)
                {
                    int c = requests.Pop();
                    if (c < 0 || (c >= visFirst && c <= visLast))
                    {
                        key = c;
                        requested = true;
                        inflight.Add(c);
                        break;
                    }
                }
            }

            if (!requested)
            {
                key = NextBackground();
                if (key < 0)
                {
                    try { wake.Wait(300, ct); } catch (OperationCanceledException) { break; }
                    continue;
                }
            }

            try
            {
                if (key < 0) ProcessFolder(-key - 1);
                else Process(key, requested);
            }
            catch { }
            finally
            {
                if (requested) lock (reqLock) inflight.Remove(key);
            }
        }
    }

    int NextBackground()
    {
        if (bgFinished) return -1;
        while (true)
        {
            int i = Interlocked.Increment(ref bgCursor);
            if (i >= Files.Length) { bgFinished = true; return -1; }
            var f = Files[i];
            if (!Store.Has(f.Name, f.Mtime, f.Size)) return i;
        }
    }

    void Process(int i, bool visible)
    {
        var f = Files[i];
        int g = i + Folders.Length;
        if (Store.TryGet(f.Name, f.Mtime, f.Size, out var data))
        {
            if (!visible) return;                       // background pass only creates what is missing
            if (data.Length == 0)
            {
                ready.Enqueue(new ThumbResult { GridIndex = g, Failed = true });
                return;
            }
            try
            {
                ready.Enqueue(new ThumbResult { GridIndex = g, Bmp = ImageLoader.BitmapFromJpeg(data) });
                return;
            }
            catch
            {
                Store.Remove(f.Name);                   // damaged entry: regenerate
            }
        }
        Generate(i, visible);
    }

    void Generate(int i, bool visible)
    {
        var f = Files[i];
        int g = i + Folders.Length;
        byte[] data;
        try { data = ImageLoader.CreateThumbJpeg(f.FullPath); }
        catch { data = Array.Empty<byte>(); }

        try { if (Store.Put(f.Name, f.Mtime, f.Size, data)) Interlocked.Increment(ref done); }
        catch { }

        if (visible || (i >= visFirst && i <= visLast))
        {
            if (data.Length == 0) ready.Enqueue(new ThumbResult { GridIndex = g, Failed = true });
            else
            {
                try { ready.Enqueue(new ThumbResult { GridIndex = g, Bmp = ImageLoader.BitmapFromJpeg(data) }); }
                catch { ready.Enqueue(new ThumbResult { GridIndex = g, Failed = true }); }
            }
        }
    }

    /// <summary>Folder tile preview: up to 4 first photos of the subfolder, taken from (and saved to) its own cache.</summary>
    void ProcessFolder(int k)
    {
        Bitmap preview = null;
        try { preview = BuildFolderPreview(Folders[k].FullPath, cts.Token); }
        catch { }
        ready.Enqueue(new ThumbResult { GridIndex = k, Bmp = preview, Failed = preview == null });
    }

    static Bitmap BuildFolderPreview(string dir, CancellationToken ct)
    {
        var opts = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        var picks = new List<FileInfo>();
        foreach (var fi in new DirectoryInfo(dir).EnumerateFiles("*", opts))
        {
            if (ct.IsCancellationRequested) return null;
            if (fi.Name.StartsWith("._", StringComparison.Ordinal) || !ImageLoader.IsImage(fi.Name)) continue;
            picks.Add(fi);
            if (picks.Count >= 64) break;   // enough to choose from without walking a 50k-file folder
        }
        if (picks.Count == 0) return null;
        picks.Sort((a, b) => Native.StrCmpLogicalW(a.Name, b.Name));

        var thumbs = new List<Bitmap>();
        var store = StoreManager.Acquire(dir);
        try
        {
            foreach (var fi in picks)
            {
                if (thumbs.Count >= FolderPreviewImages || ct.IsCancellationRequested) break;
                long mt = fi.LastWriteTimeUtc.Ticks;
                if (!store.TryGet(fi.Name, mt, fi.Length, out var data))
                {
                    try { data = ImageLoader.CreateThumbJpeg(fi.FullName); }
                    catch { data = Array.Empty<byte>(); }
                    try { store.Put(fi.Name, mt, fi.Length, data); } catch { }
                }
                if (data == null || data.Length == 0) continue;
                try { thumbs.Add(ImageLoader.BitmapFromJpeg(data)); } catch { }
            }
            store.Flush();
        }
        finally
        {
            StoreManager.Release(store);
        }

        if (thumbs.Count == 0) return null;
        try { return Compose(thumbs); }
        finally { foreach (var t in thumbs) t.Dispose(); }
    }

    /// <summary>1 photo: as is; 2-4 photos: 2x2 mosaic of square crops.</summary>
    static Bitmap Compose(List<Bitmap> thumbs)
    {
        const int size = 256, gap = 4;
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);   // gaps show the folder colour of the tile
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        if (thumbs.Count == 1)
        {
            DrawCover(g, thumbs[0], new Rectangle(0, 0, size, size));
            return bmp;
        }
        int cell = (size - gap) / 2;
        for (int i = 0; i < thumbs.Count && i < 4; i++)
        {
            var r = new Rectangle((i % 2) * (cell + gap), (i / 2) * (cell + gap), cell, cell);
            DrawCover(g, thumbs[i], r);
        }
        return bmp;
    }

    static void DrawCover(Graphics g, Bitmap src, Rectangle dst)
    {
        float k = Math.Max((float)dst.Width / src.Width, (float)dst.Height / src.Height);
        float sw = dst.Width / k, sh = dst.Height / k;
        var srcRect = new RectangleF((src.Width - sw) / 2f, (src.Height - sh) / 2f, sw, sh);
        g.DrawImage(src, dst, srcRect, GraphicsUnit.Pixel);
    }

    // ------------------------------------------------------------ shutdown

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        cts.Cancel();
        Task.Run(Shutdown);
    }

    /// <summary>Blocking variant used when the application exits, so the cache index is flushed.</summary>
    public void DisposeSync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        cts.Cancel();
        Shutdown();
    }

    void Shutdown()
    {
        try { wake.Release(Math.Max(1, workers?.Length ?? 1)); } catch { }
        if (workers != null)
            foreach (var t in workers) { try { t.Join(3000); } catch { } }
        try { Store.Flush(); } catch { }
        StoreManager.Release(Store);
        while (ready.TryDequeue(out var r)) r.Bmp?.Dispose();
    }
}

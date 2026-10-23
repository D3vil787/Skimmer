using System.Collections.Concurrent;
using System.Drawing;

namespace Skimmer;

sealed class FileEntry
{
    public string Name;
    public string FullPath;
    public long Size;
    public long Mtime;   // LastWriteTimeUtc ticks
    public DateTime Modified => new DateTime(Mtime, DateTimeKind.Utc).ToLocalTime();
}

struct ThumbResult
{
    public int Index;
    public Bitmap Bmp;     // null when Failed
    public bool Failed;
}

/// <summary>
/// The folder currently shown. Owns the file list, the cache store and a pool of worker
/// threads that (1) load / generate thumbnails for what is on screen first and
/// (2) then quietly index the rest of the folder.
/// </summary>
sealed class FolderSession : IDisposable
{
    public readonly string Path;
    public readonly FileEntry[] Files;
    public readonly FolderStore Store;
    public readonly int MissingAtOpen;

    readonly CancellationTokenSource cts = new();
    readonly object reqLock = new();
    readonly Stack<int> requests = new();
    readonly HashSet<int> inflight = new();
    readonly SemaphoreSlim wake = new(0);
    readonly ConcurrentQueue<ThumbResult> ready = new();
    Thread[] workers;
    int bgCursor = -1;
    volatile bool bgFinished;
    int done;
    int disposed;

    /// <summary>Index range (with prefetch margin) that the grid currently cares about.</summary>
    public volatile int VisFirst;
    public volatile int VisLast;

    public int Done => Volatile.Read(ref done);

    /// <summary>True while thumbnails of this folder are still being produced.</summary>
    public bool IsBusy
    {
        get
        {
            if (!bgFinished) return true;
            lock (reqLock) return requests.Count > 0 || inflight.Count > 0;
        }
    }

    FolderSession(string path, FileEntry[] files, FolderStore store, int missing)
    {
        Path = path;
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
            return new FolderSession(path, files, store, missing);
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

    /// <summary>Replaces the pending request list. Indices are given in the order they should be served.</summary>
    public void Request(IList<int> inPriorityOrder)
    {
        lock (reqLock)
        {
            requests.Clear();
            for (int k = inPriorityOrder.Count - 1; k >= 0; k--)
            {
                int idx = inPriorityOrder[k];
                if (!inflight.Contains(idx)) requests.Push(idx);
            }
        }
        if (workers != null && wake.CurrentCount < workers.Length) wake.Release(workers.Length);
    }

    public bool TryTakeResult(out ThumbResult r) => ready.TryDequeue(out r);

    /// <summary>Decoded cached thumbnail (used by the viewer as an instant placeholder).</summary>
    public Bitmap LoadThumbBitmap(int i)
    {
        var f = Files[i];
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
            int i = -1;
            bool visible = false;
            lock (reqLock)
            {
                while (requests.Count > 0)
                {
                    int c = requests.Pop();
                    if (c >= VisFirst && c <= VisLast) { i = c; visible = true; inflight.Add(c); break; }
                }
            }

            if (!visible)
            {
                i = NextBackground();
                if (i < 0)
                {
                    try { wake.Wait(300, ct); } catch (OperationCanceledException) { break; }
                    continue;
                }
            }

            try { Process(i, visible); }
            catch { }
            finally
            {
                if (visible) lock (reqLock) inflight.Remove(i);
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
        if (Store.TryGet(f.Name, f.Mtime, f.Size, out var data))
        {
            if (!visible) return;                       // background pass only creates what is missing
            if (data.Length == 0)
            {
                ready.Enqueue(new ThumbResult { Index = i, Failed = true });
                return;
            }
            try
            {
                ready.Enqueue(new ThumbResult { Index = i, Bmp = ImageLoader.BitmapFromJpeg(data) });
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
        byte[] data;
        try { data = ImageLoader.CreateThumbJpeg(f.FullPath); }
        catch { data = Array.Empty<byte>(); }

        try { if (Store.Put(f.Name, f.Mtime, f.Size, data)) Interlocked.Increment(ref done); }
        catch { }

        if (visible || (i >= VisFirst && i <= VisLast))
        {
            if (data.Length == 0) ready.Enqueue(new ThumbResult { Index = i, Failed = true });
            else
            {
                try { ready.Enqueue(new ThumbResult { Index = i, Bmp = ImageLoader.BitmapFromJpeg(data) }); }
                catch { ready.Enqueue(new ThumbResult { Index = i, Failed = true }); }
            }
        }
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

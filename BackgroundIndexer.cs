namespace Skimmer;

/// <summary>
/// Walks the folders chosen in Settings and builds their thumbnail cache while the app is running.
/// One low-priority thread; steps aside whenever the folder the user is looking at is still busy.
/// </summary>
sealed class BackgroundIndexer
{
    readonly Func<bool> activeFolderBusy;
    readonly ManualResetEventSlim wake = new(false);
    readonly Thread thread;
    volatile bool stop;
    volatile bool restart;

    public volatile string CurrentFolder = "";
    public volatile bool Active;
    public long ThumbsMade;

    public BackgroundIndexer(Func<bool> activeFolderBusy)
    {
        this.activeFolderBusy = activeFolderBusy;
        thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.Lowest, Name = "Skimmer-indexer" };
    }

    public void Start() => thread.Start();

    /// <summary>Settings changed: begin a fresh pass.</summary>
    public void Reconfigure()
    {
        restart = true;
        wake.Set();
    }

    public void Stop()
    {
        stop = true;
        wake.Set();
        try { thread.Join(3000); } catch { }
    }

    void Run()
    {
        while (!stop)
        {
            restart = false;
            wake.Reset();
            var s = AppSettings.Current;
            var roots = s.IndexFolders.ToArray();

            if (s.BackgroundIndexing && roots.Length > 0)
            {
                foreach (var root in roots)
                {
                    if (stop || restart) break;
                    try { ScanRoot(root, s.IndexSubfolders); } catch { }
                }
            }
            Active = false;
            CurrentFolder = "";

            if (stop) break;
            if (restart) continue;

            int minutes = s.BackgroundIndexing && roots.Length > 0 ? Math.Clamp(s.RescanMinutes, 1, 24 * 60) : 1;
            wake.Wait(TimeSpan.FromMinutes(minutes));
        }
    }

    void ScanRoot(string root, bool recurse)
    {
        if (!Directory.Exists(root)) return;
        var stack = new Stack<string>();
        stack.Push(root);
        var dirOpts = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
        };

        while (stack.Count > 0 && !stop && !restart)
        {
            string dir = stack.Pop();
            try { IndexFolder(dir); } catch { }

            if (!recurse) continue;
            try
            {
                foreach (var sub in new DirectoryInfo(dir).EnumerateDirectories("*", dirOpts))
                {
                    // skip junctions / symlinks to avoid loops
                    if ((sub.Attributes & FileAttributes.ReparsePoint) != 0 && sub.LinkTarget != null) continue;
                    stack.Push(sub.FullName);
                }
            }
            catch { }
        }
    }

    void WaitWhileUserBusy()
    {
        while (!stop && !restart && activeFolderBusy())
            Thread.Sleep(250);
    }

    void IndexFolder(string dir)
    {
        WaitWhileUserBusy();
        if (stop || restart) return;

        var files = FolderSession.ListFiles(dir);
        if (files.Length == 0) return;

        var store = StoreManager.Acquire(dir);
        try
        {
            var live = new HashSet<string>(files.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            StoreManager.TryCompact(store, live);

            foreach (var f in files)
            {
                if (stop || restart) break;
                if (store.Has(f.Name, f.Mtime, f.Size)) continue;

                Active = true;
                CurrentFolder = dir;
                WaitWhileUserBusy();
                if (stop || restart) break;

                byte[] data;
                try { data = ImageLoader.CreateThumbJpeg(f.FullPath); }
                catch { data = Array.Empty<byte>(); }
                try { if (store.Put(f.Name, f.Mtime, f.Size, data)) Interlocked.Increment(ref ThumbsMade); }
                catch { }
            }
            store.Flush();
        }
        finally
        {
            StoreManager.Release(store);
        }
    }
}

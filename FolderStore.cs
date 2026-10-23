using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Skimmer;

struct CacheEntry
{
    public long Mtime;
    public long Size;
    public long Offset;
    public int Length;   // 0 = "this file could not be decoded" marker
}

/// <summary>
/// Thumbnail cache for ONE folder: two files in the cache directory.
///   {key}.dat - concatenated JPEG thumbnails (append-only)
///   {key}.idx - append-only records: name, mtime, size, offset, length
/// The whole index is loaded into memory when the folder is opened, so a
/// lookup is a dictionary hit plus one positional read (thread-safe).
/// </summary>
sealed class FolderStore : IDisposable
{
    const int Magic = 0x314D4B53; // "SKM1"

    readonly object gate = new();
    readonly Dictionary<string, CacheEntry> map = new(StringComparer.OrdinalIgnoreCase);
    readonly string idxPath, datPath;

    SafeFileHandle dat;
    FileStream idx;
    BinaryWriter bw;
    long datEnd;
    long idxLen;      // length of the valid part of the idx file
    int records;      // records in idx file, including superseded ones
    int unflushed;

    public string FolderPath { get; }
    public int RefCount;   // guarded by StoreManager

    public static string KeyFor(string folder)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(folder.TrimEnd('\\', '/').ToLowerInvariant()));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    public FolderStore(string folder)
    {
        FolderPath = folder;
        Directory.CreateDirectory(AppPaths.CacheDir);
        string key = KeyFor(folder);
        idxPath = Path.Combine(AppPaths.CacheDir, key + ".idx");
        datPath = Path.Combine(AppPaths.CacheDir, key + ".dat");
        LoadOrCreate();
        try { File.SetLastWriteTimeUtc(idxPath, DateTime.UtcNow); } catch { }   // "used just now" marker for the cleaner
    }

    void LoadOrCreate()
    {
        map.Clear();
        records = 0;
        idxLen = 0;

        dat = File.OpenHandle(datPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        datEnd = RandomAccess.GetLength(dat);

        bool ok = false;
        long good = 0;
        if (File.Exists(idxPath))
        {
            try { (ok, good) = ReadIndex(); } catch { ok = false; }
        }

        if (!ok)
        {
            // Unreadable or missing index: start from scratch.
            map.Clear();
            records = 0;
            RandomAccess.SetLength(dat, 0);
            datEnd = 0;
            idxLen = 0;
        }
        else
        {
            idxLen = good;
        }
    }

    (bool ok, long good) ReadIndex()
    {
        using var fs = new FileStream(idxPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536, FileOptions.SequentialScan);
        using var br = new BinaryReader(fs, Encoding.UTF8);
        if (fs.Length < 8 || br.ReadInt32() != Magic) return (false, 0);
        br.ReadString(); // folder path (informational)
        long good = fs.Position;
        while (fs.Position < fs.Length)
        {
            try
            {
                string name = br.ReadString();
                long m = br.ReadInt64();
                long s = br.ReadInt64();
                long off = br.ReadInt64();
                int len = br.ReadInt32();
                good = fs.Position;
                records++;
                if (off < 0 || len < 0 || off + len > datEnd) continue;   // data file was truncated
                map[name] = new CacheEntry { Mtime = m, Size = s, Offset = off, Length = len };
            }
            catch (EndOfStreamException) { break; }
        }
        return (true, good);
    }

    void EnsureWriter()
    {
        if (idx != null) return;
        idx = new FileStream(idxPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite, 16384);
        idx.SetLength(idxLen);
        idx.Seek(idxLen, SeekOrigin.Begin);
        bw = new BinaryWriter(idx, Encoding.UTF8, leaveOpen: true);
        if (idxLen == 0)
        {
            bw.Write(Magic);
            bw.Write(FolderPath);
            bw.Flush();
        }
    }

    public bool Has(string name, long mtime, long size)
    {
        lock (gate)
            return map.TryGetValue(name, out var e) && e.Mtime == mtime && e.Size == size;
    }

    /// <summary>Returns true when an entry exists. data is empty for a "cannot decode" marker.</summary>
    public bool TryGet(string name, long mtime, long size, out byte[] data)
    {
        CacheEntry e;
        lock (gate)
        {
            if (!map.TryGetValue(name, out e) || e.Mtime != mtime || e.Size != size)
            {
                data = null;
                return false;
            }
        }
        if (e.Length == 0) { data = Array.Empty<byte>(); return true; }
        var buf = new byte[e.Length];
        int n = RandomAccess.Read(dat, buf, e.Offset);
        if (n != e.Length) { data = null; return false; }
        data = buf;
        return true;
    }

    public void Remove(string name)
    {
        lock (gate) map.Remove(name);
    }

    /// <summary>Stores a thumbnail (or an empty "failed" marker). Returns false if an up-to-date entry already existed.</summary>
    public bool Put(string name, long mtime, long size, byte[] data)
    {
        lock (gate)
        {
            if (map.TryGetValue(name, out var old) && old.Mtime == mtime && old.Size == size) return false;
            EnsureWriter();
            long off = datEnd;
            if (data.Length > 0)
            {
                RandomAccess.Write(dat, data, off);
                datEnd += data.Length;
            }
            bw.Write(name);
            bw.Write(mtime);
            bw.Write(size);
            bw.Write(off);
            bw.Write(data.Length);
            records++;
            if (++unflushed >= 64) FlushLocked();
            map[name] = new CacheEntry { Mtime = mtime, Size = size, Offset = off, Length = data.Length };
            return true;
        }
    }

    public void Flush()
    {
        lock (gate) FlushLocked();
    }

    void FlushLocked()
    {
        try { bw?.Flush(); idx?.Flush(); } catch { }
        unflushed = 0;
    }

    /// <summary>
    /// Drops thumbnails of deleted/replaced files when they make up a big part of the cache.
    /// Call only while nobody else uses this store.
    /// </summary>
    public void CompactIfNeeded(HashSet<string> liveNames)
    {
        lock (gate)
        {
            int liveCount = 0;
            foreach (var k in map.Keys) if (liveNames.Contains(k)) liveCount++;
            int garbage = records - liveCount;
            if (records < 200 || garbage < records * 0.3) return;
            try { CompactLocked(liveNames); } catch { }
        }
    }

    void CompactLocked(HashSet<string> live)
    {
        FlushLocked();
        string tmpDat = datPath + ".tmp", tmpIdx = idxPath + ".tmp";
        try
        {
            using (var nd = new FileStream(tmpDat, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
            using (var ni = new FileStream(tmpIdx, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
            using (var w = new BinaryWriter(ni, Encoding.UTF8))
            {
                w.Write(Magic);
                w.Write(FolderPath);
                long off = 0;
                foreach (var kv in map)
                {
                    if (!live.Contains(kv.Key)) continue;
                    var e = kv.Value;
                    if (e.Length > 0)
                    {
                        var buf = new byte[e.Length];
                        if (RandomAccess.Read(dat, buf, e.Offset) != e.Length) continue;
                        nd.Write(buf, 0, buf.Length);
                    }
                    w.Write(kv.Key);
                    w.Write(e.Mtime);
                    w.Write(e.Size);
                    w.Write(off);
                    w.Write(e.Length);
                    off += e.Length;
                }
            }
        }
        catch
        {
            TryDelete(tmpDat);
            TryDelete(tmpIdx);
            return;
        }

        // swap files, then reload state from disk (also recovers if a move fails)
        bw?.Dispose(); bw = null;
        idx?.Dispose(); idx = null;
        dat?.Dispose(); dat = null;
        try
        {
            File.Move(tmpDat, datPath, true);
            File.Move(tmpIdx, idxPath, true);
        }
        catch
        {
            TryDelete(tmpDat);
            TryDelete(tmpIdx);
        }
        LoadOrCreate();
    }

    static void TryDelete(string p)
    {
        try { File.Delete(p); } catch { }
    }

    public void Dispose()
    {
        lock (gate)
        {
            FlushLocked();
            bw?.Dispose(); bw = null;
            idx?.Dispose(); idx = null;
            dat?.Dispose(); dat = null;
        }
    }
}

/// <summary>Shares one FolderStore between the open folder and the background indexer.</summary>
static class StoreManager
{
    static readonly Dictionary<string, FolderStore> open = new(StringComparer.OrdinalIgnoreCase);

    static string Norm(string p) => p.TrimEnd('\\', '/');

    public static FolderStore Acquire(string folder)
    {
        lock (open)
        {
            string k = Norm(folder);
            if (!open.TryGetValue(k, out var s))
            {
                s = new FolderStore(folder);
                open[k] = s;
            }
            s.RefCount++;
            return s;
        }
    }

    public static void Release(FolderStore s)
    {
        lock (open)
        {
            if (--s.RefCount <= 0)
            {
                open.Remove(Norm(s.FolderPath));
                s.Dispose();
            }
        }
    }

    /// <summary>Compacts only if the caller is the sole user of the store.</summary>
    public static void TryCompact(FolderStore s, HashSet<string> live)
    {
        lock (open)
        {
            if (s.RefCount == 1) s.CompactIfNeeded(live);
        }
    }
}

/// <summary>Removes old cache folders: by age of last use, then by total size cap.</summary>
static class CacheCleaner
{
    public static (int files, long bytes) Clean(AppSettings s)
    {
        int count = 0;
        long freed = 0;
        string dir = AppPaths.CacheDir;
        if (!Directory.Exists(dir)) return (0, 0);

        try
        {
            var sets = Directory.EnumerateFiles(dir, "*.idx")
                .Select(p => (idx: new FileInfo(p), dat: new FileInfo(Path.ChangeExtension(p, ".dat"))))
                .ToList();

            // 1) by age
            var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, s.CacheMaxAgeDays));
            foreach (var g in sets.ToList())
            {
                if (g.idx.LastWriteTimeUtc < cutoff && TryDeletePair(g.idx, g.dat, ref count, ref freed))
                    sets.Remove(g);
            }

            // 2) by total size
            long cap = Math.Max(64, s.CacheMaxSizeMB) * 1024L * 1024L;
            long total = sets.Sum(g => Len(g.idx) + Len(g.dat));
            if (total > cap)
            {
                foreach (var g in sets.OrderBy(g => g.idx.LastWriteTimeUtc))
                {
                    if (total <= cap * 0.9) break;
                    long sz = Len(g.idx) + Len(g.dat);
                    if (TryDeletePair(g.idx, g.dat, ref count, ref freed)) total -= sz;
                }
            }

            // 3) leftovers: .dat without .idx, interrupted compaction temp files
            foreach (var p in Directory.EnumerateFiles(dir))
            {
                string ext = Path.GetExtension(p).ToLowerInvariant();
                bool orphanDat = ext == ".dat" && !File.Exists(Path.ChangeExtension(p, ".idx"));
                if (orphanDat || ext == ".tmp")
                    TryDeleteFile(new FileInfo(p), ref count, ref freed);
            }
        }
        catch { }
        return (count, freed);
    }

    public static (int files, long bytes) DeleteAll()
    {
        int count = 0;
        long freed = 0;
        string dir = AppPaths.CacheDir;
        if (!Directory.Exists(dir)) return (0, 0);
        try
        {
            foreach (var p in Directory.EnumerateFiles(dir, "*.idx").ToList())
                TryDeletePair(new FileInfo(p), new FileInfo(Path.ChangeExtension(p, ".dat")), ref count, ref freed);
            foreach (var p in Directory.EnumerateFiles(dir).ToList())
                TryDeleteFile(new FileInfo(p), ref count, ref freed);
        }
        catch { }
        return (count, freed);
    }

    public static long TotalSize()
    {
        try
        {
            return Directory.Exists(AppPaths.CacheDir)
                ? Directory.EnumerateFiles(AppPaths.CacheDir).Sum(p => new FileInfo(p).Length)
                : 0;
        }
        catch { return 0; }
    }

    static long Len(FileInfo f) => f.Exists ? f.Length : 0;

    // Files held open by a live FolderStore cannot be deleted (no FileShare.Delete), which protects the folder in use.
    static bool TryDeletePair(FileInfo idx, FileInfo dat, ref int count, ref long freed)
    {
        if (dat.Exists && !TryDeleteFile(dat, ref count, ref freed)) return false;
        return TryDeleteFile(idx, ref count, ref freed);
    }

    static bool TryDeleteFile(FileInfo f, ref int count, ref long freed)
    {
        try
        {
            if (!f.Exists) return true;
            long len = f.Length;
            f.Delete();
            count++;
            freed += len;
            return true;
        }
        catch { return false; }
    }
}

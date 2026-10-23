using System.Text.Json;

namespace Skimmer;

sealed class AppSettings
{
    // Background indexing
    public List<string> IndexFolders { get; set; } = new();
    public bool BackgroundIndexing { get; set; } = true;
    public bool IndexSubfolders { get; set; } = true;
    public int RescanMinutes { get; set; } = 30;

    // Cache lifetime
    public int CacheMaxAgeDays { get; set; } = 30;
    public int CacheMaxSizeMB { get; set; } = 4096;

    // View
    public int TileSize { get; set; } = 160;
    public bool ShowNames { get; set; } = true;
    public int SortMode { get; set; } = 0;          // 0 name, 1 date, 2 size
    public bool SortDescending { get; set; } = false;
    public bool ShowOverlay { get; set; } = true;   // info bar in full-screen viewer
    public string Language { get; set; } = "auto";  // auto | ru | en

    // Updates
    public bool AutoUpdateCheck { get; set; } = true;
    public string SkippedVersion { get; set; } = "";

    // Window state
    public string LastFolder { get; set; } = "";
    public int WindowX { get; set; } = int.MinValue;
    public int WindowY { get; set; } = int.MinValue;
    public int WindowW { get; set; } = 1280;
    public int WindowH { get; set; } = 820;
    public bool Maximized { get; set; } = false;
    public int TreeWidth { get; set; } = 270;

    static AppSettings current;
    public static AppSettings Current => current ??= Load();

    static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile));
                if (s != null)
                {
                    s.IndexFolders ??= new List<string>();
                    return s;
                }
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(AppPaths.SettingsFile,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

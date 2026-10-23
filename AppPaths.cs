namespace Skimmer;

/// <summary>
/// Where Skimmer keeps its settings and thumbnail cache.
/// Portable by design: next to Skimmer.exe. If that folder is read-only
/// (e.g. Program Files), falls back to %LOCALAPPDATA%\Skimmer.
/// </summary>
static class AppPaths
{
    public static readonly string DataDir;

    static AppPaths()
    {
        string exeDir = null;
        try { exeDir = Path.GetDirectoryName(Environment.ProcessPath); } catch { }
        if (string.IsNullOrEmpty(exeDir)) exeDir = AppContext.BaseDirectory;

        if (CanWrite(exeDir))
        {
            DataDir = exeDir;
        }
        else
        {
            DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Skimmer");
            Directory.CreateDirectory(DataDir);
        }
    }

    public static string SettingsFile => Path.Combine(DataDir, "Skimmer.settings.json");
    public static string CacheDir => Path.Combine(DataDir, "SkimmerCache");
    public static string LogFile => Path.Combine(DataDir, "Skimmer.log");

    static bool CanWrite(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, ".skimmer_write_test_" + Environment.ProcessId);
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }
}

namespace Skimmer;

/// <summary>
/// Where Skimmer keeps its settings and thumbnail cache.
/// Portable by design: next to Skimmer.exe. If that folder is read-only
/// (e.g. Program Files), falls back to %LOCALAPPDATA%\Skimmer.
/// </summary>
static class AppPaths
{
    public static readonly string DataDir;
    public static readonly string ExeDir;

    static AppPaths()
    {
        string exeDir = null;
        try { exeDir = Path.GetDirectoryName(Environment.ProcessPath); } catch { }
        if (string.IsNullOrEmpty(exeDir)) exeDir = AppContext.BaseDirectory;
        ExeDir = exeDir;

        // Started straight from a zip (Windows unpacks it into %TEMP% each time) - nothing would persist there.
        if (!IsTemp(exeDir) && CanWrite(exeDir))
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
    public static string LogsDirectory => Path.Combine(DataDir, "logs");

    /// <summary>Theme, accent and density (UiKit standard file name).</summary>
    public static string ThemeSettingsFile => Path.Combine(DataDir, "theme-settings.json");

    /// <summary>Last update check, dismissed and skipped versions.</summary>
    public static string UpdateStateFile => Path.Combine(DataDir, "update-state.json");

    static bool IsTemp(string dir)
    {
        try
        {
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\') + "\\";
            string d = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
            return d.StartsWith(temp, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

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

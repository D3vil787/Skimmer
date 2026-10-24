using System.Text;
using System.Text.Json;

namespace Skimmer;

/// <summary>update-state.json next to the program (brand book: "Выпуск"): when updates were last checked,
/// which version's banner was dismissed ("Позже" / "×") and which version was skipped in the dialog.</summary>
sealed class UpdateState
{
    public DateTime LastCheckUtc { get; set; } = DateTime.MinValue;
    public string DismissedVersion { get; set; } = "";
    public string SkippedVersion { get; set; } = "";

    static UpdateState current;
    public static UpdateState Current => current ??= Load();

    static UpdateState Load()
    {
        UpdateState s = null;
        try
        {
            if (File.Exists(AppPaths.UpdateStateFile))
                s = JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(AppPaths.UpdateStateFile));
        }
        catch { }
        s ??= new UpdateState();

        // 0.2 - 0.5 kept the skipped version in Skimmer.settings.json
        var legacy = AppSettings.Current.SkippedVersion;
        if (string.IsNullOrEmpty(s.SkippedVersion) && !string.IsNullOrEmpty(legacy))
        {
            s.SkippedVersion = legacy;
            AppSettings.Current.SkippedVersion = "";
            s.Save();
            AppSettings.Current.Save();
        }
        return s;
    }

    public void Save()
    {
        try
        {
            AtomicFile.WriteAllText(AppPaths.UpdateStateFile,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        }
        catch { }
    }

    /// <summary>The banner for this release was hidden or the version was skipped.</summary>
    public bool IsSuppressed(string tag) =>
        string.Equals(tag, DismissedVersion, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(tag, SkippedVersion, StringComparison.OrdinalIgnoreCase);
}

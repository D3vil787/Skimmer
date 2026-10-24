using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Skimmer;

/// <summary>
/// Interface texts from assets/Languages/ru.json and en.json (embedded into the exe).
/// The language is chosen in Settings (auto / ru / en) and applied at start;
/// English is the fallback for a missing key.
/// </summary>
static class Lang
{
    static readonly Dictionary<string, string> current;
    static readonly Dictionary<string, string> fallback;

    public static string Code { get; }

    static Lang()
    {
        string lang = AppSettings.Current.Language ?? "auto";
        if (lang == "auto")
        {
            string sys = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            lang = sys is "ru" or "uk" or "be" or "kk" ? "ru" : "en";
        }
        Code = lang == "ru" ? "ru" : "en";
        fallback = Load("en");
        current = Code == "en" ? fallback : Load(Code);
    }

    static Dictionary<string, string> Load(string code)
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Skimmer.Languages.{code}.json");
            if (s == null) return new Dictionary<string, string>();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(s) ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>Text by key; the key itself when it is missing in both languages.</summary>
    public static string Get(string key) =>
        current.TryGetValue(key, out var v) ? v : fallback.TryGetValue(key, out var f) ? f : key;

    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}

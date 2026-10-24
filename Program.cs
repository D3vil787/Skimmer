using System.Windows.Forms;

namespace Skimmer;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        System.Windows.Forms.Application.ThreadException += (s, e) => LogAndShow(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => LogAndShow(e.ExceptionObject as Exception);

        UpdateService.CleanupLeftovers();
        InitTheme();

        string arg = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        System.Windows.Forms.Application.Run(new MainForm(arg));

        UpdateService.StartPendingRestart();
    }

    /// <summary>
    /// UiKit theme start-up (brand book, "Правила реализации"): dark native controls allowed, theme-settings.json
    /// loaded, the Windows light/dark setting reported for "Same as Windows".
    /// The theme chosen in Skimmer 0.4 - 0.5 (Skimmer.settings.json) is carried over once.
    /// </summary>
    static void InitTheme()
    {
        ThemeNative.AllowDarkMode(true);
        var store = new ThemeSettingsStore();
        var theme = store.Load();

        string legacy = AppSettings.Current.Theme;
        if (!File.Exists(store.FilePath) && !string.IsNullOrEmpty(legacy))
        {
            theme = theme with
            {
                ThemeId = legacy switch
                {
                    "dark" => ThemeCatalog.GraphiteDarkId,
                    "system" => ThemeCatalog.SystemId,
                    _ => ThemeCatalog.FluentLightId
                }
            };
            store.Save(theme);
            ThemeLog.Write(Lang.Format("theme.log.migrated", legacy, theme.ThemeId));
        }

        ThemeService.SetSystemTheme(new RegistrySystemThemeReader().IsLightTheme());
        ThemeService.Apply(theme);
    }

    static void LogAndShow(Exception ex)
    {
        try { File.AppendAllText(AppPaths.LogFile, $"{DateTime.Now:s} {ex}\r\n\r\n"); } catch { }
        try { MessageBox.Show(ex?.Message ?? Lang.Get("err.unknown"), Lang.Get("app.title"), MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
    }
}

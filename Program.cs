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

        string arg = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        System.Windows.Forms.Application.Run(new MainForm(arg));

        UpdateService.StartPendingRestart();
    }

    static void LogAndShow(Exception ex)
    {
        try { File.AppendAllText(AppPaths.LogFile, $"{DateTime.Now:s} {ex}\r\n\r\n"); } catch { }
        try { MessageBox.Show(ex?.Message ?? "Unknown error", "Skimmer", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
    }
}

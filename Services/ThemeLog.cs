#nullable enable
using System.Globalization;
using System.Text;

namespace Skimmer.Services;

/// <summary>
/// Append-only журнал <c>logs\theme.log</c> (v1.10.0): повреждённый
/// <c>theme-settings.json</c>, недоступная облачная папка и перенос старой
/// настройки темы.
/// </summary>
/// <remarks>
/// Тема применяется до показа главного окна, поэтому диалогов здесь быть не может —
/// как и при восстановлении вкладок в v1.8.0, вся диагностика уходит в файл.
/// Неудачная запись подавляется: переносимая папка может быть только для чтения.
/// </remarks>
public static class ThemeLog
{
    /// <summary>Имя файла журнала.</summary>
    public const string FileName = "theme.log";

    private static readonly object WriteSync = new();

    /// <summary>Основной путь: <c>logs\</c> рядом с исполняемым файлом.</summary>
    public static string PrimaryPath => Path.Combine(AppPaths.LogsDirectory, FileName);

    /// <summary>Запасной путь для случая, когда переносимая папка только для чтения.</summary>
    public static string FallbackPath =>
        Path.Combine(Path.GetTempPath(), "Skimmer", "logs", FileName);

    /// <summary>Дописывает одну строку с отметкой времени.</summary>
    /// <param name="message">Текст сообщения без завершающего перевода строки.</param>
    /// <returns>Путь, куда удалось записать, или <c>null</c>.</returns>
    public static string? Write(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        string line = string.Format(
            CultureInfo.InvariantCulture,
            "{0:yyyy-MM-dd HH:mm:ss}  {1}{2}",
            DateTimeOffset.Now,
            message,
            Environment.NewLine);

        foreach (string path in Candidates())
        {
            try
            {
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                lock (WriteSync)
                {
                    File.AppendAllText(path, line, new UTF8Encoding(false));
                }

                return path;
            }
            catch (Exception ex) when (IsExpectedWriteFailure(ex))
            {
                // Пробуем следующий путь: отсутствие журнала не должно мешать теме.
            }
        }

        return null;
    }

    /// <summary>Пути-кандидаты в порядке приоритета, без повторов.</summary>
    public static IEnumerable<string> Candidates() =>
        new[] { PrimaryPath, FallbackPath }.Distinct(StringComparer.OrdinalIgnoreCase);

    private static bool IsExpectedWriteFailure(Exception exception) => exception
        is IOException
        or UnauthorizedAccessException
        or NotSupportedException
        or System.Security.SecurityException
        or ArgumentException;
}

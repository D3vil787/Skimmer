#nullable enable
using Microsoft.Win32;

namespace Skimmer.Ui;

/// <summary>
/// Источник системной настройки «светлая/тёмная тема приложений». Выделен в
/// интерфейс, чтобы карточку «Как в Windows» можно было проверить тестами без
/// записи в реестр.
/// </summary>
public interface ISystemThemeReader
{
    /// <summary>Системная тема приложений светлая.</summary>
    bool IsLightTheme();
}

/// <summary>
/// Читает <c>AppsUseLightTheme</c> из
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize</c>.
/// Любая ошибка чтения трактуется как светлая тема.
/// </summary>
public sealed class RegistrySystemThemeReader : ISystemThemeReader
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ValueName = "AppsUseLightTheme";

    /// <inheritdoc />
    public bool IsLightTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath);

            // Значения нет на сборках Windows без тёмной темы — там она светлая.
            return key?.GetValue(ValueName) is not int value || value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException
                                     or UnauthorizedAccessException
                                     or IOException
                                     or ObjectDisposedException)
        {
            return true;
        }
    }
}

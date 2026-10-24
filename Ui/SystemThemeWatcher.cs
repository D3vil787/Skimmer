#nullable enable
using Microsoft.Win32;

namespace Skimmer.Ui;

/// <summary>
/// Следит за системной настройкой «светлая/тёмная тема» для карточки
/// «Как в Windows» (v1.10.0). Смена темы Windows применяется без перезапуска:
/// приложение только перерисовывается, окна сессий не пересоздаются.
/// </summary>
/// <remarks>
/// Подписка на <see cref="SystemEvents.UserPreferenceChanged"/> живёт в статическом
/// событии и держит объект, поэтому <see cref="Dispose"/> обязательно отписывается —
/// та же утечка, что чинилась в планировщике обновлений v1.9.0.
/// </remarks>
public sealed class SystemThemeWatcher : IDisposable
{
    private readonly ISystemThemeReader _reader;
    private bool _isLight;
    private bool _disposed;

    /// <summary>Создаёт наблюдателя и сразу читает текущее состояние.</summary>
    /// <param name="reader">Источник настройки; по умолчанию — реестр.</param>
    public SystemThemeWatcher(ISystemThemeReader? reader = null)
    {
        _reader = reader ?? new RegistrySystemThemeReader();
        _isLight = ReadIsLight();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        IsSubscribed = true;
    }

    /// <summary>Системная тема светлая.</summary>
    public bool IsLight => _isLight;

    /// <summary>Идентификатор темы каталога, соответствующий системной настройке.</summary>
    public string ThemeId => ThemeCatalog.ForSystem(_isLight).Id;

    /// <summary>Подписка на системные события активна (проверяется тестом на утечку).</summary>
    public bool IsSubscribed { get; private set; }

    /// <summary>Системная тема изменилась.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Перечитывает настройку. Событие поднимается только при фактическом
    /// изменении: <c>UserPreferenceChanged</c> приходит и на смену шрифтов,
    /// раскладки и цветовых профилей.
    /// </summary>
    /// <returns><c>true</c>, если значение изменилось.</returns>
    public bool Refresh()
    {
        if (_disposed)
        {
            return false;
        }

        bool light = ReadIsLight();
        if (light == _isLight)
        {
            return false;
        }

        _isLight = light;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        IsSubscribed = false;
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
        {
            Refresh();
        }
    }

    /// <summary>Ошибка чтения настройки означает светлую тему, а не сбой приложения.</summary>
    private bool ReadIsLight()
    {
        try
        {
            return _reader.IsLightTheme();
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                     or UnauthorizedAccessException
                                     or System.Security.SecurityException
                                     or IOException
                                     or NotSupportedException)
        {
            return true;
        }
    }
}

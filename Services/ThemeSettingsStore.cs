#nullable enable
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Skimmer.Ui;

namespace Skimmer.Services;

/// <summary>
/// Чтение и запись <c>theme-settings.json</c> (v1.10.0). Устроено как
/// хранилище сессий Connexa: атомарная запись, а сбой не считается ошибкой —
/// Connexa должна запускаться и с повреждённым, и с недоступным файлом темы.
/// </summary>
public sealed class ThemeSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _path;

    /// <summary>Создаёт хранилище по текущему месту хранения темы.</summary>
    public ThemeSettingsStore()
        : this(AppPaths.ThemeSettingsFile)
    {
    }

    /// <summary>Создаёт хранилище для указанного файла.</summary>
    public ThemeSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    /// <summary>Путь к файлу настроек темы — локальный или в облачной папке.</summary>
    public string FilePath => _path;

    /// <summary>
    /// Загружает настройки темы. Отсутствующий, недоступный или повреждённый файл
    /// даёт значения по умолчанию и строку в <c>logs\theme.log</c>.
    /// </summary>
    public ThemeSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new ThemeSettings();
            }

            string json = File.ReadAllText(_path, Encoding.UTF8);
            return JsonSerializer.Deserialize<ThemeSettings>(json, JsonOptions) ?? new ThemeSettings();
        }
        catch (JsonException ex)
        {
            // Повреждённый файл читается как «темы не выбирали»: ближайшее
            // сохранение перезапишет его целиком, откладывать копию незачем.
            ThemeLog.Write(string.Format(Lang.Get("theme.log.corrupt"), _path, ex.Message));
            return new ThemeSettings();
        }
        catch (Exception ex) when (ex is IOException
                                     or UnauthorizedAccessException
                                     or NotSupportedException)
        {
            ThemeLog.Write(string.Format(Lang.Get("theme.log.read_failed"), _path, ex.Message));
            return new ThemeSettings();
        }
    }

    /// <summary>
    /// Сохраняет настройки атомарно: сначала <c>*.tmp</c>, затем замена. Облачный
    /// клиент не должен увидеть недописанный файл и разослать его на другие ПК.
    /// </summary>
    /// <returns><c>true</c>, если файл записан.</returns>
    public bool Save(ThemeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            AtomicFile.WriteAllText(_path, json, new UTF8Encoding(false));
            return true;
        }
        catch (Exception ex) when (ex is IOException
                                     or UnauthorizedAccessException
                                     or NotSupportedException)
        {
            // Папка приложения или облачная папка может быть недоступна для записи.
            // Тема остаётся применённой в текущей сессии; диалога пользователь не видит.
            ThemeLog.Write(string.Format(Lang.Get("theme.log.save_failed"), _path, ex.Message));
            return false;
        }
    }

    /// <summary>
    /// Переносит старую настройку <c>AppSettings.Theme</c> в этот файл при первом
    /// запуске v1.10.0: <see cref="LegacyAppTheme.Light"/> даёт
    /// <c>fluent-light</c>, <see cref="LegacyAppTheme.Dark"/> — <c>graphite-dark</c>.
    /// </summary>
    /// <remarks>
    /// Существующий <c>theme-settings.json</c> означает, что пользователь уже
    /// выбирал тему в новом окне, и переносить поверх его выбора нельзя — поэтому
    /// перенос выполняется ровно один раз.
    /// </remarks>
    /// <param name="current">Настройки, прочитанные из файла.</param>
    /// <param name="legacyTheme">Значение из <c>settings.json</c> или <c>null</c>.</param>
    /// <returns>Настройки с перенесённой темой либо <paramref name="current"/>.</returns>
    public ThemeSettings Migrate(ThemeSettings current, LegacyAppTheme? legacyTheme)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (legacyTheme is null || File.Exists(_path))
        {
            return current;
        }

        ThemeSettings migrated = current with
        {
            ThemeId = legacyTheme == LegacyAppTheme.Dark
                ? ThemeCatalog.GraphiteDarkId
                : ThemeCatalog.FluentLightId,
        };

        Save(migrated);
        ThemeLog.Write(string.Format(Lang.Get("theme.log.migrated"), legacyTheme, migrated.ThemeId));
        return migrated;
    }
}

#nullable enable
using Skimmer.Ui;

namespace Skimmer.Services;

/// <summary>
/// Старое значение темы из <c>settings.json</c> (до v1.10.0). Перечисление
/// сохранено целиком — v1.9.x писала его числом, и смена типа поля отправила бы
/// весь файл настроек в «повреждённые».
/// </summary>
public enum LegacyAppTheme
{
    /// <summary>Светлая тема — переносится в <c>fluent-light</c>.</summary>
    Light,

    /// <summary>Тёмная тема — переносится в <c>graphite-dark</c>.</summary>
    Dark,
}

/// <summary>
/// Настройки оформления из <c>theme-settings.json</c> (v1.10.0). Вынесены из
/// <c>settings.json</c> по образцу <c>session-state.json</c>: при общей облачной
/// конфигурации тема может остаться делом одного ПК.
/// </summary>
public sealed record ThemeSettings
{
    /// <summary>
    /// Идентификатор темы из <see cref="ThemeCatalog"/> либо
    /// <see cref="ThemeCatalog.SystemId"/> для карточки «Как в Windows».
    /// </summary>
    public string ThemeId { get; init; } = ThemeCatalog.FluentLightId;

    /// <summary>
    /// Акцентный цвет в формате <c>#RRGGBB</c>. <c>null</c> — акцент самой темы.
    /// </summary>
    public string? AccentColor { get; init; }

    /// <summary>Плотность списка: от неё зависит высота строки дерева.</summary>
    public ThemeDensity Density { get; init; } = ThemeDensity.Normal;

    /// <summary>Показывать точку активной сессии у строк дерева.</summary>
    public bool ShowSessionIndicator { get; init; } = true;
}

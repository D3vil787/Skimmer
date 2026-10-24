#nullable enable
using System.Drawing;

namespace Skimmer.Ui;

/// <summary>Стиль отрисовки полосы вкладок сессий.</summary>
public enum TabStyle
{
    /// <summary>Полоска акцента по верхней кромке активной вкладки (как до v1.10.0).</summary>
    TopAccentBar,

    /// <summary>«Таблетка»: скруглённый прямоугольник вокруг активной вкладки.</summary>
    Pill,

    /// <summary>
    /// Линия акцента по нижней кромке активной вкладки, углы острые (v1.11.0,
    /// Terminal Ops).
    /// </summary>
    BottomLine,
}

/// <summary>Чем обведена «таблетка» активной вкладки при <see cref="TabStyle.Pill"/>.</summary>
public enum TabPillBorder
{
    /// <summary>
    /// Акцентом (как в v1.10.0). Белая карточка на светлом фоне видна только по
    /// кромке, поэтому Fluent Light без обводки потеряла бы активную вкладку.
    /// </summary>
    Accent,

    /// <summary>
    /// Едва заметной светлой линией — <see cref="ThemeDraw.PillBorder"/> поверх
    /// заливки (v1.11.0, Aurora Glass).
    /// </summary>
    Faint,

    /// <summary>Ничем: «таблетку» отличает только заливка.</summary>
    None,
}

/// <summary>Вид индикатора активной сессии в дереве подключений.</summary>
public enum SessionIndicatorStyle
{
    /// <summary>Точка цвета <see cref="ThemePalette.SessionLive"/> (как в v1.10.0).</summary>
    Dot,

    /// <summary>Точка с мягким свечением вокруг (v1.11.0, Aurora Glass).</summary>
    GlowingDot,

    /// <summary>Квадрат со свечением (v1.11.0, Terminal Ops).</summary>
    GlowingSquare,
}

/// <summary>Плотность списка: от неё зависит высота строки дерева подключений.</summary>
public enum ThemeDensity
{
    /// <summary>Компактная — больше строк на экране.</summary>
    Compact,

    /// <summary>Обычная, значение по умолчанию.</summary>
    Normal,

    /// <summary>Просторная — удобнее попадать мышью.</summary>
    Spacious,
}

/// <summary>Высота строки для каждой плотности.</summary>
public static class ThemeDensityExtensions
{
    /// <summary>Высота строки дерева в пикселях при 100 % DPI.</summary>
    public static int RowHeight(this ThemeDensity density) => density switch
    {
        ThemeDensity.Compact => 24,
        ThemeDensity.Spacious => 32,
        _ => 28,
    };
}

/// <summary>
/// Концы градиентов темы (v1.11.0). Начала лежат в палитре, поэтому тема без
/// градиентов — это просто <c>null</c>: ни одно поле палитры не остаётся пустым.
/// </summary>
/// <param name="FormEnd">Куда уходит градиент фона окна (начало — <see cref="ThemePalette.FormBack"/>).</param>
/// <param name="ChromeEnd">Куда уходит градиент полосы меню (начало — <see cref="ThemePalette.ChromeBack"/>).</param>
/// <param name="RowSelectionEnd">Куда уходит подсветка выбранной строки дерева.</param>
public sealed record ThemeGradients(Color FormEnd, Color ChromeEnd, Color RowSelectionEnd);

/// <summary>
/// Одна тема каталога: идентификатор, ключи локализации, палитра и те параметры
/// отрисовки, которые меняются вместе с ней.
/// </summary>
/// <param name="Id">Устойчивый идентификатор для <c>theme-settings.json</c>.</param>
/// <param name="NameKey">Ключ локализации названия темы.</param>
/// <param name="DescriptionKey">Ключ локализации описания для карточки.</param>
/// <param name="IsDark">Тема тёмная: от этого зависят заголовок окна и тема нативных контролов.</param>
/// <param name="DefaultAccent">Акцент темы, пока пользователь не выбрал свой.</param>
/// <param name="TabStyle">Стиль полосы вкладок.</param>
/// <param name="CornerRadius">Радиус скругления карточек, «таблеток» и подсветки строки.</param>
/// <param name="Palette">Цвета темы.</param>
/// <param name="ChromeFontFamily">
/// Шрифт дерева, вкладок и меню — список семейств через запятую в порядке
/// предпочтения, как в CSS. <c>null</c> означает Segoe UI; диалоги остаются на
/// Segoe UI при любом значении (см. <see cref="ChromeFont"/>).
/// </param>
/// <param name="Gradients">Концы градиентов или <c>null</c> у темы с плоскими заливками.</param>
/// <param name="SessionIndicator">Вид индикатора активной сессии в дереве.</param>
/// <param name="PillBorder">
/// Обводка «таблетки» активной вкладки. Параметр темы, а не разбор её
/// идентификатора: вкладки рисует один общий код (v1.11.0).
/// </param>
public sealed record ThemeDefinition(
    string Id,
    string NameKey,
    string DescriptionKey,
    bool IsDark,
    Color DefaultAccent,
    TabStyle TabStyle,
    int CornerRadius,
    ThemePalette Palette,
    string? ChromeFontFamily = null,
    ThemeGradients? Gradients = null,
    SessionIndicatorStyle SessionIndicator = SessionIndicatorStyle.Dot,
    TabPillBorder PillBorder = TabPillBorder.Accent);

/// <summary>Вариант акцентного цвета в окне «Темы».</summary>
/// <param name="Id">Идентификатор варианта (для <c>AccessibleName</c> и тестов).</param>
/// <param name="NameKey">Ключ локализации названия цвета.</param>
/// <param name="Color">Цвет до подгонки под контраст темы.</param>
public sealed record AccentOption(string Id, string NameKey, Color Color);

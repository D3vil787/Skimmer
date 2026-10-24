#nullable enable
using System.Drawing;

namespace Skimmer.Ui;

/// <summary>
/// Палитра одной темы оформления. Поля соответствуют таблицам «Палитры» из
/// <c>DevPlan_v1.10.0_Themes.md</c> и <c>DevPlan_v1.11.0_ThemesPack.md</c>: по одному
/// цвету на роль, без оттенков «на глаз». Производные цвета (подсветка строки под
/// курсором, рамка «таблетки») считаются из этих значений в <see cref="ThemeDraw"/>,
/// а концы градиентов лежат в <see cref="ThemeGradients"/>.
/// </summary>
public sealed record ThemePalette(
    Color FormBack,
    Color ChromeBack,
    Color SidebarBack,
    Color PanelBack,
    Color InputBack,
    Color InputBorder,
    Color Fore,
    Color Muted,
    Color Border,
    Color RowSelectionBack,
    Color RowSelectionFore,
    Color MenuHoverBack,
    Color TabBack,
    Color TabActiveBack,
    Color TabActiveFore,
    Color TabInactiveFore,
    Color Accent,
    Color StatusBack,
    Color StatusFore,
    Color FolderIcon,
    Color RdpIcon,
    Color WebIcon,
    Color SshIcon,
    Color VncIcon,
    Color AdminIcon,
    Color SessionLive,
    Color TerminalBack,
    Color TerminalFore,
    Color TerminalCursor,
    Color TerminalSelection);

/// <summary>Свойства палитры, которые считаются из её цветов.</summary>
public static class ThemePaletteExtensions
{
    /// <summary>
    /// Фон палитры тёмный. Нужно отрисовке иконок: внутренние детали на тёмном
    /// фоне приходится затемнять, а на светлом — осветлять.
    /// </summary>
    public static bool IsDarkBackground(this ThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        return ThemeContrast.RelativeLuminance(palette.FormBack) < 0.5;
    }
}

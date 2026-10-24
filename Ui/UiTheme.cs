#nullable enable
using System.Drawing;

namespace Skimmer.Ui;

/// <summary>
/// Единое оформление интерфейса. Шрифт Segoe UI 10pt применяется ко всем формам
/// (Task 4) — достаточно унаследовать шрифт от формы. Цвета живут в палитре
/// активной темы (<see cref="ThemeService"/>); здесь остались только те, что от
/// темы не зависят.
/// </summary>
public static class UiTheme
{
    /// <summary>Имя основного шрифта интерфейса.</summary>
    public const string FontFamily = "Segoe UI";

    /// <summary>Базовый размер шрифта в пунктах. По BRANDBOOK.md это 10 pt (в Connexa было 9).</summary>
    public const float FontSize = 10f;

    /// <summary>Мелкий шрифт: пояснения под полями, строка ссылок.</summary>
    public const float SmallFontSize = 9f;

    /// <summary>Моноширинный шрифт для терминала и полей с путями.</summary>
    public const string MonoFontFamily = "Consolas";

    /// <summary>Основной шрифт интерфейса — Segoe UI 9pt.</summary>
    public static Font Default { get; } = new(FontFamily, FontSize, FontStyle.Regular);

    /// <summary>Шрифт средней насыщенности для активных элементов.</summary>
    public static Font Medium { get; } = new("Segoe UI Semibold", FontSize, FontStyle.Regular);

    /// <summary>Полужирный вариант основного шрифта.</summary>
    public static Font Bold { get; } = new(FontFamily, FontSize, FontStyle.Bold);

    /// <summary>Шрифт заголовков диалогов (до BRANDBOOK.md; новые окна используют <see cref="Title"/>).</summary>
    public static Font Heading { get; } = new(FontFamily, 12f, FontStyle.Regular);

    /// <summary>Заголовок страницы или диалога: Segoe UI Semibold 15 pt.</summary>
    public static Font Title { get; } = new("Segoe UI Semibold", 15f, FontStyle.Regular);

    /// <summary>Заголовок раздела или карточки: Segoe UI Semibold 11 pt, цвет акцента.</summary>
    public static Font Subtitle { get; } = new("Segoe UI Semibold", 11f, FontStyle.Regular);

    /// <summary>Мелкий шрифт: Segoe UI 9 pt, цвет «приглушённый».</summary>
    public static Font Small { get; } = new(FontFamily, SmallFontSize, FontStyle.Regular);

    /// <summary>Символы на кнопках панели инструментов: Segoe UI Symbol 11 pt.</summary>
    public static Font Symbol { get; } = new("Segoe UI Symbol", 11f, FontStyle.Regular);

    /// <summary>Шрифт для символов-индикаторов (✓ / ✕).</summary>
    public static Font Glyph { get; } = new(FontFamily, 12f, FontStyle.Bold);

    /// <summary>Моноширинный шрифт.</summary>
    public static Font Mono { get; } = new(MonoFontFamily, FontSize, FontStyle.Regular);

    /// <summary>
    /// Шрифт «хрома» активной темы: дерево подключений, полоса вкладок и меню.
    /// У большинства тем это тот же Segoe UI, что и в диалогах; Terminal Ops
    /// просит моноширинный — см. <see cref="ChromeFont"/>.
    /// </summary>
    /// <remarks>
    /// Диалоги берут шрифт из <see cref="Default"/> и за темой не следуют: смена
    /// метрик шрифта сдвинула бы в них всю раскладку.
    /// </remarks>
    public static Font Chrome => ChromeFont.For(ThemeService.Current);

    /// <summary>Шрифт хрома средней насыщенности — подпись активной вкладки.</summary>
    public static Font ChromeMedium => ChromeFont.MediumFor(ThemeService.Current);

    /// <summary>Акцентный цвет активной темы — выбранный пользователем или свой у темы.</summary>
    public static Color Accent => ThemeService.Accent;

    /// <summary>
    /// Цвет успешного состояния. Состояние — не оформление: зелёный остаётся
    /// зелёным в любой теме. Читаемость в обеих проверяется
    /// <c>ThemeCatalogTests</c>.
    /// </summary>
    public static Color Success { get; } = Color.FromArgb(22, 145, 82);

    /// <summary>Цвет ошибки.</summary>
    public static Color Error { get; } = Color.FromArgb(197, 48, 48);

    /// <summary>Цвет предупреждения: действие не выполнено, но вкладка работает.</summary>
    public static Color Warning { get; } = Color.FromArgb(183, 121, 31);

    /// <summary>Приглушённый цвет текста для активной темы.</summary>
    public static Color Muted => ThemeService.Palette.Muted;

    /// <summary>Цвет подсветки целевой папки при drag &amp; drop.</summary>
    public static Color DropTarget =>
        ThemeDraw.DropTarget(ThemeService.Palette.SidebarBack, ThemeService.Accent);

    /// <summary>
    /// Фон терминала xterm.js. С v1.11.0 это не константа: терминал следует за
    /// активной темой вместе с остальным интерфейсом.
    /// </summary>
    public static Color TerminalBackground => ThemeService.Palette.TerminalBack;

    /// <summary>
    /// Применяет базовый шрифт и текущую тему к форме, включая цвет заголовка окна.
    /// Форма следует за сменой темы, пока жива: подписка снимается в
    /// <see cref="Form.HandleDestroyed"/>.
    /// </summary>
    public static void Apply(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        form.Font = Default;
        ThemeService.Apply(form);
        form.Load += (_, _) => ThemeService.Apply(form);

        void ApplyChrome(object? sender, EventArgs args) => ThemeService.ApplyWindowChrome(form);

        void ApplyAll(object? sender, EventArgs args)
        {
            ThemeService.Apply(form);
            ThemeService.ApplyWindowChrome(form);
        }

        form.HandleCreated += ApplyChrome;
        ThemeService.ThemeChanged += ApplyAll;

        // Отписка именно в Disposed: HandleDestroyed не приходит у формы, которую
        // создали, но так и не показали, и подписка на статическое событие
        // продержала бы её в памяти до конца процесса.
        form.Disposed += (_, _) =>
        {
            form.HandleCreated -= ApplyChrome;
            ThemeService.ThemeChanged -= ApplyAll;
        };
    }
}

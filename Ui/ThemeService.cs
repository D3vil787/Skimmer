#nullable enable
using System.Drawing;
using System.Globalization;
using Skimmer.Services;

namespace Skimmer.Ui;

/// <summary>
/// Статический фасад системы тем: какая тема активна, какой у неё акцент и
/// плотность, и как применить палитру к дереву контролов. Остальной код обращается
/// только сюда, поэтому добавление темы в <see cref="ThemeCatalog"/> ничего здесь
/// не меняет.
/// </summary>
public static class ThemeService
{
    /// <summary>Минимальный контраст акцента с фоном окна (WCAG AA для крупных элементов).</summary>
    private const double AccentContrast = 3.0;

    private static ThemeSettings _settings = new();
    private static ThemeDefinition _current = ThemeCatalog.Get(null);
    private static ThemePalette _palette = _current.Palette;
    private static bool _systemIsLight = true;

    /// <summary>Выбранные настройки темы — то, что лежит в <c>theme-settings.json</c>.</summary>
    public static ThemeSettings Settings => _settings;

    /// <summary>Активная тема. Для карточки «Как в Windows» — та, что выбрала система.</summary>
    public static ThemeDefinition Current => _current;

    /// <summary>
    /// Палитра активной темы, в которой <see cref="ThemePalette.Accent"/> уже заменён
    /// выбранным пользователем акцентом.
    /// </summary>
    public static ThemePalette Palette => _palette;

    /// <summary>Акцентный цвет: выбранный пользователем или собственный акцент темы.</summary>
    public static Color Accent => _palette.Accent;

    /// <summary>Концы градиентов активной темы или <c>null</c>, если заливки плоские.</summary>
    public static ThemeGradients? Gradients => _current.Gradients;

    /// <summary>Плотность списка.</summary>
    public static ThemeDensity Density => _settings.Density;

    /// <summary>Показывать ли индикатор активных сессий в дереве.</summary>
    public static bool ShowSessionIndicator => _settings.ShowSessionIndicator;

    /// <summary>Выбрана карточка «Как в Windows».</summary>
    public static bool FollowsSystem => ThemeCatalog.IsSystem(_settings.ThemeId);

    /// <summary>Последнее известное состояние системной настройки «светлая тема».</summary>
    public static bool SystemIsLight => _systemIsLight;

    /// <summary>
    /// Активная тема изменилась: сменилась сама тема, акцент, плотность или
    /// индикатор сессий. Подписчики только перерисовываются — пересоздавать окна
    /// и забирать фокус у открытых сессий нельзя.
    /// </summary>
    public static event EventHandler? ThemeChanged;

    /// <summary>
    /// Применяет настройки темы. <see cref="ThemeChanged"/> поднимается только при
    /// фактическом изменении: лишняя перерисовка в открытой RDP-сессии — лишний
    /// мерцающий кадр.
    /// </summary>
    public static void Apply(ThemeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_settings == settings)
        {
            return;
        }

        _settings = settings;
        Resolve();
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Сообщает текущее состояние системной настройки «светлая/тёмная».
    /// Перерисовка происходит только когда выбрана карточка «Как в Windows».
    /// </summary>
    public static void SetSystemTheme(bool light)
    {
        if (_systemIsLight == light)
        {
            return;
        }

        _systemIsLight = light;
        if (!FollowsSystem)
        {
            return;
        }

        Resolve();
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Акцент для темы: выбранный пользователем цвет, подогнанный под контраст с
    /// фоном, либо акцент самой темы. Нечитаемое значение игнорируется.
    /// </summary>
    public static Color ResolveAccent(ThemeDefinition theme, string? accentColor)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (!TryParseHex(accentColor, out Color chosen))
        {
            return theme.DefaultAccent;
        }

        return ThemeContrast.EnsureContrast(chosen, theme.Palette.FormBack, AccentContrast);
    }

    /// <summary>Разбирает цвет в формате <c>#RRGGBB</c> (решётка не обязательна).</summary>
    public static bool TryParseHex(string? value, out Color color)
    {
        color = Color.Empty;
        string text = (value ?? string.Empty).Trim().TrimStart('#');
        if (text.Length != 6
            || !int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int packed))
        {
            return false;
        }

        color = Color.FromArgb((packed >> 16) & 0xFF, (packed >> 8) & 0xFF, packed & 0xFF);
        return true;
    }

    /// <summary>Записывает цвет в формате <c>#RRGGBB</c> для файла настроек.</summary>
    public static string ToHex(Color color) => string.Format(
        CultureInfo.InvariantCulture,
        "#{0:X2}{1:X2}{2:X2}",
        color.R,
        color.G,
        color.B);

    /// <summary>Применяет текущую тему к контролу и всем его дочерним.</summary>
    public static void Apply(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        ApplyControl(control);
        control.Invalidate(invalidateChildren: true);
    }

    /// <summary>Применяет текущую тему к полосе меню или контекстному меню.</summary>
    public static void Apply(ToolStrip strip)
    {
        ArgumentNullException.ThrowIfNull(strip);
        strip.BackColor = _palette.ChromeBack;
        strip.ForeColor = _palette.Fore;

        // Меню входит в «хром», поэтому следует за шрифтом темы. Диалоги шрифт не
        // меняют: у них нет ToolStrip, а форма берёт его из UiTheme.Default.
        strip.Font = UiTheme.Chrome;
        strip.RenderMode = ToolStripRenderMode.Professional;
        strip.Renderer = new ThemedMenuRenderer(_current, _palette);
        ApplyItems(strip.Items);
        strip.Invalidate();
    }

    /// <summary>
    /// Приводит заголовок окна в соответствие с темой. Вызывается при создании
    /// дескриптора и при каждой смене темы — см. <see cref="UiTheme.Apply(Form)"/>.
    /// </summary>
    public static void ApplyWindowChrome(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (form.IsHandleCreated)
        {
            ThemeNative.ApplyWindowChrome(form.Handle, _current);
        }
    }

    private static void Resolve()
    {
        _current = FollowsSystem
            ? ThemeCatalog.ForSystem(_systemIsLight)
            : ThemeCatalog.Get(_settings.ThemeId);
        _palette = _current.Palette with { Accent = ResolveAccent(_current, _settings.AccentColor) };

        // Кисти и битмапы свечения прежней палитры больше не понадобятся.
        GradientPainter.Reset();
    }

    private static void ApplyControl(Control control)
    {
        control.ForeColor = _palette.Fore;
        control.BackColor = control switch
        {
            Form => _palette.FormBack,
            TreeView => _palette.SidebarBack,
            TextBoxBase or ComboBox or NumericUpDown or CheckedListBox
                or ListBox or ListView or DataGridView => _palette.InputBack,
            MenuStrip or ToolStrip or StatusStrip => _palette.ChromeBack,
            TabControl or TabPage => _palette.TabBack,
            _ => _palette.PanelBack,
        };

        switch (control)
        {
            case Button button:
                // Кнопка в стиле Standard игнорирует BackColor в тёмной теме
                // (проверено спайком), поэтому все кнопки плоские в любой теме.
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = FlatStyle.Flat;

                // Рамка кнопки — общий цвет границ палитры, а не цвет рамки поля
                // ввода (v1.11.0). У Aurora Glass InputBorder #4A3F7E на фоне
                // панели #1C1540 читался как светлая обводка вокруг кнопок
                // «назад/вперёд/обновить» в Web-вкладке.
                button.FlatAppearance.BorderColor = _palette.Border;
                break;

            case DataGridView grid:
                grid.BackgroundColor = _palette.InputBack;
                grid.GridColor = _palette.Border;
                grid.DefaultCellStyle.BackColor = _palette.InputBack;
                grid.DefaultCellStyle.ForeColor = _palette.Fore;
                grid.DefaultCellStyle.SelectionBackColor = _palette.RowSelectionBack;
                grid.DefaultCellStyle.SelectionForeColor = _palette.RowSelectionFore;
                grid.ColumnHeadersDefaultCellStyle.BackColor = _palette.ChromeBack;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = _palette.Fore;
                grid.EnableHeadersVisualStyles = false;
                ApplyExplorerTheme(grid);
                break;

            case TreeView tree:
                tree.LineColor = _palette.Border;
                ApplyExplorerTheme(tree);
                break;

            case ListBox or ListView or CheckedListBox:
                ApplyExplorerTheme(control);
                break;

            case TextBoxBase { Multiline: true }:
                ApplyExplorerTheme(control);
                break;

            case ToolStrip strip:
                Apply(strip);
                break;
        }

        foreach (Control child in control.Controls)
        {
            ApplyControl(child);
        }
    }

    /// <summary>
    /// Полосы прокрутки рисует сама Windows, цвета WinForms их не касаются:
    /// контрол переводится на тёмную или светлую тему проводника.
    /// </summary>
    private static void ApplyExplorerTheme(Control control)
    {
        if (control.IsHandleCreated)
        {
            ThemeNative.ApplyExplorerTheme(control.Handle, _current.IsDark);
        }
    }

    private static void ApplyItems(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            item.BackColor = _palette.ChromeBack;
            item.ForeColor = _palette.Fore;
            if (item is ToolStripDropDownItem dropDownItem)
            {
                dropDownItem.DropDown.BackColor = _palette.ChromeBack;
                dropDownItem.DropDown.ForeColor = _palette.Fore;
                ApplyItems(dropDownItem.DropDownItems);
            }
        }
    }
}

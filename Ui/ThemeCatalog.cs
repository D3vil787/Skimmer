#nullable enable
using System.Drawing;

namespace Skimmer.Ui;

/// <summary>
/// Каталог тем оформления из UiKit (копия Connexa). В Skimmer оставлены только
/// Fluent Light и Graphite Dark, как требует BRANDBOOK.md.
/// Каталог тем оформления (v1.10.0). Окно «Темы» и <see cref="ThemeService"/>
/// строятся только из этого списка: новая тема — новая запись с палитрой,
/// остальной код не меняется (так в v1.11.0 добавляются Terminal Ops и Aurora Glass).
/// </summary>
public static class ThemeCatalog
{
    /// <summary>Идентификатор светлой темы — и значение по умолчанию.</summary>
    public const string FluentLightId = "fluent-light";

    /// <summary>Идентификатор тёмной темы.</summary>
    public const string GraphiteDarkId = "graphite-dark";

    /// <summary>
    /// Псевдо-идентификатор карточки «Как в Windows». В каталоге его нет:
    /// активной становится <see cref="FluentLightId"/> или <see cref="GraphiteDarkId"/>
    /// в зависимости от системной настройки.
    /// </summary>
    public const string SystemId = "system";

    /// <summary>Ключ локализации названия карточки «Как в Windows».</summary>
    public const string SystemNameKey = "theme.system";

    /// <summary>Ключ локализации описания карточки «Как в Windows».</summary>
    public const string SystemDescriptionKey = "theme.system.desc";

    private static readonly ThemeDefinition FluentLight = new(
        Id: FluentLightId,
        NameKey: "theme.fluent_light",
        DescriptionKey: "theme.fluent_light.desc",
        IsDark: false,
        DefaultAccent: Hex("0F6CBD"),
        TabStyle: TabStyle.Pill,
        CornerRadius: 8,
        Palette: new ThemePalette(
            FormBack: Hex("EEF1F6"),
            ChromeBack: Hex("EEF1F6"),
            SidebarBack: Hex("EEF1F6"),
            PanelBack: Hex("EEF1F6"),
            InputBack: Hex("FFFFFF"),
            InputBorder: Hex("D3D8E0"),
            Fore: Hex("24272E"),
            Muted: Hex("5F6676"),
            Border: Hex("DFE3EA"),
            RowSelectionBack: Hex("FFFFFF"),
            RowSelectionFore: Hex("10131A"),
            MenuHoverBack: Hex("E1E5EC"),
            TabBack: Hex("EEF1F6"),
            TabActiveBack: Hex("FFFFFF"),
            TabActiveFore: Hex("10131A"),
            TabInactiveFore: Hex("464D5C"),
            Accent: Hex("0F6CBD"),
            StatusBack: Hex("EEF1F6"),
            StatusFore: Hex("4F5666"),
            FolderIcon: Hex("C98F0A"),
            RdpIcon: Hex("0F6CBD"),
            WebIcon: Hex("1F8A74"),

            // SSH, VNC и Admin Console в эскизах не нарисованы — оттенки подобраны
            // в тон остальным иконкам темы и проверены спайком (RESULT.md).
            SshIcon: Hex("4A5566"),
            VncIcon: Hex("B4610E"),
            AdminIcon: Hex("6A5BA8"),
            SessionLive: Hex("1E9E5A"),

            // Терминал светлой темы остаётся тёмным: это те же цвета, что стояли
            // константами в index.html до v1.11.0, поэтому вид SSH-вкладки у
            // пользователя Fluent Light не изменился.
            TerminalBack: Hex("1E1E1E"),
            TerminalFore: Hex("D4D4D4"),
            TerminalCursor: Hex("F0F0F0"),
            TerminalSelection: Hex("264F78")));

    private static readonly ThemeDefinition GraphiteDark = new(
        Id: GraphiteDarkId,
        NameKey: "theme.graphite_dark",
        DescriptionKey: "theme.graphite_dark.desc",
        IsDark: true,
        DefaultAccent: Hex("5B9DFF"),
        TabStyle: TabStyle.TopAccentBar,
        CornerRadius: 6,
        Palette: new ThemePalette(
            FormBack: Hex("14161B"),
            ChromeBack: Hex("1A1D23"),
            SidebarBack: Hex("1A1D23"),
            PanelBack: Hex("14161B"),
            InputBack: Hex("12141A"),
            InputBorder: Hex("2D323E"),
            Fore: Hex("C9D1DE"),
            Muted: Hex("8791A3"),
            Border: Hex("272B34"),
            RowSelectionBack: Hex("252B38"),
            RowSelectionFore: Hex("FFFFFF"),
            MenuHoverBack: Hex("272C37"),
            TabBack: Hex("1A1D23"),
            TabActiveBack: Hex("14161B"),
            TabActiveFore: Hex("F2F5FA"),
            TabInactiveFore: Hex("98A2B5"),
            Accent: Hex("5B9DFF"),
            StatusBack: Hex("1A1D23"),
            StatusFore: Hex("98A2B5"),
            FolderIcon: Hex("E5B454"),
            RdpIcon: Hex("6FAEFF"),
            WebIcon: Hex("4CC9AE"),
            SshIcon: Hex("A7B3C6"),
            VncIcon: Hex("F0A35E"),
            AdminIcon: Hex("B9A7F5"),
            SessionLive: Hex("3DD68C"),
            TerminalBack: Hex("12141A"),
            TerminalFore: Hex("C9D1DE"),
            TerminalCursor: Hex("5B9DFF"),
            TerminalSelection: Hex("2A3A55")));

    /// <summary>Темы в том порядке, в котором они показываются в окне «Темы».</summary>
    public static IReadOnlyList<ThemeDefinition> All { get; } =
        [FluentLight, GraphiteDark];

    /// <summary>
    /// Шесть акцентных вариантов окна «Темы». Для светлой темы каждый из них
    /// затемняется до контраста ≥ 3:1 с фоном — см. <see cref="ThemeService.ResolveAccent"/>.
    /// </summary>
    public static IReadOnlyList<AccentOption> AccentOptions { get; } =
    [
        new("blue", "theme.accent.blue", Hex("5B9DFF")),
        new("teal", "theme.accent.teal", Hex("2DD4BF")),
        new("violet", "theme.accent.violet", Hex("A78BFA")),
        new("orange", "theme.accent.orange", Hex("F59E5B")),
        new("pink", "theme.accent.pink", Hex("F472B6")),
        new("green", "theme.accent.green", Hex("84CC16")),
    ];

    /// <summary>
    /// Тема по идентификатору. Неизвестное значение (включая
    /// <see cref="SystemId"/> и <c>null</c>) даёт <see cref="FluentLightId"/>:
    /// файл настроек мог прийти из более новой версии Connexa.
    /// </summary>
    public static ThemeDefinition Get(string? id) =>
        All.FirstOrDefault(theme => string.Equals(theme.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? FluentLight;

    /// <summary>Тема, соответствующая системной настройке «светлая/тёмная».</summary>
    public static ThemeDefinition ForSystem(bool light) => light ? FluentLight : GraphiteDark;

    /// <summary>Идентификатор обозначает карточку «Как в Windows».</summary>
    public static bool IsSystem(string? id) =>
        string.Equals(id?.Trim(), SystemId, StringComparison.OrdinalIgnoreCase);

    private static Color Hex(string value) => ColorTranslator.FromHtml("#" + value);
}

using System.Drawing;
using Skimmer.Ui;
using Xunit;

namespace Skimmer.Tests;

/// <summary>
/// Каталог тем: полнота палитр, уникальность идентификаторов и контраст по WCAG.
/// Цвета заданы в DevPlan таблицей, поэтому проверяется не «похоже на эскиз», а
/// читаемость каждой пары текст/фон. Проверки идут по <see cref="ThemeCatalog.All"/>,
/// поэтому новая тема (v1.11.0 — Terminal Ops и Aurora Glass) попадает под них сама.
/// </summary>
public class ThemeCatalogTests
{
    /// <summary>Минимальный контраст для обычного текста (WCAG AA).</summary>
    private const double TextContrast = 4.5;

    /// <summary>Минимальный контраст для акцента и крупных элементов (WCAG AA).</summary>
    private const double AccentContrast = 3.0;

    [Fact]
    public void All_ContainsEveryThemeInCardOrderWithUniqueIds()
    {
        IReadOnlyList<ThemeDefinition> themes = ThemeCatalog.All;

        // Порядок карточек окна «Темы» задаётся каталогом, а не окном (v1.11.0).
        // Skimmer keeps only the two brand-book themes (BRANDBOOK.md, "Темы").
        Assert.Equal(
            [
                ThemeCatalog.FluentLightId,
                ThemeCatalog.GraphiteDarkId,
            ],
            themes.Select(theme => theme.Id));
        Assert.Equal(themes.Count, themes.Select(theme => theme.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.False(themes[0].IsDark);
        Assert.True(themes.Skip(1).All(theme => theme.IsDark));
    }

    [Theory]
    [InlineData(ThemeCatalog.FluentLightId)]
    [InlineData(ThemeCatalog.GraphiteDarkId)]
    public void Get_ReturnsRequestedTheme(string id)
    {
        Assert.Equal(id, ThemeCatalog.Get(id).Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("neon-sunset")]
    [InlineData(ThemeCatalog.SystemId)]
    public void Get_FallsBackToFluentLightForUnknownId(string? id)
    {
        Assert.Equal(ThemeCatalog.FluentLightId, ThemeCatalog.Get(id).Id);
    }

    /// <summary>
    /// «Как в Windows» остаётся выбором между Fluent Light и Graphite Dark: новые
    /// темы системной настройке не соответствуют и выбираются только вручную.
    /// </summary>
    [Fact]
    public void ForSystem_StaysOnTheTwoThemesThatMatchTheWindowsSetting()
    {
        Assert.Equal(ThemeCatalog.FluentLightId, ThemeCatalog.ForSystem(light: true).Id);
        Assert.Equal(ThemeCatalog.GraphiteDarkId, ThemeCatalog.ForSystem(light: false).Id);
    }

    /// <summary>
    /// Темы v1.10.0 не изменились: ни шрифта хрома, ни градиентов, ни свечения у
    /// индикатора — иначе обновление переоформило бы уже принятые темы.
    /// </summary>
    [Theory]
    [InlineData(ThemeCatalog.FluentLightId, TabStyle.Pill, 8)]
    [InlineData(ThemeCatalog.GraphiteDarkId, TabStyle.TopAccentBar, 6)]
    public void ThemesFromV1100_KeepTheirAppearance(string id, TabStyle style, int cornerRadius)
    {
        ThemeDefinition theme = ThemeCatalog.Get(id);

        Assert.Equal(style, theme.TabStyle);
        Assert.Equal(cornerRadius, theme.CornerRadius);
        Assert.Null(theme.ChromeFontFamily);
        Assert.Null(theme.Gradients);
        Assert.Equal(SessionIndicatorStyle.Dot, theme.SessionIndicator);
    }

    [Fact]
    public void EveryTheme_HasCompletePaletteAndMetadata()
    {
        foreach (ThemeDefinition theme in ThemeCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(theme.NameKey), theme.Id);
            Assert.False(string.IsNullOrWhiteSpace(theme.DescriptionKey), theme.Id);

            // Верхняя граница — радиус «таблеток» Aurora Glass (12).
            Assert.InRange(theme.CornerRadius, 0, 12);
            Assert.Equal(theme.Palette.Accent, theme.DefaultAccent);

            foreach ((string name, Color color) in Colors(theme.Palette))
            {
                // Пропущенное поле палитры в WinForms равно Color.Empty — именно это
                // и ловит проверка: «цвет задан» значит «он не пустой и не прозрачный».
                Assert.False(color.IsEmpty, $"{theme.Id}.{name} не задан");
                Assert.Equal(255, color.A);
            }
        }
    }

    [Fact]
    public void EveryTheme_KeepsTextReadableOnItsBackground()
    {
        foreach (ThemeDefinition theme in ThemeCatalog.All)
        {
            ThemePalette palette = theme.Palette;
            AssertContrast(theme, "Fore/FormBack", palette.Fore, palette.FormBack, TextContrast);
            AssertContrast(theme, "Fore/PanelBack", palette.Fore, palette.PanelBack, TextContrast);
            AssertContrast(theme, "Fore/InputBack", palette.Fore, palette.InputBack, TextContrast);
            AssertContrast(theme, "Fore/SidebarBack", palette.Fore, palette.SidebarBack, TextContrast);
            AssertContrast(theme, "Muted/FormBack", palette.Muted, palette.FormBack, TextContrast);
            AssertContrast(theme, "Muted/SidebarBack", palette.Muted, palette.SidebarBack, TextContrast);
            AssertContrast(theme, "TabInactiveFore/TabBack", palette.TabInactiveFore, palette.TabBack, TextContrast);
            AssertContrast(theme, "TabActiveFore/TabActiveBack", palette.TabActiveFore, palette.TabActiveBack, TextContrast);
            AssertContrast(theme, "RowSelectionFore/RowSelectionBack", palette.RowSelectionFore, palette.RowSelectionBack, TextContrast);
            AssertContrast(theme, "StatusFore/StatusBack", palette.StatusFore, palette.StatusBack, TextContrast);
        }
    }

    /// <summary>
    /// Терминал SSH берёт цвета из темы (v1.11.0), поэтому его читаемость — такое же
    /// условие, как и у остального текста.
    /// </summary>
    [Fact]
    public void EveryTheme_KeepsTheTerminalReadable()
    {
        foreach (ThemeDefinition theme in ThemeCatalog.All)
        {
            ThemePalette palette = theme.Palette;
            AssertContrast(theme, "TerminalFore/TerminalBack", palette.TerminalFore, palette.TerminalBack, TextContrast);
            AssertContrast(theme, "TerminalCursor/TerminalBack", palette.TerminalCursor, palette.TerminalBack, AccentContrast);
            AssertContrast(theme, "TerminalFore/TerminalSelection", palette.TerminalFore, palette.TerminalSelection, AccentContrast);
        }
    }

    [Fact]
    public void DefaultAccent_StandsOutAgainstTheWindowBackground()
    {
        foreach (ThemeDefinition theme in ThemeCatalog.All)
        {
            AssertContrast(theme, "DefaultAccent/FormBack", theme.DefaultAccent, theme.Palette.FormBack, AccentContrast);
        }
    }

    [Fact]
    public void EveryAccentOption_IsAdjustedToStandOutInBothThemes()
    {
        Assert.Equal(6, ThemeCatalog.AccentOptions.Count);
        Assert.Equal(
            ThemeCatalog.AccentOptions.Count,
            ThemeCatalog.AccentOptions.Select(option => option.Id).Distinct(StringComparer.Ordinal).Count());

        foreach (ThemeDefinition theme in ThemeCatalog.All)
        {
            foreach (AccentOption option in ThemeCatalog.AccentOptions)
            {
                Color resolved = ThemeService.ResolveAccent(theme, ThemeService.ToHex(option.Color));
                AssertContrast(theme, $"accent {option.Id}", resolved, theme.Palette.FormBack, AccentContrast);
                Assert.False(string.IsNullOrWhiteSpace(option.NameKey), option.Id);
            }
        }
    }

    [Fact]
    public void SharedStatusColours_StayReadableInBothThemes()
    {
        // Success/Error/Warning не входят в палитру темы (цвет состояния не должен
        // меняться вместе с оформлением), поэтому они проверяются отдельно.
        foreach (ThemeDefinition theme in ThemeCatalog.All)
        {
            AssertContrast(theme, "Success", UiTheme.Success, theme.Palette.PanelBack, AccentContrast);
            AssertContrast(theme, "Error", UiTheme.Error, theme.Palette.PanelBack, AccentContrast);
            AssertContrast(theme, "Warning", UiTheme.Warning, theme.Palette.PanelBack, AccentContrast);
        }
    }

    [Fact]
    public void ContrastRatio_MatchesTheWcagReferenceValues()
    {
        Assert.Equal(21.0, ThemeContrast.ContrastRatio(Color.Black, Color.White), 2);
        Assert.Equal(1.0, ThemeContrast.ContrastRatio(Color.White, Color.White), 2);
    }

    [Fact]
    public void EnsureContrast_DarkensOnlyWhenTheColourIsTooPale()
    {
        var background = Color.FromArgb(0xEE, 0xF1, 0xF6);
        var pale = Color.FromArgb(0x84, 0xCC, 0x16);

        Assert.True(ThemeContrast.ContrastRatio(pale, background) < AccentContrast);
        Color adjusted = ThemeContrast.EnsureContrast(pale, background, AccentContrast);
        Assert.True(ThemeContrast.ContrastRatio(adjusted, background) >= AccentContrast);

        // Цвет, который и так контрастен, не меняется вовсе.
        var dark = Color.FromArgb(0x0F, 0x6C, 0xBD);
        Assert.Equal(dark, ThemeContrast.EnsureContrast(dark, background, AccentContrast));
    }

    [Theory]
    [InlineData(ThemeDensity.Compact, 24)]
    [InlineData(ThemeDensity.Normal, 28)]
    [InlineData(ThemeDensity.Spacious, 32)]
    public void Density_MapsToTheDocumentedRowHeight(ThemeDensity density, int expected)
    {
        Assert.Equal(expected, density.RowHeight());
    }

    private static void AssertContrast(
        ThemeDefinition theme,
        string what,
        Color foreground,
        Color background,
        double minimum)
    {
        double ratio = ThemeContrast.ContrastRatio(foreground, background);
        Assert.True(
            ratio >= minimum,
            $"{theme.Id}: {what} — контраст {ratio:0.00}:1, нужно не меньше {minimum:0.0}:1");
    }

    private static IEnumerable<(string Name, Color Color)> Colors(ThemePalette palette) =>
        typeof(ThemePalette)
            .GetProperties()
            .Where(property => property.PropertyType == typeof(Color))
            .Select(property => (property.Name, (Color)property.GetValue(palette)!));
}

using System.Drawing;
using System.Reflection;
using System.Text.Json;
using Skimmer.Ui;
using Xunit;

namespace Skimmer.Tests;

/// <summary>Skimmer-specific checks on top of the UiKit palette tests.</summary>
public class SkimmerThemeTests
{
    const double TextContrast = 4.5;

    /// <summary>Brand book: text on an accent fill (banner, primary button) must stay readable.</summary>
    [Fact]
    public void TextOnDefaultAccent_IsReadableInBothThemes()
    {
        foreach (ThemeDefinition theme in ThemeCatalog.All)
        {
            Color accent = theme.DefaultAccent;
            Color text = UiStyle.OnAccent(accent);
            Assert.True(ThemeContrast.ContrastRatio(text, accent) >= TextContrast,
                $"{theme.Id}: text on accent {ThemeContrast.ContrastRatio(text, accent):0.00}:1");
        }
    }

    /// <summary>Selected thumbnail tile = 22 % accent over the card colour: file names must stay readable with every accent.</summary>
    [Fact]
    public void SelectedTileName_IsReadableWithEveryAccent()
    {
        foreach (ThemeDefinition theme in ThemeCatalog.All)
        {
            var accents = ThemeCatalog.AccentOptions
                .Select(o => ThemeService.ResolveAccent(theme, ThemeService.ToHex(o.Color)))
                .Append(theme.DefaultAccent);
            foreach (Color accent in accents)
            {
                Color fill = ThemeDraw.Blend(theme.Palette.InputBack, accent, 0.22f);
                double ratio = ThemeContrast.ContrastRatio(theme.Palette.Fore, fill);
                Assert.True(ratio >= TextContrast, $"{theme.Id}: name on selection {ratio:0.00}:1");
            }
        }
    }

    /// <summary>ru.json and en.json carry the same keys, and every value is filled in.</summary>
    [Fact]
    public void LanguageFiles_HaveTheSameKeys()
    {
        var ru = Load("ru");
        var en = Load("en");
        Assert.NotEmpty(ru);
        Assert.Empty(ru.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(ru.Keys));
        Assert.DoesNotContain(ru, kv => string.IsNullOrWhiteSpace(kv.Value));
        Assert.DoesNotContain(en, kv => string.IsNullOrWhiteSpace(kv.Value));
    }

    static Dictionary<string, string> Load(string code)
    {
        Assembly app = typeof(UiStyle).Assembly;
        using Stream s = app.GetManifestResourceStream($"Skimmer.Languages.{code}.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(s)!;
    }
}

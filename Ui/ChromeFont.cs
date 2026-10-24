#nullable enable
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Text;

namespace Skimmer.Ui;

/// <summary>
/// Шрифт «хрома» темы (v1.11.0): дерево подключений, полоса вкладок, меню и
/// строка состояния. Тема просит семейство списком в порядке предпочтения
/// (<see cref="ThemeDefinition.ChromeFontFamily"/>), здесь берётся первое
/// установленное; если не установлено ни одного — Segoe UI.
/// </summary>
/// <remarks>
/// Список установленных семейств читается из системы один раз: перебор
/// <see cref="InstalledFontCollection"/> на каждую строку дерева стоил бы дороже
/// самой отрисовки. Готовые <see cref="Font"/> тоже кэшируются и живут до конца
/// процесса — их отдают наружу как общий ресурс, освобождать их вызывающему нельзя.
/// </remarks>
public static class ChromeFont
{
    private static readonly ConcurrentDictionary<string, Font> Fonts = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<HashSet<string>> InstalledFamilies = new(ReadInstalledFamilies);

    /// <summary>Шрифт хрома для темы.</summary>
    public static Font For(ThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        string family = Resolve(theme.ChromeFontFamily, IsInstalled);
        return Fonts.GetOrAdd(family, name => new Font(name, UiTheme.FontSize, FontStyle.Regular));
    }

    /// <summary>
    /// Шрифт средней насыщенности того же семейства — им подписывается активная
    /// вкладка. У моноширинных семейств полужирный вариант есть не всегда, поэтому
    /// при его отсутствии GDI+ отдаёт обычный: это нормально и ничего не ломает.
    /// </summary>
    public static Font MediumFor(ThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (theme.ChromeFontFamily is null)
        {
            return UiTheme.Medium;
        }

        string family = Resolve(theme.ChromeFontFamily, IsInstalled);
        return Fonts.GetOrAdd(
            family + " #bold",
            _ => new Font(family, UiTheme.FontSize, FontStyle.Bold));
    }

    /// <summary>
    /// Выбирает семейство из списка через запятую: первое установленное или
    /// <see cref="UiTheme.FontFamily"/>, если подходящего нет.
    /// </summary>
    /// <param name="families">Список семейств в порядке предпочтения или <c>null</c>.</param>
    /// <param name="isInstalled">Проверка наличия семейства в системе.</param>
    public static string Resolve(string? families, Func<string, bool> isInstalled)
    {
        ArgumentNullException.ThrowIfNull(isInstalled);
        foreach (string candidate in (families ?? string.Empty).Split(','))
        {
            string family = candidate.Trim();
            if (family.Length > 0 && isInstalled(family))
            {
                return family;
            }
        }

        return UiTheme.FontFamily;
    }

    /// <summary>Семейство установлено в системе.</summary>
    public static bool IsInstalled(string family)
    {
        ArgumentNullException.ThrowIfNull(family);
        return InstalledFamilies.Value.Contains(family);
    }

    /// <summary>
    /// Забывает уже созданные шрифты. Нужен тестам; в приложении шрифты хрома живут
    /// столько же, сколько процесс. Старые экземпляры намеренно не освобождаются:
    /// на них могли остаться ссылки в контролах, и <c>Dispose</c> уронил бы их
    /// отрисовку — освобождение оставлено сборщику мусора.
    /// </summary>
    public static void Reset() => Fonts.Clear();

    private static HashSet<string> ReadInstalledFamilies()
    {
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var installed = new InstalledFontCollection();
            foreach (FontFamily family in installed.Families)
            {
                families.Add(family.Name);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // Перечисление шрифтов недоступно (сервер без шрифтового кэша):
            // хром остаётся на Segoe UI, а не падает вместе с окном.
        }

        return families;
    }
}

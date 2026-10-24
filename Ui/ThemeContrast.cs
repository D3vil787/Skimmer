#nullable enable
using System.Drawing;

namespace Skimmer.Ui;

/// <summary>
/// Контраст по WCAG 2.1. Нужен в двух местах: тесты проверяют читаемость палитр
/// каталога, а выбранный пользователем акцент подгоняется под фон светлой темы —
/// яркий салатовый на почти белом фоне иначе не виден.
/// </summary>
public static class ThemeContrast
{
    /// <summary>Шаг затемнения при подгонке под контраст.</summary>
    private const double DarkenStep = 0.96;

    /// <summary>Сколько шагов допускается, прежде чем цвет считается безнадёжным.</summary>
    private const int MaximumSteps = 200;

    /// <summary>Относительная яркость цвета по WCAG (0 — чёрный, 1 — белый).</summary>
    public static double RelativeLuminance(Color color) =>
        (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));

    /// <summary>Коэффициент контраста двух цветов: от 1:1 до 21:1.</summary>
    public static double ContrastRatio(Color first, Color second)
    {
        double a = RelativeLuminance(first);
        double b = RelativeLuminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>
    /// Возвращает цвет, контрастный фону не меньше чем <paramref name="minimumRatio"/>.
    /// Уже контрастный цвет возвращается без изменений; слишком светлый затемняется,
    /// слишком тёмный — осветляется.
    /// </summary>
    public static Color EnsureContrast(Color color, Color background, double minimumRatio)
    {
        if (ContrastRatio(color, background) >= minimumRatio)
        {
            return color;
        }

        // На светлом фоне цвет можно только затемнять, на тёмном — только осветлять:
        // движение в другую сторону приближает его к фону.
        bool darken = RelativeLuminance(background) > RelativeLuminance(color)
                      || RelativeLuminance(background) > 0.5;

        Color candidate = color;
        for (int step = 0; step < MaximumSteps; step++)
        {
            candidate = darken ? Darken(candidate) : Lighten(candidate);
            if (ContrastRatio(candidate, background) >= minimumRatio)
            {
                return candidate;
            }
        }

        // Крайний случай: берём заведомо контрастный чёрный или белый.
        return darken ? Color.Black : Color.White;
    }

    private static Color Darken(Color color) => Color.FromArgb(
        color.A,
        Scale(color.R, DarkenStep),
        Scale(color.G, DarkenStep),
        Scale(color.B, DarkenStep));

    private static Color Lighten(Color color) => Color.FromArgb(
        color.A,
        Blend(color.R),
        Blend(color.G),
        Blend(color.B));

    /// <summary>Затемнение: умножение канала, но не меньше чем на единицу за шаг.</summary>
    private static int Scale(int channel, double factor) =>
        Math.Clamp(channel == 0 ? 0 : Math.Min((int)(channel * factor), channel - 1), 0, 255);

    /// <summary>Осветление: подмешивание белого тем же шагом.</summary>
    private static int Blend(int channel) =>
        Math.Clamp(Math.Max((int)(channel + ((255 - channel) * (1 - DarkenStep))), channel + 1), 0, 255);

    private static double Linear(int channel)
    {
        double value = channel / 255.0;
        return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}

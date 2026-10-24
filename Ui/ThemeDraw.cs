#nullable enable
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Skimmer.Ui;

/// <summary>
/// Примитивы отрисовки темы: скруглённые прямоугольники, шевроны и смешивание
/// цветов. Один набор на дерево подключений, вкладки и меню — иначе радиус
/// скругления в трёх местах расходится.
/// </summary>
public static class ThemeDraw
{
    /// <summary>
    /// Путь скруглённого прямоугольника. При нулевом радиусе — обычный прямоугольник,
    /// радиус больше половины меньшей стороны обрезается.
    /// </summary>
    public static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return path;
        }

        int limit = Math.Min(bounds.Width, bounds.Height) / 2;
        int corner = Math.Clamp(radius, 0, Math.Max(0, limit));
        if (corner == 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        int diameter = corner * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Заливает скруглённый прямоугольник.</summary>
    public static void FillRounded(Graphics graphics, Rectangle bounds, int radius, Color color)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        using GraphicsPath path = RoundedPath(bounds, radius);
        using var brush = new SolidBrush(color);
        SmoothingMode previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.FillPath(brush, path);
        graphics.SmoothingMode = previous;
    }

    /// <summary>Обводит скруглённый прямоугольник.</summary>
    public static void DrawRounded(
        Graphics graphics,
        Rectangle bounds,
        int radius,
        Color color,
        float width = 1f)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        using GraphicsPath path = RoundedPath(bounds, radius);
        using var pen = new Pen(color, width);
        SmoothingMode previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.DrawPath(pen, path);
        graphics.SmoothingMode = previous;
    }

    /// <summary>
    /// Шеврон вместо нативного +/−: «&gt;» для свёрнутого узла, «v» для развёрнутого.
    /// Центрируется в <paramref name="bounds"/>.
    /// </summary>
    public static void DrawChevron(
        Graphics graphics,
        Rectangle bounds,
        bool expanded,
        Color color,
        float thickness)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        float size = Math.Min(bounds.Width, bounds.Height) * 0.34f;
        float centerX = bounds.Left + (bounds.Width / 2f);
        float centerY = bounds.Top + (bounds.Height / 2f);

        SmoothingMode previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, thickness)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };

        PointF[] points = expanded
            ?
            [
                new PointF(centerX - size, centerY - (size * 0.55f)),
                new PointF(centerX, centerY + (size * 0.55f)),
                new PointF(centerX + size, centerY - (size * 0.55f)),
            ]
            :
            [
                new PointF(centerX - (size * 0.55f), centerY - size),
                new PointF(centerX + (size * 0.55f), centerY),
                new PointF(centerX - (size * 0.55f), centerY + size),
            ];
        graphics.DrawLines(pen, points);

        graphics.SmoothingMode = previous;
    }

    /// <summary>
    /// Смешивает два цвета: <paramref name="amount"/> равное 0 даёт
    /// <paramref name="from"/>, равное 1 — <paramref name="to"/>.
    /// </summary>
    public static Color Blend(Color from, Color to, float amount)
    {
        float share = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)(from.R + ((to.R - from.R) * share)),
            (int)(from.G + ((to.G - from.G) * share)),
            (int)(from.B + ((to.B - from.B) * share)));
    }

    /// <summary>Цвет подсветки строки или вкладки под курсором.</summary>
    public static Color Hover(Color background, Color selection) => Blend(background, selection, 0.6f);

    /// <summary>
    /// Заливка невыбранной вкладки под курсором: полпути от полосы к активной
    /// вкладке. Подсветка обязана оставаться слабее самой «таблетки», иначе вкладка
    /// под курсором читается как выбранная.
    /// </summary>
    public static Color TabHover(Color strip, Color active) => Blend(strip, active, 0.5f);

    /// <summary>
    /// Очень слабая светлая рамка «таблетки» — белый 18 % поверх её заливки. Нужна
    /// тёмной теме, где «таблетка» и так отличается от полосы заливкой: яркая
    /// обводка превратила бы ряд вкладок в ряд коробок (v1.11.0, Aurora Glass).
    /// </summary>
    public static Color PillBorder(Color fill) => Blend(fill, Color.White, 0.18f);

    /// <summary>Заливка папки-цели при drag &amp; drop.</summary>
    public static Color DropTarget(Color background, Color accent) => Blend(background, accent, 0.22f);
}

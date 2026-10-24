#nullable enable
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Skimmer.Ui;

/// <summary>
/// Градиенты и свечение тем v1.11.0 (Aurora Glass, индикатор сессии Terminal Ops).
/// В WinForms нет ни размытия, ни прозрачных панелей, поэтому «стекло» — это
/// стилизация: непрозрачные градиенты и нарисованное кодом свечение.
/// </summary>
/// <remarks>
/// Кисти и битмапы свечения кэшируются по размеру и цветам: полоса меню, подсветка
/// строки дерева и индикатор перерисовываются на каждое движение мыши и на каждый
/// кадр перетаскивания вкладки, и создавать там <see cref="LinearGradientBrush"/>
/// заново — это и есть заметная нагрузка. Кэш ограничен <see cref="CacheLimit"/>
/// записями и сбрасывается при смене темы (<see cref="ThemeService"/>), поэтому
/// меняющиеся размеры окна его не раздувают.
/// </remarks>
public static class GradientPainter
{
    /// <summary>Сколько разных заливок держится в кэше, прежде чем он сбрасывается.</summary>
    private const int CacheLimit = 64;

    private static readonly ConcurrentDictionary<GradientKey, LinearGradientBrush> Brushes = new();
    private static readonly ConcurrentDictionary<GlowKey, Bitmap> Glows = new();

    /// <summary>Заливает прямоугольник вертикальным градиентом сверху вниз.</summary>
    public static void FillVertical(Graphics graphics, Rectangle bounds, Color from, Color to) =>
        Fill(graphics, bounds, from, to, LinearGradientMode.Vertical, radius: null);

    /// <summary>Заливает прямоугольник горизонтальным градиентом слева направо.</summary>
    public static void FillHorizontal(Graphics graphics, Rectangle bounds, Color from, Color to) =>
        Fill(graphics, bounds, from, to, LinearGradientMode.Horizontal, radius: null);

    /// <summary>
    /// Заливает скруглённый прямоугольник горизонтальным градиентом. Скругление
    /// рисуется путём, а не <c>Region</c>: вырезанный регион даёт рваные края.
    /// </summary>
    public static void FillRounded(
        Graphics graphics,
        Rectangle bounds,
        int radius,
        Color from,
        Color to) =>
        Fill(graphics, bounds, from, to, LinearGradientMode.Horizontal, radius);

    /// <summary>
    /// Рисует мягкое свечение вокруг индикатора сессии: в центре — его цвет,
    /// к краю прозрачность растёт до нуля.
    /// </summary>
    /// <param name="graphics">Куда рисовать.</param>
    /// <param name="core">Прямоугольник самого индикатора.</param>
    /// <param name="color">Цвет свечения.</param>
    /// <param name="spread">На сколько пикселей свечение выходит за индикатор.</param>
    public static void DrawGlow(Graphics graphics, Rectangle core, Color color, int spread)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (IsDegenerate(core) || spread <= 0)
        {
            return;
        }

        Rectangle halo = Rectangle.Inflate(core, spread, spread);
        Bitmap glow = GlowFor(halo.Size, color);
        graphics.DrawImageUnscaled(glow, halo.Location);
    }

    /// <summary>
    /// Забывает кэшированные заливки. Вызывается при смене темы: держать кисти
    /// прежней палитры незачем.
    /// </summary>
    public static void Reset()
    {
        foreach (GradientKey key in Brushes.Keys)
        {
            if (Brushes.TryRemove(key, out LinearGradientBrush? brush))
            {
                brush.Dispose();
            }
        }

        foreach (GlowKey key in Glows.Keys)
        {
            if (Glows.TryRemove(key, out Bitmap? glow))
            {
                glow.Dispose();
            }
        }
    }

    private static bool IsDegenerate(Rectangle bounds) => bounds.Width <= 0 || bounds.Height <= 0;

    /// <summary>
    /// Заливает прямоугольник кэшированной кистью нужного размера.
    /// </summary>
    /// <remarks>
    /// Кисть кэширована по размеру, а не по положению, поэтому под нужное место
    /// сдвигается система координат <see cref="Graphics"/>. Трансформацию самой
    /// кисти для этого использовать нельзя: у кисти с <see cref="LinearGradientMode"/>
    /// в ней лежит поворот, задающий направление градиента, и
    /// <c>ResetTransform</c> превратил бы вертикальный градиент в горизонтальный.
    /// </remarks>
    private static void Fill(
        Graphics graphics,
        Rectangle bounds,
        Color from,
        Color to,
        LinearGradientMode mode,
        int? radius)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (IsDegenerate(bounds))
        {
            return;
        }

        LinearGradientBrush brush = BrushFor(bounds.Size, from, to, mode);
        var local = new Rectangle(Point.Empty, bounds.Size);

        GraphicsState state = graphics.Save();
        graphics.TranslateTransform(bounds.Left, bounds.Top);
        if (radius is int corner)
        {
            using GraphicsPath path = ThemeDraw.RoundedPath(local, corner);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.FillPath(brush, path);
        }
        else
        {
            graphics.FillRectangle(brush, local);
        }

        graphics.Restore(state);
    }

    private static LinearGradientBrush BrushFor(Size size, Color from, Color to, LinearGradientMode mode)
    {
        var key = new GradientKey(size.Width, size.Height, from.ToArgb(), to.ToArgb(), mode);
        if (Brushes.TryGetValue(key, out LinearGradientBrush? cached))
        {
            return cached;
        }

        DropCacheIfFull();
        return Brushes.GetOrAdd(
            key,
            new LinearGradientBrush(new Rectangle(Point.Empty, size), from, to, mode));
    }

    private static Bitmap GlowFor(Size size, Color color)
    {
        var key = new GlowKey(size.Width, size.Height, color.ToArgb());
        if (Glows.TryGetValue(key, out Bitmap? cached))
        {
            return cached;
        }

        DropCacheIfFull();
        return Glows.GetOrAdd(key, RenderGlow(size, color));
    }

    /// <summary>
    /// Рисует свечение один раз в битмап: <see cref="PathGradientBrush"/> со
    /// сглаживанием стоит дороже готовой картинки, а меняется оно только вместе
    /// с темой или DPI.
    /// </summary>
    private static Bitmap RenderGlow(Size size, Color color)
    {
        var bitmap = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height));
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(Point.Empty, bitmap.Size);
        using GraphicsPath path = new();
        path.AddEllipse(bounds);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(190, color),
            SurroundColors = [Color.FromArgb(0, color)],
            CenterPoint = new PointF(bounds.Width / 2f, bounds.Height / 2f),
        };
        graphics.FillPath(brush, path);
        return bitmap;
    }

    /// <summary>
    /// Сбрасывает кэш целиком, когда он дорос до предела. Выбрасывать записи по
    /// одной незачем: заливки дешевле создать заново, чем вести учёт обращений.
    /// </summary>
    private static void DropCacheIfFull()
    {
        if (Brushes.Count + Glows.Count >= CacheLimit)
        {
            Reset();
        }
    }

    private readonly record struct GradientKey(
        int Width,
        int Height,
        int From,
        int To,
        LinearGradientMode Mode);

    private readonly record struct GlowKey(int Width, int Height, int Color);
}

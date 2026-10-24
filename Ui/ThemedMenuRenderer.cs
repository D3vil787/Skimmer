#nullable enable
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Skimmer.Ui;

/// <summary>
/// Цветовая таблица меню по палитре темы — обобщение прежней <c>DarkColorTable</c>
/// на любую палитру.
/// </summary>
public sealed class ThemedColorTable(ThemePalette palette) : ProfessionalColorTable
{
    /// <summary>Палитра, из которой берутся цвета.</summary>
    public ThemePalette Palette { get; } = palette;

    /// <inheritdoc />
    public override Color MenuStripGradientBegin => Palette.ChromeBack;

    /// <inheritdoc />
    public override Color MenuStripGradientEnd => Palette.ChromeBack;

    /// <inheritdoc />
    public override Color ToolStripDropDownBackground => Palette.ChromeBack;

    /// <inheritdoc />
    public override Color ImageMarginGradientBegin => Palette.ChromeBack;

    /// <inheritdoc />
    public override Color ImageMarginGradientMiddle => Palette.ChromeBack;

    /// <inheritdoc />
    public override Color ImageMarginGradientEnd => Palette.ChromeBack;

    /// <inheritdoc />
    public override Color MenuItemSelected => Palette.MenuHoverBack;

    /// <inheritdoc />
    public override Color MenuItemSelectedGradientBegin => Palette.MenuHoverBack;

    /// <inheritdoc />
    public override Color MenuItemSelectedGradientEnd => Palette.MenuHoverBack;

    /// <inheritdoc />
    public override Color MenuItemPressedGradientBegin => Palette.MenuHoverBack;

    /// <inheritdoc />
    public override Color MenuItemPressedGradientMiddle => Palette.MenuHoverBack;

    /// <inheritdoc />
    public override Color MenuItemPressedGradientEnd => Palette.MenuHoverBack;

    /// <inheritdoc />
    public override Color MenuBorder => Palette.Border;

    /// <inheritdoc />
    public override Color MenuItemBorder => Palette.MenuHoverBack;

    /// <inheritdoc />
    public override Color SeparatorDark => Palette.Border;

    /// <inheritdoc />
    public override Color SeparatorLight => Palette.ChromeBack;

    /// <inheritdoc />
    public override Color CheckBackground => Palette.Accent;

    /// <inheritdoc />
    public override Color CheckSelectedBackground => Palette.Accent;

    /// <inheritdoc />
    public override Color CheckPressedBackground => Palette.Accent;
}

/// <summary>
/// Рендерер меню темы: скруглённая подсветка пункта, цвета из палитры, свои
/// разделители, галочки и стрелки подменю. Используется и светлой темой — иначе
/// полоса меню осталась бы системно-серой рядом с палитрой Fluent Light.
/// </summary>
public sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
{
    private readonly ThemePalette _palette;
    private readonly ThemeGradients? _gradients;
    private readonly int _cornerRadius;
    private readonly bool _isDark;

    /// <summary>Создаёт рендерер для темы.</summary>
    public ThemedMenuRenderer(ThemeDefinition theme, ThemePalette palette)
        : base(new ThemedColorTable(palette))
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        _gradients = theme.Gradients;
        _cornerRadius = theme.CornerRadius;
        _isDark = theme.IsDark;
    }

    /// <inheritdoc />
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        // Полоса целиком, а не e.AffectedBounds: там лежит только потребовавшая
        // перерисовки часть, и градиент, построенный по ней, разошёлся бы с уже
        // нарисованным соседним куском. Лишнее отсечёт область отсечения Graphics.
        FillChrome(
            e.Graphics,
            new Rectangle(0, 0, e.ToolStrip.Width, e.ToolStrip.Height),
            e.ToolStrip is ToolStripDropDown);
    }

    /// <summary>
    /// Заливает фон «хрома»: плоский <see cref="ThemePalette.ChromeBack"/> или
    /// горизонтальный градиент до <see cref="ThemeGradients.ChromeEnd"/> у темы с
    /// градиентами (Aurora Glass).
    /// </summary>
    /// <param name="graphics">Куда рисовать.</param>
    /// <param name="bounds">Прямоугольник полосы меню целиком — по нему строится градиент.</param>
    /// <param name="flat">
    /// Залить одним цветом. Так рисуется выпадающее меню: градиент в узком
    /// вертикальном списке читается как грязь, а не как глубина.
    /// </param>
    private void FillChrome(Graphics graphics, Rectangle bounds, bool flat)
    {
        if (_gradients is null || flat)
        {
            using var brush = new SolidBrush(_palette.ChromeBack);
            graphics.FillRectangle(brush, bounds);
            return;
        }

        GradientPainter.FillHorizontal(graphics, bounds, _palette.ChromeBack, _gradients.ChromeEnd);
    }

    /// <inheritdoc />
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.ToolStrip is ToolStripDropDown)
        {
            var bounds = new Rectangle(
                0,
                0,
                Math.Max(0, e.ToolStrip.Width - 1),
                Math.Max(0, e.ToolStrip.Height - 1));
            ThemeDraw.DrawRounded(e.Graphics, bounds, _cornerRadius, _palette.Border);
            return;
        }

        // Полоса меню: только нижняя граница, в тон палитре.
        using var pen = new Pen(_palette.Border);
        e.Graphics.DrawLine(pen, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
    }

    /// <inheritdoc />
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Item is not ToolStripMenuItem item)
        {
            base.OnRenderMenuItemBackground(e);
            return;
        }

        var bounds = new Rectangle(Point.Empty, item.Size);

        // Пункт рисуется в своих координатах, а градиент полосы меню идёт по всей её
        // ширине: заливается прямоугольник полосы, а видна его часть под пунктом.
        FillChrome(
            e.Graphics,
            new Rectangle(
                -item.Bounds.Left,
                -item.Bounds.Top,
                item.Owner?.Width ?? bounds.Width,
                item.Owner?.Height ?? bounds.Height),
            item.IsOnDropDown);

        if (!item.Enabled || (!item.Selected && !item.Pressed))
        {
            return;
        }

        bool topLevel = !item.IsOnDropDown;
        Rectangle highlight = topLevel
            ? Rectangle.Inflate(bounds, -2, -3)
            : new Rectangle(
                bounds.Left + 2,
                bounds.Top + 1,
                Math.Max(1, bounds.Width - 5),
                Math.Max(1, bounds.Height - 2));

        Color fill = item.Pressed && topLevel
            ? ThemeDraw.Blend(_palette.MenuHoverBack, _palette.Accent, 0.25f)
            : _palette.MenuHoverBack;
        ThemeDraw.FillRounded(e.Graphics, highlight, _cornerRadius, fill);
    }

    /// <inheritdoc />
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        e.TextColor = e.Item?.Enabled == false ? _palette.Muted : _palette.Fore;
        base.OnRenderItemText(e);
    }

    /// <inheritdoc />
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        using (var background = new SolidBrush(_palette.ChromeBack))
        {
            e.Graphics.FillRectangle(background, new Rectangle(Point.Empty, e.Item.Size));
        }

        using var pen = new Pen(_palette.Border);
        int y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
    }

    /// <inheritdoc />
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        using var brush = new SolidBrush(_palette.ChromeBack);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    /// <inheritdoc />
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        Rectangle bounds = e.ImageRectangle;
        if (bounds.Width <= 2 || bounds.Height <= 2)
        {
            return;
        }

        Rectangle box = Rectangle.Inflate(bounds, -1, -1);
        ThemeDraw.FillRounded(e.Graphics, box, Math.Max(2, _cornerRadius / 2), _palette.Accent);

        using var pen = new Pen(_isDark ? _palette.FormBack : Color.White, 2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        SmoothingMode previous = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawLines(pen,
        [
            new PointF(box.Left + (box.Width * 0.24f), box.Top + (box.Height * 0.52f)),
            new PointF(box.Left + (box.Width * 0.44f), box.Top + (box.Height * 0.72f)),
            new PointF(box.Left + (box.Width * 0.76f), box.Top + (box.Height * 0.30f)),
        ]);
        e.Graphics.SmoothingMode = previous;
    }

    /// <inheritdoc />
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Direction == ArrowDirection.Right)
        {
            ThemeDraw.DrawChevron(e.Graphics, e.ArrowRectangle, expanded: false, _palette.Muted, 1.6f);
            return;
        }

        e.ArrowColor = _palette.Fore;
        base.OnRenderArrow(e);
    }
}

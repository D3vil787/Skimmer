using System.Drawing;
using System.Windows.Forms;

namespace Skimmer;

static class Theme
{
    public static readonly Color Back = Color.FromArgb(30, 30, 30);
    public static readonly Color Bar = Color.FromArgb(40, 40, 40);
    public static readonly Color GridBack = Color.FromArgb(34, 34, 34);
    public static readonly Color Tile = Color.FromArgb(48, 48, 48);
    public static readonly Color TileSelected = Color.FromArgb(0, 90, 160);
    public static readonly Color Accent = Color.FromArgb(0, 120, 215);
    public static readonly Color Text = Color.FromArgb(225, 225, 225);
    public static readonly Color TextDim = Color.FromArgb(150, 150, 150);
    public static readonly Color Border = Color.FromArgb(70, 70, 70);
    public static readonly Color Input = Color.FromArgb(52, 52, 52);

    public static void DarkTitleBar(Form f)
    {
        try
        {
            int v = 1;
            Native.DwmSetWindowAttribute(f.Handle, 20, ref v, 4);   // DWMWA_USE_IMMERSIVE_DARK_MODE
        }
        catch { }
    }

    public static void DarkControl(Control c)
    {
        try { Native.SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { }
    }

    public static Button FlatButton(string text, string tip, ToolTip tt, EventHandler click, int width = 36)
    {
        var b = new Button
        {
            Text = text,
            Width = width,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Bar,
            ForeColor = Text,
            Font = new Font("Segoe UI Symbol", 11f),
            Margin = new Padding(2, 5, 2, 0),
            TabStop = false,
            UseVisualStyleBackColor = false
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(65, 65, 65);
        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(85, 85, 85);
        if (click != null) b.Click += click;
        if (tip != null) tt?.SetToolTip(b, tip);
        return b;
    }
}

sealed class DarkColors : ProfessionalColorTable
{
    static readonly Color bg = Color.FromArgb(40, 40, 40);
    static readonly Color hover = Color.FromArgb(65, 65, 65);
    static readonly Color press = Color.FromArgb(85, 85, 85);

    public override Color ToolStripDropDownBackground => bg;
    public override Color MenuItemSelected => hover;
    public override Color MenuItemBorder => hover;
    public override Color MenuBorder => Theme.Border;
    public override Color MenuItemSelectedGradientBegin => hover;
    public override Color MenuItemSelectedGradientEnd => hover;
    public override Color MenuItemPressedGradientBegin => press;
    public override Color MenuItemPressedGradientEnd => press;
    public override Color ImageMarginGradientBegin => bg;
    public override Color ImageMarginGradientMiddle => bg;
    public override Color ImageMarginGradientEnd => bg;
    public override Color SeparatorDark => Theme.Border;
    public override Color SeparatorLight => Theme.Border;
    public override Color ToolStripBorder => bg;
}

sealed class DarkRenderer : ToolStripProfessionalRenderer
{
    public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.Text;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = Theme.Text;
        base.OnRenderItemText(e);
    }
}

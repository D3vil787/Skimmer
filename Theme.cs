using System.Drawing;
using System.Windows.Forms;

namespace Skimmer;

static class Theme
{
    /// <summary>Resolved once at startup from Settings ("dark" | "light" | "system").</summary>
    public static readonly bool IsDark = ResolveDark(AppSettings.Current.Theme);

    public static readonly Color Back = IsDark ? Color.FromArgb(30, 30, 30) : Color.FromArgb(249, 249, 249);
    public static readonly Color Bar = IsDark ? Color.FromArgb(40, 40, 40) : Color.FromArgb(238, 238, 238);
    public static readonly Color GridBack = IsDark ? Color.FromArgb(34, 34, 34) : Color.FromArgb(255, 255, 255);
    public static readonly Color Tile = IsDark ? Color.FromArgb(48, 48, 48) : Color.FromArgb(240, 240, 240);
    public static readonly Color TileSelected = IsDark ? Color.FromArgb(0, 90, 160) : Color.FromArgb(204, 228, 247);
    public static readonly Color Accent = Color.FromArgb(0, 120, 215);
    public static readonly Color Text = IsDark ? Color.FromArgb(225, 225, 225) : Color.FromArgb(30, 30, 30);
    public static readonly Color TextDim = IsDark ? Color.FromArgb(150, 150, 150) : Color.FromArgb(110, 110, 110);
    public static readonly Color Border = IsDark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(200, 200, 200);
    public static readonly Color Input = IsDark ? Color.FromArgb(52, 52, 52) : Color.FromArgb(255, 255, 255);
    public static readonly Color Hover = IsDark ? Color.FromArgb(65, 65, 65) : Color.FromArgb(222, 222, 222);
    public static readonly Color Pressed = IsDark ? Color.FromArgb(85, 85, 85) : Color.FromArgb(205, 205, 205);
    public static readonly Color ButtonBack = IsDark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(228, 228, 228);
    public static readonly Color Link = IsDark ? Color.FromArgb(110, 180, 255) : Color.FromArgb(0, 95, 184);
    public static readonly Color LinkActive = IsDark ? Color.FromArgb(150, 205, 255) : Color.FromArgb(0, 70, 140);
    public static readonly Color Error = IsDark ? Color.FromArgb(255, 130, 120) : Color.FromArgb(190, 30, 20);

    static bool ResolveDark(string mode)
    {
        if (mode == "light") return false;
        if (mode == "dark") return true;
        // "system": Windows "Choose your app mode"
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int light) return light == 0;
        }
        catch { }
        return true;
    }

    /// <summary>Title bar in the app's theme (Windows 10 1809+ / 11).</summary>
    public static void DarkTitleBar(Form f)
    {
        try
        {
            int v = IsDark ? 1 : 0;
            Native.DwmSetWindowAttribute(f.Handle, 20, ref v, 4);   // DWMWA_USE_IMMERSIVE_DARK_MODE
        }
        catch { }
    }

    /// <summary>Scrollbars / tree glyphs in the app's theme.</summary>
    public static void DarkControl(Control c)
    {
        try { Native.SetWindowTheme(c.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
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
        b.FlatAppearance.MouseOverBackColor = Hover;
        b.FlatAppearance.MouseDownBackColor = Pressed;
        if (click != null) b.Click += click;
        if (tip != null) tt?.SetToolTip(b, tip);
        return b;
    }
}

sealed class DarkColors : ProfessionalColorTable
{
    static readonly Color bg = Theme.Bar;
    static readonly Color hover = Theme.Hover;
    static readonly Color press = Theme.Pressed;

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

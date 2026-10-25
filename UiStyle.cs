using System.Runtime.CompilerServices;

namespace Skimmer;

/// <summary>Brand-book role of a control; colours and fonts are re-applied from the palette on every theme change.</summary>
enum UiRole
{
    IconButton,      // 36 x 30, Segoe UI Symbol 11, no border (toolbar)
    Button,          // flat, panel background, 1 px border, min 90 x 30
    PrimaryButton,   // accent fill, text by contrast; one per window
    Toolbar,         // chrome background for the strip and everything on it
    GroupTitle,      // Segoe UI Semibold 11, accent colour
    Hint,            // Segoe UI 9, muted colour
    Muted,           // muted colour, default font
    Link,            // accent colour, underline on hover
    Mono,            // Consolas 10 (paths)
    Title,           // Segoe UI Semibold 15
}

/// <summary>Custom-drawn controls repaint themselves from <see cref="ThemeService.Palette"/>.</summary>
interface IThemeAware
{
    void ApplyTheme();
}

/// <summary>
/// Glue between the UiKit theme service and Skimmer's windows (BRANDBOOK.md, "Правила реализации"):
/// every form goes through <see cref="Attach"/>, which runs <see cref="UiTheme.Apply(Form)"/> and then
/// re-applies the brand-book roles after each theme change, without recreating windows or moving focus.
/// </summary>
static class UiStyle
{
    static readonly ConditionalWeakTable<Control, object> roles = new();

    public static T Role<T>(T control, UiRole role) where T : Control
    {
        roles.AddOrUpdate(control, role);
        Restyle(control);
        return control;
    }

    public static void Attach(Form form)
    {
        UiTheme.Apply(form);

        void OnChanged(object sender, EventArgs e) => Restyle(form);
        ThemeService.ThemeChanged += OnChanged;          // runs after UiTheme's own handler
        form.Load += (_, _) => Restyle(form);            // after UiTheme's Load handler
        form.Disposed += (_, _) => ThemeService.ThemeChanged -= OnChanged;
        Restyle(form);
    }

    /// <summary>Text colour on an accent fill: white or near-black, whichever reads better (brand book: "Палитра").</summary>
    /// <summary>Near-black text for light accent fills (brand book: "почти чёрный #0B0D12").</summary>
    public static readonly Color Ink = ColorTranslator.FromHtml("#0B0D12");

    public static Color OnAccent(Color accent)
    {
        Color light = Color.White;
        Color dark = Ink;
        return ThemeContrast.ContrastRatio(light, accent) >= ThemeContrast.ContrastRatio(dark, accent) ? light : dark;
    }

    public static Color ButtonHover => ThemeService.Palette.MenuHoverBack;
    public static Color ButtonPressed => ThemeDraw.Blend(ThemeService.Palette.MenuHoverBack, ThemeService.Accent, 0.25f);

    /// <summary>Re-applies roles below <paramref name="root"/> (the theme colours themselves come from ThemeService.Apply).</summary>
    public static void Restyle(Control root)
    {
        ApplyRole(root);
        foreach (Control child in root.Controls) Restyle(child);
        if (root is IThemeAware aware) aware.ApplyTheme();
    }

    static void ApplyRole(Control c)
    {
        var p = ThemeService.Palette;
        if (c is StatusStrip status)
        {
            ThemeService.Apply(status);
            status.BackColor = p.StatusBack;
            foreach (ToolStripItem item in status.Items)
            {
                item.BackColor = p.StatusBack;
                item.ForeColor = p.StatusFore;
                if (item is ToolStripStatusLabel { IsLink: true } link)
                {
                    link.LinkColor = p.Accent;
                    link.ActiveLinkColor = p.Accent;
                    link.VisitedLinkColor = p.Accent;
                }
            }
        }

        if (!roles.TryGetValue(c, out var r)) return;
        switch ((UiRole)r)
        {
            case UiRole.IconButton when c is Button b:
                b.FlatStyle = FlatStyle.Flat;
                b.UseVisualStyleBackColor = false;
                b.FlatAppearance.BorderSize = 0;
                b.BackColor = c.Parent?.BackColor ?? p.ChromeBack;
                b.ForeColor = p.Fore;
                b.Font = UiTheme.Symbol;
                b.FlatAppearance.MouseOverBackColor = ButtonHover;
                b.FlatAppearance.MouseDownBackColor = ButtonPressed;
                break;

            case UiRole.Button when c is Button b:
                b.FlatStyle = FlatStyle.Flat;
                b.UseVisualStyleBackColor = false;
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = p.Border;
                b.BackColor = p.PanelBack;
                b.ForeColor = p.Fore;
                b.Font = UiTheme.Default;
                b.FlatAppearance.MouseOverBackColor = ButtonHover;
                b.FlatAppearance.MouseDownBackColor = ButtonPressed;
                break;

            case UiRole.PrimaryButton when c is Button b:
            {
                Color accent = p.Accent;
                Color text = OnAccent(accent);
                b.FlatStyle = FlatStyle.Flat;
                b.UseVisualStyleBackColor = false;
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = accent;
                b.BackColor = accent;
                b.ForeColor = text;
                b.Font = UiTheme.Default;
                b.FlatAppearance.MouseOverBackColor = ThemeDraw.Blend(accent, text, 0.12f);
                b.FlatAppearance.MouseDownBackColor = ThemeDraw.Blend(accent, text, 0.24f);
                break;
            }

            case UiRole.Toolbar:
                PaintChrome(c, p);
                break;

            case UiRole.GroupTitle:
                c.Font = UiTheme.Subtitle;
                c.ForeColor = p.Accent;
                break;

            case UiRole.Title:
                c.Font = UiTheme.Title;
                c.ForeColor = p.Fore;
                break;

            case UiRole.Hint:
                c.Font = UiTheme.Small;
                c.ForeColor = p.Muted;
                break;

            case UiRole.Muted:
                c.ForeColor = p.Muted;
                break;

            case UiRole.Mono:
                c.Font = UiTheme.Mono;
                break;

            case UiRole.Link when c is LinkLabel l:
                l.LinkColor = p.Accent;
                l.ActiveLinkColor = p.Accent;
                l.VisitedLinkColor = p.Accent;
                l.LinkBehavior = LinkBehavior.HoverUnderline;
                break;
        }
    }

    /// <summary>Toolbar: chrome background for the strip and its plain children; inputs keep the input colour.</summary>
    static void PaintChrome(Control c, ThemePalette p)
    {
        c.BackColor = p.ChromeBack;
        foreach (Control child in c.Controls)
        {
            if (child is TextBoxBase or ComboBox or IThemeAware) continue;
            if (roles.TryGetValue(child, out var r) && (UiRole)r != UiRole.Toolbar && (UiRole)r != UiRole.IconButton) continue;
            PaintChrome(child, p);
            if (roles.TryGetValue(child, out r) && (UiRole)r == UiRole.IconButton) ApplyRole(child);
        }
    }

    // ------------------------------------------------------------ factories (sizes for 100 %, forms scale by DPI)

    public static Button IconButton(string symbol, string tip, ToolTip tt, EventHandler click)
    {
        var b = new Button
        {
            Text = symbol,
            Size = new Size(36, 30),
            Margin = new Padding(2, 6, 2, 6),
            TabStop = false
        };
        if (click != null) b.Click += click;
        if (tip != null) tt?.SetToolTip(b, tip);
        return Role(b, UiRole.IconButton);
    }

    public static Button Button(string text, EventHandler click = null, bool primary = false)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(90, 30),
            Padding = new Padding(10, 0, 10, 0),
            Margin = new Padding(8, 0, 0, 0)
        };
        if (click != null) b.Click += click;
        return Role(b, primary ? UiRole.PrimaryButton : UiRole.Button);
    }

    public static Label Label(string text, UiRole? role = null)
    {
        var l = new Label { Text = text, AutoSize = true, Margin = new Padding(0, 6, 8, 0) };
        return role.HasValue ? Role(l, role.Value) : l;
    }

    /// <summary>Settings-style hint under a field: small muted text that wraps at the given width.</summary>
    public static Label Hint(string text, int width)
    {
        var l = new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            Margin = new Padding(0, 2, 0, 6)
        };
        return Role(l, UiRole.Hint);
    }
}

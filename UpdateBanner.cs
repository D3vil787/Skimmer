namespace Skimmer;

/// <summary>
/// Brand-book update banner ("Баннер обновлений"): a 46 px strip filled with the accent colour under the toolbar,
/// "New version X (you have Y)", a "What's new" link and the buttons Update, Later and ×.
/// It appears without a sound and never takes the focus.
/// </summary>
sealed class UpdateBanner : Panel, IThemeAware
{
    readonly Label text = new();
    readonly LinkLabel whatsNew = new();
    readonly Button btnUpdate = new();
    readonly Button btnLater = new();
    readonly Button btnClose = new();
    readonly ToolTip tips = new();

    public UpdateInfo Info { get; private set; }

    public event Action UpdateClicked;
    public event Action WhatsNewClicked;
    public event Action Dismissed;   // "Later" or "×": hide for this version

    public UpdateBanner()
    {
        Dock = DockStyle.Top;
        Height = 46;
        Padding = new Padding(12, 6, 8, 6);
        Visible = false;
        TabStop = false;

        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.Transparent
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        text.AutoSize = true;
        text.Anchor = AnchorStyles.Left;
        text.Margin = new Padding(0, 0, 12, 0);

        whatsNew.AutoSize = true;
        whatsNew.Anchor = AnchorStyles.Left;
        whatsNew.Text = Lang.Get("banner.whatsnew");
        whatsNew.LinkBehavior = LinkBehavior.AlwaysUnderline;
        whatsNew.TabStop = false;
        whatsNew.LinkClicked += (_, _) => WhatsNewClicked?.Invoke();

        Setup(btnUpdate, Lang.Get("banner.update"), 90);
        btnUpdate.Click += (_, _) => UpdateClicked?.Invoke();
        Setup(btnLater, Lang.Get("banner.later"), 90);
        btnLater.Click += (_, _) => Dismissed?.Invoke();
        Setup(btnClose, "×", 30);
        btnClose.Font = UiTheme.Symbol;
        btnClose.Click += (_, _) => Dismissed?.Invoke();
        tips.SetToolTip(btnClose, Lang.Get("banner.close"));

        row.Controls.Add(text, 0, 0);
        row.Controls.Add(whatsNew, 1, 0);
        row.Controls.Add(btnUpdate, 3, 0);
        row.Controls.Add(btnLater, 4, 0);
        row.Controls.Add(btnClose, 5, 0);
        Controls.Add(row);
    }

    static void Setup(Button b, string caption, int minWidth)
    {
        b.Text = caption;
        b.AutoSize = true;
        b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        b.MinimumSize = new Size(minWidth, 30);
        b.Padding = new Padding(8, 0, 8, 0);
        b.Margin = new Padding(8, 0, 0, 0);
        b.Anchor = AnchorStyles.Right;
        b.FlatStyle = FlatStyle.Flat;
        b.UseVisualStyleBackColor = false;
        b.TabStop = false;
    }

    /// <summary>Shows the banner for a release without taking the focus.</summary>
    public void ShowFor(UpdateInfo info)
    {
        Info = info;
        text.Text = Lang.Format("banner.text", info.VersionText, UpdateService.CurrentText);
        ApplyTheme();
        Visible = true;
    }

    public void HideBanner()
    {
        Visible = false;
        Info = null;
    }

    /// <summary>Main button of the strip: the accent darkened toward the brand-book ink (#0B0D12).</summary>
    public static Color UpdateButtonFill(Color accent) => ThemeDraw.Blend(accent, UiStyle.Ink, 0.22f);

    public void ApplyTheme()
    {
        var p = ThemeService.Palette;
        Color fill = p.Accent;
        Color fore = UiStyle.OnAccent(fill);

        // every child sits on the accent: the generic theme pass paints labels with the panel colour
        BackColor = fill;
        foreach (Control c in Controls)
        {
            c.BackColor = fill;
            foreach (Control cc in c.Controls) cc.BackColor = fill;
        }

        text.ForeColor = fore;
        text.Font = UiTheme.Default;
        whatsNew.Font = UiTheme.Default;
        whatsNew.LinkColor = fore;
        whatsNew.ActiveLinkColor = fore;
        whatsNew.VisitedLinkColor = fore;

        // "Update": darker shade of the accent, text by contrast, no border
        Color main = UpdateButtonFill(fill);
        btnUpdate.Font = UiTheme.Medium;
        btnUpdate.BackColor = main;
        btnUpdate.ForeColor = UiStyle.OnAccent(main);
        btnUpdate.FlatAppearance.BorderSize = 0;
        btnUpdate.FlatAppearance.BorderColor = main;
        btnUpdate.FlatAppearance.MouseOverBackColor = ThemeDraw.Blend(main, UiStyle.Ink, 0.12f);
        btnUpdate.FlatAppearance.MouseDownBackColor = ThemeDraw.Blend(main, UiStyle.Ink, 0.24f);

        // "Later": outline in a softened text colour; "×": text only
        btnLater.Font = UiTheme.Default;
        btnLater.FlatAppearance.BorderSize = 1;
        btnLater.FlatAppearance.BorderColor = ThemeDraw.Blend(fill, fore, 0.45f);
        foreach (var b in new[] { btnLater, btnClose })
        {
            b.BackColor = fill;
            b.ForeColor = fore;
            b.FlatAppearance.MouseOverBackColor = ThemeDraw.Blend(fill, UiStyle.Ink, 0.12f);
            b.FlatAppearance.MouseDownBackColor = ThemeDraw.Blend(fill, UiStyle.Ink, 0.24f);
        }
        btnClose.FlatAppearance.BorderSize = 0;
        btnClose.FlatAppearance.BorderColor = fill;
        Invalidate(true);
    }
}

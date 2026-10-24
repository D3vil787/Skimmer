using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Skimmer;

/// <summary>
/// Settings dialog by the brand book ("Окно настроек"): fixed 560 px wide, built on layout panels,
/// groups in the standard order (program-specific, data and cache, updates, appearance),
/// small muted hints under fields, OK / Cancel at the bottom right (Enter / Esc).
/// Theme, accent and density apply immediately and are rolled back on Cancel;
/// the language changes on the next start.
/// </summary>
sealed class SettingsForm : Form, IThemeAware
{
    const int ContentWidth = 532;   // 560 - 2 x 14 padding

    readonly Action clearCache;
    readonly Func<Task<UpdateInfo>> checkUpdates;
    readonly ThemeSettings originalTheme = ThemeService.Settings;
    readonly ThemeSettingsStore themeStore = new();

    readonly CheckBox chkBg = new();
    readonly ListBox lstFolders = new();
    readonly CheckBox chkSub = new();
    readonly NumericUpDown numRescan = new();
    readonly NumericUpDown numAge = new();
    readonly NumericUpDown numSize = new();
    readonly Label lblUsed = new();
    readonly CheckBox chkUpd = new();
    readonly ComboBox cmbTheme = new();
    readonly ComboBox cmbDensity = new();
    readonly ComboBox cmbLang = new();
    readonly FlowLayoutPanel swatches = new();
    readonly ToolTip tips = new();
    Button btnCheck;
    bool loading = true;

    /// <summary>Set when "Check now" found a newer version; the main window then offers it.</summary>
    public UpdateInfo FoundUpdate;

    public SettingsForm(Action clearCache, Func<Task<UpdateInfo>> checkUpdates)
    {
        this.clearCache = clearCache;
        this.checkUpdates = checkUpdates;
        var s = AppSettings.Current;

        Text = Lang.Get("set.title");
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(14),
            MinimumSize = new Size(560, 0),
            MaximumSize = new Size(560, 0),
            Dock = DockStyle.Fill
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        root.Controls.Add(UiStyle.Label(Lang.Get("set.title"), UiRole.Title));

        // ---------------- 1. program-specific: background indexing
        var gIdx = Section(root, Lang.Get("set.group.indexing"));
        chkBg.Text = Lang.Get("set.bg.enable");
        chkBg.AutoSize = true;
        chkBg.Checked = s.BackgroundIndexing;
        Span(gIdx, chkBg);

        lstFolders.Height = 96;
        lstFolders.Dock = DockStyle.Fill;
        lstFolders.BorderStyle = BorderStyle.FixedSingle;
        lstFolders.IntegralHeight = false;
        foreach (var f in s.IndexFolders) lstFolders.Items.Add(f);
        var folderButtons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        var btnAdd = UiStyle.Button(Lang.Get("set.add"), (_, _) => AddFolder());
        var btnRemove = UiStyle.Button(Lang.Get("set.remove"), (_, _) =>
        {
            if (lstFolders.SelectedIndex >= 0) lstFolders.Items.RemoveAt(lstFolders.SelectedIndex);
        });
        btnAdd.Margin = new Padding(8, 0, 0, 8);
        btnRemove.Margin = new Padding(8, 0, 0, 0);
        folderButtons.Controls.AddRange(new Control[] { btnAdd, btnRemove });
        var folderRow = Row(lstFolders, folderButtons);
        Span(gIdx, folderRow);

        chkSub.Text = Lang.Get("set.sub");
        chkSub.AutoSize = true;
        chkSub.Checked = s.IndexSubfolders;
        Span(gIdx, chkSub);
        Field(gIdx, Lang.Get("set.rescan"), Num(numRescan, 1, 24 * 60, s.RescanMinutes));
        Span(gIdx, UiStyle.Hint(Lang.Get("set.bg.hint"), ContentWidth));

        // ---------------- 2. data and cache
        var gData = Section(root, Lang.Get("set.group.data"));
        var lblPath = UiStyle.Role(new Label
        {
            Text = AppPaths.CacheDir,
            AutoSize = true,
            MaximumSize = new Size(ContentWidth - 160, 0),
            Margin = new Padding(0, 6, 0, 0)
        }, UiRole.Mono);
        Field(gData, Lang.Get("set.cache.location"), lblPath);
        lblUsed.AutoSize = true;
        lblUsed.Margin = new Padding(0, 4, 0, 4);
        lblUsed.Text = Lang.Format("set.cache.used", Fmt.Size(CacheCleaner.TotalSize()));
        Span(gData, UiStyle.Role(lblUsed, UiRole.Muted));
        Field(gData, Lang.Get("set.cache.age"), Num(numAge, 1, 3650, s.CacheMaxAgeDays));
        Field(gData, Lang.Get("set.cache.size"), Num(numSize, 64, 1_000_000, s.CacheMaxSizeMB));
        Span(gData, UiStyle.Hint(Lang.Get("set.cache.hint"), ContentWidth));
        var cacheButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        var btnOpen = UiStyle.Button(Lang.Get("set.cache.open"), (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(AppPaths.CacheDir);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.CacheDir}\"") { UseShellExecute = true });
            }
            catch { }
        });
        btnOpen.Margin = Padding.Empty;
        cacheButtons.Controls.Add(btnOpen);
        cacheButtons.Controls.Add(UiStyle.Button(Lang.Get("set.cache.clear"), (_, _) => DoClear()));
        Span(gData, cacheButtons);

        // ---------------- 3. updates
        var gUpd = Section(root, Lang.Get("set.group.updates"));
        chkUpd.Text = Lang.Get("set.upd.auto");
        chkUpd.AutoSize = true;
        chkUpd.Checked = s.AutoUpdateCheck;
        Span(gUpd, chkUpd);
        btnCheck = UiStyle.Button(Lang.Get("set.upd.check"), (_, _) => OnCheckNow());
        btnCheck.Margin = Padding.Empty;
        var version = UiStyle.Label(Lang.Format("set.upd.version", UpdateService.CurrentText), UiRole.Muted);
        version.Anchor = AnchorStyles.Left;
        version.Margin = new Padding(0, 0, 12, 0);
        var updRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        updRow.Controls.Add(version);
        updRow.Controls.Add(btnCheck);
        Span(gUpd, updRow);
        Span(gUpd, UiStyle.Hint(Lang.Get("set.upd.hint"), ContentWidth));

        // ---------------- 4. appearance: theme, accent, density, language
        var gLook = Section(root, Lang.Get("set.group.appearance"));
        Combo(cmbTheme, Lang.Get(ThemeCatalog.Get(ThemeCatalog.FluentLightId).NameKey),
                        Lang.Get(ThemeCatalog.Get(ThemeCatalog.GraphiteDarkId).NameKey),
                        Lang.Get(ThemeCatalog.SystemNameKey));
        string id = ThemeService.Settings.ThemeId;
        cmbTheme.SelectedIndex = ThemeCatalog.IsSystem(id) ? 2 : ThemeCatalog.Get(id).IsDark ? 1 : 0;
        cmbTheme.SelectedIndexChanged += (_, _) => Preview();
        Field(gLook, Lang.Get("set.theme"), cmbTheme);

        swatches.AutoSize = true;
        swatches.WrapContents = false;
        swatches.Margin = new Padding(0, 4, 0, 4);
        AddSwatch(null, Lang.Get("theme.accent.default"));
        foreach (var opt in ThemeCatalog.AccentOptions) AddSwatch(ThemeService.ToHex(opt.Color), Lang.Get(opt.NameKey));
        Field(gLook, Lang.Get("set.accent"), swatches);

        Combo(cmbDensity, Lang.Get("density.compact") + " (24 px)", Lang.Get("density.normal") + " (28 px)", Lang.Get("density.spacious") + " (32 px)");
        cmbDensity.SelectedIndex = (int)ThemeService.Settings.Density;
        cmbDensity.SelectedIndexChanged += (_, _) => Preview();
        Field(gLook, Lang.Get("set.density"), cmbDensity);
        Span(gLook, UiStyle.Hint(Lang.Get("set.theme.hint"), ContentWidth));

        Combo(cmbLang, Lang.Get("set.lang.auto"), Lang.Get("lang.ru"), Lang.Get("lang.en"));
        cmbLang.SelectedIndex = s.Language == "ru" ? 1 : s.Language == "en" ? 2 : 0;
        Field(gLook, Lang.Get("set.lang"), cmbLang);
        Span(gLook, UiStyle.Hint(Lang.Get("set.lang.hint"), ContentWidth));

        // ---------------- OK / Cancel (bottom right)
        var ok = UiStyle.Button(Lang.Get("ok"), (_, _) => Apply(), primary: true);
        ok.DialogResult = DialogResult.OK;
        var cancel = UiStyle.Button(Lang.Get("cancel"));
        cancel.DialogResult = DialogResult.Cancel;
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = false,
            Margin = new Padding(0, 14, 0, 0)
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        root.Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;

        Controls.Add(root);
        loading = false;
        UiStyle.Attach(this);
    }

    // ------------------------------------------------------------ layout helpers

    /// <summary>A group: accent subtitle and a two-column table (label | field). Groups are 10 px apart.</summary>
    static TableLayoutPanel Section(TableLayoutPanel root, string title)
    {
        var t = UiStyle.Label(title, UiRole.GroupTitle);
        t.Margin = new Padding(0, root.Controls.Count > 1 ? 10 : 6, 0, 4);
        root.Controls.Add(t);

        var g = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        g.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(g);
        return g;
    }

    static void Field(TableLayoutPanel g, string label, Control field)
    {
        var l = UiStyle.Label(label);
        l.Anchor = AnchorStyles.Left;
        l.Margin = new Padding(0, 4, 12, 4);
        field.Anchor = AnchorStyles.Left;
        if (field.Margin == Padding.Empty || field.Margin == new Padding(3)) field.Margin = new Padding(0, 4, 0, 4);
        int r = g.RowCount++;
        g.Controls.Add(l, 0, r);
        g.Controls.Add(field, 1, r);
    }

    static void Span(TableLayoutPanel g, Control c)
    {
        int r = g.RowCount++;
        if (c.Margin == new Padding(3)) c.Margin = new Padding(0, 4, 0, 4);
        g.Controls.Add(c, 0, r);
        g.SetColumnSpan(c, 2);
    }

    static TableLayoutPanel Row(Control fill, Control right)
    {
        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fill.Margin = Padding.Empty;
        t.Controls.Add(fill, 0, 0);
        t.Controls.Add(right, 1, 0);
        return t;
    }

    static NumericUpDown Num(NumericUpDown n, int min, int max, int value)
    {
        n.Minimum = min;
        n.Maximum = max;
        n.Value = Math.Clamp(value, min, max);
        n.Width = 90;
        n.BorderStyle = BorderStyle.FixedSingle;
        return n;
    }

    static void Combo(ComboBox c, params string[] items)
    {
        c.DropDownStyle = ComboBoxStyle.DropDownList;
        c.FlatStyle = FlatStyle.Flat;
        c.Width = 220;
        c.Items.AddRange(items);
    }

    // ------------------------------------------------------------ accent swatches

    void AddSwatch(string hex, string name)
    {
        var b = new Button
        {
            Size = new Size(26, 26),
            Margin = new Padding(0, 0, 6, 0),
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            Tag = hex ?? "",
            TabStop = true,
            AccessibleName = name
        };
        tips.SetToolTip(b, name);
        b.Click += (_, _) =>
        {
            selectedAccent = hex;
            Preview();
        };
        swatches.Controls.Add(b);
    }

    string selectedAccent = ThemeService.Settings.AccentColor;

    /// <summary>Swatches show the accent as it will look in the current theme (already adjusted for contrast).</summary>
    void PaintSwatches()
    {
        var p = ThemeService.Palette;
        foreach (Button b in swatches.Controls)
        {
            string hex = (string)b.Tag;
            Color c = hex.Length == 0
                ? ThemeService.Current.DefaultAccent
                : ThemeService.ResolveAccent(ThemeService.Current, hex);
            bool selected = string.IsNullOrEmpty(selectedAccent)
                ? hex.Length == 0
                : string.Equals(hex, selectedAccent, StringComparison.OrdinalIgnoreCase);
            b.BackColor = c;
            b.FlatAppearance.MouseOverBackColor = c;
            b.FlatAppearance.MouseDownBackColor = c;
            b.FlatAppearance.BorderColor = selected ? p.Fore : p.Border;
            b.FlatAppearance.BorderSize = selected ? 2 : 1;
            b.Text = "";
        }
    }

    public void ApplyTheme() => PaintSwatches();

    // ------------------------------------------------------------ live theme preview

    ThemeSettings Chosen() => ThemeService.Settings with
    {
        ThemeId = cmbTheme.SelectedIndex switch
        {
            1 => ThemeCatalog.GraphiteDarkId,
            2 => ThemeCatalog.SystemId,
            _ => ThemeCatalog.FluentLightId
        },
        AccentColor = string.IsNullOrEmpty(selectedAccent) ? null : selectedAccent,
        Density = (ThemeDensity)Math.Clamp(cmbDensity.SelectedIndex, 0, 2)
    };

    void Preview()
    {
        if (loading) return;
        ThemeService.Apply(Chosen());
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (DialogResult != DialogResult.OK) ThemeService.Apply(originalTheme);   // Cancel / Esc / ×: roll back the preview
        base.OnFormClosed(e);
    }

    // ------------------------------------------------------------ actions

    void AddFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = Lang.Get("set.folderdlg"), UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string p = dlg.SelectedPath;
        foreach (var it in lstFolders.Items)
            if (string.Equals((string)it, p, StringComparison.OrdinalIgnoreCase)) return;
        lstFolders.Items.Add(p);
    }

    async void OnCheckNow()
    {
        btnCheck.Enabled = false;
        try
        {
            var info = await checkUpdates();
            if (IsDisposed) return;
            if (info == null)
            {
                MessageBox.Show(this, Lang.Format("upd.uptodate", UpdateService.CurrentText), Lang.Get("app.title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                Apply();
                FoundUpdate = info;
                DialogResult = DialogResult.OK;
            }
        }
        catch (Exception ex)
        {
            // a manual check reports errors; the automatic one stays silent
            if (!IsDisposed)
                MessageBox.Show(this, Lang.Format("upd.checkfail", ex.Message), Lang.Get("app.title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (!IsDisposed) btnCheck.Enabled = true;
        }
    }

    void DoClear()
    {
        clearCache?.Invoke();
        var (files, bytes) = CacheCleaner.DeleteAll();
        MessageBox.Show(this, Lang.Format("set.cache.cleared", files, Fmt.Size(bytes)), Lang.Get("app.title"),
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        lblUsed.Text = Lang.Format("set.cache.used", Fmt.Size(CacheCleaner.TotalSize()));
    }

    void Apply()
    {
        var s = AppSettings.Current;
        s.BackgroundIndexing = chkBg.Checked;
        s.AutoUpdateCheck = chkUpd.Checked;
        s.IndexSubfolders = chkSub.Checked;
        s.RescanMinutes = (int)numRescan.Value;
        s.CacheMaxAgeDays = (int)numAge.Value;
        s.CacheMaxSizeMB = (int)numSize.Value;
        s.Language = cmbLang.SelectedIndex == 1 ? "ru" : cmbLang.SelectedIndex == 2 ? "en" : "auto";
        s.IndexFolders = lstFolders.Items.Cast<string>().ToList();
        s.Save();

        var theme = Chosen();
        ThemeService.Apply(theme);
        themeStore.Save(theme);
    }
}

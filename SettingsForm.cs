using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Skimmer;

sealed class SettingsForm : Form
{
    readonly Action clearCache;
    readonly Func<Task<UpdateInfo>> checkUpdates;
    readonly CheckBox chkUpd = new();
    Button btnCheck;

    /// <summary>Set when "Check now" found a newer version; the main window then offers it.</summary>
    public UpdateInfo FoundUpdate;
    readonly CheckBox chkBg = new();
    readonly ListBox lstFolders = new();
    readonly CheckBox chkSub = new();
    readonly NumericUpDown numRescan = new();
    readonly NumericUpDown numAge = new();
    readonly NumericUpDown numSize = new();
    readonly ComboBox cmbLang = new();
    readonly ComboBox cmbTheme = new();

    /// <summary>Theme or language changed: they are applied by restarting the app.</summary>
    public bool NeedsRestart;
    readonly Label lblUsed = new();

    public SettingsForm(Action clearCache, Func<Task<UpdateInfo>> checkUpdates)
    {
        this.clearCache = clearCache;
        this.checkUpdates = checkUpdates;
        var s = AppSettings.Current;

        Text = L.T("set.title");
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 694);
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);

        // --- background indexing ---
        var gb1 = Group(L.T("set.bg"), 10, 10, 540, 250);
        chkBg.Text = L.T("set.bg.enable");
        chkBg.AutoSize = true;
        chkBg.Location = new Point(14, 24);
        chkBg.Checked = s.BackgroundIndexing;
        gb1.Controls.Add(chkBg);

        lstFolders.Location = new Point(14, 52);
        lstFolders.Size = new Size(400, 130);
        lstFolders.BackColor = Theme.Input;
        lstFolders.ForeColor = Theme.Text;
        lstFolders.BorderStyle = BorderStyle.FixedSingle;
        foreach (var f in s.IndexFolders) lstFolders.Items.Add(f);
        gb1.Controls.Add(lstFolders);

        var btnAdd = Btn(L.T("set.add"), 424, 52, 104);
        btnAdd.Click += (_, _) => AddFolder();
        gb1.Controls.Add(btnAdd);

        var btnRemove = Btn(L.T("set.remove"), 424, 86, 104);
        btnRemove.Click += (_, _) =>
        {
            if (lstFolders.SelectedIndex >= 0) lstFolders.Items.RemoveAt(lstFolders.SelectedIndex);
        };
        gb1.Controls.Add(btnRemove);

        chkSub.Text = L.T("set.sub");
        chkSub.AutoSize = true;
        chkSub.Location = new Point(14, 192);
        chkSub.Checked = s.IndexSubfolders;
        gb1.Controls.Add(chkSub);

        gb1.Controls.Add(Lbl(L.T("set.rescan"), 14, 220));
        Num(numRescan, 1, 24 * 60, s.RescanMinutes, 250, 217, 80);
        gb1.Controls.Add(numRescan);
        Controls.Add(gb1);

        // --- cache ---
        var gb2 = Group(L.T("set.cache"), 10, 270, 540, 210);
        var lblPath = Lbl(L.F("set.cache.path", AppPaths.CacheDir), 14, 24);
        lblPath.AutoSize = false;
        lblPath.Size = new Size(510, 36);
        lblPath.AutoEllipsis = true;
        gb2.Controls.Add(lblPath);

        lblUsed.AutoSize = true;
        lblUsed.Location = new Point(14, 62);
        lblUsed.Text = L.F("set.cache.used", Fmt.Size(CacheCleaner.TotalSize()));
        gb2.Controls.Add(lblUsed);

        gb2.Controls.Add(Lbl(L.T("set.cache.age"), 14, 94));
        Num(numAge, 1, 3650, s.CacheMaxAgeDays, 400, 91, 80);
        gb2.Controls.Add(numAge);

        gb2.Controls.Add(Lbl(L.T("set.cache.size"), 14, 126));
        Num(numSize, 64, 1_000_000, s.CacheMaxSizeMB, 400, 123, 80);
        gb2.Controls.Add(numSize);

        var btnOpen = Btn(L.T("set.cache.open"), 14, 160, 190);
        btnOpen.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(AppPaths.CacheDir);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.CacheDir}\"") { UseShellExecute = true });
            }
            catch { }
        };
        gb2.Controls.Add(btnOpen);

        var btnClear = Btn(L.T("set.cache.clear"), 214, 160, 190);
        btnClear.Click += (_, _) => DoClear();
        gb2.Controls.Add(btnClear);
        Controls.Add(gb2);

        // --- updates ---
        var gb3 = Group(L.T("set.upd"), 10, 490, 540, 88);
        chkUpd.Text = L.T("set.upd.auto");
        chkUpd.AutoSize = true;
        chkUpd.Location = new Point(14, 24);
        chkUpd.Checked = s.AutoUpdateCheck;
        gb3.Controls.Add(chkUpd);
        gb3.Controls.Add(Lbl(L.F("set.upd.version", UpdateService.CurrentText), 14, 56));
        btnCheck = Btn(L.T("set.upd.check"), 330, 50, 198);
        btnCheck.Click += (_, _) => OnCheckNow();
        gb3.Controls.Add(btnCheck);
        Controls.Add(gb3);

        // --- theme ---
        Controls.Add(Lbl(L.T("set.theme"), 14, 594));
        cmbTheme.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbTheme.FlatStyle = FlatStyle.Flat;
        cmbTheme.BackColor = Theme.Input;
        cmbTheme.ForeColor = Theme.Text;
        cmbTheme.Items.AddRange(new object[] { L.T("theme.dark"), L.T("theme.light"), L.T("theme.system") });
        cmbTheme.SelectedIndex = s.Theme == "light" ? 1 : s.Theme == "system" ? 2 : 0;
        cmbTheme.Location = new Point(330, 590);
        cmbTheme.Width = 180;
        Controls.Add(cmbTheme);

        // --- language ---
        Controls.Add(Lbl(L.T("set.lang"), 14, 628));
        cmbLang.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbLang.FlatStyle = FlatStyle.Flat;
        cmbLang.BackColor = Theme.Input;
        cmbLang.ForeColor = Theme.Text;
        cmbLang.Items.AddRange(new object[] { L.T("set.lang.auto"), "Русский", "English" });
        cmbLang.SelectedIndex = s.Language == "ru" ? 1 : s.Language == "en" ? 2 : 0;
        cmbLang.Location = new Point(330, 624);
        cmbLang.Width = 180;
        Controls.Add(cmbLang);

        // --- OK / Cancel ---
        var ok = Btn(L.T("ok"), 360, 658, 90);
        ok.DialogResult = DialogResult.OK;
        ok.Click += (_, _) => Apply();
        var cancel = Btn(L.T("cancel"), 460, 658, 90);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(this);
    }

    void AddFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = L.T("set.folderdlg"), UseDescriptionForTitle = true };
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
                MessageBox.Show(this, L.F("upd.uptodate", UpdateService.CurrentText), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            if (!IsDisposed)
                MessageBox.Show(this, L.F("upd.checkfail", ex.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        MessageBox.Show(this, L.F("set.cache.cleared", files, Fmt.Size(bytes)), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        lblUsed.Text = L.F("set.cache.used", Fmt.Size(CacheCleaner.TotalSize()));
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
        string lang = cmbLang.SelectedIndex == 1 ? "ru" : cmbLang.SelectedIndex == 2 ? "en" : "auto";
        string theme = cmbTheme.SelectedIndex == 1 ? "light" : cmbTheme.SelectedIndex == 2 ? "system" : "dark";
        NeedsRestart = lang != s.Language || theme != s.Theme;
        s.Language = lang;
        s.Theme = theme;
        s.IndexFolders = lstFolders.Items.Cast<string>().ToList();
        s.Save();
    }

    // ------------------------------------------------------------ small helpers

    static GroupBox Group(string text, int x, int y, int w, int h) => new()
    {
        Text = text,
        Location = new Point(x, y),
        Size = new Size(w, h),
        ForeColor = Theme.Text,
        BackColor = Theme.Back
    };

    static Label Lbl(string text, int x, int y) => new()
    {
        Text = text,
        AutoSize = true,
        Location = new Point(x, y),
        ForeColor = Theme.Text
    };

    static Button Btn(string text, int x, int y, int w)
    {
        var b = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(w, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.ButtonBack,
            ForeColor = Theme.Text,
            UseVisualStyleBackColor = false
        };
        b.FlatAppearance.BorderColor = Theme.Border;
        return b;
    }

    static void Num(NumericUpDown n, int min, int max, int value, int x, int y, int w)
    {
        n.Minimum = min;
        n.Maximum = max;
        n.Value = Math.Clamp(value, min, max);
        n.Location = new Point(x, y);
        n.Width = w;
        n.BackColor = Theme.Input;
        n.ForeColor = Theme.Text;
        n.BorderStyle = BorderStyle.FixedSingle;
    }
}

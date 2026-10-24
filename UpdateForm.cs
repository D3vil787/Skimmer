using System.Diagnostics;

namespace Skimmer;

/// <summary>
/// Update dialog by the brand book ("Диалог обновления", 520 x 400): release notes, a progress bar and the buttons
/// "Install and restart" (primary), "Later" and "Skip this version". The caller installs when DialogResult is OK.
/// </summary>
sealed class UpdateForm : Form
{
    readonly UpdateInfo info;
    readonly Label lblStatus = new();
    readonly TextBox txtNotes = new();
    readonly ProgressBar bar = new();
    readonly Button btnInstall;
    readonly Button btnLater;
    readonly Button btnSkip;
    readonly bool canInstall = UpdateService.CanSelfUpdate;
    CancellationTokenSource cts;

    public string DownloadedFile;
    public bool SkipRequested;

    public UpdateForm(UpdateInfo info)
    {
        this.info = info;

        Text = Lang.Get("app.title");
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 400);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(14)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // title
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // notes
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // status
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // progress
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // buttons

        var title = UiStyle.Label(Lang.Format("upd.header", info.VersionText, UpdateService.CurrentText), UiRole.Title);
        title.Margin = new Padding(0, 0, 0, 8);
        root.Controls.Add(title, 0, 0);

        txtNotes.Multiline = true;
        txtNotes.ReadOnly = true;
        txtNotes.ScrollBars = ScrollBars.Vertical;
        txtNotes.Dock = DockStyle.Fill;
        txtNotes.BorderStyle = BorderStyle.FixedSingle;
        txtNotes.Margin = Padding.Empty;
        string notes = (info.Notes ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n").Trim();
        if (notes.Length > 4000) notes = notes.Substring(0, 4000) + "…";
        txtNotes.Text = notes;
        root.Controls.Add(txtNotes, 0, 1);

        lblStatus.AutoSize = true;
        lblStatus.MaximumSize = new Size(492, 0);
        lblStatus.Margin = new Padding(0, 8, 0, 4);
        if (!canInstall) lblStatus.Text = Lang.Get("upd.manual");
        root.Controls.Add(UiStyle.Role(lblStatus, UiRole.Hint), 0, 2);

        bar.Dock = DockStyle.Fill;
        bar.Height = 12;
        bar.Minimum = 0;
        bar.Maximum = 100;
        bar.Visible = false;
        bar.Margin = new Padding(0, 0, 0, 4);
        root.Controls.Add(bar, 0, 3);

        btnInstall = UiStyle.Button(canInstall ? Lang.Get("upd.install") : Lang.Get("upd.open"), (_, _) => OnInstall(), primary: true);
        btnLater = UiStyle.Button(Lang.Get("upd.later"));
        btnLater.DialogResult = DialogResult.Cancel;
        btnSkip = UiStyle.Button(Lang.Get("upd.skip"), (_, _) =>
        {
            SkipRequested = true;
            DialogResult = DialogResult.Cancel;
        });
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = false,
            Margin = new Padding(0, 8, 0, 0)
        };
        buttons.Controls.Add(btnLater);
        buttons.Controls.Add(btnInstall);
        buttons.Controls.Add(btnSkip);
        root.Controls.Add(buttons, 0, 4);

        Controls.Add(root);
        AcceptButton = btnInstall;
        CancelButton = btnLater;
        UiStyle.Attach(this);
    }

    async void OnInstall()
    {
        if (!canInstall)
        {
            try { Process.Start(new ProcessStartInfo(info.PageUrl) { UseShellExecute = true }); } catch { }
            DialogResult = DialogResult.Cancel;
            return;
        }

        btnInstall.Enabled = false;
        btnSkip.Enabled = false;
        bar.Value = 0;
        bar.Visible = true;
        UiStyle.Role(lblStatus, UiRole.Hint);
        lblStatus.Text = Lang.Format("upd.downloading", 0);

        cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<int>(p =>
            {
                if (IsDisposed) return;
                bar.Value = Math.Clamp(p, 0, 100);
                lblStatus.Text = Lang.Format("upd.downloading", p);
            });
            DownloadedFile = await UpdateService.DownloadAsync(info, progress, cts.Token);
            if (!IsDisposed) DialogResult = DialogResult.OK;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            bar.Visible = false;
            lblStatus.ForeColor = UiTheme.Error;   // status colour, the same in both themes
            lblStatus.Text = Lang.Format("upd.failed", ex.Message);
            btnInstall.Enabled = true;
            btnSkip.Enabled = true;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        cts?.Cancel();
        base.OnFormClosing(e);
    }
}

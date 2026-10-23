using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Skimmer;

/// <summary>Shows what is new, downloads and verifies the update. The caller installs it when DialogResult is OK.</summary>
sealed class UpdateForm : Form
{
    readonly UpdateInfo info;
    readonly Label lblHead = new();
    readonly Label lblStatus = new();
    readonly TextBox txtNotes = new();
    readonly ProgressBar bar = new();
    readonly Button btnInstall;
    readonly Button btnLater;
    readonly Button btnSkip;
    CancellationTokenSource cts;

    public string DownloadedFile;
    public bool SkipRequested;

    public UpdateForm(UpdateInfo info)
    {
        this.info = info;

        Text = L.T("upd.title");
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 400);
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);

        lblHead.Text = L.F("upd.header", info.VersionText, UpdateService.CurrentText);
        lblHead.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        lblHead.AutoSize = false;
        lblHead.Location = new Point(14, 12);
        lblHead.Size = new Size(492, 26);
        Controls.Add(lblHead);

        txtNotes.Multiline = true;
        txtNotes.ReadOnly = true;
        txtNotes.ScrollBars = ScrollBars.Vertical;
        txtNotes.Location = new Point(14, 44);
        txtNotes.Size = new Size(492, 246);
        txtNotes.BackColor = Theme.Input;
        txtNotes.ForeColor = Theme.Text;
        txtNotes.BorderStyle = BorderStyle.FixedSingle;
        string notes = (info.Notes ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n").Trim();
        if (notes.Length > 4000) notes = notes.Substring(0, 4000) + "…";
        txtNotes.Text = notes;
        Controls.Add(txtNotes);

        lblStatus.AutoSize = false;
        lblStatus.Location = new Point(14, 298);
        lblStatus.Size = new Size(492, 36);
        lblStatus.ForeColor = Theme.TextDim;
        Controls.Add(lblStatus);

        bar.Location = new Point(14, 336);
        bar.Size = new Size(492, 14);
        bar.Minimum = 0;
        bar.Maximum = 100;
        bar.Visible = false;
        Controls.Add(bar);

        bool canInstall = UpdateService.CanSelfUpdate;
        if (!canInstall) lblStatus.Text = L.T("upd.manual");

        btnInstall = MakeButton(canInstall ? L.T("upd.install") : L.T("upd.open"), 14, 360, 200);
        btnInstall.Click += (_, _) => OnInstall(canInstall);
        btnLater = MakeButton(L.T("upd.later"), 224, 360, 100);
        btnLater.DialogResult = DialogResult.Cancel;
        btnSkip = MakeButton(L.T("upd.skip"), 334, 360, 172);
        btnSkip.Click += (_, _) =>
        {
            SkipRequested = true;
            DialogResult = DialogResult.Cancel;
        };
        Controls.AddRange(new Control[] { btnInstall, btnLater, btnSkip });
        AcceptButton = btnInstall;
        CancelButton = btnLater;
    }

    static Button MakeButton(string text, int x, int y, int w)
    {
        var b = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(w, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Theme.Text,
            UseVisualStyleBackColor = false
        };
        b.FlatAppearance.BorderColor = Theme.Border;
        return b;
    }

    async void OnInstall(bool canInstall)
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
        lblStatus.ForeColor = Theme.TextDim;
        lblStatus.Text = L.F("upd.downloading", 0);

        cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<int>(p =>
            {
                if (IsDisposed) return;
                bar.Value = Math.Clamp(p, 0, 100);
                lblStatus.Text = L.F("upd.downloading", p);
            });
            DownloadedFile = await UpdateService.DownloadAsync(info, progress, cts.Token);
            if (!IsDisposed) DialogResult = DialogResult.OK;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            bar.Visible = false;
            lblStatus.ForeColor = Color.FromArgb(255, 130, 120);
            lblStatus.Text = L.F("upd.failed", ex.Message);
            btnInstall.Enabled = true;
            btnSkip.Enabled = true;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(this);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        cts?.Cancel();
        base.OnFormClosing(e);
    }
}

using System.Media;

namespace SftpEsitle;

/// <summary>Uygulamadan önce özet; silinecek dosya varsa kırmızı uyarı ve onay kutusu.</summary>
sealed class ConfirmDialog : Form
{
    readonly bool _hasDeletes;
    readonly TableLayoutPanel _root;

    public ConfirmDialog(IReadOnlyList<SyncItem> todo, string local, string remote, bool backup)
    {
        var ups = todo.Where(i => i.Action == SyncAction.Upload).ToList();
        var downs = todo.Where(i => i.Action == SyncAction.Download).ToList();
        var dels = todo.Where(i => i.Action == SyncAction.Delete).ToList();
        var delRemote = dels.Where(i => i.DeletesRemote).ToList();
        var delLocal = dels.Where(i => i.DeletesLocal).ToList();
        int delBoth = dels.Count(i => i.DeletesRemote && i.DeletesLocal);
        _hasDeletes = dels.Count > 0;

        SuspendLayout();
        Text = _hasDeletes ? "DİKKAT — Silinecek dosyalar var" : "Onayla";
        Font = Theme.Base;
        BackColor = Theme.Bg;
        StartPosition = FormStartPosition.CenterParent;
        ForeColor = Theme.Text;
        Win11.Attach(this, dialog: true);
        MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
        FormBorderStyle = _hasDeletes ? FormBorderStyle.Sizable : FormBorderStyle.FixedDialog;
        Size = _hasDeletes ? new Size(700, 640) : new Size(560, 330);
        MinimumSize = _hasDeletes ? new Size(560, 480) : Size;
        Padding = new Padding(16);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Bg };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _root = root;

        var summary = new Label
        {
            AutoSize = true, Font = Theme.Base, ForeColor = Theme.Text, Margin = new Padding(2, 0, 0, 10),
            Text =
                $"↑  Upload   (yerel → sunucu):  {ups.Count} dosya  ·  {Fmt.Size(ups.Sum(i => i.TransferSize))}\n" +
                $"↓  Download (sunucu → yerel):  {downs.Count} dosya  ·  {Fmt.Size(downs.Sum(i => i.TransferSize))}\n" +
                $"✕  Sil:  sunucudan {delRemote.Count}, yerelden {delLocal.Count} dosya" + (delBoth > 0 ? $"  ({delBoth} tanesi iki taraftan)" : "") + "\n\n" +
                $"Üzerine yazılacak:  sunucuda {ups.Count(i => i.RemoteSize != null)}, yerelde {downs.Count(i => i.LocalSize != null)} dosya" +
                (backup ? "   (.bak yedeği alınacak)" : "") +
                $"\n\nYerel:   {local}\nSunucu:  {remote}",
        };
        root.Controls.Add(summary, 0, 0);

        var ok = new RoundButton { Text = _hasDeletes ? "Sil ve uygula" : "Uygula", Glyph = _hasDeletes ? Glyph.Delete : Glyph.Accept, Size = new Size(170, 38) };
        var cancel = new RoundButton { Text = "Vazgeç", Look = RoundButton.Kind.Secondary, Size = new Size(110, 38) };
        ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        if (_hasDeletes)
        {
            var warn = new WarnCard { Dock = DockStyle.Fill };
            var wt = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Theme.DangerSoft };
            wt.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            wt.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            wt.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            wt.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            wt.Controls.Add(new Label
            {
                AutoSize = true, ForeColor = Theme.Danger, Font = Theme.Heading, Margin = new Padding(0, 0, 0, 4),
                Text = $"⚠  {dels.Count} DOSYA SİLİNECEK",
            }, 0, 0);
            var detail = delRemote.Count > 0
                ? (backup
                    ? "Sunucudaki dosyalar silinmek yerine dosya.bak olarak yeniden adlandırılacak."
                    : "Sunucudan silinen dosyalar GERİ ALINAMAZ. Listeyi dikkatlice kontrol et.")
                : "";
            if (delLocal.Count > 0) detail += (detail.Length > 0 ? "\n" : "") + "Yerelden silinenler Geri Dönüşüm Kutusu'na gider.";
            wt.Controls.Add(new Label { AutoSize = true, ForeColor = Theme.DangerText, Font = Theme.Base, Margin = new Padding(0, 0, 0, 8), Text = detail }, 0, 1);

            var list = new ListBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Mono,
                IntegralHeight = false, SelectionMode = SelectionMode.None, BackColor = Theme.Card, ForeColor = Theme.Text,
            };
            foreach (var i in dels.Where(i => i.DeletesRemote && i.DeletesLocal)) list.Items.Add("İKİSİ   ✕  " + i.RelPath);
            foreach (var i in dels.Where(i => i.DeletesRemote && !i.DeletesLocal)) list.Items.Add("SUNUCU  ✕  " + i.RelPath);
            foreach (var i in dels.Where(i => !i.DeletesRemote)) list.Items.Add("YEREL   ✕  " + i.RelPath);
            wt.Controls.Add(list, 0, 2);

            var agree = new FluentCheck
            {
                AutoSize = true, Font = Theme.SemiBold, ForeColor = Theme.Danger, Margin = new Padding(0, 10, 0, 0),
                Text = $"Bu {dels.Count} dosyanın silineceğini anladım",
            };
            agree.CheckedChanged += (_, _) => ok.Enabled = agree.Checked;
            ok.Enabled = false;
            wt.Controls.Add(agree, 0, 3);

            warn.Controls.Add(wt);
            root.Controls.Add(warn, 0, 1);
        }

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, BackColor = Theme.Bg, Padding = new Padding(0, 12, 0, 0),
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 2);

        Controls.Add(root);
        CancelButton = cancel;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
    }

    /// <summary>DPI ölçeklemesinden sonra pencereyi içeriğe (uzun yollar, butonlar) sığacak kadar büyütür.</summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var pref = _root.GetPreferredSize(Size.Empty);
        var area = Screen.FromControl(this).WorkingArea;
        int w = Math.Min(Math.Max(ClientSize.Width, pref.Width + Padding.Horizontal), area.Width * 9 / 10);
        int h = _hasDeletes
            ? ClientSize.Height
            : Math.Min(pref.Height + Padding.Vertical, area.Height * 9 / 10);
        h = Math.Max(h, ClientSize.Height);
        if (w != ClientSize.Width || h != ClientSize.Height)
        {
            if (!_hasDeletes) MinimumSize = Size.Empty;
            ClientSize = new Size(w, h);
            if (!_hasDeletes) MinimumSize = Size;
            CenterToParent();
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_hasDeletes) SystemSounds.Exclamation.Play();
    }

    sealed class WarnCard : CardPanel
    {
        public WarnCard() { BackColor = Theme.DangerSoft; Padding = new Padding(16, 14, 16, 14); }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Bg);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float k = DeviceDpi / 96f;
            using var path = Theme.Round(new RectangleF(1, 1, Width - 2.5f, Height - 2.5f), 8 * k);
            using var b = new SolidBrush(Theme.DangerSoft);
            g.FillPath(b, path);
            using var pen = new Pen(Theme.Blend(Theme.Danger, Theme.DangerSoft, 0.6f), 1.2f * k);
            g.DrawPath(pen, path);
        }
    }
}

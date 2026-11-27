using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace SftpEsitle;

public sealed class MainForm : Form
{
    readonly AppSettings S = AppSettings.Load();
    SftpClient? client;
    List<SyncItem> items = new();
    List<SyncItem> view = new();
    CancellationTokenSource? cts;
    bool busy;
    int sortCol = 1;
    bool sortAsc = true;
    string comparedLocal = "", comparedRemote = "", pinKey = "";

    InputBox tbHost = null!, tbPort = null!, tbUser = null!, tbPass = null!, tbLocal = null!, tbRemote = null!, tbExclude = null!, tbSearch = null!;
    ComboBox cbSite = null!;
    RoundButton btnSite = null!;
    bool loadingSites;
    CheckBox cbRemember = null!, cbBackup = null!, cbOwner = null!, cbHideSame = null!, cbSkipDot = null!;
    RoundButton btnConnect = null!, btnBrowseLocal = null!, btnBrowseRemote = null!, btnCompare = null!, btnApply = null!, btnCancel = null!;
    RoundButton btnAllUp = null!, btnAllDown = null!, btnAllSkip = null!, btnSuggest = null!, btnInstall = null!, btnFiles = null!;
    FileBrowserForm? files;
    Label lblStatus = null!, lblProgress = null!;
    SummaryBar summary = null!;
    DataGridView grid = null!;
    RichTextBox log = null!;
    SlimProgress progress = null!;
    HeaderPanel header = null!;
    readonly ToolTip tip = new() { InitialDelay = 400 };

    public MainForm()
    {
        Text = $"SFTP Eşitle  v{AppInfo.Version}";
        Font = Theme.Base;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        StartPosition = FormStartPosition.CenterScreen;
        Win11.Attach(this);
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { }

        SuspendLayout();
        Size = new Size(1220, 840);
        MinimumSize = new Size(1020, 700);
        BuildUi();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
        PerformLayout();

        LoadSettings();
        UpdateConnState();
        RefreshView();
        Log("Hazır. Sunucu bilgilerini gir, klasörleri seç ve “Karşılaştır”a tıkla.", Theme.Muted);
    }

    // ───────────────────────────── Arayüz ─────────────────────────────

    static Label L(string text) => new()
    {
        Text = text, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Theme.Muted, Font = Theme.Small, Margin = new Padding(0),
    };

    static Label Title(string text) => new()
    {
        Text = text, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft,
        ForeColor = Theme.Text, Font = Theme.CardTitle, Margin = new Padding(0),
    };

    static CheckBox Check(string text, bool on) => new FluentCheck
    {
        Text = text, Checked = on, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Theme.Text,
        Font = Theme.Small, Margin = new Padding(6, 0, 10, 0),
    };

    static TableLayoutPanel Table(int cols, int rows) => new()
    {
        Dock = DockStyle.Fill, ColumnCount = cols, RowCount = rows, BackColor = Theme.Bg,
        Margin = new Padding(0), Padding = new Padding(0),
    };

    void BuildUi()
    {
        // ── İçerik ──
        var content = Table(1, 5);
        content.BackColor = Theme.Bg;
        content.Padding = new Padding(16, 14, 16, 10);
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 234));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 124));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));

        // ── Kartlar ──
        var cards = Table(2, 1);
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        cards.Controls.Add(BuildConnectionCard(), 0, 0);
        cards.Controls.Add(BuildFolderCard(), 1, 0);
        content.Controls.Add(cards, 0, 0);

        // ── Araç çubuğu ──
        var bar = Table(2, 1);
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        btnCompare = new RoundButton { Text = "Karşılaştır", Glyph = Glyph.Sync, Size = new Size(160, 38), Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 14, 0) };
        btnCompare.Click += async (_, _) => await CompareAsync();
        tip.SetToolTip(btnCompare, "İki klasörü tarar; tarih ve boyuta göre farkları listeler. Hiçbir dosya değiştirilmez.");
        summary = new SummaryBar { Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 10) };
        bar.Controls.Add(btnCompare, 0, 0);
        bar.Controls.Add(summary, 1, 0);
        content.Controls.Add(bar, 0, 1);

        content.Controls.Add(BuildGridCard(), 0, 2);
        content.Controls.Add(BuildLogCard(), 0, 3);
        content.Controls.Add(BuildFooter(), 0, 4);

        // ── Başlık ──
        header = new HeaderPanel();
        if (!Installer.IsRunningInstalled)
        {
            btnInstall = new RoundButton
            {
                Look = RoundButton.Kind.Secondary,
                Glyph = Glyph.Install,
                Text = Installer.IsInstalled ? "Kurulu sürümü güncelle" : "Bilgisayara kur",
                Size = new Size(170, 36),
            };
            btnInstall.Click += (_, _) =>
            {
                SaveSettings();
                if (Installer.RunInstallWizard(this)) Close();
            };
            tip.SetToolTip(btnInstall, "Başlat menüsüne (ve istersen masaüstüne) kısayol ekler. Yönetici yetkisi gerekmez.");
            header.Controls.Add(btnInstall);
            header.RightButtons.Add(btnInstall);
        }
        btnFiles = new RoundButton { Look = RoundButton.Kind.Secondary, Glyph = Glyph.Folder, Text = "Dosyalar", Size = new Size(120, 36) };
        btnFiles.Click += (_, _) => ShowFiles();
        tip.SetToolTip(btnFiles, "Yerel ve sunucu dosyalarını ayrı bir pencerede gösterir; dosyaları Notepad ile açar.");
        header.Controls.Add(btnFiles);
        header.RightButtons.Add(btnFiles);

        var btnTheme = new RoundButton { Look = RoundButton.Kind.Secondary, Glyph = Glyph.Palette, Text = "Tema", Size = new Size(100, 36) };
        var themeMenu = new ContextMenuStrip { Font = Theme.Base };
        foreach (var (mode, label) in new[] { ("system", "Sistem (Windows'u izle)"), ("light", "Açık"), ("dark", "Koyu") })
        {
            var mi = new ToolStripMenuItem(label) { Tag = mode };
            mi.Click += (_, _) => SetTheme(mode);
            themeMenu.Items.Add(mi);
        }
        themeMenu.Opening += (_, _) =>
        {
            foreach (ToolStripMenuItem mi in themeMenu.Items) mi.Checked = (string)mi.Tag! == S.ThemeMode;
        };
        btnTheme.Click += (_, _) => themeMenu.Show(btnTheme, new Point(0, btnTheme.Height));
        tip.SetToolTip(btnTheme, "Açık / koyu görünüm. Değişiklik uygulama yeniden başlatılınca geçerli olur.");
        header.Controls.Add(btnTheme);
        header.RightButtons.Add(btnTheme);

        Controls.Add(content);
        Controls.Add(header);
    }

    Control BuildConnectionCard()
    {
        var card = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) };
        var t = Table(4, 4);
        t.BackColor = Theme.Card;
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = Title("Sunucu bağlantısı");
        t.Controls.Add(title, 0, 0); t.SetColumnSpan(title, 2);
        lblStatus = new Label { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopRight, Font = Theme.SmallSemi, Margin = new Padding(0, 3, 0, 0) };
        t.Controls.Add(lblStatus, 2, 0); t.SetColumnSpan(lblStatus, 2);

        tbHost = new InputBox { Dock = DockStyle.Fill, Placeholder = "örn. 192.168.1.10" };
        tbPort = new InputBox { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };
        tbUser = new InputBox { Dock = DockStyle.Fill };
        tbPass = new InputBox { Dock = DockStyle.Fill, Password = true, Margin = new Padding(0, 3, 0, 3) };
        foreach (var ib in new[] { tbHost, tbPort, tbUser, tbPass })
            ib.Box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; btnConnect.PerformClick(); } };

        t.Controls.Add(L("Sunucu IP"), 0, 1); t.Controls.Add(tbHost, 1, 1);
        t.Controls.Add(L("Port"), 2, 1); t.Controls.Add(tbPort, 3, 1);
        t.Controls.Add(L("Kullanıcı"), 0, 2); t.Controls.Add(tbUser, 1, 2);
        t.Controls.Add(L("Şifre"), 2, 2); t.Controls.Add(tbPass, 3, 2);

        btnConnect = new RoundButton { Text = "Bağlan", Glyph = Glyph.Connect, Size = new Size(120, 38), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 8, 0, 4) };
        btnConnect.Click += async (_, _) =>
        {
            if (client?.IsConnected == true) Disconnect();
            else await EnsureConnectedAsync();
        };
        cbRemember = Check("Şifreyi hatırla", false);
        cbRemember.Margin = new Padding(2, 18, 0, 0);
        cbRemember.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        tip.SetToolTip(cbRemember, "Şifre bu bilgisayarda Windows hesabına özel olarak şifrelenip saklanır.");
        t.Controls.Add(cbRemember, 1, 3);
        t.Controls.Add(btnConnect, 3, 3);

        card.Controls.Add(t);
        return card;
    }

    Control BuildFolderCard()
    {
        var card = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(8, 0, 0, 0) };
        var t = Table(3, 5);
        t.BackColor = Theme.Card;
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.RowCount = 6;
        t.RowStyles.Insert(1, new RowStyle(SizeType.Absolute, 42));

        var title = Title("Klasörler");
        t.Controls.Add(title, 0, 0);
        cbSkipDot = Check("Nokta ile başlayanları atla  (.git, .codex, .agent…)", true);
        cbSkipDot.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        cbSkipDot.Margin = new Padding(0, 2, 0, 0);
        tip.SetToolTip(cbSkipDot, "Adı nokta ile başlayan tüm dosya ve klasörler listelenmez ve aktarılmaz.\nDikkat: .htaccess, .env gibi dosyalar da atlanır.");
        cbSkipDot.CheckedChanged += async (_, _) =>
        {
            if (items.Count > 0 && !busy) await CompareAsync();
        };
        t.Controls.Add(cbSkipDot, 1, 0); t.SetColumnSpan(cbSkipDot, 2);

        tbLocal = new InputBox { Dock = DockStyle.Fill, Placeholder = @"örn. D:\Projeler\site" };
        tbRemote = new InputBox { Dock = DockStyle.Fill, Placeholder = "örn. /var/www/html" };
        tbExclude = new InputBox { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };
        tip.SetToolTip(tbExclude.Box, "Boşluk veya noktalı virgülle ayır. Örnek: database vendor *.log storage/cache\nİsim her seviyede eşleşir; /database sadece en üsttekini atlar.");

        btnBrowseLocal = new RoundButton { Text = "Gözat…", Look = RoundButton.Kind.Secondary, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };
        btnBrowseRemote = new RoundButton { Text = "Gözat…", Look = RoundButton.Kind.Secondary, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };
        btnBrowseLocal.Click += (_, _) => BrowseLocal();
        btnBrowseRemote.Click += async (_, _) => await BrowseRemoteAsync();

        // ── Site seçimi ──
        cbSite = new ComboBox
        {
            Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
            Font = Theme.Input, BackColor = Theme.Control, ForeColor = Theme.Text, Margin = new Padding(0, 6, 8, 3),
        };
        cbSite.SelectedIndexChanged += (_, _) => { if (!loadingSites) SelectSite(); };
        tip.SetToolTip(cbSite, "Kayıtlı site seçince yerel/sunucu klasörü ve hariç tut listesi otomatik dolar.");
        btnSite = new RoundButton { Text = "Siteler", Glyph = Glyph.Library, Look = RoundButton.Kind.Secondary, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };
        var siteMenu = new ContextMenuStrip { Font = Theme.Base, ShowImageMargin = false };
        siteMenu.Items.Add("＋  Yeni site olarak kaydet…", null, (_, _) => NewSite());
        var miUpdate = siteMenu.Items.Add("↻  Bu siteyi şimdiki klasörlerle güncelle", null, (_, _) => UpdateSite(true));
        var miRename = siteMenu.Items.Add("✎  Yeniden adlandır…", null, (_, _) => RenameSite());
        siteMenu.Items.Add(new ToolStripSeparator());
        var miDelete = siteMenu.Items.Add("✕  Siteyi listeden kaldır", null, (_, _) => DeleteSite());
        miDelete.ForeColor = Theme.Danger;
        siteMenu.Opening += (_, _) => miUpdate.Enabled = miRename.Enabled = miDelete.Enabled = CurrentSite != null;
        btnSite.Click += (_, _) => siteMenu.Show(btnSite, new Point(0, btnSite.Height));

        t.Controls.Add(L("Site"), 0, 1); t.Controls.Add(cbSite, 1, 1); t.Controls.Add(btnSite, 2, 1);
        t.Controls.Add(L("Yerel klasör"), 0, 2); t.Controls.Add(tbLocal, 1, 2); t.Controls.Add(btnBrowseLocal, 2, 2);
        t.Controls.Add(L("Sunucu klasörü"), 0, 3); t.Controls.Add(tbRemote, 1, 3); t.Controls.Add(btnBrowseRemote, 2, 3);
        t.Controls.Add(L("Hariç tut"), 0, 4); t.Controls.Add(tbExclude, 1, 4); t.SetColumnSpan(tbExclude, 2);

        card.Controls.Add(t);
        return card;
    }

    Control BuildGridCard()
    {
        var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(12, 10, 12, 8) };
        var t = Table(1, 2);
        t.BackColor = Theme.Card;
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var strip = Table(4, 1);
        strip.BackColor = Theme.Card;
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        tbSearch = new InputBox { Dock = DockStyle.Fill, Placeholder = "Dosya ara…", Margin = new Padding(0, 4, 12, 6) };
        tbSearch.Box.TextChanged += (_, _) => RefreshView();
        cbHideSame = Check("Aynı olanları gizle", true);
        cbHideSame.CheckedChanged += (_, _) => RefreshView();

        var bulk = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Card, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
        btnAllUp = SmallBtn("Tümü upload", Theme.Up, Glyph.Upload);
        btnAllDown = SmallBtn("Tümü download", Theme.Down, Glyph.Download);
        btnAllSkip = SmallBtn("Hepsini atla", null, Glyph.Remove);
        btnSuggest = SmallBtn("Önerilen", null, Glyph.Undo);
        tip.SetToolTip(btnAllUp, "Listede görünen ve yerelde bulunan tüm farklı dosyaları Upload yapar.");
        tip.SetToolTip(btnAllDown, "Listede görünen ve sunucuda bulunan tüm farklı dosyaları Download yapar.");
        tip.SetToolTip(btnSuggest, "Her dosya için önerilen işleme döner (yeni olan kazanır).");
        btnAllUp.Click += (_, _) => SetAction(view.Where(i => i.Status != SyncStatus.Same), SyncAction.Upload);
        btnAllDown.Click += (_, _) => SetAction(view.Where(i => i.Status != SyncStatus.Same), SyncAction.Download);
        btnAllSkip.Click += (_, _) => SetAction(view, SyncAction.Skip);
        btnSuggest.Click += (_, _) =>
        {
            foreach (var i in view.Where(i => i.Action != SyncAction.AlwaysSkip)) i.Action = i.Suggested;
            grid.Invalidate(); UpdateSummary();
        };
        tip.SetToolTip(btnAllSkip, "“Sürekli atla” olanlar değişmez.");
        bulk.Controls.AddRange(new Control[] { btnAllUp, btnAllDown, btnAllSkip, btnSuggest });

        strip.Controls.Add(tbSearch, 0, 0);
        strip.Controls.Add(cbHideSame, 1, 0);
        strip.Controls.Add(bulk, 3, 0);
        t.Controls.Add(strip, 0, 0);

        grid = BuildGrid();
        t.Controls.Add(grid, 0, 1);
        card.Controls.Add(t);
        return card;
    }

    RoundButton SmallBtn(string text, Color? color, string? glyph = null)
    {
        var b = new RoundButton { Text = text, Glyph = glyph, Look = RoundButton.Kind.Secondary, TextColorOverride = color, Size = new Size(120, 32), Margin = new Padding(6, 6, 0, 6) };
        b.FitText();
        return b;
    }

    DataGridView BuildGrid()
    {
        var g = new DataGridView
        {
            Dock = DockStyle.Fill,
            VirtualMode = true,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
            BorderStyle = BorderStyle.None,
            BackgroundColor = Theme.Card,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = Theme.Divider,
            EnableHeadersVisualStyles = false,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ShowCellToolTips = true,
            StandardTab = true,
            Margin = new Padding(0, 2, 0, 0),
        };
        typeof(DataGridView).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(g, true);

        g.DefaultCellStyle = new DataGridViewCellStyle
        {
            Font = Theme.Base, ForeColor = Theme.Text, BackColor = Theme.Card,
            SelectionBackColor = Theme.AccentSoft, SelectionForeColor = Theme.Text,
            Padding = new Padding(8, 0, 8, 0), WrapMode = DataGridViewTriState.False,
        };
        g.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            Font = Theme.SmallSemi, ForeColor = Theme.Muted, BackColor = Theme.Card,
            SelectionBackColor = Theme.Card, SelectionForeColor = Theme.Muted,
            Padding = new Padding(8, 0, 8, 0), Alignment = DataGridViewContentAlignment.MiddleLeft,
        };

        DataGridViewTextBoxColumn Col(string name, int width, bool fill = false) => new()
        {
            HeaderText = name, Width = width, SortMode = DataGridViewColumnSortMode.Programmatic,
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
            MinimumWidth = 60,
        };
        g.Columns.AddRange(Col("DURUM", 160), Col("DOSYA", 300, true), Col("YEREL  (boyut · tarih)", 215), Col("SUNUCU  (boyut · tarih)", 215), Col("İŞLEM", 140));

        g.CellValueNeeded += (_, e) =>
        {
            if (e.RowIndex >= view.Count) return;
            var it = view[e.RowIndex];
            e.Value = e.ColumnIndex switch
            {
                2 => Fmt.Side(it.LocalSize, it.LocalTime),
                3 => Fmt.Side(it.RemoteSize, it.RemoteTime),
                _ => "",
            };
        };
        g.CellPainting += Grid_CellPainting;
        g.CellFormatting += (_, e) =>
        {
            if (e.RowIndex >= view.Count || e.CellStyle == null) return;
            var it = view[e.RowIndex];
            if (e.ColumnIndex is 2 or 3)
            {
                bool newer = (e.ColumnIndex == 2 && it.Status == SyncStatus.LocalNewer) || (e.ColumnIndex == 3 && it.Status == SyncStatus.RemoteNewer);
                bool missing = e.ColumnIndex == 2 ? it.LocalSize == null : it.RemoteSize == null;
                e.CellStyle.ForeColor = missing ? Theme.Faint : newer ? Theme.Text : Theme.Muted;
                e.CellStyle.Font = newer ? Theme.SemiBold : Theme.Base;
            }
        };
        g.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= view.Count) return;
            var it = view[e.RowIndex];
            e.ToolTipText = it.Error != null ? "Hata: " + it.Error
                : e.ColumnIndex == 4 ? "Tıkla: işlemi değiştir\nU = upload · D = download · Del = sil · S = atla · A = sürekli atla\nSağ tık: menü"
                : it.RelPath;
        };
        g.CellMouseClick += (_, e) =>
        {
            if (busy || e.RowIndex < 0 || e.RowIndex >= view.Count || e.Button != MouseButtons.Left || e.ColumnIndex != 4) return;
            Cycle(view[e.RowIndex]);
        };
        g.CellMouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && e.RowIndex < view.Count && !g.Rows[e.RowIndex].Selected)
            {
                g.ClearSelection();
                g.Rows[e.RowIndex].Selected = true;
                g.CurrentCell = g.Rows[e.RowIndex].Cells[1];
            }
        };
        g.ColumnHeaderMouseClick += (_, e) =>
        {
            if (sortCol == e.ColumnIndex) sortAsc = !sortAsc; else { sortCol = e.ColumnIndex; sortAsc = true; }
            RefreshView();
        };
        g.KeyDown += (_, e) =>
        {
            if (busy) return;
            var sel = Selected().ToList();
            if (sel.Count == 0) return;
            switch (e.KeyCode)
            {
                case Keys.U: SetAction(sel, SyncAction.Upload, true); e.Handled = true; break;
                case Keys.D: SetAction(sel, SyncAction.Download, true); e.Handled = true; break;
                case Keys.Delete: SetAction(sel, SyncAction.Delete, true); e.Handled = true; break;
                case Keys.S: SetAction(sel, SyncAction.Skip, true); e.Handled = true; break;
                case Keys.A when !e.Control: SetAction(sel, SyncAction.AlwaysSkip, true); e.Handled = true; break;
                case Keys.Space: foreach (var i in sel) Cycle(i); e.Handled = true; break;
            }
        };
        g.Paint += (_, e) =>
        {
            if (view.Count > 0) return;
            string msg = items.Count == 0
                ? "Bağlan, klasörleri seç ve “Karşılaştır”a tıkla.\nHiçbir dosya sen “Uygula” demeden değişmez."
                : (cbHideSame.Checked && tbSearch.Text.Length == 0 ? "Farklı dosya yok — iki klasör eşit ✓" : "Bu filtreye uyan dosya yok.");
            var r = g.ClientRectangle;
            r.Y += g.ColumnHeadersHeight; r.Height -= g.ColumnHeadersHeight;
            TextRenderer.DrawText(e.Graphics, msg, Theme.Input, r, Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        };

        var menu = new ContextMenuStrip { Font = Theme.Base, ShowImageMargin = false };
        ToolStripMenuItem Mi(string text, string keys, SyncAction a, Color? color = null)
        {
            var mi = new ToolStripMenuItem(text, null, (_, _) => SetAction(Selected(), a, true)) { ShortcutKeyDisplayString = keys };
            if (color is Color c) mi.ForeColor = c;
            return mi;
        }
        menu.Items.Add(Mi("↑  Upload (sunucuya gönder)", "U", SyncAction.Upload));
        menu.Items.Add(Mi("↓  Download (yerele indir)", "D", SyncAction.Download));
        menu.Items.Add(Mi("✕  Sil (bulunduğu her yerden)", "Del", SyncAction.Delete, Theme.Danger));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Mi("–  Atla (bu sefer)", "S", SyncAction.Skip));
        menu.Items.Add(Mi("■  Sürekli atla (hep hatırla)", "A", SyncAction.AlwaysSkip));
        menu.Items.Add(new ToolStripSeparator());
        var miOpenLocal = new ToolStripMenuItem("Notepad ile aç (yerel)", null, (_, _) => OpenInNotepadLocal());
        var miOpenRemote = new ToolStripMenuItem("Notepad ile aç (sunucu)", null, async (_, _) => await OpenInNotepadRemote());
        menu.Items.Add(miOpenLocal);
        menu.Items.Add(miOpenRemote);
        menu.Items.Add("Yerel dosyayı klasörde göster", null, (_, _) => RevealLocal());
        menu.Opening += (_, e) =>
        {
            if (busy || !Selected().Any()) { e.Cancel = true; return; }
            var it = Selected().First();
            miOpenLocal.Enabled = it.LocalSize != null;
            miOpenRemote.Enabled = it.RemoteSize != null;
        };
        g.ContextMenuStrip = menu;
        return g;
    }

    Control BuildLogCard()
    {
        var card = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0), Padding = new Padding(14, 8, 10, 8) };
        log = new RichTextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Text,
            Font = Theme.Mono, DetectUrls = false, ScrollBars = RichTextBoxScrollBars.Vertical,
        };
        var title = new Label { Text = "GÜNLÜK", Dock = DockStyle.Top, Height = 20, Font = Theme.SmallSemi, ForeColor = Theme.Muted };
        card.Controls.Add(log);
        card.Controls.Add(title);
        return card;
    }

    Control BuildFooter()
    {
        var t = Table(5, 1);
        t.BackColor = Theme.Bg;
        t.Padding = new Padding(2, 6, 0, 0);
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var prog = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Margin = new Padding(0, 0, 20, 0) };
        progress = new SlimProgress { Dock = DockStyle.Top, Height = 6 };
        lblProgress = new Label { Dock = DockStyle.Top, Height = 26, Font = Theme.Small, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        prog.Controls.Add(progress);
        prog.Controls.Add(lblProgress);

        cbBackup = Check("Üzerine yazmadan önce .bak yedeği al", false);
        cbOwner = Check("Yeni dosyalara klasörün sahibini ver", true);
        tip.SetToolTip(cbOwner, "Sunucuda yeni oluşan dosya/klasörler root yerine üst klasörün sahibine (örn. www-data) ait olur.");
        tip.SetToolTip(cbBackup, "Değişen dosyanın eski hâli aynı yere dosya.bak olarak saklanır.");

        btnCancel = new RoundButton { Text = "Durdur", Glyph = Glyph.Stop, Look = RoundButton.Kind.Secondary, TextColorOverride = Theme.Danger, Size = new Size(110, 40), Anchor = AnchorStyles.None, Margin = new Padding(8, 0, 8, 0), Enabled = false };
        btnCancel.Click += (_, _) =>
        {
            cts?.Cancel();
            lblProgress.Text = "Durduruluyor… (aktarılan dosya bitince)";
        };
        btnApply = new RoundButton { Text = "Uygula", Glyph = Glyph.Accept, Size = new Size(170, 40), Anchor = AnchorStyles.None, Margin = new Padding(0) };
        btnApply.Click += async (_, _) => await ApplyAsync();

        t.Controls.Add(prog, 0, 0);
        t.Controls.Add(cbBackup, 1, 0);
        t.Controls.Add(cbOwner, 2, 0);
        t.Controls.Add(btnCancel, 3, 0);
        t.Controls.Add(btnApply, 4, 0);
        return t;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        float k = DeviceDpi / 96f;
        grid.RowTemplate.Height = (int)(36 * k);
        grid.ColumnHeadersHeight = (int)(36 * k);
        int[] widths = { 150, 300, 220, 220, 175 };
        for (int i = 0; i < widths.Length; i++)
            if (grid.Columns[i].AutoSizeMode != DataGridViewAutoSizeColumnMode.Fill) grid.Columns[i].Width = (int)(widths[i] * k);
        foreach (var b in new[] { btnAllUp, btnAllDown, btnAllSkip, btnSuggest, btnFiles }) b.FitText();
        btnInstall?.FitText();
        header.PerformLayout();
        RefreshView();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Ekrana sığdır ve ortala (DPI ölçeklemesinden sonra)
        var wa = Screen.FromControl(this).WorkingArea;
        Size = new Size(Math.Min(Width, (int)(wa.Width * 0.94)), Math.Min(Height, (int)(wa.Height * 0.94)));
        Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Y + (wa.Height - Height) / 2);
    }

    // ───────────────────────────── Izgara çizimi ─────────────────────────────

    static (string text, Color fore, Color back) StatusStyle(SyncItem i) =>
        i.Error != null ? ("Hata", Theme.Danger, Theme.DangerSoft) : i.Status switch
        {
            SyncStatus.Same => ("Aynı", Theme.Ok, Theme.OkSoft),
            SyncStatus.LocalOnly => ("Sadece yerelde", Theme.Up, Theme.UpSoft),
            SyncStatus.LocalNewer => ("Yerel daha yeni", Theme.Up, Theme.UpSoft),
            SyncStatus.RemoteOnly => ("Sadece sunucuda", Theme.Down, Theme.DownSoft),
            SyncStatus.RemoteNewer => ("Sunucu daha yeni", Theme.Down, Theme.DownSoft),
            _ => ("Boyut farklı", Theme.Warn, Theme.WarnSoft),
        };

    void Grid_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= view.Count || e.ColumnIndex is 2 or 3) return;
        var it = view[e.RowIndex];
        var g = e.Graphics!;
        float k = DeviceDpi / 96f;
        e.PaintBackground(e.CellBounds, true);
        var b = e.CellBounds;
        int pad = (int)(8 * k), cy = b.Y + b.Height / 2;

        switch (e.ColumnIndex)
        {
            case 0:
            {
                var (text, fore, back) = StatusStyle(it);
                Theme.Pill(g, b.X + pad, cy, text, fore, back, Theme.SmallSemi, k);
                break;
            }
            case 1:
            {
                var rel = it.RelPath;
                int slash = rel.LastIndexOf('/');
                string dir = slash >= 0 ? rel[..(slash + 1)] : "", name = slash >= 0 ? rel[(slash + 1)..] : rel;
                var rect = new Rectangle(b.X + pad, b.Y, b.Width - pad * 2, b.Height);
                const TextFormatFlags f = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
                int dw = dir.Length > 0 ? TextRenderer.MeasureText(g, dir, Theme.Base, Size.Empty, f).Width : 0;
                int nw = TextRenderer.MeasureText(g, name, Theme.SemiBold, Size.Empty, f).Width;
                if (dw + nw <= rect.Width)
                {
                    if (dw > 0) TextRenderer.DrawText(g, dir, Theme.Base, rect, Theme.Muted, f);
                    TextRenderer.DrawText(g, name, Theme.SemiBold, new Rectangle(rect.X + dw, rect.Y, rect.Width - dw, rect.Height), Theme.Text, f);
                }
                else
                    TextRenderer.DrawText(g, rel, Theme.Base, rect, Theme.Text, f | TextFormatFlags.PathEllipsis);
                break;
            }
            case 4:
            {
                if (it.Status == SyncStatus.Same && it.Action == SyncAction.Skip)
                {
                    TextRenderer.DrawText(g, "—", Theme.Base, new Rectangle(b.X + pad * 2, b.Y, b.Width, b.Height), Theme.Faint,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    break;
                }
                var (text, fore, back) = it.Action switch
                {
                    SyncAction.Upload => ("↑  Upload", Theme.OnAccent, Theme.Up),
                    SyncAction.Download => ("↓  Download", Theme.OnAccent, Theme.Down),
                    SyncAction.Delete => ($"✕  {it.DeleteWhere} sil", Theme.OnAccent, Theme.Danger),
                    SyncAction.AlwaysSkip => ("■  Sürekli atla", Theme.Text, Theme.Blend(Theme.Text, Theme.Card, 0.14f)),
                    _ => ("–  Atla", Theme.Skip, Theme.SkipSoft),
                };
                int w = Theme.Pill(g, b.X + pad, cy, text, fore, back, Theme.SmallSemi, k);
                if (it.Action != it.Suggested && it.Action != SyncAction.AlwaysSkip)
                {
                    using var dot = new SolidBrush(Theme.Warn);
                    int d = (int)(6 * k);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.FillEllipse(dot, b.X + pad + w + (int)(6 * k), cy - d / 2, d, d);
                }
                break;
            }
        }
        e.Handled = true;
    }

    // ───────────────────────────── Liste ─────────────────────────────

    static int StatusOrder(SyncStatus s) => s switch
    {
        SyncStatus.LocalOnly => 0, SyncStatus.LocalNewer => 1, SyncStatus.RemoteOnly => 2,
        SyncStatus.RemoteNewer => 3, SyncStatus.SizeDiffers => 4, _ => 5,
    };

    void RefreshView()
    {
        if (grid == null) return;
        IEnumerable<SyncItem> v = items;
        if (cbHideSame.Checked) v = v.Where(i => i.Status != SyncStatus.Same || i.Error != null);
        var q = tbSearch.Text.Trim();
        if (q.Length > 0) v = v.Where(i => i.RelPath.Contains(q, StringComparison.OrdinalIgnoreCase));

        var cmp = StringComparer.OrdinalIgnoreCase;
        IOrderedEnumerable<SyncItem> o = sortCol switch
        {
            0 => sortAsc ? v.OrderBy(i => i.Error != null ? -1 : StatusOrder(i.Status)) : v.OrderByDescending(i => i.Error != null ? -1 : StatusOrder(i.Status)),
            2 => sortAsc ? v.OrderBy(i => i.LocalTime ?? DateTime.MinValue) : v.OrderByDescending(i => i.LocalTime ?? DateTime.MinValue),
            3 => sortAsc ? v.OrderBy(i => i.RemoteTime ?? DateTime.MinValue) : v.OrderByDescending(i => i.RemoteTime ?? DateTime.MinValue),
            4 => sortAsc ? v.OrderBy(i => (int)i.Action) : v.OrderByDescending(i => (int)i.Action),
            _ => sortAsc ? v.OrderBy(i => i.RelPath, cmp) : v.OrderByDescending(i => i.RelPath, cmp),
        };
        view = (sortCol == 1 ? o : o.ThenBy(i => i.RelPath, cmp)).ToList();

        for (int c = 0; c < grid.Columns.Count; c++)
            grid.Columns[c].HeaderCell.SortGlyphDirection = c == sortCol ? (sortAsc ? SortOrder.Ascending : SortOrder.Descending) : SortOrder.None;

        grid.RowCount = 0;
        grid.RowCount = view.Count;
        grid.ClearSelection();
        grid.Invalidate();
        UpdateSummary();
    }

    void UpdateSummary()
    {
        summary.Up = items.Count(i => i.Action == SyncAction.Upload);
        summary.Down = items.Count(i => i.Action == SyncAction.Download);
        summary.Del = items.Count(i => i.Action == SyncAction.Delete);
        summary.Skip = items.Count(i => i.Action is SyncAction.Skip or SyncAction.AlwaysSkip && i.Status != SyncStatus.Same);
        summary.Same = items.Count(i => i.Status == SyncStatus.Same);
        summary.Errors = items.Count(i => i.Error != null);
        summary.UpBytes = items.Where(i => i.Action == SyncAction.Upload).Sum(i => i.TransferSize);
        summary.DownBytes = items.Where(i => i.Action == SyncAction.Download).Sum(i => i.TransferSize);
        summary.Invalidate();
        int n = summary.Up + summary.Down + summary.Del;
        btnApply.Text = n > 0 ? $"Uygula  ({n})" : "Uygula";
        btnApply.Enabled = !busy && n > 0;
    }

    IEnumerable<SyncItem> Selected() =>
        grid.SelectedRows.Cast<DataGridViewRow>().Where(r => r.Index < view.Count).Select(r => view[r.Index]);

    /// <param name="explicitChoice">true: kullanıcı seçili satırlar için seçti ("sürekli atla" da değişir).
    /// false: toplu buton; "sürekli atla" olanlara dokunulmaz.</param>
    void SetAction(IEnumerable<SyncItem> list, SyncAction a, bool explicitChoice = false)
    {
        if (busy) return;
        foreach (var i in list)
        {
            if (!i.Allows(a)) continue;
            if (!explicitChoice && i.Action == SyncAction.AlwaysSkip) continue;
            i.Action = a;
        }
        grid.Invalidate();
        UpdateSummary();
        SavePins();
    }

    void Cycle(SyncItem i)
    {
        var allowed = new[] { SyncAction.Upload, SyncAction.Download, SyncAction.Delete, SyncAction.Skip, SyncAction.AlwaysSkip }
            .Where(i.Allows).ToList();
        i.Action = allowed[(allowed.IndexOf(i.Action) + 1) % allowed.Count];
        grid.Invalidate();
        UpdateSummary();
        SavePins();
    }

    /// <summary>"Sürekli atla" seçimlerini bu sunucu+klasör çifti için kalıcı olarak kaydeder.</summary>
    void SavePins()
    {
        if (pinKey.Length == 0) return;
        var set = new HashSet<string>(S.AlwaysSkip.GetValueOrDefault(pinKey) ?? new(), StringComparer.OrdinalIgnoreCase);
        foreach (var i in items)
        {
            if (i.Action == SyncAction.AlwaysSkip) set.Add(i.RelPath);
            else set.Remove(i.RelPath);
        }
        if (set.Count == 0) S.AlwaysSkip.Remove(pinKey);
        else S.AlwaysSkip[pinKey] = set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        S.Save();
    }

    void RevealLocal()
    {
        var it = Selected().FirstOrDefault();
        if (it == null || comparedLocal.Length == 0) return;
        var p = SyncEngine.LocalJoin(comparedLocal, it.LocalRel ?? it.RelPath);
        if (File.Exists(p)) Process.Start("explorer.exe", $"/select,\"{p}\"");
        else if (Directory.Exists(Path.GetDirectoryName(p))) Process.Start("explorer.exe", $"\"{Path.GetDirectoryName(p)}\"");
    }

    // ───────────────────────────── Dosyalar penceresi ─────────────────────────────

    FileBrowserForm ShowFiles()
    {
        if (files == null || files.IsDisposed)
        {
            files = new FileBrowserForm(tbLocal.Text.Trim(), tbRemote.Text.Trim(), CreateExtraClientAsync)
            {
                StartPosition = FormStartPosition.Manual,
                Location = Location,
                CenterOn = Bounds,
            };
        }
        files.Reveal();
        return files;
    }

    void OpenInNotepadLocal()
    {
        var it = Selected().FirstOrDefault();
        if (it == null || comparedLocal.Length == 0) return;
        ShowFiles().OpenLocalFile(SyncEngine.LocalJoin(comparedLocal, it.LocalRel ?? it.RelPath));
    }

    async Task OpenInNotepadRemote()
    {
        var it = Selected().FirstOrDefault();
        if (it == null || comparedRemote.Length == 0) return;
        await ShowFiles().OpenRemoteFile(SyncEngine.RemoteJoin(comparedRemote, it.RemoteRel ?? it.RelPath));
    }

    /// <summary>Dosyalar penceresi için ayrı bir SFTP bağlantısı açar (eşitlemeyle çakışmasın diye).</summary>
    async Task<SftpClient?> CreateExtraClientAsync()
    {
        if (!await EnsureConnectedAsync()) return null;
        var ci = client!.ConnectionInfo;
        string host = ci.Host, user = ci.Username, pass = tbPass.Text;
        int port = ci.Port;
        try { return await Task.Run(() => NewClient(host, port, user, pass)); }
        catch (Exception ex)
        {
            MessageBox.Show(files is { Visible: true } f ? f : this, Friendly(ex), "Bağlanılamadı", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
    }

    SftpClient NewClient(string host, int port, string user, string pass)
    {
        var kb = new KeyboardInteractiveAuthenticationMethod(user);
        kb.AuthenticationPrompt += (_, e) => { foreach (var p in e.Prompts) p.Response = pass; };
        var ci = new ConnectionInfo(host, port, user, new PasswordAuthenticationMethod(user, pass), kb)
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        var c = new SftpClient(ci) { KeepAliveInterval = TimeSpan.FromSeconds(30), OperationTimeout = TimeSpan.FromMinutes(2) };
        c.HostKeyReceived += (_, e) => e.CanTrust = (bool)Invoke(new Func<bool>(() => TrustHostKey(host, port, e.FingerPrintSHA256)));
        try { c.Connect(); }
        catch { c.Dispose(); throw; }
        return c;
    }

    // ───────────────────────────── Bağlantı ─────────────────────────────

    void UpdateConnState()
    {
        bool on = client?.IsConnected == true;
        lblStatus.Text = on ? $"●  Bağlı: {client!.ConnectionInfo.Username}@{client.ConnectionInfo.Host}" : "●  Bağlı değil";
        lblStatus.ForeColor = on ? Theme.Ok : Theme.Muted;
        btnConnect.Text = on ? "Bağlantıyı kes" : "Bağlan";
        btnConnect.Glyph = on ? Glyph.Disconnect : Glyph.Connect;
        btnConnect.Look = on ? RoundButton.Kind.Secondary : RoundButton.Kind.Primary;
        btnConnect.Invalidate();
        foreach (var c in new Control[] { tbHost, tbPort, tbUser, tbPass }) c.Enabled = !on && !busy;
    }

    void Disconnect()
    {
        try { client?.Disconnect(); } catch { }
        client?.Dispose();
        client = null;
        Log("Bağlantı kapatıldı.", Theme.Muted);
        UpdateConnState();
    }

    async Task<bool> EnsureConnectedAsync()
    {
        if (client?.IsConnected == true) return true;
        if (client != null) { client.Dispose(); client = null; }

        string host = tbHost.Text.Trim(), user = tbUser.Text.Trim(), pass = tbPass.Text;
        if (host.Length == 0) { Warn("Sunucu IP adresini gir."); tbHost.Box.Focus(); return false; }
        if (!int.TryParse(tbPort.Text.Trim(), out int port) || port is < 1 or > 65535) { Warn("Port geçersiz."); tbPort.Box.Focus(); return false; }
        if (user.Length == 0) user = "root";
        if (pass.Length == 0) { Warn("Şifreyi gir."); tbPass.Box.Focus(); return false; }

        SetBusy(true, $"{host}:{port} adresine bağlanılıyor…");
        Log($"Bağlanılıyor: {user}@{host}:{port}");
        try
        {
            client = await Task.Run(() => NewClient(host, port, user, pass));
            Log($"Bağlandı ✓  ({client.ConnectionInfo.ServerVersion})", Theme.Ok);
            SaveSettings();
            return true;
        }
        catch (Exception ex)
        {
            client = null;
            var msg = Friendly(ex);
            Log("Bağlanılamadı: " + msg, Theme.Danger);
            MessageBox.Show(this, msg, "Bağlanılamadı", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            SetBusy(false);
            UpdateConnState();
        }
    }

    bool TrustHostKey(string host, int port, string fp)
    {
        var key = $"{host}:{port}";
        if (S.KnownHosts.TryGetValue(key, out var known))
        {
            if (known == fp) return true;
            var r = MessageBox.Show(this,
                "⚠  DİKKAT: Sunucunun güvenlik anahtarı DEĞİŞMİŞ!\n\n" +
                $"Sunucu: {key}\nKayıtlı:  SHA256:{known}\nŞimdiki: SHA256:{fp}\n\n" +
                "Sunucu yeniden kurulduysa bu normaldir. Değilse araya biri girmiş olabilir.\n\nYeni anahtara güvenip devam edilsin mi?",
                "Sunucu anahtarı değişti", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) return false;
        }
        else
        {
            var r = MessageBox.Show(this,
                $"Bu sunucuya ilk kez bağlanılıyor.\n\nSunucu: {key}\nAnahtar parmak izi:\nSHA256:{fp}\n\n" +
                "Bu sunucuya güveniyor musun? (Anahtar kaydedilir, değişirse uyarılırsın.)",
                "Yeni sunucu", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return false;
        }
        S.KnownHosts[key] = fp;
        S.Save();
        return true;
    }

    static string Friendly(Exception ex) => ex switch
    {
        SshAuthenticationException => "Kullanıcı adı veya şifre hatalı.\n\n(Sunucuda root ile şifreli giriş kapalı olabilir: sshd_config → PermitRootLogin)",
        SshConnectionException { DisconnectReason: Renci.SshNet.Messages.Transport.DisconnectReason.HostKeyNotVerifiable } =>"Sunucu anahtarı onaylanmadı, bağlantı iptal edildi.",
        SshOperationTimeoutException => "Zaman aşımı. IP ve port doğru mu, sunucu erişilebilir mi?",
        SocketException { SocketErrorCode: SocketError.ConnectionRefused } => "Bağlantı reddedildi. Port doğru mu? SSH servisi bu portta çalışıyor mu?",
        SocketException { SocketErrorCode: SocketError.HostNotFound } => "Sunucu bulunamadı. Adres doğru mu?",
        SocketException { SocketErrorCode: SocketError.TimedOut } => "Zaman aşımı. IP/port doğru mu, güvenlik duvarı engelliyor olabilir mi?",
        SocketException se => "Ağ hatası: " + se.Message,
        SftpPermissionDeniedException => "İzin reddedildi: " + ex.Message,
        SftpPathNotFoundException => "Yol bulunamadı: " + ex.Message,
        SshConnectionException => "Bağlantı koptu: " + ex.Message,
        _ => ex.Message,
    };

    // ───────────────────────────── Klasör seçimi ─────────────────────────────

    void BrowseLocal()
    {
        using var d = new FolderBrowserDialog
        {
            Description = "Eşitlenecek yerel klasörü seç",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };
        if (Directory.Exists(tbLocal.Text)) d.InitialDirectory = tbLocal.Text;
        if (d.ShowDialog(this) == DialogResult.OK) tbLocal.Text = d.SelectedPath;
    }

    async Task BrowseRemoteAsync()
    {
        if (busy || !await EnsureConnectedAsync()) return;
        using var d = new RemoteFolderDialog(client!, tbRemote.Text);
        if (d.ShowDialog(this) == DialogResult.OK) tbRemote.Text = d.SelectedPath;
    }

    // ───────────────────────────── Karşılaştır ─────────────────────────────

    async Task CompareAsync()
    {
        if (busy) return;
        var local = tbLocal.Text.Trim();
        if (local.Length == 0 || !Directory.Exists(local)) { Warn("Geçerli bir yerel klasör seç."); return; }
        if (tbRemote.Text.Trim().Length == 0) { Warn("Sunucu klasörünü yaz veya seç."); return; }
        var remote = SyncEngine.NormalizeRemote(tbRemote.Text);
        tbRemote.Text = remote;
        if (!await EnsureConnectedAsync()) return;
        var c = client!;

        // Sunucu klasörü var mı?
        bool exists;
        try { exists = await Task.Run(() => c.Exists(remote) && c.GetAttributes(remote).IsDirectory); }
        catch (Exception ex) { Fail(ex); return; }
        if (!exists)
        {
            if (MessageBox.Show(this, $"Sunucuda bu klasör yok:\n{remote}\n\nOluşturulsun mu?", "Klasör yok",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                await Task.Run(() =>
                {
                    var path = "";
                    foreach (var seg in remote.Split('/', StringSplitOptions.RemoveEmptyEntries))
                    {
                        path += "/" + seg;
                        if (!c.Exists(path)) c.CreateDirectory(path);
                    }
                });
                Log("Sunucuda klasör oluşturuldu: " + remote, Theme.Ok);
            }
            catch (Exception ex) { Fail(ex); return; }
        }

        UpdateSite(false);
        SaveSettings();
        cts = new CancellationTokenSource();
        SetBusy(true, "Karşılaştırılıyor…");
        Log($"Karşılaştırılıyor:  {local}   ⇄   {remote}");
        var sw = Stopwatch.StartNew();
        var warnings = new List<string>();
        var prog = new Progress<string>(s => lblProgress.Text = s);
        try
        {
            var ex = new ExcludeMatcher(tbExclude.Text, cbSkipDot.Checked);
            var token = cts.Token;
            items = await Task.Run(() => SyncEngine.Compare(c, local, remote, ex, prog, warnings, token));
            comparedLocal = local; comparedRemote = remote;
            pinKey = $"{tbHost.Text.Trim()}:{tbPort.Text.Trim()}|{remote}|{Path.GetFullPath(local).TrimEnd('\\').ToLowerInvariant()}";
            int pinned = 0;
            if (S.AlwaysSkip.TryGetValue(pinKey, out var pins))
            {
                var set = new HashSet<string>(pins, StringComparer.OrdinalIgnoreCase);
                foreach (var i in items.Where(i => set.Contains(i.RelPath))) { i.Action = SyncAction.AlwaysSkip; pinned++; }
            }
            if (pinned > 0) Log($"{pinned} dosya “sürekli atla” olarak işaretli.", Theme.Muted);
            RefreshView();
            foreach (var w in warnings) Log(w, Theme.Warn);
            int diff = items.Count(i => i.Status != SyncStatus.Same);
            Log($"Karşılaştırma bitti ({sw.Elapsed.TotalSeconds:0.0} sn): {items.Count} dosya, {diff} farklı, {items.Count - diff} aynı.", Theme.Ok);
            lblProgress.Text = diff == 0 ? "İki klasör eşit ✓" : $"{diff} farklı dosya bulundu. İşlemleri kontrol et, sonra “Uygula”ya bas.";
        }
        catch (OperationCanceledException) { Log("Karşılaştırma durduruldu.", Theme.Warn); lblProgress.Text = "Durduruldu."; }
        catch (Exception e) { Fail(e); }
        finally { SetBusy(false); UpdateConnState(); }
    }

    // ───────────────────────────── Uygula ─────────────────────────────

    async Task ApplyAsync()
    {
        if (busy) return;
        var todo = items.Where(i => i.IsTransfer).ToList();
        if (todo.Count == 0) { Warn("Uygulanacak işlem yok. Listeden dosyalar için Upload / Download / Sil seç."); return; }

        using (var dlg = new ConfirmDialog(todo, comparedLocal, comparedRemote, cbBackup.Checked))
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

        if (!await EnsureConnectedAsync()) return;
        var c = client!;
        cts = new CancellationTokenSource();
        SetBusy(true, "Aktarılıyor…");
        Log($"Başladı: {todo.Count(i => i.Action == SyncAction.Upload)} upload, {todo.Count(i => i.Action == SyncAction.Download)} download, {todo.Count(i => i.Action == SyncAction.Delete)} silme.");
        var sw = Stopwatch.StartNew();
        var prog = new Progress<ApplyProgress>(p =>
        {
            double v = p.BytesTotal > 0 ? (double)p.BytesDone / p.BytesTotal : (double)p.Index / Math.Max(1, p.Total);
            progress.Value = v;
            var speed = sw.Elapsed.TotalSeconds > 0.5 ? $"  ·  {Fmt.Size((long)(p.BytesDone / sw.Elapsed.TotalSeconds))}/sn" : "";
            if (cts?.IsCancellationRequested != true)
                lblProgress.Text = p.Index < p.Total ? $"{p.Index + 1} / {p.Total}   {p.File}{speed}" : "Tamamlanıyor…";
        });
        try
        {
            var opt = new ApplyOptions(cbBackup.Checked, cbOwner.Checked);
            var token = cts.Token;
            string lroot = comparedLocal, rroot = comparedRemote;
            var results = await Task.Run(() => SyncEngine.Apply(c, lroot, rroot, todo, opt, prog, token));

            int ok = 0, fail = 0;
            foreach (var r in results)
            {
                var it = r.Item;
                if (r.Error == null && r.Deleted)
                {
                    ok++;
                    items.Remove(it);
                    var parts = new List<string>();
                    if (it.DeletesRemote) parts.Add(cbBackup.Checked ? "sunucuda .bak yapıldı" : "sunucudan silindi");
                    if (it.DeletesLocal) parts.Add("yerel Geri Dönüşüm Kutusu'na gönderildi");
                    Log($"✕  {it.RelPath}  ({string.Join(", ", parts)})", Theme.Danger);
                }
                else if (r.Error == null)
                {
                    ok++;
                    var action = it.Action;
                    if (action == SyncAction.Upload) { it.RemoteSize = r.NewSize; it.RemoteTime = r.NewTime; it.LocalTime = r.NewTime; it.RemoteRel ??= it.LocalRel; }
                    else { it.LocalSize = r.NewSize; it.LocalTime = r.NewTime; it.RemoteTime = r.NewTime; it.LocalRel ??= it.RemoteRel; }
                    it.Classify();
                    Log($"{(action == SyncAction.Upload ? "↑" : "↓")}  {it.RelPath}", action == SyncAction.Upload ? Theme.Up : Theme.Down);
                }
                else
                {
                    fail++;
                    if (it.Action == SyncAction.Delete) it.Classify(); // kısmen silindiyse durumu yenile
                    it.Error = r.Error;
                    Log($"✗  {it.RelPath}:  {r.Error}", Theme.Danger);
                }
            }
            int skipped = todo.Count - results.Count;
            var summaryText = $"Bitti ({sw.Elapsed.TotalSeconds:0.0} sn): {ok} başarılı" + (fail > 0 ? $", {fail} hatalı" : "") + (skipped > 0 ? $", {skipped} durdurulduğu için yapılmadı" : "") + ".";
            Log(summaryText, fail > 0 ? Theme.Warn : Theme.Ok);
            lblProgress.Text = summaryText;
            progress.Value = fail == 0 && skipped == 0 ? 1 : progress.Value;
            RefreshView();
            if (fail > 0)
                MessageBox.Show(this, $"{fail} dosya aktarılamadı. Ayrıntılar günlükte ve listede (kırmızı “Hata”).", "Bazı dosyalar başarısız",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception e) { Fail(e); }
        finally { SetBusy(false); UpdateConnState(); }
    }

    // ───────────────────────────── Yardımcılar ─────────────────────────────

    void SetBusy(bool b, string? text = null)
    {
        busy = b;
        if (text != null) lblProgress.Text = text;
        if (b) progress.Value = 0;
        foreach (var c in new Control[] { tbLocal, tbRemote, tbExclude, btnBrowseLocal, btnBrowseRemote, btnCompare, btnConnect,
                     btnAllUp, btnAllDown, btnAllSkip, btnSuggest, cbBackup, cbOwner, cbRemember, cbSkipDot, cbSite, btnSite })
            c.Enabled = !b;
        btnCancel.Enabled = b && cts != null && !cts.IsCancellationRequested;
        if (!b) { cts = null; btnCancel.Enabled = false; }
        UpdateSummary();
        UpdateConnState();
    }

    void Log(string msg, Color? color = null)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => Log(msg, color))); return; }
        log.SelectionStart = log.TextLength;
        log.SelectionLength = 0;
        log.SelectionColor = Theme.Faint;
        log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "   ");
        log.SelectionColor = color ?? Theme.Text;
        log.AppendText(msg + "\n");
        log.SelectionStart = log.TextLength;
        log.ScrollToCaret();
    }

    void Warn(string msg) => MessageBox.Show(this, msg, "SFTP Eşitle", MessageBoxButtons.OK, MessageBoxIcon.Information);

    void Fail(Exception ex)
    {
        var msg = Friendly(ex);
        Log("Hata: " + msg, Theme.Danger);
        lblProgress.Text = "Hata oluştu.";
        MessageBox.Show(this, msg, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    void LoadSettings()
    {
        tbHost.Text = S.Host;
        tbPort.Text = S.Port.ToString();
        tbUser.Text = string.IsNullOrWhiteSpace(S.User) ? "root" : S.User;
        cbRemember.Checked = S.RememberPassword;
        if (S.RememberPassword) tbPass.Text = AppSettings.Unprotect(S.EncPassword);
        tbLocal.Text = S.LocalPath;
        tbRemote.Text = S.RemotePath;
        tbExclude.Text = S.Excludes;
        cbBackup.Checked = S.MakeBackup;
        cbOwner.Checked = S.InheritOwner;
        cbHideSame.Checked = S.HideSame;
        cbSkipDot.Checked = S.SkipDotFiles;
        RefreshSites(S.LastSite);
    }

    // ───────────────────────────── Siteler ─────────────────────────────

    SiteProfile? CurrentSite => cbSite.SelectedIndex > 0 && cbSite.SelectedIndex - 1 < S.Sites.Count ? S.Sites[cbSite.SelectedIndex - 1] : null;

    void RefreshSites(string? select)
    {
        loadingSites = true;
        cbSite.Items.Clear();
        cbSite.Items.Add(S.Sites.Count == 0 ? "— kayıtlı site yok (Siteler ▾ → Yeni) —" : "— kayıtsız —");
        foreach (var s in S.Sites) cbSite.Items.Add(s.Name);
        int idx = select == null ? -1 : S.Sites.FindIndex(s => s.Name == select);
        cbSite.SelectedIndex = idx + 1;
        loadingSites = false;
    }

    void SelectSite()
    {
        var site = CurrentSite;
        S.LastSite = site?.Name;
        if (site != null)
        {
            tbLocal.Text = site.LocalPath;
            tbRemote.Text = site.RemotePath;
            tbExclude.Text = site.Excludes;
            items = new();
            comparedLocal = comparedRemote = pinKey = "";
            RefreshView();
            lblProgress.Text = $"“{site.Name}” seçildi — “Karşılaştır”a bas.";
            Log($"Site seçildi: {site.Name}   ({site.LocalPath}  ⇄  {site.RemotePath})", Theme.Accent);
        }
        S.Save();
    }

    void NewSite()
    {
        if (tbLocal.Text.Trim().Length == 0 || tbRemote.Text.Trim().Length == 0)
        {
            Warn("Önce yerel klasörü ve sunucu klasörünü seç, sonra site olarak kaydet.");
            return;
        }
        var suggestion = Path.GetFileName(tbLocal.Text.Trim().TrimEnd('\\', '/'));
        var name = Prompt.Ask(this, "Yeni site", "Bu site için bir ad ver:", suggestion);
        if (name == null) return;
        var existing = S.Sites.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            if (MessageBox.Show(this, $"“{existing.Name}” adında bir site zaten var. Üzerine yazılsın mı?", "Yeni site",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        }
        else
        {
            existing = new SiteProfile { Name = name };
            S.Sites.Add(existing);
        }
        FillSite(existing);
        RefreshSites(existing.Name);
        S.LastSite = existing.Name;
        S.Save();
        Log($"Site kaydedildi: {existing.Name}", Theme.Ok);
    }

    void FillSite(SiteProfile s)
    {
        s.LocalPath = tbLocal.Text.Trim();
        s.RemotePath = SyncEngine.NormalizeRemote(tbRemote.Text);
        s.Excludes = tbExclude.Text;
    }

    /// <summary>Seçili sitenin klasörleri ekrandakilerden farklıysa günceller.</summary>
    void UpdateSite(bool manual)
    {
        var s = CurrentSite;
        if (s == null) return;
        bool changed = s.LocalPath != tbLocal.Text.Trim() || s.RemotePath != SyncEngine.NormalizeRemote(tbRemote.Text) || s.Excludes != tbExclude.Text;
        if (!changed) { if (manual) Log($"“{s.Name}” zaten güncel.", Theme.Muted); return; }
        FillSite(s);
        S.Save();
        Log($"“{s.Name}” sitesinin klasörleri güncellendi.", Theme.Ok);
    }

    void RenameSite()
    {
        var s = CurrentSite;
        if (s == null) return;
        var name = Prompt.Ask(this, "Yeniden adlandır", "Yeni ad:", s.Name);
        if (name == null || name == s.Name) return;
        if (S.Sites.Any(x => x != s && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { Warn("Bu adda bir site zaten var."); return; }
        s.Name = name;
        S.LastSite = name;
        RefreshSites(name);
        S.Save();
    }

    void DeleteSite()
    {
        var s = CurrentSite;
        if (s == null) return;
        if (MessageBox.Show(this, $"“{s.Name}” site listesinden kaldırılsın mı?\n\n(Hiçbir dosya silinmez; sadece kayıt kaldırılır.)", "Siteyi kaldır",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        S.Sites.Remove(s);
        S.LastSite = null;
        RefreshSites(null);
        S.Save();
        Log($"Site kaldırıldı: {s.Name}", Theme.Muted);
    }

    void SaveSettings()
    {
        S.Host = tbHost.Text.Trim();
        if (int.TryParse(tbPort.Text.Trim(), out var p)) S.Port = p;
        S.User = tbUser.Text.Trim();
        S.RememberPassword = cbRemember.Checked;
        S.EncPassword = cbRemember.Checked && tbPass.Text.Length > 0 ? AppSettings.Protect(tbPass.Text) : null;
        S.LocalPath = tbLocal.Text.Trim();
        S.RemotePath = tbRemote.Text.Trim();
        S.Excludes = tbExclude.Text;
        S.MakeBackup = cbBackup.Checked;
        S.InheritOwner = cbOwner.Checked;
        S.HideSame = cbHideSame.Checked;
        S.SkipDotFiles = cbSkipDot.Checked;
        S.Save();
    }

    void SetTheme(string mode)
    {
        if (S.ThemeMode == mode) return;
        S.ThemeMode = mode;
        S.Save();

        bool dark = mode switch { "dark" => true, "light" => false, _ => Theme.SystemDark() };
        if (dark == Theme.Dark) return; // görünüm zaten aynı

        if (busy)
        {
            Log("Tema seçimi kaydedildi; uygulama yeniden açılınca geçerli olacak.", Theme.Muted);
            return;
        }
        if (MessageBox.Show(this, "Yeni tema uygulama yeniden başlatılınca geçerli olur.\n\nŞimdi yeniden başlatılsın mı?",
                "Tema", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            Application.Restart();
        else
            Log("Tema seçimi kaydedildi; uygulama yeniden açılınca geçerli olacak.", Theme.Muted);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (busy && MessageBox.Show(this, "Bir işlem sürüyor. Yine de çıkılsın mı?", "Çıkış",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        cts?.Cancel();
        SaveSettings();
        if (files is { IsDisposed: false }) files.CloseForGood();
        try { client?.Disconnect(); } catch { }
        client?.Dispose();
        base.OnFormClosing(e);
    }
}

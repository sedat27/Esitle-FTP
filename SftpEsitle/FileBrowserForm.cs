using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Security.Cryptography;
using Renci.SshNet;

namespace SftpEsitle;

/// <summary>
/// Ayrı pencerede yerel ve sunucu dosyalarını gösterir; dosyaları Notepad (veya varsayılan program) ile açar.
/// Sunucu dosyaları geçici klasöre indirilir; Notepad'de kaydedilince sunucuya yüklemek için sorar.
/// </summary>
sealed class FileBrowserForm : Form
{
    enum Side { Local, Remote }
    sealed record Entry(string Name, string FullPath, bool IsDir, long Size, DateTime ModifiedUtc);

    /// <summary>Notepad'de açılmış bir sunucu dosyasının yerel kopyası ve izleyicisi.</summary>
    sealed class EditSession : IDisposable
    {
        public required string RemotePath { get; init; }
        public required string TempPath { get; init; }
        public required FileSystemWatcher Watcher { get; init; }
        public readonly System.Windows.Forms.Timer Debounce = new() { Interval = 800 };
        public byte[] Hash = Array.Empty<byte>();
        public DateTime RemoteMtime;
        public bool Busy;

        public void Dispose()
        {
            Debounce.Dispose();
            Watcher.EnableRaisingEvents = false;
            Watcher.Dispose();
        }
    }

    const long BigFile = 50L * 1024 * 1024;

    readonly Func<Task<SftpClient?>> _connect;
    readonly SemaphoreSlim _lock = new(1, 1);
    readonly Dictionary<string, EditSession> _edits = new(StringComparer.Ordinal);
    SftpClient? _client;
    Side _side = Side.Local;
    string _localDir, _remoteDir;
    List<Entry> _entries = new();
    int _loadVersion, _sortCol;
    bool _sortAsc = true, _loading, _reallyClose;

    readonly RoundButton _btnLocal, _btnRemote, _btnUp, _btnRefresh, _btnNotepad, _btnDefault, _btnReveal;
    readonly InputBox _tbPath, _tbFilter;
    readonly ListView _list;
    readonly Label _lblStatus;
    readonly ToolTip _tip = new() { InitialDelay = 400 };

    /// <summary>İlk açılışta bu dikdörtgenin (ana pencere) ortasına yerleşir.</summary>
    public Rectangle? CenterOn { get; set; }

    public FileBrowserForm(string localDir, string remoteDir, Func<Task<SftpClient?>> connect)
    {
        _connect = connect;
        _localDir = Directory.Exists(localDir) ? localDir : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _remoteDir = SyncEngine.NormalizeRemote(string.IsNullOrWhiteSpace(remoteDir) ? "/" : remoteDir);

        SuspendLayout();
        Text = "Dosyalar  ·  SFTP Eşitle";
        Font = Theme.Base;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(940, 680);
        MinimumSize = new Size(640, 420);
        Padding = new Padding(14, 12, 14, 10);
        KeyPreview = true;
        Win11.Attach(this);
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { }

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Theme.Bg };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        // ── Sekmeler + filtre ──
        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, BackColor = Theme.Bg, Margin = new Padding(0) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        _btnLocal = new RoundButton { Text = "Yerel", Glyph = Glyph.Folder, Size = new Size(110, 36), Margin = new Padding(0, 2, 6, 6) };
        _btnRemote = new RoundButton { Text = "Sunucu", Glyph = Glyph.Globe, Look = RoundButton.Kind.Secondary, Size = new Size(110, 36), Margin = new Padding(0, 2, 6, 6) };
        _btnLocal.Click += async (_, _) => await SwitchSide(Side.Local);
        _btnRemote.Click += async (_, _) => await SwitchSide(Side.Remote);
        _tbFilter = new InputBox { Dock = DockStyle.Fill, Placeholder = "Bu klasörde ara…", Margin = new Padding(0, 2, 0, 6) };
        _tbFilter.Box.TextChanged += (_, _) => FillList();
        top.Controls.Add(_btnLocal, 0, 0);
        top.Controls.Add(_btnRemote, 1, 0);
        top.Controls.Add(_tbFilter, 3, 0);
        root.Controls.Add(top, 0, 0);

        // ── Yol çubuğu ──
        var pathBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Theme.Bg, Margin = new Padding(0) };
        pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _btnUp = new RoundButton { Text = "Üst", Glyph = Glyph.Up, Look = RoundButton.Kind.Secondary, Size = new Size(84, 36), Margin = new Padding(0, 2, 8, 6) };
        _btnRefresh = new RoundButton { Text = "Yenile", Glyph = Glyph.Refresh, Look = RoundButton.Kind.Secondary, Size = new Size(100, 36), Margin = new Padding(8, 2, 0, 6) };
        _btnUp.Click += async (_, _) => await GoUp();
        _btnRefresh.Click += async (_, _) => await LoadDir();
        _tip.SetToolTip(_btnUp, "Üst klasöre çık (Backspace)");
        _tip.SetToolTip(_btnRefresh, "Listeyi yenile (F5)");
        _tbPath = new InputBox { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 6) };
        _tbPath.Box.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            SetDir(_tbPath.Text.Trim());
            await LoadDir();
        };
        pathBar.Controls.Add(_btnUp, 0, 0);
        pathBar.Controls.Add(_tbPath, 1, 0);
        pathBar.Controls.Add(_btnRefresh, 2, 0);
        root.Controls.Add(pathBar, 0, 1);

        // ── Liste ──
        var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(8, 8, 8, 8), Margin = new Padding(0, 4, 0, 0) };
        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            BorderStyle = BorderStyle.None,
            Font = Theme.Input,
            ForeColor = Theme.Text,
            BackColor = Theme.Card,
            MultiSelect = false,
        };
        _list.Columns.Add("Ad", 420);
        _list.Columns.Add("Boyut", 100, HorizontalAlignment.Right);
        _list.Columns.Add("Değiştirilme", 150);
        _list.ItemActivate += async (_, _) => await ActivateSelected(notepad: true);
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.ColumnClick += (_, e) =>
        {
            if (_sortCol == e.Column) _sortAsc = !_sortAsc; else { _sortCol = e.Column; _sortAsc = true; }
            FillList();
        };
        _list.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Back) { e.SuppressKeyPress = true; await GoUp(); }
        };
        _list.Resize += (_, _) => FitColumns();
        _list.ContextMenuStrip = BuildMenu();
        card.Controls.Add(_list);
        root.Controls.Add(card, 0, 2);

        // ── Alt çubuk ──
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, BackColor = Theme.Bg, Margin = new Padding(0), Padding = new Padding(0, 8, 0, 0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _lblStatus = new Label { Dock = DockStyle.Fill, Font = Theme.Small, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        _btnReveal = new RoundButton { Text = "Klasörde göster", Glyph = Glyph.Explorer, Look = RoundButton.Kind.Secondary, Size = new Size(130, 36), Margin = new Padding(8, 0, 0, 0) };
        _btnDefault = new RoundButton { Text = "Varsayılan programla aç", Glyph = Glyph.OpenFile, Look = RoundButton.Kind.Secondary, Size = new Size(170, 36), Margin = new Padding(8, 0, 0, 0) };
        _btnNotepad = new RoundButton { Text = "Notepad ile aç", Glyph = Glyph.Edit, Size = new Size(140, 36), Margin = new Padding(8, 0, 0, 0) };
        _btnReveal.Click += (_, _) => RevealSelected();
        _btnDefault.Click += async (_, _) => await ActivateSelected(notepad: false);
        _btnNotepad.Click += async (_, _) => await ActivateSelected(notepad: true);
        _tip.SetToolTip(_btnNotepad, "Çift tık veya Enter da aynı işi yapar.\nSunucu dosyası Notepad'de kaydedilince sunucuya yüklemek için sorulur.");
        bottom.Controls.Add(_lblStatus, 0, 0);
        bottom.Controls.Add(_btnReveal, 1, 0);
        bottom.Controls.Add(_btnDefault, 2, 0);
        bottom.Controls.Add(_btnNotepad, 3, 0);
        root.Controls.Add(bottom, 0, 3);

        Controls.Add(root);
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
        foreach (var b in new[] { _btnReveal, _btnDefault, _btnNotepad, _btnUp, _btnRefresh, _btnLocal, _btnRemote }) b.FitText();
    }

    ContextMenuStrip BuildMenu()
    {
        var m = new ContextMenuStrip { Font = Theme.Base, ShowImageMargin = false };
        var miNotepad = new ToolStripMenuItem("Notepad ile aç", null, async (_, _) => await ActivateSelected(true)) { ShortcutKeyDisplayString = "Enter", Font = Theme.SemiBold };
        var miDefault = new ToolStripMenuItem("Varsayılan programla aç", null, async (_, _) => await ActivateSelected(false));
        var miReveal = new ToolStripMenuItem("Klasörde göster", null, (_, _) => RevealSelected());
        var miCopy = new ToolStripMenuItem("Yolu kopyala", null, (_, _) => { if (Current is Entry e) Clipboard.SetText(e.FullPath); });
        m.Items.AddRange(new ToolStripItem[] { miNotepad, miDefault, new ToolStripSeparator(), miReveal, miCopy });
        m.Opening += (_, e) =>
        {
            var cur = Current;
            if (cur == null) { e.Cancel = true; return; }
            miNotepad.Text = cur.IsDir ? "Klasörü aç" : "Notepad ile aç";
            miDefault.Visible = !cur.IsDir;
            miReveal.Visible = _side == Side.Local;
        };
        return m;
    }

    // ───────────────────────────── Yaşam döngüsü ─────────────────────────────

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (CenterOn is Rectangle c)
        {
            var wa = Screen.FromRectangle(c).WorkingArea;
            Size = new Size(Math.Min(Width, wa.Width), Math.Min(Height, wa.Height));
            int x = Math.Clamp(c.X + (c.Width - Width) / 2, wa.Left, wa.Right - Width);
            int y = Math.Clamp(c.Y + (c.Height - Height) / 2, wa.Top, wa.Bottom - Height);
            Location = new Point(x, y);
        }
        int sz = (int)(18 * DeviceDpi / 96f);
        var il = new ImageList { ImageSize = new Size(sz, sz), ColorDepth = ColorDepth.Depth32Bit };
        il.Images.Add(Theme.FolderBitmap(sz));
        il.Images.Add(Theme.GlyphBitmap(Glyph.Page, sz, Theme.Muted));
        _list.SmallImageList = il;
        FitColumns();
        await LoadDir();
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.F5) { e.Handled = true; await LoadDir(); }
    }

    /// <summary>Kullanıcı kapatınca pencere sadece gizlenir; Notepad'de açık dosyaların izlenmesi sürer.</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_reallyClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    public void CloseForGood()
    {
        _reallyClose = true;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var s in _edits.Values) s.Dispose();
            _edits.Clear();
            try { _client?.Disconnect(); } catch { }
            _client?.Dispose();
            _client = null;
            _tip.Dispose();
        }
        base.Dispose(disposing);
    }

    public void Reveal()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    // ───────────────────────────── Gezinme ─────────────────────────────

    string CurrentDir => _side == Side.Local ? _localDir : _remoteDir;

    void SetDir(string dir)
    {
        if (_side == Side.Local) _localDir = dir;
        else _remoteDir = SyncEngine.NormalizeRemote(dir.Length == 0 ? "/" : dir);
    }

    async Task SwitchSide(Side side)
    {
        if (_side == side) return;
        _side = side;
        _btnLocal.Look = side == Side.Local ? RoundButton.Kind.Primary : RoundButton.Kind.Secondary;
        _btnRemote.Look = side == Side.Remote ? RoundButton.Kind.Primary : RoundButton.Kind.Secondary;
        _btnLocal.Invalidate(); _btnRemote.Invalidate();
        _tbFilter.Text = "";
        await LoadDir();
    }

    async Task GoUp()
    {
        string? parent;
        if (_side == Side.Local) parent = Directory.GetParent(_localDir)?.FullName;
        else
        {
            var p = _remoteDir.TrimEnd('/');
            int i = p.LastIndexOf('/');
            parent = p.Length == 0 ? null : i <= 0 ? "/" : p[..i];
        }
        if (parent == null || parent == CurrentDir) return;
        var from = CurrentDir;
        SetDir(parent);
        await LoadDir(selectPath: from);
    }

    async Task LoadDir(string? selectPath = null)
    {
        int ver = ++_loadVersion;
        var side = _side;
        var dir = CurrentDir;
        _tbPath.Text = dir;
        _loading = true;
        UpdateButtons();
        SetStatus(side == Side.Remote ? "Sunucu klasörü okunuyor…" : "Okunuyor…");
        UseWaitCursor = true;
        try
        {
            List<Entry> list;
            if (side == Side.Local) list = await Task.Run(() => ListLocal(dir));
            else
            {
                var c = await EnsureClient();
                if (c == null)
                {
                    if (ver == _loadVersion) { _entries.Clear(); FillList(); SetStatus("Sunucuya bağlanılamadı. Ana penceredeki bağlantı bilgilerini kontrol et.", Theme.Danger); }
                    return;
                }
                list = await RunRemote(cl => ListRemote(cl, dir));
            }
            if (ver != _loadVersion) return;
            _entries = list;
            FillList(selectPath);
            int dirs = list.Count(x => x.IsDir), files = list.Count - dirs;
            SetStatus($"{dirs} klasör  ·  {files} dosya" + WatchNote());
        }
        catch (Exception ex)
        {
            if (ver != _loadVersion) return;
            _entries.Clear();
            FillList();
            SetStatus("Okunamadı: " + ex.Message, Theme.Danger);
        }
        finally
        {
            if (ver == _loadVersion) { _loading = false; UseWaitCursor = false; UpdateButtons(); }
        }
    }

    static List<Entry> ListLocal(string dir)
    {
        var di = new DirectoryInfo(dir);
        var list = new List<Entry>();
        foreach (var f in di.EnumerateFileSystemInfos())
        {
            try
            {
                bool isDir = (f.Attributes & FileAttributes.Directory) != 0;
                list.Add(new Entry(f.Name, f.FullName, isDir, isDir ? 0 : ((FileInfo)f).Length, f.LastWriteTimeUtc));
            }
            catch { }
        }
        return list;
    }

    static List<Entry> ListRemote(SftpClient c, string dir)
    {
        var list = new List<Entry>();
        foreach (var e in c.ListDirectory(dir))
        {
            if (e.Name is "." or "..") continue;
            bool isDir = e.IsDirectory;
            long size = e.Length;
            if (e.IsSymbolicLink)
            {
                try { var a = c.GetAttributes(e.FullName); isDir = a.IsDirectory; size = a.Size; } catch { }
            }
            list.Add(new Entry(e.Name, e.FullName, isDir, isDir ? 0 : size, e.LastWriteTimeUtc));
        }
        return list;
    }

    void FillList(string? selectPath = null)
    {
        var q = _tbFilter.Text.Trim();
        IEnumerable<Entry> src = _entries;
        if (q.Length > 0) src = src.Where(x => x.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase));
        IOrderedEnumerable<Entry> ordered = src.OrderByDescending(x => x.IsDir);
        ordered = (_sortCol, _sortAsc) switch
        {
            (1, true) => ordered.ThenBy(x => x.Size),
            (1, false) => ordered.ThenByDescending(x => x.Size),
            (2, true) => ordered.ThenBy(x => x.ModifiedUtc),
            (2, false) => ordered.ThenByDescending(x => x.ModifiedUtc),
            (_, true) => ordered.ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => ordered.ThenByDescending(x => x.Name, StringComparer.CurrentCultureIgnoreCase),
        };

        _list.BeginUpdate();
        _list.Items.Clear();
        ListViewItem? sel = null;
        foreach (var e in ordered)
        {
            var it = new ListViewItem(e.Name, e.IsDir ? 0 : 1) { Tag = e };
            it.SubItems.Add(e.IsDir ? "" : Fmt.Size(e.Size));
            it.SubItems.Add(Fmt.Time(e.ModifiedUtc));
            if (e.Name.StartsWith('.')) it.ForeColor = Theme.Muted;
            _list.Items.Add(it);
            if (selectPath != null && e.FullPath == selectPath) sel = it;
        }
        _list.EndUpdate();
        if (sel != null) { sel.Selected = true; sel.Focused = true; sel.EnsureVisible(); }
        UpdateButtons();
    }

    void FitColumns()
    {
        if (_list.Columns.Count < 3) return;
        float k = DeviceDpi / 96f;
        _list.Columns[1].Width = (int)(100 * k);
        _list.Columns[2].Width = (int)(150 * k);
        _list.Columns[0].Width = Math.Max((int)(160 * k), _list.ClientSize.Width - _list.Columns[1].Width - _list.Columns[2].Width - SystemInformation.VerticalScrollBarWidth - 4);
    }

    Entry? Current => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as Entry : null;

    void UpdateButtons()
    {
        var cur = Current;
        bool file = cur is { IsDir: false } && !_loading;
        _btnNotepad.Enabled = file;
        _btnDefault.Enabled = file;
        _btnReveal.Visible = _side == Side.Local;
        _btnReveal.Enabled = cur != null && !_loading;
        _btnUp.Enabled = !_loading;
        _btnRefresh.Enabled = !_loading;
    }

    void SetStatus(string text, Color? color = null)
    {
        _lblStatus.Text = text;
        _lblStatus.ForeColor = color ?? Theme.Muted;
    }

    string WatchNote() => _edits.Count > 0 ? $"   ·   Notepad'de açılan {_edits.Count} sunucu dosyası izleniyor" : "";

    // ───────────────────────────── Açma ─────────────────────────────

    async Task ActivateSelected(bool notepad)
    {
        var e = Current;
        if (e == null || _loading) return;
        if (e.IsDir)
        {
            SetDir(e.FullPath);
            _tbFilter.Text = "";
            await LoadDir();
            return;
        }
        if (_side == Side.Local) OpenLocalFile(e.FullPath, notepad);
        else await OpenRemoteFile(e.FullPath, notepad);
    }

    void RevealSelected()
    {
        if (_side != Side.Local || Current is not Entry e) return;
        try { Process.Start("explorer.exe", $"/select,\"{e.FullPath}\""); } catch { }
    }

    /// <summary>Yerel dosyayı açar (ana penceredeki listeden de çağrılır).</summary>
    public void OpenLocalFile(string path, bool notepad = true)
    {
        if (!File.Exists(path)) { SetStatus("Dosya bulunamadı: " + path, Theme.Danger); return; }
        if (notepad && !ConfirmNotepad(path, out notepad)) return;
        Launch(path, notepad);
        SetStatus($"Açıldı: {Path.GetFileName(path)}" + WatchNote(), Theme.Ok);
    }

    /// <summary>Sunucu dosyasını indirip açar, kaydedilince geri yüklemek için izler.</summary>
    public async Task OpenRemoteFile(string remotePath, bool notepad = true)
    {
        var c = await EnsureClient();
        if (c == null) { SetStatus("Sunucuya bağlanılamadı.", Theme.Danger); return; }
        var name = remotePath[(remotePath.LastIndexOf('/') + 1)..];

        _edits.TryGetValue(remotePath, out var s);
        if (s?.Busy == true) { SetStatus($"“{name}” şu an yükleniyor, birazdan tekrar dene."); return; }

        try
        {
            var attrs = await RunRemote(cl => cl.GetAttributes(remotePath));
            if (attrs.IsDirectory) return;
            if (attrs.Size > BigFile && MessageBox.Show(this,
                    $"“{name}” büyük bir dosya ({Fmt.Size(attrs.Size)}).\n\nİndirilip açılsın mı?", "Büyük dosya",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            var temp = TempPathFor(c, remotePath);
            if (s != null) { s.Watcher.EnableRaisingEvents = false; s.Debounce.Stop(); s.Busy = true; }
            SetStatus($"İndiriliyor: {name}…");
            UseWaitCursor = true;
            var (hash, mtime) = await RunRemote(cl =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
                using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                    cl.DownloadFile(remotePath, fs);
                return (HashFile(temp), cl.GetAttributes(remotePath).LastWriteTimeUtc);
            });

            if (s == null)
            {
                s = new EditSession { RemotePath = remotePath, TempPath = temp, Watcher = NewWatcher(temp) };
                var session = s;
                s.Watcher.Changed += (_, _) => Poke(session);
                s.Watcher.Created += (_, _) => Poke(session);
                s.Watcher.Renamed += (_, _) => Poke(session);
                s.Debounce.Tick += async (_, _) => await OnLocalCopySaved(session);
                _edits[remotePath] = s;
            }
            s.Hash = hash;
            s.RemoteMtime = mtime;
            s.Busy = false;
            s.Watcher.EnableRaisingEvents = true;

            if (notepad && !ConfirmNotepad(temp, out notepad)) return;
            Launch(temp, notepad);
            SetStatus($"Açıldı: {name}  —  kaydedince sunucuya yüklemek için sorulacak" + WatchNote(), Theme.Ok);
        }
        catch (Exception ex)
        {
            if (s != null) { s.Busy = false; s.Watcher.EnableRaisingEvents = true; }
            SetStatus("Açılamadı: " + ex.Message, Theme.Danger);
            MessageBox.Show(this, $"“{name}” açılamadı.\n\n{ex.Message}", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { UseWaitCursor = false; }
    }

    /// <summary>Metin olmayan dosyada Notepad yerine ne yapılacağını sorar. false: vazgeçildi.</summary>
    bool ConfirmNotepad(string path, out bool notepad)
    {
        notepad = true;
        if (LooksText(path)) return true;
        var r = MessageBox.Show(this,
            $"“{Path.GetFileName(path)}” metin dosyası gibi görünmüyor.\n\nEvet: yine de Notepad ile aç\nHayır: Windows'un varsayılan programıyla aç",
            "Notepad ile aç", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (r == DialogResult.Cancel) return false;
        notepad = r == DialogResult.Yes;
        return true;
    }

    void Launch(string path, bool notepad)
    {
        try
        {
            if (notepad)
            {
                var psi = new ProcessStartInfo("notepad.exe") { UseShellExecute = false };
                psi.ArgumentList.Add(path);
                Process.Start(psi);
            }
            else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Dosya açılamadı:\n\n" + ex.Message, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static bool LooksText(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buf = new byte[8000];
            int n = fs.Read(buf, 0, buf.Length);
            if (n >= 2 && ((buf[0] == 0xFF && buf[1] == 0xFE) || (buf[0] == 0xFE && buf[1] == 0xFF))) return true; // UTF-16
            return Array.IndexOf(buf, (byte)0, 0, n) < 0;
        }
        catch { return true; }
    }

    // ───────────────────────────── Düzenleme izleme ─────────────────────────────

    FileSystemWatcher NewWatcher(string temp) => new(Path.GetDirectoryName(temp)!, Path.GetFileName(temp))
    {
        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
        SynchronizingObject = this,
    };

    static void Poke(EditSession s)
    {
        if (s.Busy) return;
        s.Debounce.Stop();
        s.Debounce.Start();
    }

    async Task OnLocalCopySaved(EditSession s)
    {
        s.Debounce.Stop();
        if (s.Busy || IsDisposed) return;
        byte[] h;
        try { h = HashFile(s.TempPath); }
        catch (IOException) { s.Debounce.Start(); return; }   // Notepad hâlâ yazıyor olabilir
        catch { return; }
        if (h.AsSpan().SequenceEqual(s.Hash)) return;

        var name = s.RemotePath[(s.RemotePath.LastIndexOf('/') + 1)..];
        s.Busy = true;
        try
        {
            Reveal();
            var r = MessageBox.Show(this,
                $"“{name}” Notepad'de kaydedildi.\n\nSunucuya yüklensin mi?\n{s.RemotePath}",
                "Sunucuya yükle", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes)
            {
                s.Hash = h;
                SetStatus($"“{name}” sunucuya yüklenmedi (yerel kopyada kaldı).");
                return;
            }
            await UploadEdit(s, h, name);
        }
        finally
        {
            s.Busy = false;
            s.Debounce.Start(); // soru açıkken tekrar kaydedildiyse yakala
        }
    }

    async Task UploadEdit(EditSession s, byte[] h, string name)
    {
        var c = await EnsureClient();
        if (c == null) { SetStatus("Sunucuya bağlanılamadı, yüklenmedi.", Theme.Danger); return; }
        try
        {
            var cur = await RunRemote(cl => cl.Exists(s.RemotePath) ? cl.GetAttributes(s.RemotePath).LastWriteTimeUtc : (DateTime?)null);
            if (cur != null && cur.Value != s.RemoteMtime && MessageBox.Show(this,
                    $"⚠  Sunucudaki “{name}”, sen açtıktan sonra değişmiş ({Fmt.Time(cur.Value)}).\n\nYine de senin kaydettiğin hâliyle üzerine yazılsın mı?",
                    "Sunucuda değişiklik var", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                s.Hash = h;
                SetStatus($"“{name}” yüklenmedi (sunucudaki sürüm korundu).", Theme.Warn);
                return;
            }

            SetStatus($"Yükleniyor: {name}…");
            var mtime = await RunRemote(cl =>
            {
                using var fs = new FileStream(s.TempPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                cl.UploadFile(fs, s.RemotePath, true);
                return cl.GetAttributes(s.RemotePath).LastWriteTimeUtc;
            });
            s.Hash = h;
            s.RemoteMtime = mtime;
            SetStatus($"✓  “{name}” sunucuya yüklendi  ({DateTime.Now:HH:mm:ss})" + WatchNote(), Theme.Ok);

            var parent = s.RemotePath[..Math.Max(1, s.RemotePath.LastIndexOf('/'))];
            if (_side == Side.Remote && parent == _remoteDir && !_loading) await LoadDir(selectPath: s.RemotePath);
        }
        catch (Exception ex)
        {
            SetStatus("Yüklenemedi: " + ex.Message, Theme.Danger);
            MessageBox.Show(this, $"“{name}” sunucuya yüklenemedi.\n\n{ex.Message}\n\nDeğişikliklerin yerel kopyada duruyor:\n{s.TempPath}",
                "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static byte[] HashFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return SHA256.HashData(fs);
    }

    static string TempPathFor(SftpClient c, string remotePath)
    {
        var root = Path.Combine(Path.GetTempPath(), "SftpEsitle", "duzenle", Safe($"{c.ConnectionInfo.Host}_{c.ConnectionInfo.Port}"));
        var parts = remotePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Safe);
        return Path.Combine(parts.Prepend(root).ToArray());
    }

    static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
        { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
          "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

    static string Safe(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var s = new string(name.Select(ch => bad.Contains(ch) ? '_' : ch).ToArray()).TrimEnd('.', ' ');
        if (s.Length == 0) s = "_";
        if (Reserved.Contains(Path.GetFileNameWithoutExtension(s))) s = "_" + s;
        return s;
    }

    // ───────────────────────────── Sunucu bağlantısı ─────────────────────────────

    async Task<SftpClient?> EnsureClient()
    {
        if (_client?.IsConnected == true) return _client;
        try { _client?.Dispose(); } catch { }
        _client = null;
        SetStatus("Sunucuya bağlanılıyor…");
        _client = await _connect();
        return _client;
    }

    /// <summary>Bu pencerenin kendi SFTP bağlantısında işi sırayla, arka planda çalıştırır.</summary>
    async Task<T> RunRemote<T>(Func<SftpClient, T> work)
    {
        var c = _client ?? throw new InvalidOperationException("Sunucuya bağlı değil.");
        await _lock.WaitAsync();
        try { return await Task.Run(() => work(c)); }
        finally { _lock.Release(); }
    }
}

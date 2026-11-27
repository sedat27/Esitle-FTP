using System.Drawing.Drawing2D;
using Renci.SshNet;

namespace SftpEsitle;

/// <summary>Sunucudaki klasörleri ağaç olarak gösterip seçtirir.</summary>
sealed class RemoteFolderDialog : Form
{
    readonly SftpClient _c;
    readonly string _start;
    readonly TreeView _tree;
    readonly InputBox _path;

    public string SelectedPath => SyncEngine.NormalizeRemote(_path.Text);

    public RemoteFolderDialog(SftpClient c, string start)
    {
        _c = c;
        _start = SyncEngine.NormalizeRemote(string.IsNullOrWhiteSpace(start) ? "/" : start);
        SuspendLayout();
        Text = "Sunucu klasörü seç";
        Font = Theme.Base;
        BackColor = Theme.Bg;
        StartPosition = FormStartPosition.CenterParent;
        ForeColor = Theme.Text;
        Win11.Attach(this, dialog: true);
        MinimizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(540, 600);
        MinimumSize = new Size(420, 400);
        Padding = new Padding(14);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Bg };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        _path = new InputBox { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8), Text = _start };
        root.Controls.Add(_path, 0, 0);

        var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        _tree = new TreeView
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            HideSelection = false,
            ShowLines = false,
            FullRowSelect = true,
            Font = Theme.Input,
            ForeColor = Theme.Text,
            BackColor = Theme.Card,
            ItemHeight = 26,
        };
        _tree.BeforeExpand += (_, e) => LoadChildren(e.Node!);
        _tree.AfterSelect += (_, e) => { if (e.Node?.Tag is string p) _path.Text = p; };
        _tree.NodeMouseDoubleClick += (_, e) => { if (e.Node?.Tag is string && e.Node.Nodes.Count == 0) Accept(); };
        card.Controls.Add(_tree);
        root.Controls.Add(card, 0, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, BackColor = Theme.Bg, Padding = new Padding(0, 10, 0, 0) };
        var ok = new RoundButton { Text = "Bu klasörü seç", Glyph = Glyph.Accept, Look = RoundButton.Kind.Primary };
        var cancel = new RoundButton { Text = "Vazgeç", Look = RoundButton.Kind.Secondary };
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 2);

        Controls.Add(root);
        AcceptButton = ok;
        CancelButton = cancel;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
        ok.FitText(); cancel.FitText();
    }

    void Accept() { DialogResult = DialogResult.OK; Close(); }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        int sz = (int)(18 * DeviceDpi / 96f);
        var il = new ImageList { ImageSize = new Size(sz, sz), ColorDepth = ColorDepth.Depth32Bit };
        il.Images.Add(Theme.FolderBitmap(sz));
        _tree.ImageList = il;
        _tree.ItemHeight = (int)(28 * DeviceDpi / 96f);

        Cursor = Cursors.WaitCursor;
        try
        {
            var rootNode = new TreeNode("/") { Tag = "/" };
            rootNode.Nodes.Add(new TreeNode("…"));
            _tree.Nodes.Add(rootNode);
            var cur = rootNode;
            LoadChildren(cur);
            cur.Expand();
            foreach (var seg in _start.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                var next = cur.Nodes.Cast<TreeNode>().FirstOrDefault(x => x.Text == seg);
                if (next == null) break;
                LoadChildren(next);
                next.Expand();
                cur = next;
            }
            _tree.SelectedNode = cur;
            cur.EnsureVisible();
            _path.Text = _start;
        }
        finally { Cursor = Cursors.Default; }
    }

    void LoadChildren(TreeNode n)
    {
        if (!(n.Nodes.Count == 1 && n.Nodes[0].Tag == null)) return;
        n.Nodes.Clear();
        Cursor = Cursors.WaitCursor;
        try
        {
            var path = (string)n.Tag!;
            var dirs = new List<(string name, string full)>();
            foreach (var e in _c.ListDirectory(path))
            {
                if (e.Name is "." or "..") continue;
                bool isDir = e.IsDirectory;
                if (!isDir && e.IsSymbolicLink)
                {
                    try { isDir = _c.GetAttributes(e.FullName).IsDirectory; } catch { }
                }
                if (isDir) dirs.Add((e.Name, e.FullName));
            }
            foreach (var (name, full) in dirs.OrderBy(d => d.name, StringComparer.OrdinalIgnoreCase))
            {
                var child = new TreeNode(name) { Tag = full };
                child.Nodes.Add(new TreeNode("…"));
                n.Nodes.Add(child);
            }
        }
        catch (Exception ex)
        {
            n.Nodes.Add(new TreeNode("(okunamadı: " + ex.Message + ")") { ForeColor = Theme.Danger });
        }
        finally { Cursor = Cursors.Default; }
    }
}

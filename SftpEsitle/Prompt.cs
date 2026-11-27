namespace SftpEsitle;

/// <summary>Tek satırlık metin soran küçük pencere.</summary>
sealed class Prompt : Form
{
    readonly InputBox _box;

    Prompt(string title, string question, string value)
    {
        SuspendLayout();
        Text = title;
        Font = Theme.Base;
        BackColor = Theme.Bg;
        StartPosition = FormStartPosition.CenterParent;
        ForeColor = Theme.Text;
        Win11.Attach(this, dialog: true);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
        Size = new Size(460, 210);
        Padding = new Padding(16);

        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Bg };
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.Controls.Add(new Label { Text = question, Dock = DockStyle.Fill, ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _box = new InputBox { Dock = DockStyle.Fill, Text = value, Margin = new Padding(0, 3, 0, 3) };
        t.Controls.Add(_box, 0, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, BackColor = Theme.Bg, Padding = new Padding(0, 8, 0, 0) };
        var ok = new RoundButton { Text = "Kaydet", Size = new Size(110, 36) };
        var cancel = new RoundButton { Text = "Vazgeç", Look = RoundButton.Kind.Secondary, Size = new Size(100, 36) };
        ok.Click += (_, _) => { if (_box.Text.Trim().Length > 0) { DialogResult = DialogResult.OK; Close(); } };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _box.Box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ok.PerformClick(); } };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        t.Controls.Add(buttons, 0, 2);

        Controls.Add(t);
        CancelButton = cancel;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _box.Box.Focus();
        _box.Box.SelectAll();
    }

    public static string? Ask(IWin32Window owner, string title, string question, string value = "")
    {
        using var p = new Prompt(title, question, value);
        return p.ShowDialog(owner) == DialogResult.OK ? p._box.Text.Trim() : null;
    }
}

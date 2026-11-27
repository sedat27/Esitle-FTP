using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SftpEsitle;

/// <summary>Windows 11 (Fluent) görünümü: açık/koyu renk paleti, yazı tipleri, simgeler.</summary>
static class Theme
{
    /// <summary>Windows "uygulama modu" koyu mu? Uygulama açılışında bir kez okunur.</summary>
    public static readonly bool Dark = ReadDark();

    static bool ReadDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    static Color C(int light, int dark) => Color.FromArgb(255, Color.FromArgb(Dark ? dark : light));

    // ── Yüzeyler ──
    public static readonly Color Bg = C(0xF3F3F3, 0x202020);          // Mica'ya yakın pencere zemini
    public static readonly Color Card = C(0xFBFBFB, 0x2B2B2B);        // kart yüzeyi
    public static readonly Color CardBorder = C(0xE5E5E5, 0x1D1D1D);
    public static readonly Color Border = C(0xE5E5E5, 0x3B3B3B);      // kontrol kenarı
    public static readonly Color BorderStrong = C(0xC6C6C6, 0x4A4A4A); // kontrol alt kenarı
    public static readonly Color Divider = C(0xEBEBEB, 0x363636);
    public static readonly Color Control = C(0xFFFFFF, 0x2D2D2D);     // ikincil düğme / giriş zemini
    public static readonly Color ControlHover = C(0xF9F9F9, 0x323232);
    public static readonly Color ControlDown = C(0xF5F5F5, 0x272727);
    public static readonly Color ControlDisabled = C(0xF5F5F5, 0x262626);
    public static readonly Color InputFocus = C(0xFFFFFF, 0x1F1F1F);
    public static readonly Color InputLine = C(0x8A8A8A, 0x9A9A9A);

    // ── Metin ──
    public static readonly Color Text = C(0x1B1B1B, 0xFFFFFF);
    public static readonly Color Muted = C(0x5F5F5F, 0xC5C5C5);
    public static readonly Color Faint = C(0xA0A0A0, 0x7A7A7A);

    // ── Vurgu (Windows 11 mavisi) ──
    public static readonly Color Accent = C(0x005FB8, 0x60CDFF);
    public static readonly Color AccentHover = C(0x1A6FC2, 0x5AB7E5);
    public static readonly Color AccentDown = C(0x3B82CB, 0x4FA2CC);
    public static readonly Color OnAccent = C(0xFFFFFF, 0x000000);
    public static readonly Color AccentDisabled = C(0xBFBFBF, 0x434343);

    // ── Durum renkleri ──
    public static readonly Color Up = C(0x0F6CBD, 0x60CDFF);
    public static readonly Color Down = C(0x0F7B0F, 0x6CCB5F);
    public static readonly Color Warn = C(0x9D5D00, 0xFCE100);
    public static readonly Color Danger = C(0xC42B1C, 0xFF99A4);
    public static readonly Color DangerText = C(0x8A1C12, 0xFFC2C8);
    public static readonly Color Skip = C(0x616161, 0xADADAD);
    public static readonly Color Ok = C(0x0F7B0F, 0x6CCB5F);

    public static readonly Color AccentSoft = Soft(Accent);
    public static readonly Color UpSoft = Soft(Up);
    public static readonly Color DownSoft = Soft(Down);
    public static readonly Color WarnSoft = Soft(Warn);
    public static readonly Color DangerSoft = Soft(Danger);
    public static readonly Color SkipSoft = Soft(Skip);
    public static readonly Color OkSoft = Soft(Ok);

    /// <summary>Rengin kart zemini üzerindeki hafif (rozet) tonu.</summary>
    public static Color Soft(Color c) => Blend(c, Card, Dark ? 0.20f : 0.12f);

    public static Color Blend(Color top, Color bottom, float a) => Color.FromArgb(
        (int)(top.R * a + bottom.R * (1 - a)), (int)(top.G * a + bottom.G * (1 - a)), (int)(top.B * a + bottom.B * (1 - a)));

    // ── Yazı tipleri ──
    static readonly string TextFamily = Pick("Segoe UI Variable Text", "Segoe UI");
    static readonly string TextSemi = Pick("Segoe UI Variable Text Semibold", "Segoe UI Semibold");
    static readonly string DisplaySemi = Pick("Segoe UI Variable Display Semib", "Segoe UI Variable Display Semibold", "Segoe UI Semibold");
    public static readonly string IconFamily = Pick("Segoe Fluent Icons", "Segoe MDL2 Assets");

    static string Pick(params string[] names)
    {
        foreach (var n in names)
        {
            try
            {
                using var f = new Font(n, 10f);
                if (string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)) return n;
            }
            catch { }
        }
        return names[^1];
    }

    public static readonly Font Base = new(TextFamily, 9.75f);
    public static readonly Font Small = new(TextFamily, 9f);
    public static readonly Font SemiBold = new(TextSemi, 9.75f);
    public static readonly Font SmallSemi = new(TextSemi, 8.75f);
    public static readonly Font CardTitle = new(TextSemi, 11f);
    public static readonly Font Heading = new(DisplaySemi, 13f);
    public static readonly Font AppTitle = new(DisplaySemi, 15f);
    public static readonly Font Input = new(TextFamily, 10f);
    public static readonly Font Mono = new(Pick("Cascadia Mono", "Consolas"), 9f);
    public static readonly Font Icon = new(IconFamily, 10f);
    public static readonly Font IconSmall = new(IconFamily, 8.5f);

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Hap şeklinde etiket çizer, genişliğini döndürür.</summary>
    public static int Pill(Graphics g, int x, int centerY, string text, Color fore, Color back, Font font, float k)
    {
        var sz = TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.NoPadding);
        int padX = (int)(9 * k);
        int h = sz.Height + (int)(7 * k);
        var r = new Rectangle(x, centerY - h / 2, sz.Width + padX * 2, h);
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Round(r, h / 2f))
        using (var b = new SolidBrush(back))
            g.FillPath(b, path);
        g.SmoothingMode = old;
        TextRenderer.DrawText(g, text, font, r, fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        return r.Width;
    }

    /// <summary>Fluent simgesini (Segoe Fluent Icons) bitmap olarak çizer — ListView/TreeView için.</summary>
    public static Bitmap GlyphBitmap(string glyph, int size, Color color)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var f = new Font(IconFamily, size * 0.62f, GraphicsUnit.Pixel);
        using var b = new SolidBrush(color);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(glyph, f, b, new RectangleF(0, 0, size, size), sf);
        return bmp;
    }

    /// <summary>Win11 sarı klasör simgesi.</summary>
    public static Bitmap FolderBitmap(int s)
    {
        var bmp = new Bitmap(s, s);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var tab = new RectangleF(s * 0.08f, s * 0.18f, s * 0.42f, s * 0.24f);
        var body = new RectangleF(s * 0.08f, s * 0.30f, s * 0.84f, s * 0.56f);
        using var b1 = new SolidBrush(Color.FromArgb(230, 162, 30));
        using var b2 = new SolidBrush(Color.FromArgb(255, 198, 64));
        using (var p = Round(tab, s * 0.08f)) g.FillPath(b1, p);
        using (var p = Round(body, s * 0.1f)) g.FillPath(b2, p);
        return bmp;
    }
}

/// <summary>Segoe Fluent Icons kod noktaları.</summary>
static class Glyph
{
    public const string Sync = "\uE895", Upload = "\uE898", Download = "\uE896", Delete = "\uE74D", Cancel = "\uE711";
    public const string Accept = "\uE73E", Folder = "\uE8B7", Document = "\uE8A5", Up = "\uE74A", Refresh = "\uE72C";
    public const string Search = "\uE721", OpenFile = "\uE8E5", Edit = "\uE70F", Globe = "\uE774", Connect = "\uE71B";
    public const string Disconnect = "\uE711", ChevronDown = "\uE70D", Stop = "\uE71A", Library = "\uE8F1", Install = "\uE896";
    public const string Undo = "\uE7A7", Remove = "\uE738", Explorer = "\uEC50", Copy = "\uE8C8", Page = "\uE7C3";
}

/// <summary>Pencere çerçevesine Windows 11 efektleri: Mica arka plan, koyu başlık çubuğu, yuvarlak köşeler.</summary>
static class Win11
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_SYSTEMBACKDROP_TYPE = 38;

    public static void Attach(Form f, bool dialog = false)
    {
        f.HandleCreated += (_, _) =>
        {
            try
            {
                int dark = Theme.Dark ? 1 : 0;
                DwmSetWindowAttribute(f.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                int round = dialog ? 3 : 2; // 2: yuvarlak, 3: küçük yuvarlak
                DwmSetWindowAttribute(f.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
                int mica = dialog ? 3 : 2;  // 2: Mica, 3: Acrylic (geçici pencere)
                DwmSetWindowAttribute(f.Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref mica, sizeof(int));
            }
            catch { /* Windows 10: efekt yok, sorun değil */ }
        };
    }
}

/// <summary>Uygulama logosu: degrade kare içinde yukarı/aşağı oklar.</summary>
static class Logo
{
    public static void Draw(Graphics g, RectangleF r)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = r.Width;
        using (var path = Theme.Round(r, s * 0.24f))
        {
            using var b = new LinearGradientBrush(r, Color.FromArgb(99, 102, 241), Color.FromArgb(147, 51, 234), 45f);
            g.FillPath(b, path);
            var top = new RectangleF(r.X, r.Y, r.Width, r.Height * 0.5f);
            using var gl = new LinearGradientBrush(top, Color.FromArgb(50, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f);
            g.SetClip(path);
            g.FillRectangle(gl, top);
            g.ResetClip();
        }

        float w = Math.Max(1.6f, s * 0.095f);
        float head = s * 0.15f;
        using var p1 = new Pen(Color.White, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var p2 = new Pen(Color.FromArgb(225, 255, 255, 255), w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

        float x1 = r.X + s * 0.37f, yt = r.Y + s * 0.26f, yb = r.Y + s * 0.74f;
        g.DrawLine(p1, x1, yb, x1, yt);
        g.DrawLines(p1, new[] { new PointF(x1 - head, yt + head), new PointF(x1, yt), new PointF(x1 + head, yt + head) });
        float x2 = r.X + s * 0.63f;
        g.DrawLine(p2, x2, yt, x2, yb);
        g.DrawLines(p2, new[] { new PointF(x2 - head, yb - head), new PointF(x2, yb), new PointF(x2 + head, yb - head) });
    }

    public static Bitmap Render(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        float m = size >= 32 ? size * 0.04f : 0f;
        Draw(g, new RectangleF(m, m, size - 2 * m, size - 2 * m));
        return bmp;
    }

    public static void SaveIco(string file)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var pngs = sizes.Select(sz =>
        {
            using var bmp = Render(sz);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }).ToList();

        using var fs = File.Create(file);
        using var bw = new BinaryWriter(fs);
        bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            bw.Write((byte)0); bw.Write((byte)0);
            bw.Write((short)1); bw.Write((short)32);
            bw.Write(pngs[i].Length); bw.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var p in pngs) bw.Write(p);
    }
}

/// <summary>Win11 tarzı düğme: 4px yuvarlak köşe, isteğe bağlı Fluent simgesi.</summary>
sealed class RoundButton : Button
{
    public enum Kind { Primary, Secondary, Subtle }
    public Kind Look { get; set; } = Kind.Primary;
    public Color? TextColorOverride { get; set; }
    /// <summary>Metnin solunda çizilen Segoe Fluent Icons simgesi.</summary>
    public string? Glyph { get; set; }
    bool _hover, _down;

    public RoundButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = Theme.Base;
        Size = new Size(110, 34);
        Margin = new Padding(4, 3, 4, 3);
    }

    protected override bool ShowFocusCues => false;

    float K => DeviceDpi / 96f;
    int GlyphWidth => Glyph == null ? 0 : (int)((string.IsNullOrEmpty(Text) ? 16 : 24) * K);

    public void FitText(int minWidth = 0)
    {
        int tw = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Font).Width;
        int w = tw + GlyphWidth + (int)((string.IsNullOrEmpty(Text) ? 20 : 28) * K);
        Width = Math.Max(w, minWidth);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaintBackground(PaintEventArgs e) => PaintBack(e.Graphics);

    void PaintBack(Graphics g)
    {
        Control? p = Parent;
        while (p != null && p.BackColor.A < 255) p = p.Parent;
        g.Clear(p?.BackColor ?? Theme.Bg);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        PaintBack(g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float k = K;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Color fill, fore; Color? border = null, bottom = null;
        switch (Look)
        {
            case Kind.Primary:
                fill = !Enabled ? Theme.AccentDisabled : _down ? Theme.AccentDown : _hover ? Theme.AccentHover : Theme.Accent;
                fore = !Enabled ? (Theme.Dark ? Color.FromArgb(160, 160, 160) : Color.White) : Theme.OnAccent;
                if (Enabled && !_down) bottom = Theme.Blend(Color.Black, fill, Theme.Dark ? 0.25f : 0.3f);
                break;
            case Kind.Subtle:
                fill = !Enabled || (!_hover && !_down) ? Color.Transparent : _down ? Theme.ControlDown : Theme.ControlHover;
                fore = !Enabled ? Theme.Faint : TextColorOverride ?? Theme.Text;
                break;
            default:
                fill = !Enabled ? Theme.ControlDisabled : _down ? Theme.ControlDown : _hover ? Theme.ControlHover : Theme.Control;
                fore = !Enabled ? Theme.Faint : _down ? Theme.Muted : TextColorOverride ?? Theme.Text;
                border = Theme.Border;
                if (Enabled && !_down) bottom = Theme.BorderStrong;
                break;
        }
        float rad = 4 * k;
        using (var path = Theme.Round(r, rad))
        {
            if (fill.A > 0)
            {
                using var b = new SolidBrush(fill);
                g.FillPath(b, path);
            }
            if (bottom is Color bc)
            {
                // Win11: alt kenar biraz daha koyu (hafif kabartma etkisi)
                using var pen = new Pen(bc, 1f);
                g.SetClip(new RectangleF(0, Height - rad * 1.5f, Width, rad * 1.5f));
                g.DrawPath(pen, path);
                g.ResetClip();
                if (border is Color top)
                {
                    using var pt = new Pen(top, 1f);
                    g.SetClip(new RectangleF(0, 0, Width, Height - rad * 1.5f));
                    g.DrawPath(pt, path);
                    g.ResetClip();
                }
            }
            else if (border is Color bd)
            {
                using var pen = new Pen(bd, 1f);
                g.DrawPath(pen, path);
            }
        }

        var content = ClientRectangle;
        if (Glyph != null)
        {
            int tw = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            int total = GlyphWidth + tw;
            int x = Math.Max((int)(8 * k), (Width - total) / 2);
            TextRenderer.DrawText(g, Glyph, Theme.Icon, new Rectangle(x, 0, (int)(16 * k), Height), fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            content = new Rectangle(x + GlyphWidth, 0, Width - x - GlyphWidth, Height);
            TextRenderer.DrawText(g, Text, Font, content, fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
        else
        {
            TextRenderer.DrawText(g, Text, Font, content, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }
}

/// <summary>Win11 tarzı onay kutusu: yuvarlak kare, vurgu renginde dolgu.</summary>
sealed class FluentCheck : CheckBox
{
    bool _hover;

    public FluentCheck()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Font = Theme.Small;
        ForeColor = Theme.Text;
    }

    float K => DeviceDpi / 96f;

    public override Size GetPreferredSize(Size proposedSize)
    {
        var t = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        return new Size((int)(28 * K) + t.Width + 2, Math.Max((int)(24 * K), t.Height + 4));
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Control? p = Parent;
        while (p != null && p.BackColor.A < 255) p = p.Parent;
        g.Clear(p?.BackColor ?? Theme.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float k = K;
        float s = 18 * k;
        var box = new RectangleF(1, (Height - s) / 2f, s, s);
        using (var path = Theme.Round(box, 4 * k))
        {
            if (Checked)
            {
                using var b = new SolidBrush(!Enabled ? Theme.AccentDisabled : _hover ? Theme.AccentHover : Theme.Accent);
                g.FillPath(b, path);
            }
            else
            {
                using var b = new SolidBrush(_hover && Enabled ? Theme.ControlHover : Theme.Control);
                g.FillPath(b, path);
                using var pen = new Pen(Enabled ? Theme.InputLine : Theme.Faint, 1f);
                g.DrawPath(pen, path);
            }
        }
        if (Checked)
            TextRenderer.DrawText(g, Glyph.Accept, Theme.IconSmall, Rectangle.Round(box), Theme.OnAccent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        var tr = new Rectangle((int)(s + 9 * k), 0, Width - (int)(s + 9 * k), Height);
        TextRenderer.DrawText(g, Text, Font, tr, Enabled ? ForeColor : Theme.Faint,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}

/// <summary>Win11 kartı: 8px yuvarlak köşe, ince kenar.</summary>
class CardPanel : Panel
{
    public CardPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
        ForeColor = Theme.Text;
        Padding = new Padding(16, 12, 16, 12);
        Margin = new Padding(0);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float k = DeviceDpi / 96f;
        using var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8 * k);
        using var b = new SolidBrush(BackColor);
        g.FillPath(b, path);
        using var pen = new Pen(Theme.CardBorder);
        g.DrawPath(pen, path);
    }
}

/// <summary>Win11 metin kutusu: ince kenar, odaklanınca vurgu renginde alt çizgi.</summary>
sealed class InputBox : Panel
{
    public readonly TextBox Box = new()
    {
        BorderStyle = BorderStyle.None,
        BackColor = Theme.Control,
        ForeColor = Theme.Text,
        Font = Theme.Input,
    };

    public InputBox()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Control;
        Size = new Size(200, 36);
        Margin = new Padding(0, 3, 8, 3);
        Cursor = Cursors.IBeam;
        Controls.Add(Box);
        Box.GotFocus += (_, _) => { Box.BackColor = Theme.InputFocus; Invalidate(); };
        Box.LostFocus += (_, _) => { Box.BackColor = Enabled ? Theme.Control : Theme.ControlDisabled; Invalidate(); };
        Click += (_, _) => Box.Focus();
    }

    [AllowNull]
    public override string Text { get => Box.Text; set => Box.Text = value ?? ""; }
    public string Placeholder { get => Box.PlaceholderText; set => Box.PlaceholderText = value; }
    public bool Password { get => Box.UseSystemPasswordChar; set => Box.UseSystemPasswordChar = value; }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int pad = (int)(10 * DeviceDpi / 96f);
        Box.SetBounds(pad, (Height - Box.Height) / 2, Math.Max(10, Width - pad * 2), Box.Height);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Box.BackColor = Enabled ? Theme.Control : Theme.ControlDisabled;
        Box.ForeColor = Enabled ? Theme.Text : Theme.Muted;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Card);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float k = DeviceDpi / 96f;
        bool focus = Box.Focused;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float rad = 4 * k;
        using var path = Theme.Round(r, rad);
        using (var b = new SolidBrush(!Enabled ? Theme.ControlDisabled : focus ? Theme.InputFocus : Theme.Control))
            g.FillPath(b, path);
        using (var pen = new Pen(Theme.Border))
            g.DrawPath(pen, path);
        if (!Enabled) return;
        // Alt çizgi: normalde ince gri, odakta 2px vurgu rengi
        float lw = focus ? 2f * k : 1f;
        using var line = new Pen(focus ? Theme.Accent : Theme.InputLine, lw);
        g.SetClip(new RectangleF(0, Height - rad, Width, rad));
        using var lp = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 0.5f - lw / 2), rad);
        g.DrawPath(line, lp);
        g.ResetClip();
    }
}

/// <summary>Win11 ilerleme çubuğu: ince iz, vurgu renginde kalın çubuk.</summary>
sealed class SlimProgress : Control
{
    double _v;
    public double Value { get => _v; set { _v = Math.Clamp(value, 0, 1); Invalidate(); } }

    public SlimProgress()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 6;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float k = DeviceDpi / 96f;
        float cy = Height / 2f, track = Math.Max(1f, 1f * k), bar = 3f * k;
        using (var b = new SolidBrush(Theme.Faint))
            g.FillRectangle(b, 0, cy - track / 2, Width, track);
        if (_v > 0)
        {
            var f = new RectangleF(0, cy - bar / 2, Math.Max(bar, (float)(Width * _v)), bar);
            using var path = Theme.Round(f, bar / 2);
            using var b = new SolidBrush(Theme.Accent);
            g.FillPath(b, path);
        }
    }
}

/// <summary>Üst kısımda sade uygulama başlığı (logo, ad, sürüm) ve sağda düğmeler.</summary>
sealed class HeaderPanel : Panel
{
    /// <summary>Sağdan sola dizilen başlık düğmeleri.</summary>
    public List<RoundButton> RightButtons { get; } = new();

    public HeaderPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
        Height = 64;
        Dock = DockStyle.Top;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int pad = (int)(16 * DeviceDpi / 96f), gap = (int)(8 * DeviceDpi / 96f);
        int x = Width - pad;
        foreach (var b in RightButtons)
        {
            x -= b.Width;
            b.Location = new Point(x, (Height - b.Height) / 2 + (int)(2 * DeviceDpi / 96f));
            x -= gap;
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        float k = DeviceDpi / 96f;
        int pad = (int)(18 * k), logo = (int)(34 * k);
        int cy = Height / 2 + (int)(2 * k);
        Logo.Draw(g, new RectangleF(pad, cy - logo / 2f, logo, logo));
        int tx = pad + logo + (int)(12 * k);

        const string title = "SFTP Eşitle";
        const string sub = "Yerel klasör ↔ sunucu klasörü  ·  ne yükleneceğine / indirileceğine sen karar ver";
        var t = TextRenderer.MeasureText(g, title, Theme.Heading, Size.Empty, TextFormatFlags.NoPadding);
        var s = TextRenderer.MeasureText(g, sub, Theme.Small, Size.Empty, TextFormatFlags.NoPadding);
        int gapY = (int)(2 * k);
        int top = cy - (t.Height + gapY + s.Height) / 2;
        TextRenderer.DrawText(g, title, Theme.Heading, new Point(tx, top), Theme.Text, TextFormatFlags.NoPadding);

        // Sürüm rozeti
        var ver = "v" + AppInfo.Version;
        var vs = TextRenderer.MeasureText(g, ver, Theme.SmallSemi, Size.Empty, TextFormatFlags.NoPadding);
        int padX = (int)(7 * k), padY = (int)(2 * k);
        var pill = new RectangleF(tx + t.Width + (int)(8 * k), top + (t.Height - vs.Height - 2 * padY) / 2f,
            vs.Width + 2 * padX, vs.Height + 2 * padY);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Theme.Round(pill, pill.Height / 2f))
        using (var pb = new SolidBrush(Theme.AccentSoft))
            g.FillPath(pb, path);
        TextRenderer.DrawText(g, ver, Theme.SmallSemi, new Point((int)pill.X + padX, (int)pill.Y + padY), Theme.Accent, TextFormatFlags.NoPadding);

        TextRenderer.DrawText(g, sub, Theme.Small, new Point(tx, top + t.Height + gapY), Theme.Muted, TextFormatFlags.NoPadding);
    }
}

/// <summary>Özet rozetleri: ↑ upload · ↓ download · atla · aynı</summary>
sealed class SummaryBar : Control
{
    public int Up, Down, Del, Skip, Same, Errors;
    public long UpBytes, DownBytes;

    public SummaryBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 34;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        float k = DeviceDpi / 96f;
        int x = (int)(6 * k), cy = Height / 2, gap = (int)(8 * k);
        x += Theme.Pill(g, x, cy, $"↑  {Up} upload" + (Up > 0 ? $"  ·  {Fmt.Size(UpBytes)}" : ""), Theme.Up, Theme.UpSoft, Theme.SmallSemi, k) + gap;
        x += Theme.Pill(g, x, cy, $"↓  {Down} download" + (Down > 0 ? $"  ·  {Fmt.Size(DownBytes)}" : ""), Theme.Down, Theme.DownSoft, Theme.SmallSemi, k) + gap;
        x += Del > 0
            ? Theme.Pill(g, x, cy, $"✕  {Del} silinecek", Theme.OnAccent, Theme.Danger, Theme.SmallSemi, k) + gap
            : Theme.Pill(g, x, cy, "✕  0 silinecek", Theme.Danger, Theme.DangerSoft, Theme.SmallSemi, k) + gap;
        x += Theme.Pill(g, x, cy, $"–  {Skip} atlanacak", Theme.Skip, Theme.SkipSoft, Theme.SmallSemi, k) + gap;
        x += Theme.Pill(g, x, cy, $"✓  {Same} aynı", Theme.Ok, Theme.OkSoft, Theme.SmallSemi, k) + gap;
        if (Errors > 0)
            Theme.Pill(g, x, cy, $"!  {Errors} hata", Theme.Danger, Theme.DangerSoft, Theme.SmallSemi, k);
    }
}

/// <summary>Win11 tarzı açılır menü: yuvarlak vurgu, tema renkleri.</summary>
sealed class FluentMenuRenderer : ToolStripProfessionalRenderer
{
    public FluentMenuRenderer() : base(new Colors()) { RoundedEdges = true; }

    sealed class Colors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Dark ? Color.FromArgb(44, 44, 44) : Color.FromArgb(249, 249, 249);
        public override Color MenuBorder => Theme.Dark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(220, 220, 220);
        public override Color ImageMarginGradientBegin => ToolStripDropDownBackground;
        public override Color ImageMarginGradientMiddle => ToolStripDropDownBackground;
        public override Color ImageMarginGradientEnd => ToolStripDropDownBackground;
        public override Color SeparatorDark => Theme.Divider;
        public override Color SeparatorLight => Theme.Divider;
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float k = e.Item.Owner?.DeviceDpi / 96f ?? 1f;
        var r = new RectangleF(4 * k, 1, e.Item.Width - 8 * k, e.Item.Height - 2);
        using var path = Theme.Round(r, 4 * k);
        using var b = new SolidBrush(Theme.Dark ? Color.FromArgb(56, 56, 56) : Color.FromArgb(236, 236, 236));
        g.FillPath(b, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item.ForeColor == SystemColors.ControlText || e.Item.ForeColor == Color.Empty || e.Item.ForeColor == SystemColors.MenuText)
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Faint;
        else if (!e.Item.Enabled) e.TextColor = Theme.Faint;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.Muted;
        base.OnRenderArrow(e);
    }
}

static class Fmt
{
    static readonly System.Globalization.CultureInfo Tr = new("tr-TR");

    public static string Size(long b)
    {
        if (b < 1024) return $"{b} B";
        double v = b; string[] u = { "KB", "MB", "GB", "TB" }; int i = -1;
        do { v /= 1024; i++; } while (v >= 1024 && i < u.Length - 1);
        return v.ToString(v < 10 ? "0.0" : "0", Tr) + " " + u[i];
    }

    public static string Time(DateTime utc) => utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Tr);

    public static string Side(long? size, DateTime? time) =>
        size == null ? "—" : $"{Size(size.Value)}   ·   {Time(time!.Value)}";
}

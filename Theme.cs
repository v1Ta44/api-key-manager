using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ApiKeyManager;

// ---------------- 主题与调色板 ----------------

public enum AppTheme { Light, Dark }

public sealed class Palette
{
    public Color Back, Surface, SurfaceAlt, Border, Text, TextDim,
        Accent, AccentHover, AccentDown, AccentText, Danger, DangerHover,
        Pill, PillText, Selection, SelectionText, GridLine;
}

public static class Theme
{
    public static AppTheme Current = AppTheme.Light;

    private static readonly Palette LightP = new Palette
    {
        Back = Color.FromArgb(244, 246, 249),
        Surface = Color.White,
        SurfaceAlt = Color.FromArgb(240, 242, 246),
        Border = Color.FromArgb(226, 230, 236),
        Text = Color.FromArgb(30, 36, 50),
        TextDim = Color.FromArgb(112, 120, 134),
        Accent = Color.FromArgb(80, 95, 232),
        AccentHover = Color.FromArgb(66, 81, 216),
        AccentDown = Color.FromArgb(54, 68, 192),
        AccentText = Color.White,
        Danger = Color.FromArgb(210, 70, 70),
        DangerHover = Color.FromArgb(186, 56, 56),
        Pill = Color.FromArgb(233, 237, 255),
        PillText = Color.FromArgb(80, 95, 232),
        Selection = Color.FromArgb(232, 236, 255),
        SelectionText = Color.FromArgb(30, 36, 50),
        GridLine = Color.FromArgb(236, 239, 244),
    };

    private static readonly Palette DarkP = new Palette
    {
        Back = Color.FromArgb(22, 24, 29),
        Surface = Color.FromArgb(30, 33, 40),
        SurfaceAlt = Color.FromArgb(38, 42, 51),
        Border = Color.FromArgb(52, 57, 68),
        Text = Color.FromArgb(232, 234, 238),
        TextDim = Color.FromArgb(150, 158, 170),
        Accent = Color.FromArgb(110, 123, 242),
        AccentHover = Color.FromArgb(128, 140, 250),
        AccentDown = Color.FromArgb(92, 104, 224),
        AccentText = Color.White,
        Danger = Color.FromArgb(228, 96, 88),
        DangerHover = Color.FromArgb(240, 116, 108),
        Pill = Color.FromArgb(42, 47, 74),
        PillText = Color.FromArgb(160, 172, 255),
        Selection = Color.FromArgb(46, 52, 78),
        SelectionText = Color.FromArgb(240, 242, 246),
        GridLine = Color.FromArgb(42, 46, 55),
    };

    public static Palette P => Current == AppTheme.Light ? LightP : DarkP;
    public static AppTheme Other => Current == AppTheme.Light ? AppTheme.Dark : AppTheme.Light;

    private static Font? _iconFont;
    public static Font IconFont => _iconFont ??= new Font("Segoe MDL2 Assets", 10f);

    // 自绘控件使用的字体全部提到静态字段：避免每次 OnPaint 都新建 Font
    // （GDI 句柄不释放会导致句柄泄漏，重绘频繁时很快耗尽）。
    private static Font? _badgeIconFont, _titleFont, _subtitleFont, _headerFont,
        _bodyFont, _inputFont, _keyFont, _pillFont, _dialogTitleFont;

    public static Font BadgeIconFont => _badgeIconFont ??= new Font("Segoe MDL2 Assets", 17f);
    public static Font TitleFont => _titleFont ??= new Font("Segoe UI Semibold", 15f);
    public static Font SubtitleFont => _subtitleFont ??= new Font("Segoe UI", 9.5f);
    public static Font HeaderFont => _headerFont ??= new Font("Segoe UI", 9.5f, FontStyle.Bold);
    public static Font BodyFont => _bodyFont ??= new Font("Segoe UI", 10f);
    public static Font InputFont => _inputFont ??= new Font("Segoe UI", 10.5f);
    public static Font KeyFont => _keyFont ??= new Font("Consolas", 10.5f);
    public static Font PillFont => _pillFont ??= new Font("Segoe UI", 9.5f);
    public static Font DialogTitleFont => _dialogTitleFont ??= new Font("Segoe UI Semibold", 14f);

    // Segoe MDL2 Assets 图标
    public const string IcoAdd = "\uE710";
    public const string IcoEdit = "\uE70F";
    public const string IcoDelete = "\uE74D";
    public const string IcoCopy = "\uE8C8";
    public const string IcoLink = "\uE774";
    public const string IcoView = "\uE890";
    public const string IcoImport = "\uE896";
    public const string IcoExport = "\uE898";
    public const string IcoFile = "\uE8A5";
    public const string IcoSettings = "\uE713";
    public const string IcoLock = "\uE72E";
    public const string IcoSearch = "\uE721";
    public const string IcoSun = "\uE706";
    public const string IcoMoon = "\uE708";

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2f;
        if (r.Width <= d || r.Height <= d)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRound(Graphics g, RectangleF r, float radius, Color fill, Color? border = null)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundRect(r, radius);
        using (var b = new SolidBrush(fill))
        {
            g.FillPath(b, path);
        }
        if (border.HasValue)
        {
            using var pen = new Pen(border.Value);
            g.DrawPath(pen, path);
        }
    }

    public static void StyleGrid(DataGridView g)
    {
        var p = P;
        g.EnableHeadersVisualStyles = false;
        g.BorderStyle = BorderStyle.None;
        g.BackgroundColor = p.Surface;
        g.GridColor = p.GridLine;
        g.CellBorderStyle = DataGridViewCellBorderStyle.None;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        g.RowHeadersVisible = false;
        g.ColumnHeadersDefaultCellStyle.BackColor = p.Surface;
        g.ColumnHeadersDefaultCellStyle.ForeColor = p.TextDim;
        g.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = p.Surface;
        g.ColumnHeadersDefaultCellStyle.SelectionForeColor = p.TextDim;
        g.DefaultCellStyle.BackColor = p.Surface;
        g.DefaultCellStyle.ForeColor = p.Text;
        g.DefaultCellStyle.SelectionBackColor = p.Selection;
        g.DefaultCellStyle.SelectionForeColor = p.SelectionText;
        g.DefaultCellStyle.Font = new Font("Segoe UI", 10f);
        g.AlternatingRowsDefaultCellStyle.BackColor = p.SurfaceAlt;
        g.AlternatingRowsDefaultCellStyle.ForeColor = p.Text;
        g.AlternatingRowsDefaultCellStyle.SelectionBackColor = p.Selection;
        g.AlternatingRowsDefaultCellStyle.SelectionForeColor = p.SelectionText;
    }
}

// ---------------- 自绘控件 ----------------

public enum BtnKind { Primary, Subtle, Ghost, Danger }

public sealed class ThemedButton : Button
{
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public BtnKind Kind = BtnKind.Subtle;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Glyph = "";

    private bool _hot;
    private bool _down;

    public ThemedButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseDownBackColor = Color.Transparent;
        FlatAppearance.MouseOverBackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Height = 34;
        Text = "";

        // 自绘按钮对读屏软件默认是"无名控件"：显式声明角色与默认名称。
        // Text 变化时由 OnTextChanged 同步到 AccessibleName。
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = "按钮";
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        // 图标字形是私有区码位，对读屏毫无意义，只暴露文字
        AccessibleName = string.IsNullOrWhiteSpace(Text) ? "按钮" : Text;
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hot = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = false; _down = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs me)
    {
        base.OnMouseDown(me);
        if (me.Button == MouseButtons.Left) { _down = true; Invalidate(); }
    }
    protected override void OnMouseUp(MouseEventArgs me) { base.OnMouseUp(me); _down = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.P;
        Color bg, fg = p.Text, border = Color.Transparent;

        switch (Kind)
        {
            case BtnKind.Primary:
                bg = _down ? p.AccentDown : _hot ? p.AccentHover : p.Accent;
                fg = p.AccentText;
                break;
            case BtnKind.Danger:
                bg = _down ? p.DangerHover : _hot ? p.DangerHover : p.Danger;
                fg = Color.White;
                break;
            case BtnKind.Ghost:
                bg = _hot ? p.SurfaceAlt : p.Surface;
                fg = _hot ? p.Text : p.TextDim;
                break;
            default:
                bg = _down || _hot ? p.SurfaceAlt : p.Surface;
                fg = p.Text;
                border = p.Border;
                break;
        }
        if (!Enabled) { bg = p.SurfaceAlt; fg = p.TextDim; }

        var rect = new RectangleF(0, 0, Width - 1, Height - 1);
        Theme.FillRound(e.Graphics, rect, 8f, bg, border == Color.Transparent ? null : border);

        string text = Text ?? "";
        bool hasGlyph = Glyph.Length > 0;
        var g0 = e.Graphics;
        SizeF textSize = TextRenderer.MeasureText(g0, text, Font);
        SizeF iconSize = hasGlyph ? TextRenderer.MeasureText(g0, Glyph, Theme.IconFont) : SizeF.Empty;
        float total = (hasGlyph ? iconSize.Width + 7 : 0) + textSize.Width;
        float x = (Width - total) / 2f;

        if (hasGlyph)
        {
            TextRenderer.DrawText(g0, Glyph, Theme.IconFont,
                new Point((int)x, (Height - (int)iconSize.Height) / 2), fg);
            x += iconSize.Width + 7;
        }
        TextRenderer.DrawText(g0, text, Font,
            new Point((int)x, (Height - (int)textSize.Height) / 2), fg);
    }
}

public sealed class Card : Panel
{
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Radius = 14f;

    public Card()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Padding = new Padding(20);

        // 纯装饰性容器：不抢焦点、不被读屏当控件列出
        TabStop = false;
        AccessibleRole = AccessibleRole.None;
    }

    private void PaintCard(Graphics g) =>
        Theme.FillRound(g, new RectangleF(0, 0, Width - 1, Height - 1), Radius, Theme.P.Surface, Theme.P.Border);

    protected override void OnPaintBackground(PaintEventArgs e) => PaintCard(e.Graphics);
    protected override void OnPaint(PaintEventArgs e) => PaintCard(e.Graphics);
}

public sealed class FieldBox : Panel
{
    public readonly TextBox Input = new();
    private bool _focused;
    private string _label = "";
    private string _placeholder = "";

    public FieldBox(bool multiline = false)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = multiline ? 104 : 38;

        Input.BorderStyle = BorderStyle.None;
        Input.Dock = DockStyle.Fill;
        Input.Font = Theme.InputFont;
        Input.Margin = new Padding(0);
        Input.Enter += (_, _) => { _focused = true; Invalidate(); };
        Input.Leave += (_, _) => { _focused = false; Invalidate(); };

        Padding = new Padding(12, multiline ? 8 : 3, 12, 3);
        Controls.Add(Input);

        // 容器本身不参与 Tab / 不被读屏单独朗读：真正的焦点是内部的 TextBox
        TabStop = false;
        AccessibleRole = AccessibleRole.None;

        ApplyTheme();
    }

    /// <summary>
    /// 关联的字段标签。设置后会同步到内部 TextBox 的 AccessibleName，
    /// 使读屏软件在聚焦时能读出"API Key"这类字段含义，而不是"编辑"。
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Label
    {
        get => _label;
        set
        {
            _label = value ?? "";
            Input.AccessibleName = _label.Length > 0 ? _label : null;
            Input.AccessibleDescription = _placeholder;
        }
    }

    /// <summary>占位提示。同时作为无障碍描述，弥补占位符对读屏不可见的问题。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Placeholder
    {
        get => _placeholder;
        set
        {
            _placeholder = value ?? "";
            Input.PlaceholderText = _placeholder;
            Input.AccessibleDescription = _placeholder.Length > 0 ? _placeholder : null;
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Text2
    {
        get => Input.Text;
        set => Input.Text = value;
    }

    /// <summary>密码类输入：读屏不应朗读内容。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsSecret
    {
        get => Input.UseSystemPasswordChar;
        set
        {
            Input.UseSystemPasswordChar = value;
            Input.AccessibleDescription = value ? "机密输入" : _placeholder;
        }
    }

    public void ApplyTheme()
    {
        var p = Theme.P;
        BackColor = p.SurfaceAlt;
        Input.BackColor = p.SurfaceAlt;
        Input.ForeColor = p.Text;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.P;
        Theme.FillRound(e.Graphics, new RectangleF(0, 0, Width - 1, Height - 1), 8f,
            p.SurfaceAlt, _focused ? p.Accent : p.Border);
    }
}

public sealed class GlyphBadge : Control
{
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Glyph = "";

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Radius = 14f;

    public GlyphBadge()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        Size = new Size(46, 46);

        // 纯装饰图标（私有区字形，读屏读出来是乱码）：彻底隐藏
        TabStop = false;
        AccessibleRole = AccessibleRole.None;
        AccessibleName = null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.FillRound(e.Graphics, new RectangleF(0, 0, Width - 1, Height - 1), Radius, Theme.P.Accent, null);
        var iconFont = Theme.BadgeIconFont;
        var sz = TextRenderer.MeasureText(e.Graphics, Glyph, iconFont);
        TextRenderer.DrawText(e.Graphics, Glyph, iconFont,
            new Point((Width - (int)sz.Width) / 2, (Height - (int)sz.Height) / 2), Color.White);
    }
}

public sealed class PillLabel : Control
{
    public PillLabel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        Size = new Size(150, 26);
        Text = "";

        // 胶囊承载的是有意义的文本（如自动锁定倒计时）：
        // 声明为静态文本让读屏能朗读，但不是焦点目标。
        TabStop = false;
        AccessibleRole = AccessibleRole.StaticText;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        AccessibleName = Text;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.P;
        Theme.FillRound(e.Graphics, new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f, p.Pill, null);
        var sz = TextRenderer.MeasureText(e.Graphics, Text, Font);
        TextRenderer.DrawText(e.Graphics, Text, Font,
            new Point((Width - sz.Width) / 2, (Height - sz.Height) / 2), p.PillText);
    }
}

// ---------------- 应用图标 ----------------

internal static class AppIcon
{
    private static Icon? _icon;

    public static Icon Get
    {
        get
        {
            if (_icon != null) return _icon;
            try
            {
                string? path = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(path))
                {
                    var extracted = Icon.ExtractAssociatedIcon(path);
                    if (extracted != null) return _icon = extracted;
                }
            }
            catch { /* 回落到动态绘制 */ }

            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Theme.FillRound(g, new RectangleF(0, 0, 31, 31), 8f, Theme.P.Accent, null);
                using var pen = new Pen(Color.White, 3f);
                g.DrawEllipse(pen, 7, 12, 10, 10);
                using var brush = new SolidBrush(Color.White);
                g.FillRectangle(brush, 16, 16, 11, 3);
                g.FillRectangle(brush, 22, 19, 3, 5);
            }
            return _icon = Icon.FromHandle(bmp.GetHicon());
        }
    }
}

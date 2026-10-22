using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RestReminder
{
    /// <summary>全局配色与圆角路径工具。</summary>
    internal static class UiTheme
    {
        // 主题取自应用图标的鼠尾草绿（Sage）：深绿主色 + 暖白底
        public static readonly Color WindowBg = Color.FromArgb(0xF6, 0xF8, 0xF4); // 暖白底 #F6F8F4
        public static readonly Color CardBg = Color.White;                        // 卡片白
        public static readonly Color CardBorder = Color.FromArgb(0xE2, 0xEB, 0xE5);
        public static readonly Color TitleColor = Color.FromArgb(0x2B, 0x3A, 0x33); // 深墨绿
        public static readonly Color LabelColor = Color.FromArgb(0x46, 0x56, 0x4E);
        public static readonly Color NoteColor = Color.FromArgb(0x8C, 0x9B, 0x93);
        public static readonly Color Accent = Color.FromArgb(0x39, 0x76, 0x65);      // #397665
        public static readonly Color AccentHover = Color.FromArgb(0x4B, 0x87, 0x75); // #4B8775
        public static readonly Color AccentPressed = Color.FromArgb(0x2E, 0x60, 0x52);
        public static readonly Color AccentSoft = Color.FromArgb(0x6B, 0x9B, 0x88);  // #6B9B88
        public static readonly Color SoftBg = Color.FromArgb(0xED, 0xF4, 0xEF);      // 浅绿提示底

        /// <summary>当前 DPI 相对设计基准（96）的缩放比例。</summary>
        public static float DpiScale(Control control)
        {
            int dpi = 96;
            if (control != null && !control.IsDisposed)
            {
                try
                {
                    dpi = control.DeviceDpi;
                }
                catch
                {
                    dpi = 96;
                }
            }

            // 无句柄时 DeviceDpi 可能回落到 96，此时直接向系统查询屏幕 DPI
            if (dpi <= 96) dpi = SystemDpi();
            if (dpi < 96) dpi = 96;
            return dpi / 96f;
        }

        /// <summary>通过屏幕 DC 查询系统 DPI（不依赖窗口句柄，构造期即可用）。</summary>
        public static int SystemDpi()
        {
            IntPtr dc = IntPtr.Zero;
            try
            {
                dc = GetDC(IntPtr.Zero);
                if (dc == IntPtr.Zero) return 96;
                int dpi = GetDeviceCaps(dc, LOGPIXELSX);
                return dpi > 0 ? dpi : 96;
            }
            catch
            {
                return 96;
            }
            finally
            {
                if (dc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, dc);
            }
        }

        private const int LOGPIXELSX = 88;

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int index);

        /// <summary>生成圆角矩形路径。</summary>
        public static GraphicsPath RoundedPath(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>
    /// 圆角卡片容器：白色圆角底 + 细边框 + 标题左侧强调条，
    /// 用于设置窗口的分区展示。
    /// </summary>
    public class RoundedCard : Panel
    {
        private string _title = "";

        public RoundedCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw
                     | ControlStyles.UserPaint, true);
            DoubleBuffered = true;
        }

        public string Title
        {
            get { return _title; }
            set { _title = value; Invalidate(); }
        }

        public int Radius { get; set; } = 12;
        public Color BorderColor { get; set; } = UiTheme.CardBorder;
        public Color FillColor { get; set; } = UiTheme.CardBg;
        public Color AccentBarColor { get; set; } = UiTheme.Accent;
        public Color TitleColor { get; set; } = UiTheme.TitleColor;

        private static readonly Font TitleFont = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 自绘坐标按 DPI 等比缩放（窗体整体已按 DPI 缩放，这里保持一致）
            float k = UiTheme.DpiScale(this);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = UiTheme.RoundedPath(rect, ScalePx(Radius, k)))
            {
                using (var b = new SolidBrush(FillColor)) g.FillPath(b, path);
                using (var p = new Pen(BorderColor)) g.DrawPath(p, path);
            }

            // 标题文字与强调条：按文字包围盒垂直居中对齐
            if (!string.IsNullOrEmpty(Title))
            {
                var textRect = new Rectangle(
                    ScalePx(32, k), ScalePx(15, k),
                    Width - ScalePx(46, k), ScalePx(22, k));

                TextRenderer.DrawText(g, Title, TitleFont, textRect, TitleColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

                int barWidth = Math.Max(3, ScalePx(4, k));
                int barHeight = Math.Max(8, ScalePx(16, k));
                int barY = textRect.Y + (textRect.Height - barHeight) / 2;
                using (var bar = UiTheme.RoundedPath(
                    new Rectangle(ScalePx(20, k), barY, barWidth, barHeight),
                    Math.Max(1, barWidth / 2)))
                using (var b = new SolidBrush(AccentBarColor))
                {
                    g.FillPath(b, bar);
                }
            }

            base.OnPaint(e);
        }

        private static int ScalePx(int logical, float k)
        {
            return (int)Math.Round(logical * k);
        }
    }

    /// <summary>
    /// 现代圆角按钮：自绘圆角底 + 悬停/按压状态，支持实心与描边两种样式。
    /// </summary>
    public class ModernButton : Control
    {
        private bool _hover;
        private bool _down;

        public Color BaseColor { get; set; } = UiTheme.Accent;
        public Color HoverColor { get; set; } = UiTheme.AccentHover;
        public Color PressedColor { get; set; } = UiTheme.AccentPressed;
        public bool Outline { get; set; }
        public int Radius { get; set; } = 10;

        public ModernButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw
                     | ControlStyles.SupportsTransparentBackColor
                     | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill, border, text;

            if (!Enabled)
            {
                // 禁用态：浅底、灰字，和整体风格一致
                fill = Color.FromArgb(0xF4, 0xF7, 0xF5);
                border = Color.FromArgb(0xDF, 0xE8, 0xE3);
                text = Color.FromArgb(0xA6, 0xB4, 0xAC);
            }
            else if (Outline)
            {
                fill = _down ? Color.FromArgb(0xE7, 0xF0, 0xEB)
                     : _hover ? Color.FromArgb(0xF2, 0xF7, 0xF4)
                     : Color.White;
                border = (_hover || _down) ? UiTheme.Accent : Color.FromArgb(0xD3, 0xE2, 0xDA);
                text = UiTheme.LabelColor;
            }
            else
            {
                fill = _down ? PressedColor : _hover ? HoverColor : BaseColor;
                border = fill;
                text = ForeColor;
            }

            using (var path = UiTheme.RoundedPath(rect, Math.Max(4, (int)Math.Round(Radius * UiTheme.DpiScale(this)))))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var p = new Pen(border)) g.DrawPath(p, path);
            }

            TextRenderer.DrawText(g, Text, Font, rect, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _down = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _down = true;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _down = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            Invalidate();
            base.OnTextChanged(e);
        }
    }

    /// <summary>
    /// 主题化复选框：从 Control 派生并完全自绘（方框、勾选、文字），
    /// 勾选色跟随主题主色。不继承 CheckBox，避免基类的默认绘制与自绘叠加。
    /// </summary>
    public class ModernCheckBox : Control
    {
        private bool _checked;
        private bool _hover;

        public event EventHandler CheckedChanged;

        public ModernCheckBox()
        {
            SetStyle(ControlStyles.UserPaint
                     | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw, true);
            BackColor = UiTheme.CardBg;
            ForeColor = UiTheme.LabelColor;
            Font = new Font("Microsoft YaHei UI", 9f);
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        /// <summary>是否勾选。</summary>
        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                EventHandler handler = CheckedChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        private int BoxSize
        {
            get { return Math.Max(12, (int)Math.Round(15 * UiTheme.DpiScale(this))); }
        }

        private int Gap
        {
            get { return (int)Math.Round(8 * UiTheme.DpiScale(this)); }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size text = TextRenderer.MeasureText(Text ?? string.Empty, Font);
            return new Size(BoxSize + Gap + text.Width + 2, Math.Max(BoxSize, text.Height) + 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 背景（不透明，保证在卡片上干净）
            using (var back = new SolidBrush(BackColor))
            {
                g.FillRectangle(back, ClientRectangle);
            }

            int box = BoxSize;
            int gap = Gap;
            var rect = new Rectangle(0, (Height - box) / 2, box, box);
            int radius = Math.Max(3, box / 4);

            Color border = Checked
                ? UiTheme.Accent
                : (_hover ? UiTheme.AccentSoft : Color.FromArgb(0xC7, 0xD7, 0xCF));

            using (var path = UiTheme.RoundedPath(rect, radius))
            {
                using (var brush = new SolidBrush(Checked ? UiTheme.Accent : Color.White))
                {
                    g.FillPath(brush, path);
                }
                using (var pen = new Pen(border, Math.Max(1.2f, box / 12f)))
                {
                    g.DrawPath(pen, path);
                }
            }

            if (Checked)
            {
                float k = box / 16f;
                using (var pen = new Pen(Color.White, 2f * k)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round,
                })
                {
                    g.DrawLines(pen, new[]
                    {
                        new PointF(rect.X + 4.0f * k, rect.Y + 8.2f * k),
                        new PointF(rect.X + 6.9f * k, rect.Y + 11.0f * k),
                        new PointF(rect.X + 12.0f * k, rect.Y + 5.2f * k),
                    });
                }
            }

            var textRect = new Rectangle(box + gap, 0, Math.Max(0, Width - box - gap), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            Invalidate();
            base.OnTextChanged(e);
        }
    }

    /// <summary>
    /// 输入框统一的细边框：作为 1px 主题色描边垫在控件下方，
    /// 让 NumericUpDown / ComboBox / TextBox 的边框风格一致。
    /// </summary>
    public class FieldBorder : Panel
    {
        public FieldBorder()
        {
            SetStyle(ControlStyles.UserPaint
                     | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            BorderColor = Color.FromArgb(0xC9, 0xD8, 0xD0);
        }

        public Color BorderColor { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            float k = Math.Max(1f, UiTheme.DpiScale(this));
            using (var pen = new Pen(BorderColor, k))
            {
                float half = k / 2f;
                e.Graphics.DrawRectangle(pen, half, half,
                    Math.Max(1f, Width - k), Math.Max(1f, Height - k));
            }
        }
    }

    /// <summary>托盘右键菜单配色（跟随主题）。</summary>
    public class ThemedMenuColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Color.White; } }
        public override Color ImageMarginGradientBegin { get { return Color.White; } }
        public override Color ImageMarginGradientMiddle { get { return Color.White; } }
        public override Color ImageMarginGradientEnd { get { return Color.White; } }
        public override Color MenuBorder { get { return Color.FromArgb(0xD3, 0xE2, 0xDA); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(0xD3, 0xE2, 0xDA); } }
        public override Color MenuItemSelected { get { return Color.FromArgb(0xED, 0xF4, 0xEF); } }
        public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(0xED, 0xF4, 0xEF); } }
        public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(0xED, 0xF4, 0xEF); } }
        public override Color MenuItemPressedGradientBegin { get { return Color.FromArgb(0xE7, 0xF0, 0xEB); } }
        public override Color MenuItemPressedGradientEnd { get { return Color.FromArgb(0xE7, 0xF0, 0xEB); } }
        public override Color SeparatorDark { get { return Color.FromArgb(0xE3, 0xEB, 0xE6); } }
        public override Color SeparatorLight { get { return Color.White; } }
        public override Color CheckBackground { get { return Color.White; } }
        public override Color CheckSelectedBackground { get { return Color.FromArgb(0xED, 0xF4, 0xEF); } }
        public override Color CheckPressedBackground { get { return Color.FromArgb(0xE7, 0xF0, 0xEB); } }
    }

    /// <summary>托盘右键菜单渲染：文字用主题色、对勾用主色。</summary>
    public class ThemedMenuRenderer : ToolStripProfessionalRenderer
    {
        public ThemedMenuRenderer() : base(new ThemedMenuColorTable()) { }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle r = e.ImageRectangle;
            if (r.Width < 8 || r.Height < 8) // 某些菜单样式下图片区为空，兜底一个勾选区
            {
                const int size = 16;
                r = new Rectangle(8, Math.Max(0, (e.Item.Height - size) / 2), size, size);
            }
            using (var pen = new Pen(UiTheme.Accent, 2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            })
            {
                g.DrawLines(pen, new[]
                {
                    new PointF(r.X + 3f, r.Y + r.Height * 0.52f),
                    new PointF(r.X + r.Width * 0.42f, r.Y + r.Height - 4f),
                    new PointF(r.X + r.Width - 3f, r.Y + 4f),
                });
            }
        }
    }
}

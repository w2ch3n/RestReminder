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
        public static readonly Color WindowBg = Color.FromArgb(243, 245, 250); // 窗口浅灰蓝底
        public static readonly Color CardBg = Color.White;                     // 卡片白
        public static readonly Color CardBorder = Color.FromArgb(227, 231, 240);
        public static readonly Color TitleColor = Color.FromArgb(31, 36, 48);
        public static readonly Color LabelColor = Color.FromArgb(58, 65, 80);
        public static readonly Color NoteColor = Color.FromArgb(138, 147, 166);
        public static readonly Color Accent = Color.FromArgb(47, 84, 235);
        public static readonly Color AccentHover = Color.FromArgb(64, 102, 245);
        public static readonly Color AccentPressed = Color.FromArgb(36, 64, 196);

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
                fill = Color.FromArgb(244, 246, 251);
                border = Color.FromArgb(228, 232, 241);
                text = Color.FromArgb(168, 176, 192);
            }
            else if (Outline)
            {
                fill = _down ? Color.FromArgb(230, 236, 248)
                     : _hover ? Color.FromArgb(240, 243, 250)
                     : Color.White;
                border = (_hover || _down) ? UiTheme.Accent : Color.FromArgb(213, 219, 232);
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
}

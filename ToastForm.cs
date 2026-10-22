using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RestReminder
{
    /// <summary>
    /// 右下角轻预告 Toast：淡入 → 停留数秒 → 淡出，点击可提前关闭。
    /// 用于“还有几分钟到久坐提醒点”的收尾提示，无按钮、不打断。
    /// </summary>
    public class ToastForm : Form
    {
        private readonly Timer _anim;
        private readonly Stopwatch _watch = new Stopwatch();
        private readonly Label _lblText;

        private const double FadeIn = 0.18;   // 秒
        private const double Hold = 6.0;      // 秒
        private const double FadeOut = 0.3;   // 秒

        private static readonly Color BgColor = Color.FromArgb(0xED, 0xF4, 0xEF);     // 浅鼠尾草底
        private static readonly Color TextColor = Color.FromArgb(0x39, 0x76, 0x65);   // 主题绿
        private static readonly Color BorderColor = Color.FromArgb(0xD8, 0xE6, 0xDE);

        public ToastForm()
        {
            // 与设置窗口一致：不使用框架自动缩放，构建完成后按 DPI 显式等比缩放
            AutoScaleMode = AutoScaleMode.None;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Size = new Size(400, 58);
            BackColor = BgColor;
            Font = new Font("Microsoft YaHei UI", 9.5f);
            Opacity = 0;
            ShowInTaskbar = false;

            _lblText = new Label
            {
                BackColor = Color.Transparent,
                ForeColor = TextColor,
                Bounds = new Rectangle(18, 0, Width - 36, Height),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
            };
            _lblText.Click += (s, e) => Hide();
            Click += (s, e) => Hide();
            Controls.Add(_lblText);

            ApplyDpiScale();
            ApplyRoundedRegion();

            _anim = new Timer { Interval = 25 };
            _anim.Tick += OnAnimTick;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        private bool _dpiScaled;

        /// <summary>按当前 DPI 等比放大 Toast 卡片（设计基准 96 DPI）。</summary>
        private void ApplyDpiScale()
        {
            if (_dpiScaled) return;
            float k = UiTheme.DpiScale(this);
            if (k <= 1.001f) return;

            _dpiScaled = true;
            SuspendLayout();
            try
            {
                Scale(new SizeF(k, k));
                ClientSize = new Size(
                    (int)Math.Round(400 * k),
                    (int)Math.Round(58 * k));
            }
            finally
            {
                ResumeLayout(false);
                PerformLayout();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            float k = UiTheme.DpiScale(this);
            int radius = Math.Max(5, (int)Math.Round(10 * k));
            var rect = new Rectangle(1, 1, Width - 2, Height - 2);
            using (var path = UiTheme.RoundedPath(rect, radius))
            using (var pen = new Pen(BorderColor, Math.Max(1f, k)))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawPath(pen, path);
            }
        }

        private void ApplyRoundedRegion()
        {
            int r = Math.Max(5, (int)Math.Round(10 * UiTheme.DpiScale(this)));
            using (var path = new GraphicsPath())
            {
                int w = Width, h = Height;
                path.AddArc(0, 0, r, r, 180, 90);
                path.AddArc(w - r, 0, r, r, 270, 90);
                path.AddArc(w - r, h - r, r, r, 0, 90);
                path.AddArc(0, h - r, r, r, 90, 90);
                path.CloseFigure();
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose(); // 释放上一次的 GDI Region
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width > 0 && Height > 0) ApplyRoundedRegion();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyDpiScale(); // 若构造时 DPI 尚未就绪，这里补做（幂等）
            ApplyRoundedRegion();
        }

        /// <summary>显示一条轻预告。</summary>
        public void ShowToast(string text)
        {
            ApplyDpiScale(); // 幂等：确保先按 DPI 放大，再计算宽度与位置
            _lblText.Text = text;

            // 宽度按文字实测自适应：高 DPI 下文字会变宽，固定宽度会被省略号截断
            float k = UiTheme.DpiScale(this);
            int padding = (int)Math.Round(36 * k);
            int textWidth = TextRenderer.MeasureText(text, _lblText.Font).Width;
            int width = Math.Max((int)Math.Round(280 * k),
                        Math.Min((int)Math.Round(560 * k), textWidth + padding));
            if (Width != width)
            {
                Width = width;
                _lblText.Width = width - padding;
                ApplyRoundedRegion();
            }

            Position();
            if (!Visible) Show();
            Position(); // 显示后按最终尺寸再校正一次位置
            Opacity = 0;
            _watch.Restart();
            _anim.Start();
        }

        private void Position()
        {
            Screen screen = Screen.FromPoint(Cursor.Position);
            if (screen == null) screen = Screen.PrimaryScreen;
            Rectangle wa = screen.WorkingArea;
            // 贴右下角，同时保证不越出工作区（高 DPI 放大后也不会跑到屏幕外）
            Location = new Point(
                Math.Max(wa.Left, wa.Right - Width - 8),
                Math.Max(wa.Top, wa.Bottom - Height - 8));
        }

        private void OnAnimTick(object sender, EventArgs e)
        {
            double t = _watch.Elapsed.TotalSeconds;
            double end = FadeIn + Hold + FadeOut;

            if (t < FadeIn)
            {
                Opacity = t / FadeIn;
            }
            else if (t < FadeIn + Hold)
            {
                Opacity = 1;
            }
            else if (t < end)
            {
                Opacity = Math.Max(0, 1 - (t - FadeIn - Hold) / FadeOut);
            }
            else
            {
                _anim.Stop();
                Hide();
                Opacity = 0;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _anim.Dispose();
            base.Dispose(disposing);
        }
    }
}

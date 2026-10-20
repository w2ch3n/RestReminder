using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RestReminder
{
    /// <summary>
    /// 右下角温和提醒卡片：只告知“已连续坐多久”，
    /// 显示若干秒后自动消失（鼠标悬停暂停倒计时、点击立即关闭），
    /// 没有任何按钮，不干涉用户是否休息——是否清零完全交给“真正离开”检测。
    /// </summary>
    public class ReminderPopupForm : Form
    {
        private const int DesignWidth = 408;
        private const int DesignHeight = 152;

        private readonly TimerEngine _engine;

        private Label _lblTitle;
        private Label _lblSeated;
        private Label _lblTip;

        private readonly System.Windows.Forms.Timer _autoClose;
        private readonly Stopwatch _shownFor = new Stopwatch();
        private double _closeAfterSeconds = 5;
        private int _hoverDepth;
        private bool _timerDisposed;

        // 时长文字自适应：字号候选（从大到小），保证长时长/高 DPI 下也完整显示
        private static readonly float[] SeatedFontSizes = { 21f, 19f, 17f, 15f, 13f, 12f };
        private Font _seatedFont;
        private float _seatedFontSize = 21f;
        private string _seatedText;

        private static readonly Color BgColor = Color.FromArgb(28, 34, 58);
        private static readonly Color SubTextColor = Color.FromArgb(168, 179, 208);

        public ReminderPopupForm(TimerEngine engine)
        {
            _engine = engine;

            // 与设置窗口一致：不使用框架自动缩放，构建完成后按 DPI 显式等比缩放
            AutoScaleMode = AutoScaleMode.None;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Size = new Size(DesignWidth, DesignHeight);
            BackColor = BgColor;
            Font = new Font("Microsoft YaHei UI", 9f);
            DoubleBuffered = true;
            Cursor = Cursors.Hand; // 提示“点击可关闭”

            BuildUi();
            ApplyDpiScale();
            ApplyRoundedRegion();

            _autoClose = new System.Windows.Forms.Timer { Interval = 100 };
            _autoClose.Tick += OnAutoCloseTick;

            _engine.Tick += OnEngineTick;
            _engine.StateChanged += OnStateChanged;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        private bool _dpiScaled;

        /// <summary>按当前 DPI 把提醒卡片等比放大（设计基准 96 DPI）。</summary>
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
                    (int)Math.Round(DesignWidth * k),
                    (int)Math.Round(DesignHeight * k));
            }
            finally
            {
                ResumeLayout(false);
                PerformLayout();
            }
        }

        // ---------- UI ----------

        private void BuildUi()
        {
            _lblTitle = new Label
            {
                Text = "该活动一下啦",
                AutoSize = true,
                BackColor = Color.Transparent,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold),
                Location = new Point(28, 18),
            };

            _lblSeated = new Label
            {
                Text = "",
                AutoSize = true,
                BackColor = Color.Transparent,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 21f, FontStyle.Bold),
                Location = new Point(26, 50),
            };
            _seatedFont = _lblSeated.Font;

            _lblTip = new Label
            {
                Text = "起来走一走、看看窗外，接杯水——小小的活动就很有效",
                BackColor = Color.Transparent,
                ForeColor = SubTextColor,
                Font = new Font("Microsoft YaHei UI", 9f),
                Bounds = new Rectangle(26, 100, 356, 44),
                AutoSize = false,
            };

            Controls.Add(_lblTitle);
            Controls.Add(_lblSeated);
            Controls.Add(_lblTip);

            // 纯提示卡片：点击任意位置立即关闭
            Click += (s, e) => Dismiss();
            MouseEnter += OnHoverEnter;
            MouseLeave += OnHoverLeave;

            foreach (Control child in Controls)
            {
                child.Click += (s, e) => Dismiss();
                child.MouseEnter += OnHoverEnter;
                child.MouseLeave += OnHoverLeave;
                child.Cursor = Cursors.Hand;
            }
        }

        private void ApplyRoundedRegion()
        {
            int r = Math.Max(6, (int)Math.Round(14 * UiTheme.DpiScale(this)));
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

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible && !_timerDisposed)
            {
                _autoClose.Stop();
                _hoverDepth = 0;
            }
        }

        // ---------- 显示与自动关闭 ----------

        public void ShowPopup()
        {
            if (_engine.State != AppState.Running) return;

            UpdateUi();
            if (!Visible) Show();
            else BringToFront();

            // 显示后（句柄与 DPI 缩放均就绪）按最终尺寸重新定位
            ApplyDpiScale();
            ApplyRoundedRegion();
            Position();

            _closeAfterSeconds = ComputeCloseSeconds();
            _hoverDepth = 0;
            _shownFor.Restart();
            _autoClose.Start();
        }

        /// <summary>
        /// 首次提醒与第 1 次重申显示完整时长；第 2 次重申起缩短到 60%（下限 3 秒），
        /// 既保证能看见，又避免反复打扰。
        /// </summary>
        private double ComputeCloseSeconds()
        {
            int seconds = Math.Max(3, _engine.Config.PopupSeconds);
            int nudge = _engine.NudgeIndex;
            return nudge <= 2 ? seconds : Math.Max(3.0, Math.Round(seconds * 0.6));
        }

        private void OnAutoCloseTick(object sender, EventArgs e)
        {
            if (_hoverDepth > 0) return; // 鼠标悬停时暂停倒计时
            if (_shownFor.Elapsed.TotalSeconds >= _closeAfterSeconds) Dismiss();
        }

        private void Dismiss()
        {
            _autoClose.Stop();
            _shownFor.Reset();
            _hoverDepth = 0;
            Hide();
        }

        private void OnHoverEnter(object sender, EventArgs e)
        {
            if (_hoverDepth++ == 0) _shownFor.Stop();
        }

        private void OnHoverLeave(object sender, EventArgs e)
        {
            if (_hoverDepth > 0) _hoverDepth--;
            if (_hoverDepth == 0 && Visible) _shownFor.Start();
        }

        private void Position()
        {
            Screen screen = Screen.FromPoint(Cursor.Position);
            if (screen == null) screen = Screen.PrimaryScreen;
            Rectangle wa = screen.WorkingArea;
            // 贴右下角，同时保证不越出工作区（高 DPI 放大后也不会跑到屏幕外）
            Location = new Point(
                Math.Max(wa.Left, wa.Right - Width - 16),
                Math.Max(wa.Top, wa.Bottom - Height - 16));
        }

        private void OnEngineTick()
        {
            if (Visible) UpdateUi();
        }

        private void OnStateChanged()
        {
            if (_engine.State != AppState.Running)
            {
                // 已离开 / 已暂停：卡片没有意义，立即收起
                Dismiss();
            }
            else if (Visible)
            {
                UpdateUi();
            }
        }

        private void UpdateUi()
        {
            ApplySeatedText(string.Format("已连续坐 {0}",
                TimerEngine.FormatDuration(_engine.SessionElapsed)));
        }

        /// <summary>
        /// 设置“已连续坐 X”文字；若文字超宽则自动减小字号，
        /// 保证在固定宽度卡片内完整显示（长时长 / 高 DPI 缩放时不会截断）。
        /// </summary>
        private void ApplySeatedText(string text)
        {
            if (text == _seatedText) return;
            _seatedText = text;

            int rightPad = (int)Math.Round(24 * UiTheme.DpiScale(this));
            int maxWidth = Width - _lblSeated.Left - rightPad;
            if (maxWidth < 120) maxWidth = 120;

            float best = SeatedFontSizes[SeatedFontSizes.Length - 1]; // 兜底最小字号
            foreach (float size in SeatedFontSizes)
            {
                using (var probe = new Font("Segoe UI", size, FontStyle.Bold))
                {
                    if (TextRenderer.MeasureText(text, probe).Width <= maxWidth)
                    {
                        best = size;
                        break;
                    }
                }
            }

            _lblSeated.Text = text;
            if (Math.Abs(best - _seatedFontSize) > 0.01f)
            {
                _seatedFontSize = best;
                Font old = _seatedFont;
                _seatedFont = new Font("Segoe UI", best, FontStyle.Bold);
                _lblSeated.Font = _seatedFont;
                if (old != null) old.Dispose();
            }
        }

        private void Unsubscribe()
        {
            _engine.Tick -= OnEngineTick;
            _engine.StateChanged -= OnStateChanged;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Unsubscribe();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            // FormClosed 不会在 Dispose 时触发，这里再退订一次，避免泄漏
            if (disposing)
            {
                Unsubscribe();
                if (_autoClose != null && !_timerDisposed)
                {
                    _timerDisposed = true;
                    _autoClose.Stop();
                    _autoClose.Tick -= OnAutoCloseTick;
                    _autoClose.Dispose();
                }
                if (_seatedFont != null)
                {
                    _seatedFont.Dispose();
                    _seatedFont = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}

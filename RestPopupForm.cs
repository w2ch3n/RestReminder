using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RestReminder
{
    /// <summary>
    /// 右下角休息提醒弹窗：展示剩余休息时长（或推迟倒计时），
    /// 支持 跳过休息 / 推迟休息，右上角 ✕ 可隐藏（休息在托盘继续）。
    /// </summary>
    public class RestPopupForm : Form
    {
        private readonly TimerEngine _engine;

        private Label _lblTitle;
        private Label _lblCountdown;
        private Label _lblSub;
        private Label _lblClose;
        private Button _btnSkip;
        private Button _btnPostpone;

        private static readonly Color BgColor = Color.FromArgb(28, 34, 58);
        private static readonly Color AccentColor = Color.FromArgb(47, 84, 235);
        private static readonly Color SubTextColor = Color.FromArgb(168, 179, 208);
        private static readonly Color BtnNeutral = Color.FromArgb(56, 66, 100);
        private static readonly Color BtnNeutralHover = Color.FromArgb(72, 84, 122);
        private static readonly Color BtnAccentHover = Color.FromArgb(64, 102, 245);

        public RestPopupForm(TimerEngine engine)
        {
            _engine = engine;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Size = new Size(384, 186);
            BackColor = BgColor;
            Font = new Font("Microsoft YaHei UI", 9f);
            DoubleBuffered = true;

            BuildUi();
            ApplyRoundedRegion();

            _engine.Tick += OnEngineTick;
            _engine.StateChanged += OnStateChanged;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        // ---------- UI 构建 ----------

        private void BuildUi()
        {
            // 标题
            _lblTitle = new Label
            {
                Text = "该休息啦！",
                AutoSize = true,
                BackColor = Color.Transparent,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold),
                Location = new Point(28, 20),
            };

            // 大号倒计时
            _lblCountdown = new Label
            {
                Text = "05:00",
                AutoSize = true,
                BackColor = Color.Transparent,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 32f, FontStyle.Bold),
                Location = new Point(24, 46),
            };

            // 副标题
            _lblSub = new Label
            {
                Text = "剩余休息时间 · 站起来活动一下吧",
                AutoSize = true,
                BackColor = Color.Transparent,
                ForeColor = SubTextColor,
                Font = new Font("Microsoft YaHei UI", 9f),
                Location = new Point(28, 104),
            };

            // 右上角关闭（隐藏弹窗，休息继续）
            _lblClose = new Label
            {
                Text = "✕",
                AutoSize = false,
                Size = new Size(26, 26),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
                ForeColor = SubTextColor,
                Font = new Font("Segoe UI", 11f),
                Cursor = Cursors.Hand,
                Location = new Point(Width - 34, 8),
            };
            _lblClose.MouseEnter += (s, e) => _lblClose.ForeColor = Color.White;
            _lblClose.MouseLeave += (s, e) => _lblClose.ForeColor = SubTextColor;
            _lblClose.Click += (s, e) => Hide();

            // 跳过休息
            _btnSkip = MakeButton("跳过休息", BtnNeutral, BtnNeutralHover, Color.White,
                new Rectangle(28, 132, 116, 34));
            _btnSkip.Click += (s, e) =>
            {
                _engine.SkipRest();
                Hide();
            };

            // 推迟休息
            _btnPostpone = MakeButton("推迟休息", AccentColor, BtnAccentHover, Color.White,
                new Rectangle(154, 132, 130, 34));
            _btnPostpone.Click += (s, e) =>
            {
                _engine.PostponeRest();
                UpdateUi();
            };

            Controls.Add(_lblTitle);
            Controls.Add(_lblCountdown);
            Controls.Add(_lblSub);
            Controls.Add(_lblClose);
            Controls.Add(_btnSkip);
            Controls.Add(_btnPostpone);
        }

        private static Button MakeButton(string text, Color back, Color hover, Color fore, Rectangle bounds)
        {
            var b = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = back,
                ForeColor = fore,
                Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Bounds = bounds,
                TabStop = false,
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = hover;
            b.FlatAppearance.MouseDownBackColor = hover;
            return b;
        }

        private void ApplyRoundedRegion()
        {
            int r = 14;
            using (var path = new GraphicsPath())
            {
                int w = Width, h = Height;
                path.AddArc(0, 0, r, r, 180, 90);
                path.AddArc(w - r, 0, r, r, 270, 90);
                path.AddArc(w - r, h - r, r, r, 0, 90);
                path.AddArc(0, h - r, r, r, 90, 90);
                path.CloseFigure();
                Region = new Region(path);
            }
        }

        // ---------- 显示与定位 ----------

        /// <summary>显示在鼠标所在屏幕的右下角（不抢焦点）。</summary>
        public void ShowPopup()
        {
            Position();
            UpdateUi();
            if (!Visible) Show();
            else BringToFront();
        }

        private void Position()
        {
            Screen screen = Screen.FromPoint(Cursor.Position);
            if (screen == null) screen = Screen.PrimaryScreen;
            Rectangle wa = screen.WorkingArea;
            Location = new Point(wa.Right - Width - 16, wa.Bottom - Height - 16);
        }

        // ---------- 状态刷新 ----------

        private void OnEngineTick()
        {
            if (Visible) UpdateUi();
        }

        private void OnStateChanged()
        {
            if (_engine.State == AppState.Work)
            {
                Hide();
            }
            else if (Visible)
            {
                UpdateUi();
            }
        }

        private void UpdateUi()
        {
            if (_engine.State == AppState.Resting)
            {
                _lblTitle.Text = "该休息啦！";
                _lblCountdown.Text = FormatTime(_engine.RestRemaining);
                _lblSub.Text = "剩余休息时间 · 站起来活动一下吧";
                _btnPostpone.Text = string.Format("推迟 {0} 分钟", _engine.Config.PostponeMinutes);
            }
            else if (_engine.State == AppState.RestPending)
            {
                _lblTitle.Text = "休息已推迟";
                _lblCountdown.Text = FormatTime(_engine.PostponeRemaining);
                _lblSub.Text = "休息将在倒计时结束后开始，先继续工作吧";
                _btnPostpone.Text = string.Format("再推迟 {0} 分钟", _engine.Config.PostponeMinutes);
            }

            bool restRelated = _engine.State == AppState.Resting || _engine.State == AppState.RestPending;
            _btnSkip.Visible = restRelated;
            _btnPostpone.Visible = restRelated;
        }

        private static string FormatTime(TimeSpan ts)
        {
            if (ts <= TimeSpan.Zero) return "00:00";
            int totalSeconds = (int)Math.Ceiling(ts.TotalSeconds);
            return string.Format("{0:00}:{1:00}", totalSeconds / 60, totalSeconds % 60);
        }

        // ---------- 关闭时解绑 ----------

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _engine.Tick -= OnEngineTick;
            _engine.StateChanged -= OnStateChanged;
            base.OnFormClosed(e);
        }
    }
}

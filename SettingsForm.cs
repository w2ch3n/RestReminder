using System;
using System.Drawing;
using System.Windows.Forms;

namespace RestReminder
{
    /// <summary>
    /// 设置窗口：自定义工作时长/休息时长/推迟时长、开机自启、
    /// 锁屏睡眠暂停、空闲检测、提示音。
    /// </summary>
    public class SettingsForm : Form
    {
        private readonly TimerEngine _engine;
        private AppConfig _config;

        private NumericUpDown _nudWork, _nudRest, _nudPostpone;
        private CheckBox _chkAutoStart, _chkLockPause, _chkIdlePause, _chkSound;

        public SettingsForm(TimerEngine engine, AppConfig config)
        {
            _engine = engine;
            _config = config;

            Text = "久坐休息提醒 - 设置";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(420, 448);
            Font = new Font("Microsoft YaHei UI", 9f);
            Icon = IconFactory.CreateAppIcon();

            BuildUi();
            LoadFromConfig();
        }

        private void BuildUi()
        {
            // ---- 时长设置 ----
            var grpTime = new GroupBox
            {
                Text = "时长设置（分钟）",
                Bounds = new Rectangle(20, 20, 380, 104),
            };
            _nudWork = MakeNud();
            _nudRest = MakeNud();
            AddRow(grpTime, "工作时长", _nudWork, 30);
            AddRow(grpTime, "休息时长", _nudRest, 66);

            // ---- 推迟时长 ----
            var grpPostpone = new GroupBox
            {
                Text = "推迟休息",
                Bounds = new Rectangle(20, 132, 380, 64),
            };
            _nudPostpone = MakeNud();
            AddRow(grpPostpone, "推迟时长（分钟）", _nudPostpone, 28);

            // ---- 行为选项 ----
            var grpOpt = new GroupBox
            {
                Text = "行为选项",
                Bounds = new Rectangle(20, 204, 380, 168),
            };

            _chkAutoStart = new CheckBox
            {
                Text = "开机自动启动",
                AutoSize = true,
                Location = new Point(18, 30),
            };
            _chkLockPause = new CheckBox
            {
                Text = "锁屏 / 睡眠时暂停计时",
                AutoSize = true,
                Location = new Point(18, 64),
            };
            _chkIdlePause = new CheckBox
            {
                Text = "离开电脑（无操作超5分钟）时暂停",
                AutoSize = true,
                Location = new Point(18, 98),
            };
            _chkSound = new CheckBox
            {
                Text = "提醒时播放提示音",
                AutoSize = true,
                Location = new Point(18, 132),
            };
            grpOpt.Controls.Add(_chkAutoStart);
            grpOpt.Controls.Add(_chkLockPause);
            grpOpt.Controls.Add(_chkIdlePause);
            grpOpt.Controls.Add(_chkSound);

            // ---- 按钮 ----
            var btnSave = new Button
            {
                Text = "保存",
                Bounds = new Rectangle(150, 388, 92, 32),
                BackColor = Color.FromArgb(47, 84, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += OnSave;

            var btnCancel = new Button
            {
                Text = "取消",
                Bounds = new Rectangle(262, 388, 92, 32),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
            };
            btnCancel.Click += (s, e) => Close();

            Controls.Add(grpTime);
            Controls.Add(grpPostpone);
            Controls.Add(grpOpt);
            Controls.Add(btnSave);
            Controls.Add(btnCancel);
        }

        private static NumericUpDown MakeNud()
        {
            return new NumericUpDown
            {
                Location = new Point(220, 0),
                Width = 70,
                Minimum = 1,
                Maximum = 600,
                Value = 5,
            };
        }

        /// <summary>向分组中添加“标签 + 数值框”一行。</summary>
        private static void AddRow(Control parent, string label, NumericUpDown nud, int y)
        {
            var lbl = new Label
            {
                Text = label,
                AutoSize = true,
                Location = new Point(18, y + 3),
            };
            nud.Location = new Point(220, y);
            parent.Controls.Add(lbl);
            parent.Controls.Add(nud);
        }

        private void LoadFromConfig()
        {
            _nudWork.Value = Clamp(_config.WorkMinutes, 1, 600);
            _nudRest.Value = Clamp(_config.RestMinutes, 1, 120);
            _nudPostpone.Value = Clamp(_config.PostponeMinutes, 1, 120);
            _chkAutoStart.Checked = _config.AutoStart;
            _chkLockPause.Checked = _config.PauseOnLockScreen;
            _chkIdlePause.Checked = _config.PauseOnIdle;
            _chkSound.Checked = _config.SoundOnRemind;
        }

        private void OnSave(object sender, EventArgs e)
        {
            _config.WorkMinutes = (int)_nudWork.Value;
            _config.RestMinutes = (int)_nudRest.Value;
            _config.PostponeMinutes = (int)_nudPostpone.Value;
            _config.AutoStart = _chkAutoStart.Checked;
            _config.PauseOnLockScreen = _chkLockPause.Checked;
            _config.PauseOnIdle = _chkIdlePause.Checked;
            _config.SoundOnRemind = _chkSound.Checked;

            _config.Save();
            AutoStart.SetEnabled(_config.AutoStart);
            _engine.ApplyConfig(_config);

            MessageBox.Show(this, "设置已保存。", "久坐休息提醒",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}

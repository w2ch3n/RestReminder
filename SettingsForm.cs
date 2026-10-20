using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace RestReminder
{
    /// <summary>
    /// 设置窗口（久坐会话制参数）——卡片式现代设计：
    /// 浅灰蓝底 + 白色圆角卡片分区 + 强调色标题条 + 圆角按钮。
    /// 固定坐标（按 100% DPI 精确计算），数值控件靠右、数字左对齐；
    /// 按钮：保存在左、取消在右，中间留空隙。
    /// </summary>
    public class SettingsForm : Form
    {
        private readonly TimerEngine _engine;
        private AppConfig _config;

        private NumericUpDown _nudRemind, _nudPre, _nudLeave, _nudRepeat, _nudPopup;
        private CheckBox _chkAutoStart, _chkSound;
        private ComboBox _cboPresence;
        private TextBox _txtSsid;
        private ModernButton _btnReadSsid;
        private Label _lblNote;
        private bool _loading; // 加载配置期间不触发自动读取热点
        private readonly List<KeyValuePair<Label, Control>> _rows = new List<KeyValuePair<Label, Control>>();

        public SettingsForm(TimerEngine engine, AppConfig config)
        {
            _engine = engine;
            _config = config;

            // 不使用框架自动缩放（在纯代码创建窗体上行为不可靠），
            // 改为界面构建完成后按 DPI 显式等比缩放，行为完全可预测。
            AutoScaleMode = AutoScaleMode.None;

            Text = "久坐休息提醒 - 设置";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(540, 574);
            BackColor = UiTheme.WindowBg;
            Font = new Font("Microsoft YaHei UI", 9f);
            Icon = IconFactory.CreateAppIcon();

            BuildUi();
            ApplyDpiScale(); // 按当前 DPI 等比放大整个界面
            // 数值在 OnLoad（句柄已创建）时设置，避免“数值未重绘”的空白
        }

        private bool _dpiScaled;

        /// <summary>
        /// 按当前 DPI 把整个界面等比放大（设计基准 96 DPI）。
        /// 150% 缩放下窗口、间距、控件与字号一起放大 1.5 倍，
        /// 不会出现“字大框小”的截断或拥挤。
        /// </summary>
        private void ApplyDpiScale()
        {
            if (_dpiScaled) return;
            float k = UiTheme.DpiScale(this);
            if (k <= 1.001f) return; // 100% 缩放：保持设计尺寸

            _dpiScaled = true;
            SuspendLayout();
            try
            {
                Scale(new SizeF(k, k));
                ClientSize = new Size(
                    (int)Math.Round(540 * k),
                    (int)Math.Round(574 * k));
            }
            finally
            {
                ResumeLayout(false);
                PerformLayout(); // 让 AutoSize 标签按新字号重新测量
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplyDpiScale(); // 若构造时 DPI 尚未就绪，这里补做（幂等）
            LoadFromConfig();
            LayoutPresenceControls(); // 按实测文字宽度自适应（适配任意 DPI 缩放）
            AlignRowLabels();         // 标签与控件文字垂直居中对齐

            // 说明文字的换行宽度按窗口实际宽度计算（高 DPI 下也会正确换行）
            if (_lblNote != null)
            {
                int sidePadding = (int)Math.Round(52 * UiTheme.DpiScale(this));
                _lblNote.MaximumSize = new Size(
                    Math.Max(200, ClientSize.Width - sidePadding), 0);
            }

            // 小屏幕/低分辨率下避免窗口底部或右侧超出工作区
            Screen screen = Screen.FromControl(this);
            if (screen != null)
            {
                Rectangle wa = screen.WorkingArea;
                if (Height > wa.Height)
                {
                    // 放不下时限制窗口高度并允许滚动（正常分辨率下不会出现滚动条）
                    AutoScroll = true;
                    Height = wa.Height;
                }
                if (Top + Height > wa.Bottom) Top = Math.Max(wa.Top, wa.Bottom - Height);
                if (Left + Width > wa.Right) Left = Math.Max(wa.Left, wa.Right - Width);
                if (Top < wa.Top) Top = wa.Top;
                if (Left < wa.Left) Left = wa.Left;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // 窗口显示后再校正一次实测布局，确保定位精确
            LayoutPresenceControls();
            AlignRowLabels();
            if (_lblNote != null)
            {
                int sidePadding = (int)Math.Round(52 * UiTheme.DpiScale(this));
                _lblNote.MaximumSize = new Size(
                    Math.Max(200, ClientSize.Width - sidePadding), 0);
            }
        }

        private void BuildUi()
        {
            // ---- 久坐提醒（阈值 + 节奏，四行一卡） ----
            var card1 = MakeCard(24, 14, 492, 180, "久坐提醒");
            _nudRemind = MakeNud(1, 600, 60);
            _nudPre = MakeNud(0, 30, 3);
            _nudRepeat = MakeNud(1, 120, 15);
            _nudPopup = MakeNud(3, 60, 5);
            AddRow(card1, "连续久坐多久提醒（分钟）", _nudRemind, 48);
            AddRow(card1, "到点前提前预告（分钟，0=关闭）", _nudPre, 80);
            AddRow(card1, "被忽略后隔多久重申（分钟）", _nudRepeat, 112);
            AddRow(card1, "提醒弹窗停留时长（秒）", _nudPopup, 144);

            // ---- 在场判定（离开阈值 + 判定方式 + 热点名称） ----
            var card3 = MakeCard(24, 208, 492, 156, "在场判定");
            _nudLeave = MakeNud(1, 30, 2);
            AddRow(card3, "多久无操作视为已离开（分钟）", _nudLeave, 48);

            var lblMode = MakeRowLabel("判定方式", 88);
            card3.Controls.Add(lblMode);
            _cboPresence = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Bounds = new Rectangle(180, 84, 292, 26),
                Font = new Font("Microsoft YaHei UI", 9f),
                BackColor = UiTheme.CardBg,
            };
            _cboPresence.Items.Add("仅检测本机键鼠活动");
            _cboPresence.Items.Add("检测本机键鼠活动或手机热点连接");
            _cboPresence.SelectedIndexChanged += (s, e) =>
            {
                SyncSsidEnabled();
                if (_loading) return;

                if (_cboPresence.SelectedIndex == 1)
                {
                    // 切到热点模式且名称为空时，自动填入当前连接的热点，省去手动输入
                    if (_txtSsid.Text.Trim().Length == 0)
                    {
                        var ssids = WifiPresenceMonitor.QueryConnectedSsids();
                        if (ssids.Count > 0) _txtSsid.Text = ssids[0];
                    }
                }
                else
                {
                    // 切回“仅检测本机键鼠活动”：清空热点名称
                    _txtSsid.Text = string.Empty;
                }
            };
            card3.Controls.Add(_cboPresence);
            RegisterRow(lblMode, _cboPresence);

            var lblSsid = MakeRowLabel("手机热点名称", 124);
            card3.Controls.Add(lblSsid);
            _txtSsid = new TextBox
            {
                Bounds = new Rectangle(236, 120, 150, 26),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 9f),
            };
            card3.Controls.Add(_txtSsid);
            RegisterRow(lblSsid, _txtSsid);

            _btnReadSsid = new ModernButton
            {
                Text = "读取当前",
                Bounds = new Rectangle(394, 120, 78, 26),
                Outline = true,
                Radius = 6,
                Font = new Font("Microsoft YaHei UI", 8.5f),
            };
            _btnReadSsid.Click += OnReadCurrentSsid;
            card3.Controls.Add(_btnReadSsid);

            // ---- 行为选项（单行并排） ----
            var card4 = MakeCard(24, 378, 492, 80, "行为选项");
            _chkAutoStart = new CheckBox
            {
                Text = "开机自动启动",
                AutoSize = true,
                BackColor = UiTheme.CardBg,
                ForeColor = UiTheme.LabelColor,
                Location = new Point(24, 48),
            };
            _chkSound = new CheckBox
            {
                Text = "提醒时播放提示音",
                AutoSize = true,
                BackColor = UiTheme.CardBg,
                ForeColor = UiTheme.LabelColor,
                Location = new Point(240, 48),
            };
            card4.Controls.Add(_chkAutoStart);
            card4.Controls.Add(_chkSound);

            // ---- 说明（自动换行） ----
            _lblNote = new Label
            {
                Text = "锁屏/睡眠一定会清零本轮久坐；按所选方式判定“离开”时同样清零。提醒可随时忽略或稍后提醒，不强制休息。",
                AutoSize = true,
                MaximumSize = new Size(488, 0),
                BackColor = UiTheme.WindowBg,
                ForeColor = UiTheme.NoteColor,
                Font = new Font("Microsoft YaHei UI", 8.5f),
                Location = new Point(26, 470),
            };
            Controls.Add(_lblNote);

            // ---- 按钮：居中排布，保存在左、取消在右，中间留空隙 ----
            var btnSave = new ModernButton
            {
                Text = "保 存",
                Bounds = new Rectangle(112, 516, 150, 42),
            };
            btnSave.Click += OnSave;

            var btnCancel = new ModernButton
            {
                Text = "取 消",
                Bounds = new Rectangle(278, 516, 150, 42),
                Outline = true,
            };
            btnCancel.Click += (s, e) => Close();

            Controls.Add(btnSave);
            Controls.Add(btnCancel);
        }

        /// <summary>创建卡片分区（强调条与标题由卡片自绘，保证垂直对齐）。</summary>
        private RoundedCard MakeCard(int x, int y, int w, int h, string title)
        {
            var card = new RoundedCard
            {
                Bounds = new Rectangle(x, y, w, h),
                BackColor = UiTheme.WindowBg, // 让圆角外露出的角落与窗口底色一致
                Title = title,
            };
            Controls.Add(card);
            return card;
        }

        /// <summary>
        /// 向卡片添加“标签 + 数值控件”一行。
        /// 数值控件宽 92，靠卡片右缘（右边留 20px 内边距）。
        /// </summary>
        private void AddRow(Control card, string label, NumericUpDown nud, int y)
        {
            var lbl = new Label
            {
                Text = label,
                AutoSize = true,
                BackColor = UiTheme.CardBg,
                ForeColor = UiTheme.LabelColor,
                Location = new Point(24, y + 4),
            };
            nud.Location = new Point(card.Width - 20 - 92, y);
            card.Controls.Add(lbl);
            card.Controls.Add(nud);
            RegisterRow(lbl, nud);
        }

        /// <summary>登记“标签 + 控件”，供显示后做垂直居中对齐。</summary>
        private void RegisterRow(Label label, Control control)
        {
            _rows.Add(new KeyValuePair<Label, Control>(label, control));
        }

        /// <summary>
        /// 让左侧标签与右侧控件的文字垂直居中对齐。
        /// 控件（下拉框、文本框等）的实际高度由字体/DPI 决定，
        /// 因此按“控件高度 - 标签高度”实时计算，而不是写死偏移。
        /// </summary>
        private void AlignRowLabels()
        {
            foreach (var row in _rows)
            {
                Label label = row.Key;
                Control control = row.Value;
                if (label == null || control == null || label.IsDisposed || control.IsDisposed) continue;
                if (control.Height <= 0) continue;

                // 缩放会把 AutoSize 标签的尺寸二次放大，这里强制按当前 DPI 重新测量
                label.AutoSize = false;
                label.AutoSize = true;

                label.Top = control.Top + (control.Height - label.Height) / 2;
            }
        }

        /// <summary>添加一行右侧控件用的标签（与 AddRow 的标签样式一致）。</summary>
        private static Label MakeRowLabel(string text, int y)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                BackColor = UiTheme.CardBg,
                ForeColor = UiTheme.LabelColor,
                Location = new Point(24, y + 4),
            };
        }

        /// <summary>
        /// 在场判定区的控件尺寸按【实测文字宽度】计算，而不是写死像素：
        /// 窗体已按 DPI 等比缩放，这里再用实测宽度兜底，任何缩放下都不会截断。
        /// </summary>
        private void LayoutPresenceControls()
        {
            if (_cboPresence.Parent == null) return;

            float k = UiTheme.DpiScale(this);          // 当前 DPI 相对设计基准(96)的比例
            int margin = (int)Math.Round(20 * k);      // 卡片右侧内边距
            int cardWidth = _cboPresence.Parent.Width;
            int right = cardWidth - margin;

            // 下拉框：最宽选项的实测宽度 + 下拉箭头 + 内边距
            int textWidth = 0;
            foreach (object item in _cboPresence.Items)
            {
                if (item == null) continue;
                textWidth = Math.Max(textWidth,
                    TextRenderer.MeasureText(item.ToString(), _cboPresence.Font).Width);
            }
            int comboWidth = textWidth + SystemInformation.VerticalScrollBarWidth + (int)Math.Round(14 * k);
            comboWidth = Math.Min(comboWidth, right - (int)Math.Round(120 * k)); // 左侧给标签留空间
            _cboPresence.Left = right - comboWidth;
            _cboPresence.Width = comboWidth;

            // “读取当前”按钮：实测文字宽度 + 内边距
            Size buttonText = TextRenderer.MeasureText(_btnReadSsid.Text, _btnReadSsid.Font);
            int buttonWidth = buttonText.Width + (int)Math.Round(20 * k);
            _btnReadSsid.Left = right - buttonWidth;
            _btnReadSsid.Width = buttonWidth;

            // 热点名称输入框：填满标签右侧到按钮左侧的空间
            int gap = (int)Math.Round(8 * k);
            int fieldLeft = Math.Max((int)Math.Round(150 * k), _cboPresence.Left);
            _txtSsid.Left = fieldLeft;
            _txtSsid.Width = Math.Max((int)Math.Round(80 * k), _btnReadSsid.Left - gap - fieldLeft);

            // 按钮与输入框垂直居中对齐
            if (_txtSsid.Height > 0)
            {
                _btnReadSsid.Top = _txtSsid.Top + (_txtSsid.Height - _btnReadSsid.Height) / 2;
            }
        }

        /// <summary>热点名称输入框/读取按钮只在“热点在场”方式下可用。</summary>
        private void SyncSsidEnabled()
        {
            bool on = _cboPresence.SelectedIndex == 1;
            _txtSsid.Enabled = on;
            _btnReadSsid.Enabled = on;
        }

        /// <summary>把当前连接的 Wi-Fi 名称填进热点名输入框。</summary>
        private void OnReadCurrentSsid(object sender, EventArgs e)
        {
            var ssids = WifiPresenceMonitor.QueryConnectedSsids();
            if (ssids.Count == 0)
            {
                MessageBox.Show(this,
                    "没有读取到已连接的 Wi-Fi 热点。\n请确认这台电脑当前连的就是手机热点。",
                    "久坐休息提醒", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _txtSsid.Text = ssids[0];
        }

        private static NumericUpDown MakeNud(int min, int max, int def)
        {
            return new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Value = def,
                Width = 92,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = HorizontalAlignment.Left, // 数字左对齐
                ThousandsSeparator = false,
            };
        }

        private void LoadFromConfig()
        {
            _loading = true;
            try
            {
                _nudRemind.Value = Clamp(_config.RemindMinutes, 1, 600);
                _nudPre.Value = Clamp(_config.PreRemindMinutes, 0, 30);
                _nudLeave.Value = Clamp(_config.LeaveMinutes, 1, 30);
                _nudRepeat.Value = Clamp(_config.RepeatMinutes, 1, 120);
                _nudPopup.Value = Clamp(_config.PopupSeconds, 3, 60);
                _chkAutoStart.Checked = _config.AutoStart;
                _chkSound.Checked = _config.SoundOnRemind;

                _cboPresence.SelectedIndex = _config.PresenceMode == 1 ? 1 : 0;
                // 仅本机模式下不展示热点名称（与切换时清空的规则一致）
                _txtSsid.Text = _config.PresenceMode == 1 ? _config.PresenceSsid : string.Empty;
                SyncSsidEnabled();
            }
            finally
            {
                _loading = false;
            }

            _nudRemind.Update();
            _nudPre.Update();
            _nudLeave.Update();
            _nudRepeat.Update();
            _nudPopup.Update();
        }

        private void OnSave(object sender, EventArgs e)
        {
            int remind = (int)_nudRemind.Value;
            _config.RemindMinutes = remind;
            // 提前预告必须早于提醒点，否则永远不会触发
            _config.PreRemindMinutes = Math.Min((int)_nudPre.Value, Math.Max(0, remind - 1));
            _config.LeaveMinutes = (int)_nudLeave.Value;
            _config.RepeatMinutes = (int)_nudRepeat.Value;
            _config.PopupSeconds = (int)_nudPopup.Value;
            _config.AutoStart = _chkAutoStart.Checked;
            _config.SoundOnRemind = _chkSound.Checked;

            // 在场检测（0=只看本机键鼠；1=热点连着也算在场；后续可扩展更多值）
            int presenceMode = _cboPresence.SelectedIndex == 1 ? 1 : 0;
            _config.PresenceMode = presenceMode;
            _config.PresenceSsid = presenceMode == 1 ? _txtSsid.Text.Trim() : string.Empty;

            if (_config.PresenceMode == 1 && _config.PresenceSsid.Length == 0)
            {
                MessageBox.Show(this,
                    "选择“检测本机键鼠活动或手机热点连接”时，需要填写手机热点名称（Wi-Fi 名称）。\n" +
                    "可以点右侧“读取当前”自动填入。",
                    "久坐休息提醒", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

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

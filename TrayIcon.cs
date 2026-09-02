using System;
using System.Drawing;
using System.Windows.Forms;

namespace RestReminder
{
    /// <summary>
    /// 系统托盘：图标、悬浮提示、右键菜单（立即休息/跳过/暂停/设置/开机自启/退出）。
    /// </summary>
    public class TrayIcon : IDisposable
    {
        private readonly TimerEngine _engine;
        private readonly NotifyIcon _notify;
        private readonly ToolStripMenuItem _miStatus;
        private readonly ToolStripMenuItem _miSkip;
        private readonly ToolStripMenuItem _miPause;
        private readonly ToolStripMenuItem _miAutoStart;

        public event Action SettingsRequested;
        public event Action ExitRequested;

        public TrayIcon(TimerEngine engine)
        {
            _engine = engine;

            _miStatus = new ToolStripMenuItem("状态") { Enabled = false };
            _miSkip = new ToolStripMenuItem("跳过休息");
            _miPause = new ToolStripMenuItem("暂停计时");
            _miAutoStart = new ToolStripMenuItem("开机自启") { CheckOnClick = true };
            var miRest = new ToolStripMenuItem("立即休息");
            var miSettings = new ToolStripMenuItem("设置...");
            var miExit = new ToolStripMenuItem("退出");

            _miSkip.Click += (s, e) => _engine.SkipRest();
            _miPause.Click += (s, e) => _engine.SetManualPause(!_engine.ManualPaused);
            miRest.Click += (s, e) => _engine.StartRestNow();
            miSettings.Click += (s, e) => SettingsRequested?.Invoke();
            miExit.Click += (s, e) => ExitRequested?.Invoke();
            _miAutoStart.CheckedChanged += (s, e) => AutoStart.SetEnabled(_miAutoStart.Checked);

            var menu = new ContextMenuStrip();
            menu.Items.Add(_miStatus);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miRest);
            menu.Items.Add(_miSkip);
            menu.Items.Add(_miPause);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miSettings);
            menu.Items.Add(_miAutoStart);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miExit);

            _notify = new NotifyIcon
            {
                Icon = IconFactory.CreateAppIcon(),
                Text = "久坐休息提醒",
                ContextMenuStrip = menu,
                Visible = true,
            };
            _notify.DoubleClick += (s, e) => SettingsRequested?.Invoke();

            _engine.Tick += UpdateUi;
            _engine.StateChanged += UpdateUi;
            UpdateUi();
        }

        private void UpdateUi()
        {
            _miStatus.Text = _engine.StatusText;
            bool restRelated = _engine.State == AppState.Resting || _engine.State == AppState.RestPending;
            _miSkip.Enabled = restRelated;
            _miPause.Text = _engine.ManualPaused ? "继续计时" : "暂停计时";
            _miAutoStart.Checked = AutoStart.IsEnabled();
            _notify.Text = string.Format("久坐休息提醒 - {0}", _engine.StatusText);
        }

        public void ShowBalloon(string title, string text)
        {
            _notify.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);
        }

        public void Dispose()
        {
            _engine.Tick -= UpdateUi;
            _engine.StateChanged -= UpdateUi;
            _notify.Visible = false;
            _notify.Dispose();
        }
    }
}

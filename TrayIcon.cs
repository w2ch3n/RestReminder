using System;
using System.Drawing;
using System.Windows.Forms;

namespace RestReminder
{
    /// <summary>
    /// 系统托盘：图标、悬浮提示、右键菜单
    /// （暂停提醒 / 重置本轮 / 设置 / 开机自启 / 退出）。
    /// </summary>
    public class TrayIcon : IDisposable
    {
        private const int TooltipMaxLength = 63; // NotifyIcon.Text 的 .NET Framework 上限

        private readonly TimerEngine _engine;
        private readonly NotifyIcon _notify;
        private readonly ToolStripMenuItem _miPause;
        private readonly ToolStripMenuItem _miAutoStart;
        private readonly ContextMenuStrip _menu;
        private readonly Action<bool> _onPauseChanged;   // 保存委托，才能在 Dispose 时正确退订
        private string _lastTooltip;
        private bool _syncingAutoStart;

        public event Action SettingsRequested;
        public event Action ExitRequested;

        public TrayIcon(TimerEngine engine)
        {
            _engine = engine;
            _onPauseChanged = _ => UpdateUi();

            _miPause = new ToolStripMenuItem("暂停提醒");
            _miAutoStart = new ToolStripMenuItem("开机自启") { CheckOnClick = true };
            var miReset = new ToolStripMenuItem("重置本轮久坐计时");
            var miSettings = new ToolStripMenuItem("设置...");
            var miExit = new ToolStripMenuItem("退出");

            _miPause.Click += (s, e) => _engine.SetManualPause(!_engine.ManualPaused);
            miReset.Click += (s, e) => _engine.ResetSession();
            miSettings.Click += (s, e) => SettingsRequested?.Invoke();
            miExit.Click += (s, e) => ExitRequested?.Invoke();
            _miAutoStart.CheckedChanged += (s, e) =>
            {
                if (_syncingAutoStart) return; // 程序同步状态时不回写注册表
                AutoStart.SetEnabled(_miAutoStart.Checked);
            };

            // 托盘悬浮提示里已经展示时间，菜单不再重复；宽度恢复默认，保证勾选区可见
            var menu = new ContextMenuStrip
            {
                Font = new Font("Microsoft YaHei UI", 9f),
                Renderer = new ThemedMenuRenderer(),
            };
            _menu = menu;
            menu.Items.Add(_miPause);
            menu.Items.Add(miReset);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miSettings);
            menu.Items.Add(_miAutoStart);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miExit);

            // 打开菜单时才从注册表读取自启状态，避免每次 Tick 都访问注册表
            menu.Opening += (s, e) => SyncMenuState();

            _notify = new NotifyIcon
            {
                // 托盘图标尺寸按 DPI 计算：100%→16px、150%→24px
                // （部分环境下 SystemInformation.SmallIconSize 不随 DPI 缩放，取两者较大值兜底）
                Icon = IconFactory.CreateAppIcon(GetTrayIconSize()),
                Text = "久坐休息提醒",
                ContextMenuStrip = menu,
                Visible = true,
            };
            _notify.DoubleClick += (s, e) => SettingsRequested?.Invoke();

            _engine.Tick += UpdateUi;
            _engine.StateChanged += UpdateUi;
            _engine.PauseChanged += _onPauseChanged;
            UpdateUi();
        }

        /// <summary>
        /// 托盘图标应使用的像素尺寸（随 DPI 变化）。
        /// 多个来源取最大值，避免某些环境下系统度量未随 DPI 缩放导致图标偏小。
        /// </summary>
        private static int GetTrayIconSize()
        {
            int size = SystemInformation.SmallIconSize.Width;

            int dpi = UiTheme.SystemDpi();          // 屏幕 DC 的 DPI（100%→96、150%→144）
            size = Math.Max(size, (int)Math.Round(16.0 * dpi / 96.0));

            try
            {
                using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                {
                    size = Math.Max(size, (int)Math.Round(16.0 * g.DpiX / 96.0));
                }
            }
            catch
            {
                // 忽略：已有其它来源的尺寸
            }

            return size;
        }

        private void SyncMenuState()
        {
            _syncingAutoStart = true;
            try
            {
                _miAutoStart.Checked = AutoStart.IsEnabled();
            }
            finally
            {
                _syncingAutoStart = false;
            }
        }

        private void UpdateUi()
        {
            _miPause.Text = _engine.ManualPaused ? "继续提醒（重新计时）" : "暂停提醒";

            string tip = string.Format("久坐休息提醒 - {0}", _engine.StatusText);
            // NotifyIcon.Text 超过 63 个字符会抛 ArgumentException（崩溃），这里截断保护
            if (tip.Length > TooltipMaxLength)
            {
                tip = tip.Substring(0, TooltipMaxLength - 1) + "…";
            }
            if (tip != _lastTooltip)
            {
                _lastTooltip = tip;
                _notify.Text = tip;
            }
        }

        public void Dispose()
        {
            _engine.Tick -= UpdateUi;
            _engine.StateChanged -= UpdateUi;
            _engine.PauseChanged -= _onPauseChanged;
            _notify.Visible = false;
            _notify.Dispose();
            _menu.Dispose();
        }
    }
}

using System;
using System.Threading;
using System.Windows.Forms;

namespace RestReminder
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\RestReminder_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    // 已有实例在运行，直接退出（避免重复提醒）
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                AppConfig config = AppConfig.Load();
                using (var engine = new TimerEngine(config))
                {
                    Application.Run(new AppContext(engine, config));
                }
            }
        }
    }

    /// <summary>
    /// 无主窗口的 ApplicationContext：持有托盘、弹窗、设置窗口，串联引擎事件。
    /// </summary>
    internal class AppContext : ApplicationContext
    {
        private readonly TimerEngine _engine;
        private readonly AppConfig _config;
        private readonly TrayIcon _tray;
        private readonly RestPopupForm _popup;
        private SettingsForm _settings;

        public AppContext(TimerEngine engine, AppConfig config)
        {
            _engine = engine;
            _config = config;

            _tray = new TrayIcon(engine);
            _tray.SettingsRequested += ShowSettings;
            _tray.ExitRequested += ExitApp;

            _popup = new RestPopupForm(engine);

            _engine.RestDue += OnRestDue;
            _engine.RestEnded += OnRestEnded;
            _engine.StateChanged += OnStateChanged;

            // 确保注册表自启动项与配置一致
            if (AutoStart.IsEnabled() != config.AutoStart)
            {
                AutoStart.SetEnabled(config.AutoStart);
            }

            // 延迟 1 秒显示启动气泡（等待托盘就绪）
            var starter = new System.Windows.Forms.Timer { Interval = 1000 };
            starter.Tick += (s, e) =>
            {
                starter.Stop();
                starter.Dispose();
                _tray.ShowBalloon("久坐休息提醒",
                    string.Format("已启动：每工作 {0} 分钟提醒休息 {1} 分钟。右键托盘图标可设置。",
                        config.WorkMinutes, config.RestMinutes));
            };
            starter.Start();
        }

        private void OnRestDue()
        {
            if (_config.SoundOnRemind)
            {
                System.Media.SystemSounds.Exclamation.Play();
            }
            _popup.ShowPopup();
        }

        private void OnRestEnded()
        {
            _tray.ShowBalloon("休息结束", "休息时间到，开始新一轮工作吧！");
        }

        private void OnStateChanged()
        {
            if (_engine.State == AppState.Work)
            {
                _popup.Hide();
            }
        }

        private void ShowSettings()
        {
            if (_settings == null || _settings.IsDisposed)
            {
                _settings = new SettingsForm(_engine, _config);
                _settings.FormClosed += (s, e) => _settings = null;
            }
            _settings.Show();
            _settings.Activate();
        }

        private void ExitApp()
        {
            // engine 由 Main 中的 using 块统一释放
            _tray.Dispose();
            _popup.Dispose();
            ExitThread();
        }
    }
}

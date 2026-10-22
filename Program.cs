using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace RestReminder
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // 全局异常兜底：记录日志而不是静默崩溃
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => LogError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogError(e.ExceptionObject as Exception);

            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\RestReminder_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    // 已有实例在运行：给出明确提示，避免“双击没反应”的困惑
                    try
                    {
                        Application.EnableVisualStyles();
                        MessageBox.Show(
                            "久坐休息提醒已经在运行。\n请查看任务栏右下角的托盘图标（双击图标可打开设置）。",
                            "久坐休息提醒",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    catch
                    {
                        // 提示失败时静默退出
                    }
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

        /// <summary>把异常追加写入 %APPDATA%\RestReminder\error.log。</summary>
        private static void LogError(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(AppConfig.ConfigDirectory);
                string logPath = Path.Combine(AppConfig.ConfigDirectory, "error.log");

                // 日志超过 256KB 时重置，避免无限增长
                var info = new FileInfo(logPath);
                if (info.Exists && info.Length > 256 * 1024) File.Delete(logPath);

                File.AppendAllText(
                    logPath,
                    string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}{2}{2}",
                        DateTime.Now, ex, Environment.NewLine));
            }
            catch
            {
                // 日志写入失败时静默，避免二次崩溃
            }
        }
    }

    /// <summary>
    /// 无主窗口的 ApplicationContext：持有托盘、提醒弹窗、轻预告 Toast 与设置窗口，
    /// 串联引擎的温和提醒事件。
    /// </summary>
    internal class AppContext : ApplicationContext
    {
        private readonly TimerEngine _engine;
        private readonly AppConfig _config;
        private readonly TrayIcon _tray;
        private readonly ReminderPopupForm _popup;
        private readonly ToastForm _toast;
        private SettingsForm _settings;

        public AppContext(TimerEngine engine, AppConfig config)
        {
            _engine = engine;
            _config = config;

            _tray = new TrayIcon(engine);
            _tray.SettingsRequested += ShowSettings;
            _tray.ExitRequested += ExitApp;

            _popup = new ReminderPopupForm(engine);
            _toast = new ToastForm();

            // 到点前轻预告（Toast）
            _engine.PreReminderDue += () => _toast.ShowToast(
                string.Format("还有 {0} 分钟到久坐提醒点，可以先收个尾",
                    config.PreRemindMinutes));

            // 提醒（含温和重申）
            _engine.ReminderDue += () =>
            {
                if (config.SoundOnRemind)
                {
                    System.Media.SystemSounds.Exclamation.Play();
                }
                _popup.ShowPopup();
            };

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

                // 用主题化 Toast 代替系统气泡：图标、配色、边距都可由应用控制
                _toast.ShowToast(string.Format(
                    "已启动 · 连续久坐 {0} 分钟提醒活动", config.RemindMinutes));
            };
            starter.Start();
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
            // engine 由 Main 中的 using 块统一释放（会保存统计）
            _tray.Dispose();
            _popup.Dispose();
            _toast.Dispose();
            ExitThread();
        }
    }
}

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RestReminder
{
    /// <summary>应用状态。</summary>
    public enum AppState
    {
        Running,    // 正在累计连续久坐时长（用户在场）
        Away,       // 已离开（无操作超阈值 / 锁屏 / 睡眠），本轮已清零
        Paused,     // 用户手动暂停提醒
    }

    /// <summary>在场检测方式（对应配置 PresenceMode，后续可扩展更多值）。</summary>
    public enum PresenceMode
    {
        InputOnly = 0,       // 仅检测本机键鼠活动
        InputOrHotspot = 1,  // 检测本机键鼠活动或手机热点连接（手机在工位即视为在场）
    }

    /// <summary>
    /// 计时引擎（久坐会话制，温和提醒）：
    /// - 用户持续在场时累计“连续久坐时长”；在场判定可选：
    ///   ① 仅检测本机键鼠活动（默认）；
    ///   ② 检测本机键鼠活动或手机热点连接（双机办公时，手机在工位就算在场）；
    /// - 判定为离开（超过阈值 / 热点断开且无操作），或锁屏/睡眠，本轮清零，回来后重新开始；
    /// - 连续久坐达到 RemindMinutes 触发提醒（纯提示卡片，自动消失，无按钮）；
    ///   未离开时每 RepeatMinutes 温和重申，首次重申后逐次缩短显示时长——永不强制休息；
    /// - 到点前 PreRemindMinutes 分钟发一次轻预告（Toast，仅每轮首次）；
    /// - 计时使用单调时钟（Stopwatch），不受系统时间调整影响。
    /// </summary>
    public class TimerEngine : IDisposable
    {
        private readonly Timer _timer;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private TimeSpan _lastTick;
        private bool _manualPause;
        private bool _preFired;          // 本轮会话是否已发过提前预告
        private TimeSpan _nextNudgeAt;   // 下一次提醒时间点（会话时长域）
        private int _nudgeIndex;         // 本轮第几次提醒（1=首次，2=第 1 次重申…）
        private readonly DayStats _stats;
        private int _tickCount;

        private WifiPresenceMonitor _wifi;   // 热点在场检测（仅热点模式启用）
        private bool _wifiLost;              // 热点是否已断连（用于宽限期判断）
        private TimeSpan _wifiLostSince;     // 断连开始时刻（单调时钟）

        public AppConfig Config { get; private set; }
        public AppState State { get; private set; } = AppState.Running;
        public bool ManualPaused { get { return _manualPause; } }
        public TimeSpan SessionElapsed { get; private set; }
        public int NudgeIndex { get { return _nudgeIndex; } }
        public TimeSpan TodaySeated { get { return _stats.Seated; } }

        // 事件
        public event Action Tick;               // 每 500ms 刷新（托盘/弹窗）
        public event Action StateChanged;       // 状态切换
        public event Action<bool> PauseChanged; // 暂停状态变化
        public event Action PreReminderDue;     // 到点前的轻预告（Toast）
        public event Action ReminderDue;        // 提醒弹窗（含温和重申）

        public TimerEngine(AppConfig config)
        {
            Config = config;
            _stats = DayStats.Load();

            _timer = new Timer { Interval = 500 };
            _timer.Tick += OnTick;
            _lastTick = _clock.Elapsed;
            _timer.Start();

            EnsureWifiMonitor();

            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _timer.Dispose();

            if (_wifi != null)
            {
                _wifi.Dispose();
                _wifi = null;
            }

            _stats.Save();
        }

        // ---------- 在场判定 ----------

        /// <summary>按需创建/销毁热点检测线程（只有热点模式且填了热点名才启动）。</summary>
        private void EnsureWifiMonitor()
        {
            bool need = Config.PresenceMode == (int)PresenceMode.InputOrHotspot
                        && !string.IsNullOrEmpty(Config.PresenceSsid);

            if (need && _wifi == null)
            {
                _wifi = new WifiPresenceMonitor(15);
                _wifi.Start();
            }
            else if (!need && _wifi != null)
            {
                _wifi.Dispose();
                _wifi = null;
            }

            _wifiLost = false;
        }

        /// <summary>
        /// 判断此刻是否“在工位”：
        /// 本机有键鼠操作；或（热点模式下）手机热点仍在位。
        /// 热点短暂断连时给予宽限，避免 Wi-Fi 闪断导致误清零。
        /// </summary>
        private bool IsPresent()
        {
            if (NativeMethods.IdleTime < TimeSpan.FromMinutes(Config.LeaveMinutes))
            {
                _wifiLost = false;
                return true;
            }

            if (Config.PresenceMode == (int)PresenceMode.InputOrHotspot
                && _wifi != null
                && !string.IsNullOrEmpty(Config.PresenceSsid))
            {
                if (_wifi.IsConnectedTo(Config.PresenceSsid))
                {
                    _wifiLost = false;
                    return true;
                }

                if (!_wifiLost)
                {
                    _wifiLost = true;
                    _wifiLostSince = _clock.Elapsed;
                }
                if ((_clock.Elapsed - _wifiLostSince).TotalSeconds
                    < Math.Max(1, Config.PresenceGraceSeconds))
                {
                    return true; // 宽限期内仍视为在场
                }
            }

            return false;
        }

        // ---------- 系统事件：锁屏 / 睡眠（视为离开，本轮清零） ----------

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock ||
                e.Reason == SessionSwitchReason.ConsoleDisconnect ||
                e.Reason == SessionSwitchReason.RemoteDisconnect)
            {
                GoAway();
            }
            // 解锁/连接：无需处理，主循环检测到输入后会重新开始一轮
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend) GoAway();
            // 唤醒后：主循环检测到输入后会重新开始一轮
        }

        private void GoAway()
        {
            if (State == AppState.Running) EndSession();
        }

        // ---------- 主循环 ----------

        private void OnTick(object sender, EventArgs e)
        {
            TimeSpan now = _clock.Elapsed;
            TimeSpan dt = now - _lastTick;
            _lastTick = now;

            if (dt <= TimeSpan.Zero) return;
            // 兜底：睡眠/挂起等事件丢失时，最多计 5 秒，避免把离开时间计入久坐
            if (dt > TimeSpan.FromSeconds(5)) dt = TimeSpan.FromSeconds(5);

            if (++_tickCount % 120 == 0) _stats.Save(); // 约每分钟存一次统计

            if (State == AppState.Paused)
            {
                Tick?.Invoke();
                return;
            }

            bool present = IsPresent();
            if (present)
            {
                if (State == AppState.Away) StartSession();
                SessionElapsed += dt;
                _stats.AddSeated(dt);
                CheckPreReminder();
                CheckReminder();
            }
            else
            {
                if (State == AppState.Running) EndSession();
            }

            Tick?.Invoke();
        }

        private void CheckPreReminder()
        {
            int pre = Config.PreRemindMinutes;
            if (!_preFired && pre > 0 && Config.RemindMinutes > pre &&
                SessionElapsed.TotalMinutes >= Config.RemindMinutes - pre)
            {
                _preFired = true;
                PreReminderDue?.Invoke();
            }
        }

        private void CheckReminder()
        {
            if (SessionElapsed.TotalMinutes >= Config.RemindMinutes &&
                SessionElapsed >= _nextNudgeAt)
            {
                _nextNudgeAt = SessionElapsed + TimeSpan.FromMinutes(Config.RepeatMinutes);
                _nudgeIndex++; // 1=首次提醒，2=第 1 次重申，之后依次递增
                ReminderDue?.Invoke();
            }
        }

        // ---------- 会话边界 ----------

        private void StartSession()
        {
            SessionElapsed = TimeSpan.Zero;
            _preFired = false;
            _nextNudgeAt = TimeSpan.Zero;
            _nudgeIndex = 0;
            State = AppState.Running;
            StateChanged?.Invoke();
        }

        private void EndSession()
        {
            SessionElapsed = TimeSpan.Zero;
            _preFired = false;
            _nextNudgeAt = TimeSpan.Zero;
            _nudgeIndex = 0;
            State = AppState.Away;
            StateChanged?.Invoke();
        }

        /// <summary>托盘：手动暂停 / 恢复提醒（恢复后重新开始一轮）。</summary>
        public void SetManualPause(bool pause)
        {
            if (_manualPause == pause) return;
            _manualPause = pause;

            if (pause)
            {
                State = AppState.Paused;
                StateChanged?.Invoke();
            }
            else
            {
                bool present = IsPresent();
                State = present ? AppState.Running : AppState.Away;
                SessionElapsed = TimeSpan.Zero;
                _preFired = false;
                _nextNudgeAt = TimeSpan.Zero;
                _nudgeIndex = 0;
                StateChanged?.Invoke();
            }

            PauseChanged?.Invoke(pause);
            Tick?.Invoke();
        }

        /// <summary>托盘：手动重置本轮久坐计时。</summary>
        public void ResetSession()
        {
            bool present = IsPresent();
            SessionElapsed = TimeSpan.Zero;
            _preFired = false;
            _nextNudgeAt = TimeSpan.Zero;
            _nudgeIndex = 0;
            State = present ? AppState.Running : AppState.Away;
            StateChanged?.Invoke();
            Tick?.Invoke();
        }

        /// <summary>设置保存后应用新参数。</summary>
        public void ApplyConfig(AppConfig newConfig)
        {
            Config = newConfig;
            EnsureWifiMonitor();
            Tick?.Invoke();
        }

        // ---------- 展示 ----------

        public string StatusText
        {
            get
            {
                switch (State)
                {
                    case AppState.Paused:
                        return "已暂停 · 久坐提醒关闭中";
                    case AppState.Away:
                        return "已离开 · 久坐计时已清零";
                    default:
                        return string.Format("已连续坐 {0} · 今日累计 {1}",
                            FormatDuration(SessionElapsed),
                            FormatDuration(_stats.Seated));
                }
            }
        }

        /// <summary>把时长显示为中文（如 “1 小时 5 分” / “42 分钟”）。</summary>
        public static string FormatDuration(TimeSpan ts)
        {
            if (ts <= TimeSpan.Zero) return "0 分钟";
            int mins = (int)ts.TotalMinutes;
            if (mins < 1) return "1 分钟内";
            int h = mins / 60;
            int m = mins % 60;
            if (h > 0)
            {
                return m == 0
                    ? string.Format("{0} 小时", h)
                    : string.Format("{0} 小时 {1} 分", h, m);
            }
            return string.Format("{0} 分钟", mins);
        }
    }

    /// <summary>少量 Win32 P/Invoke。</summary>
    internal static class NativeMethods
    {
        /// <summary>获取自上次键盘/鼠标输入以来的空闲时间。</summary>
        public static TimeSpan IdleTime
        {
            get
            {
                var info = new LASTINPUTINFO();
                info.cbSize = (uint)Marshal.SizeOf(info);
                if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;

                // GetTickCount 与 dwTime 都是 32 位毫秒计数，约 24.9 天回绕一次
                //（Windows 快速启动不会重置该计数）。必须用 32 位模差值计算：
                // 若用有符号数相减，回绕后会得到巨大负数，导致“永远判定为在场”、
                // 离开检测失效。模 2^32 差值在回绕场景下依然正确。
                uint delta = unchecked(GetTickCount() - info.dwTime);
                return TimeSpan.FromMilliseconds(delta);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("kernel32.dll")]
        private static extern uint GetTickCount();
    }
}

using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RestReminder
{
    /// <summary>应用运行状态。</summary>
    public enum AppState
    {
        Work,       // 工作中
        RestPending,// 休息已推迟，等待开始
        Resting,    // 休息中
    }

    /// <summary>
    /// 计时引擎：维护 工作 -> 休息 的状态机。
    /// 通过 SystemEvents 监听锁屏/解锁、睡眠/唤醒，暂停时不计时。
    /// </summary>
    public class TimerEngine : IDisposable
    {
        private readonly Timer _timer;
        private DateTime _lastTick;
        private bool _manualPause;
        private bool _systemPause;
        private bool _idlePause;

        public AppConfig Config { get; private set; }

        public AppState State { get; private set; } = AppState.Work;
        public bool Paused { get { return _manualPause || _systemPause || _idlePause; } }

        /// <summary>当前暂停原因（用于提示）。</summary>
        public string PauseReason
        {
            get
            {
                if (_manualPause) return "手动暂停";
                if (_systemPause) return "锁屏或睡眠";
                if (_idlePause) return "用户离开";
                return "";
            }
        }

        public TimeSpan WorkElapsed { get; private set; }
        public TimeSpan WorkTotal { get { return TimeSpan.FromMinutes(Config.WorkMinutes); } }
        public TimeSpan RestRemaining { get; private set; }
        public TimeSpan RestTotal { get { return TimeSpan.FromMinutes(Config.RestMinutes); } }
        public TimeSpan PostponeRemaining { get; private set; }

        // 事件
        public event Action Tick;                      // 每秒/每半秒刷新
        public event Action StateChanged;              // 状态切换（弹窗显隐、托盘刷新）
        public event Action<bool> PauseChanged;        // 暂停状态变化
        public event Action RestDue;                   // 到点该休息了（弹窗显示）
        public event Action RestEnded;                 // 休息结束（气泡提示）

        public TimerEngine(AppConfig config)
        {
            Config = config;
            RestRemaining = RestTotal;
            WorkElapsed = TimeSpan.Zero;

            _timer = new Timer { Interval = 500 };
            _timer.Tick += OnTick;
            _lastTick = DateTime.Now;
            _timer.Start();

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
        }

        // ---------- 系统事件：锁屏 / 睡眠 ----------

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock ||
                e.Reason == SessionSwitchReason.ConsoleDisconnect ||
                e.Reason == SessionSwitchReason.RemoteDisconnect)
            {
                _systemPause = true;
                RefreshPause();
            }
            else if (e.Reason == SessionSwitchReason.SessionUnlock ||
                     e.Reason == SessionSwitchReason.ConsoleConnect ||
                     e.Reason == SessionSwitchReason.RemoteConnect)
            {
                _systemPause = false;
                RefreshPause();
            }
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend)
            {
                _systemPause = true;
                RefreshPause();
            }
            else if (e.Mode == PowerModes.Resume)
            {
                _systemPause = false;
                RefreshPause();
            }
        }

        private void RefreshPause()
        {
            bool wasPaused = Paused;
            if (!Paused) _lastTick = DateTime.Now; // 恢复时重置基准，避免跳变
            PauseChanged?.Invoke(Paused);
            if (wasPaused != Paused) Tick?.Invoke();
        }

        // ---------- 主计时循环 ----------

        private void OnTick(object sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            TimeSpan dt = now - _lastTick;
            _lastTick = now;

            // 兜底：若某次间隔异常大（例如挂起事件丢失），最多计 5 秒
            if (dt <= TimeSpan.Zero) return;
            if (dt > TimeSpan.FromSeconds(5)) dt = TimeSpan.FromSeconds(5);

            // 可选：检测用户空闲（键盘/鼠标无操作）
            bool wantIdlePause = Config.PauseOnIdle && State != AppState.Resting &&
                NativeMethods.IdleTime > TimeSpan.FromMinutes(Config.IdleMinutes);
            if (wantIdlePause != _idlePause)
            {
                _idlePause = wantIdlePause;
                RefreshPause();
            }

            if (!Paused)
            {
                switch (State)
                {
                    case AppState.Work:
                        WorkElapsed += dt;
                        if (WorkElapsed >= WorkTotal) StartRest();
                        break;

                    case AppState.RestPending:
                        PostponeRemaining -= dt;
                        if (PostponeRemaining <= TimeSpan.Zero) BeginResting();
                        break;

                    case AppState.Resting:
                        RestRemaining -= dt;
                        if (RestRemaining <= TimeSpan.Zero) EndRest();
                        break;
                }
            }

            Tick?.Invoke();
        }

        // ---------- 状态转换 ----------

        /// <summary>工作计时结束，立即进入休息并弹出提醒。</summary>
        private void StartRest()
        {
            WorkElapsed = TimeSpan.Zero;
            RestRemaining = RestTotal;
            PostponeRemaining = TimeSpan.Zero;
            State = AppState.Resting;
            StateChanged?.Invoke();
            RestDue?.Invoke();
        }

        /// <summary>推迟结束，正式进入休息。</summary>
        private void BeginResting()
        {
            PostponeRemaining = TimeSpan.Zero;
            State = AppState.Resting;
            StateChanged?.Invoke();
        }

        /// <summary>休息结束，回到工作。</summary>
        private void EndRest()
        {
            RestRemaining = TimeSpan.Zero;
            State = AppState.Work;
            WorkElapsed = TimeSpan.Zero;
            StateChanged?.Invoke();
            RestEnded?.Invoke();
        }

        /// <summary>跳过当前休息，立即回到工作状态。</summary>
        public void SkipRest()
        {
            if (State == AppState.Work) return;
            State = AppState.Work;
            WorkElapsed = TimeSpan.Zero;
            PostponeRemaining = TimeSpan.Zero;
            RestRemaining = RestTotal;
            StateChanged?.Invoke();
        }

        /// <summary>推迟休息：推迟 PostponeMinutes 分钟后开始（可连续推迟）。</summary>
        public void PostponeRest()
        {
            if (State != AppState.Resting && State != AppState.RestPending) return;
            if (State == AppState.Resting)
            {
                // 从休息中推迟：暂停剩余休息，改为推迟倒计时
                PostponeRemaining = TimeSpan.FromMinutes(Config.PostponeMinutes);
                State = AppState.RestPending;
            }
            else
            {
                PostponeRemaining += TimeSpan.FromMinutes(Config.PostponeMinutes);
            }
            StateChanged?.Invoke();
        }

        /// <summary>立即开始休息（托盘菜单）。</summary>
        public void StartRestNow()
        {
            WorkElapsed = TimeSpan.Zero;
            RestRemaining = RestTotal;
            PostponeRemaining = TimeSpan.Zero;
            State = AppState.Resting;
            StateChanged?.Invoke();
            RestDue?.Invoke();
        }

        // ---------- 手动暂停 ----------

        public void SetManualPause(bool pause)
        {
            _manualPause = pause;
            RefreshPause();
        }

        public bool ManualPaused { get { return _manualPause; } }

        /// <summary>应用新配置（设置保存后调用）。</summary>
        public void ApplyConfig(AppConfig newConfig)
        {
            Config = newConfig;
            // 若当前累计时长已超过新工作时长，回到初始状态重新计时
            if (State == AppState.Work && WorkElapsed >= WorkTotal)
            {
                WorkElapsed = TimeSpan.Zero;
                StateChanged?.Invoke();
            }
            if (State == AppState.Resting && RestRemaining > RestTotal)
            {
                RestRemaining = RestTotal;
                StateChanged?.Invoke();
            }
            Tick?.Invoke();
        }

        /// <summary>当前状态的中文描述（用于托盘提示）。</summary>
        public string StatusText
        {
            get
            {
                string prefix = Paused ? "⏸ " : "";
                switch (State)
                {
                    case AppState.Work:
                        return string.Format("{0}工作中 {1} / {2}",
                            prefix,
                            Format(WorkTotal - WorkElapsed),
                            Format(WorkTotal));
                    case AppState.RestPending:
                        return string.Format("{0}休息推迟，{1} 后开始",
                            prefix, Format(PostponeRemaining));
                    case AppState.Resting:
                        return string.Format("{0}休息中，剩余 {1}",
                            prefix, Format(RestRemaining));
                    default:
                        return "";
                }
            }
        }

        private static string Format(TimeSpan ts)
        {
            if (ts <= TimeSpan.Zero) return "00:00";
            int totalSeconds = (int)Math.Ceiling(ts.TotalSeconds);
            return string.Format("{0:00}:{1:00}", totalSeconds / 60, totalSeconds % 60);
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
                info.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(info);
                if (GetLastInputInfo(ref info))
                    return TimeSpan.FromMilliseconds(Environment.TickCount - info.dwTime);
                return TimeSpan.Zero;
            }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
    }
}

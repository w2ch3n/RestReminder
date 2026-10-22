using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;

namespace RestReminder
{
    /// <summary>
    /// 通过“当前连接的 Wi-Fi 热点名称”检测手机是否在工位：
    /// 联网电脑连着手机热点时，说明手机就在附近（人在工位，哪怕是在另一台电脑前工作）。
    ///
    /// 实现要点：
    /// - 用 <c>netsh wlan show interfaces</c> 读取当前连接的 SSID
    ///   （中英文系统的输出里 SSID 标签都不会被本地化；用 OEM 代码页解码，支持中文热点名）；
    /// - 后台线程定时轮询，结果加锁缓存，不阻塞 UI 线程；
    /// - 任何读取失败按“未连接”处理，由引擎的宽限期兜底，避免 Wi-Fi 闪断误清零。
    /// </summary>
    public sealed class WifiPresenceMonitor : IDisposable
    {
        private readonly object _lock = new object();
        private readonly int _intervalMs;
        private volatile bool _stopped;
        private bool _disposed;
        private Thread _thread;
        private HashSet<string> _ssids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public WifiPresenceMonitor(int intervalSeconds)
        {
            _intervalMs = Math.Max(5, intervalSeconds) * 1000;
        }

        public void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "RestReminder.WifiPresence",
            };
            _thread.Start();
        }

        /// <summary>当前是否连着指定 SSID（忽略大小写）。</summary>
        public bool IsConnectedTo(string ssid)
        {
            if (string.IsNullOrEmpty(ssid)) return false;
            string target = ssid.Trim();
            lock (_lock)
            {
                return _ssids.Contains(target);
            }
        }

        private void Loop()
        {
            while (!_stopped)
            {
                Refresh();
                // 分片休眠，保证 Dispose 能及时退出
                for (int i = 0; i < _intervalMs / 250 && !_stopped; i++)
                {
                    Thread.Sleep(250);
                }
            }
        }

        private void Refresh()
        {
            List<string> found;
            if (!TryQueryConnectedSsids(out found))
            {
                // 读取失败（netsh 异常等）：保留上一次结果，避免把瞬时故障当成“手机离开”
                return;
            }
            lock (_lock)
            {
                _ssids = new HashSet<string>(found, StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// 同步读取一次当前已连接的 SSID 列表（供设置窗口“读取当前”按钮使用）。
        /// 读取失败或没有无线网卡时返回空列表。
        /// </summary>
        public static List<string> QueryConnectedSsids()
        {
            List<string> result;
            TryQueryConnectedSsids(out result);
            return result;
        }

        /// <summary>
        /// 读取当前已连接的 SSID；返回 false 表示读取过程失败（与“未连接”区分）。
        /// </summary>
        private static bool TryQueryConnectedSsids(out List<string> result)
        {
            result = new List<string>();
            try
            {
                var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = ConsoleEncoding(),
                };

                using (var process = Process.Start(psi))
                {
                    if (process == null) return false;

                    // 先异步读取、再等退出：任一环节超时都不会无限阻塞
                    string output = string.Empty;
                    try
                    {
                        var readTask = process.StandardOutput.ReadToEndAsync();
                        if (process.WaitForExit(4000))
                        {
                            if (readTask.Wait(1000)) output = readTask.Result;
                        }
                        else
                        {
                            try { process.Kill(); } catch { }
                        }
                    }
                    catch
                    {
                        // 读取异常按“无输出”处理
                    }

                    foreach (string raw in output.Split('\n'))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0) continue;

                        int colon = line.IndexOf(':');
                        if (colon <= 0) continue;

                        // 只取 “SSID : xxx” 行（排除 BSSID 等其它行）
                        string key = line.Substring(0, colon).Trim();
                        if (!string.Equals(key, "SSID", StringComparison.OrdinalIgnoreCase)) continue;

                        string ssid = line.Substring(colon + 1).Trim();
                        if (ssid.Length > 0 && !result.Contains(ssid))
                        {
                            result.Add(ssid);
                        }
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>控制台程序输出使用 OEM 代码页（中文系统为 936），据此解码 SSID。</summary>
        private static Encoding ConsoleEncoding()
        {
            try
            {
                return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            }
            catch
            {
                return Encoding.Default;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _stopped = true;
            try
            {
                if (_thread != null) _thread.Join(1500);
            }
            catch
            {
                // 忽略线程退出异常
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace RestReminder
{
    /// <summary>
    /// 应用配置：工作时长、休息时长、推迟时长、开机自启、声音提醒等。
    /// 以简单的 key=value 文本格式保存在 %APPDATA%\RestReminder\config.txt。
    /// </summary>
    public class AppConfig
    {
        public int WorkMinutes = 45;        // 工作时长（分钟）
        public int RestMinutes = 5;         // 休息时长（分钟）
        public int PostponeMinutes = 5;     // 推迟时长（分钟）
        public bool AutoStart = false;      // 开机自启
        public bool PauseOnLockScreen = true; // 锁屏/睡眠时暂停计时（需求核心，默认开启）
        public bool PauseOnIdle = false;    // 检测到用户离开（键盘鼠标空闲）时暂停
        public int IdleMinutes = 5;         // 空闲多少分钟后暂停
        public bool SoundOnRemind = true;   // 提醒时播放提示音

        private const string FileName = "config.txt";

        public static string ConfigDirectory
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RestReminder");
                return dir;
            }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(ConfigDirectory, FileName); }
        }

        /// <summary>从磁盘加载配置；文件不存在或损坏时使用默认值。</summary>
        public static AppConfig Load()
        {
            var cfg = new AppConfig();
            try
            {
                if (!File.Exists(ConfigPath)) return cfg;

                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string raw in File.ReadAllLines(ConfigPath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }

                cfg.WorkMinutes     = GetInt(map, "WorkMinutes", cfg.WorkMinutes, 1, 600);
                cfg.RestMinutes     = GetInt(map, "RestMinutes", cfg.RestMinutes, 1, 120);
                cfg.PostponeMinutes = GetInt(map, "PostponeMinutes", cfg.PostponeMinutes, 1, 120);
                cfg.AutoStart       = GetBool(map, "AutoStart", cfg.AutoStart);
                cfg.PauseOnLockScreen = GetBool(map, "PauseOnLockScreen", cfg.PauseOnLockScreen);
                cfg.PauseOnIdle     = GetBool(map, "PauseOnIdle", cfg.PauseOnIdle);
                cfg.IdleMinutes     = GetInt(map, "IdleMinutes", cfg.IdleMinutes, 1, 120);
                cfg.SoundOnRemind   = GetBool(map, "SoundOnRemind", cfg.SoundOnRemind);
            }
            catch
            {
                // 任何读取异常都回退到默认值，避免程序无法启动
            }
            return cfg;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectory);
                var lines = new List<string>
                {
                    "# RestReminder 配置文件",
                    "# 修改后需重启应用生效",
                    "WorkMinutes=" + WorkMinutes,
                    "RestMinutes=" + RestMinutes,
                    "PostponeMinutes=" + PostponeMinutes,
                    "AutoStart=" + AutoStart,
                    "PauseOnLockScreen=" + PauseOnLockScreen,
                    "PauseOnIdle=" + PauseOnIdle,
                    "IdleMinutes=" + IdleMinutes,
                    "SoundOnRemind=" + SoundOnRemind,
                };
                File.WriteAllLines(ConfigPath, lines);
            }
            catch
            {
                // 保存失败不阻塞主流程
            }
        }

        private static int GetInt(Dictionary<string, string> map, string key, int def, int min, int max)
        {
            string v;
            if (!map.TryGetValue(key, out v)) return def;
            int n;
            if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return def;
            if (n < min) n = min;
            if (n > max) n = max;
            return n;
        }

        private static bool GetBool(Dictionary<string, string> map, string key, bool def)
        {
            string v;
            if (!map.TryGetValue(key, out v)) return def;
            bool b;
            if (bool.TryParse(v, out b)) return b;
            return v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}

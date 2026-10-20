using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace RestReminder
{
    /// <summary>
    /// 应用配置（久坐会话制）：
    /// 连续久坐多久提醒、到点前提前预告、多久无操作视为已离开、
    /// 提醒被忽略后的重申间隔、“稍后提醒”时长、自启与声音。
    /// 以 key=value 文本保存在 %APPDATA%\RestReminder\config.txt。
    /// </summary>
    public class AppConfig
    {
        public int RemindMinutes = 60;      // 连续久坐多久提醒（分钟）
        public int PreRemindMinutes = 3;    // 到点前提前预告（分钟，0=关闭）
        public int LeaveMinutes = 2;        // 无键盘鼠标操作多久视为已离开（分钟）
        public int RepeatMinutes = 15;      // 提醒被忽略后隔多久温和重申（分钟）
        public int PopupSeconds = 5;       // 提醒弹窗停留时长（秒），悬停暂停、点击即关
        public bool AutoStart = false;      // 开机自启
        public bool SoundOnRemind = true;   // 提醒时播放提示音

        // 在场检测（可扩展：后续增加新方式时新增枚举值即可，未知值按 0 处理）
        //   0 = 仅检测本机键鼠活动
        //   1 = 检测本机键鼠活动或手机热点连接（手机在工位即视为在场）
        public int PresenceMode = 0;
        public string PresenceSsid = "";        // 手机热点名称（PresenceMode=1 时生效，支持中文）
        public int PresenceGraceSeconds = 60;   // 热点断连宽限（秒），避免 Wi-Fi 闪断导致误清零

        private const string FileName = "config.txt";

        public static string ConfigDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RestReminder");
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

                // 新参数
                cfg.RemindMinutes    = GetInt(map, "RemindMinutes", cfg.RemindMinutes, 1, 600);
                cfg.PreRemindMinutes = GetInt(map, "PreRemindMinutes", cfg.PreRemindMinutes, 0, 30);
                cfg.LeaveMinutes     = GetInt(map, "LeaveMinutes", cfg.LeaveMinutes, 1, 30);
                cfg.RepeatMinutes    = GetInt(map, "RepeatMinutes", cfg.RepeatMinutes, 1, 120);
                cfg.PopupSeconds     = GetInt(map, "PopupSeconds", cfg.PopupSeconds, 3, 60);
                cfg.AutoStart        = GetBool(map, "AutoStart", cfg.AutoStart);
                cfg.SoundOnRemind    = GetBool(map, "SoundOnRemind", cfg.SoundOnRemind);

                // 在场检测（取值区间放宽，方便后续扩展新方式）
                cfg.PresenceMode         = GetInt(map, "PresenceMode", cfg.PresenceMode, 0, 9);
                cfg.PresenceSsid         = GetString(map, "PresenceSsid", cfg.PresenceSsid);
                cfg.PresenceGraceSeconds = GetInt(map, "PresenceGraceSeconds", cfg.PresenceGraceSeconds, 5, 600);

                // 兼容旧版本配置（1.0.x 工作周期制）：
                // WorkMinutes -> RemindMinutes，IdleMinutes -> LeaveMinutes
                if (!map.ContainsKey("RemindMinutes") && map.ContainsKey("WorkMinutes"))
                {
                    cfg.RemindMinutes = GetInt(map, "WorkMinutes", cfg.RemindMinutes, 1, 600);
                }
                if (!map.ContainsKey("LeaveMinutes") && map.ContainsKey("IdleMinutes"))
                {
                    cfg.LeaveMinutes = GetInt(map, "IdleMinutes", cfg.LeaveMinutes, 1, 30);
                }

                // 提前预告必须早于提醒点，否则永远不会触发
                if (cfg.PreRemindMinutes >= cfg.RemindMinutes)
                {
                    cfg.PreRemindMinutes = Math.Max(0, cfg.RemindMinutes - 1);
                }
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
                    "# 建议在设置窗口中修改，保存后立即生效；手动编辑本文件后需重启应用",
                    "RemindMinutes=" + RemindMinutes,
                    "PreRemindMinutes=" + PreRemindMinutes,
                    "LeaveMinutes=" + LeaveMinutes,
                    "RepeatMinutes=" + RepeatMinutes,
                    "PopupSeconds=" + PopupSeconds,
                    "AutoStart=" + AutoStart,
                    "SoundOnRemind=" + SoundOnRemind,
                    "",
                    "# 在场判定方式：0=仅检测本机键鼠活动；1=检测本机键鼠活动或手机热点连接（可扩展更多值）",
                    "PresenceMode=" + PresenceMode,
                    "# 手机热点名称（PresenceMode=1 时生效，支持中文）",
                    "PresenceSsid=" + PresenceSsid,
                    "# 热点断连宽限（秒），避免 Wi-Fi 闪断导致误清零",
                    "PresenceGraceSeconds=" + PresenceGraceSeconds,
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

        private static string GetString(Dictionary<string, string> map, string key, string def)
        {
            string v;
            if (!map.TryGetValue(key, out v)) return def;
            return v;
        }
    }
}

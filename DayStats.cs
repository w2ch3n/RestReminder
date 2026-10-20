using System;
using System.Globalization;
using System.IO;

namespace RestReminder
{
    /// <summary>
    /// 每日累计久坐统计：仅用于托盘悬浮提示（“今日累计”），
    /// 跨天自动清零，不做任何强制。
    /// </summary>
    public class DayStats
    {
        public DateTime Date = DateTime.Today;
        public TimeSpan Seated;

        private static string Path
        {
            get { return System.IO.Path.Combine(AppConfig.ConfigDirectory, "stats.txt"); }
        }

        public bool IsToday
        {
            get { return Date == DateTime.Today; }
        }

        public static DayStats Load()
        {
            var s = new DayStats();
            try
            {
                if (File.Exists(Path))
                {
                    foreach (string raw in File.ReadAllLines(Path))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith("#")) continue;
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string key = line.Substring(0, eq).Trim();
                        string val = line.Substring(eq + 1).Trim();

                        if (string.Equals(key, "date", StringComparison.OrdinalIgnoreCase))
                        {
                            DateTime d;
                            if (DateTime.TryParseExact(val, "yyyy-MM-dd",
                                    CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                                s.Date = d;
                        }
                        else if (string.Equals(key, "seated", StringComparison.OrdinalIgnoreCase))
                        {
                            long sec;
                            if (long.TryParse(val, NumberStyles.Integer,
                                    CultureInfo.InvariantCulture, out sec) && sec > 0)
                                s.Seated = TimeSpan.FromSeconds(sec);
                        }
                    }
                }
                s.EnsureToday(); // 跨天或日期缺失时重置
            }
            catch
            {
                s.Date = DateTime.Today;
                s.Seated = TimeSpan.Zero;
            }
            return s;
        }

        /// <summary>若已跨天则清零，开始新的一天。</summary>
        public void EnsureToday()
        {
            if (!IsToday)
            {
                Date = DateTime.Today;
                Seated = TimeSpan.Zero;
            }
        }

        public void AddSeated(TimeSpan dt)
        {
            EnsureToday();
            Seated += dt;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(AppConfig.ConfigDirectory);
                File.WriteAllLines(Path, new[]
                {
                    "date=" + Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    "seated=" + ((long)Seated.TotalSeconds).ToString(CultureInfo.InvariantCulture),
                });
            }
            catch
            {
                // 保存失败不阻塞
            }
        }
    }
}

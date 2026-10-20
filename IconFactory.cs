using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace RestReminder
{
    /// <summary>
    /// 运行时绘制应用/托盘图标（一个深色圆角底 + 绿色对勾），
    /// 避免依赖外部 .ico 资源文件，便于跨平台交叉编译。
    /// </summary>
    public static class IconFactory
    {
        private static Icon _cached;

        /// <summary>
        /// 获取应用图标（进程内只创建一次并缓存）。
        /// 每次调用 GetHicon 都会新建一个 HICON，反复创建会泄漏 GDI 句柄，
        /// 因此这里缓存复用同一个 Icon 实例。
        /// </summary>
        public static Icon CreateAppIcon()
        {
            if (_cached == null) _cached = BuildIcon();
            return _cached;
        }

        private static Icon BuildIcon()
        {
            using (var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    // 圆角方块背景
                    using (var path = RoundedRect(new Rectangle(1, 1, 30, 30), 7))
                    using (var bg = new SolidBrush(Color.FromArgb(47, 84, 235)))
                    {
                        g.FillPath(bg, path);
                    }

                    // 白色对勾
                    using (var pen = new Pen(Color.White, 4f)
                    {
                        StartCap = LineCap.Round,
                        EndCap = LineCap.Round,
                        LineJoin = LineJoin.Round
                    })
                    {
                        g.DrawLines(pen, new[]
                        {
                            new PointF(8f, 17f),
                            new PointF(13.5f, 22.5f),
                            new PointF(24f, 10f),
                        });
                    }
                }

                // HICON 句柄随缓存 Icon 存活到进程结束（只创建一次，不重复泄漏）
                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    return Icon.FromHandle(hIcon);
                }
                catch
                {
                    return SystemIcons.Application;
                }
            }
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}

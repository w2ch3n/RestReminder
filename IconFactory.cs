using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace RestReminder
{
    /// <summary>
    /// 应用图标：从内嵌的多尺寸 .ico（16~256）里**按像素尺寸精确取帧**，
    /// 不依赖 Icon(stream,size) 的选择策略，保证托盘/窗口拿到的是原生尺寸的帧。
    /// exe 本身的图标由项目文件里的 ApplicationIcon 指定。
    /// </summary>
    public static class IconFactory
    {
        private const string ResourceName = "RestReminder.RestReminder.ico";

        private static readonly Dictionary<int, Icon> Cache = new Dictionary<int, Icon>();

        /// <summary>默认图标（32px，用于窗口标题栏等）。</summary>
        public static Icon CreateAppIcon()
        {
            return CreateAppIcon(32);
        }

        /// <summary>按像素尺寸获取图标（进程内缓存，避免重复创建句柄）。</summary>
        public static Icon CreateAppIcon(int size)
        {
            if (size <= 0) size = 32;

            lock (Cache)
            {
                Icon cached;
                if (Cache.TryGetValue(size, out cached)) return cached;

                Icon icon = LoadIcon(size);
                Cache[size] = icon;
                return icon;
            }
        }

        private static Icon LoadIcon(int size)
        {
            try
            {
                byte[] data = ReadResource();
                if (data == null) return SystemIcons.Application;

                byte[] single = ExtractFrame(data, size);
                if (single != null)
                {
                    using (var stream = new MemoryStream(single, writable: false))
                    {
                        return new Icon(stream);
                    }
                }

                // 兜底：整份 ICO 交给 Icon 自行选择
                using (var stream = new MemoryStream(data, writable: false))
                {
                    return new Icon(stream, size, size);
                }
            }
            catch
            {
                // 资源异常时退回系统图标，保证程序仍可启动
                return SystemIcons.Application;
            }
        }

        private static byte[] ReadResource()
        {
            using (Stream stream = Assembly.GetExecutingAssembly()
                       .GetManifestResourceStream(ResourceName))
            {
                if (stream == null) return null;

                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    return buffer.ToArray();
                }
            }
        }

        /// <summary>
        /// 从多尺寸 ICO 中挑出与目标尺寸最接近的一帧，重新打包成只含该帧的 ICO 数据。
        /// </summary>
        private static byte[] ExtractFrame(byte[] ico, int size)
        {
            if (ico == null || ico.Length < 6 + 16) return null;

            int type = BitConverter.ToUInt16(ico, 2);
            int count = BitConverter.ToUInt16(ico, 4);
            if (type != 1 || count <= 0) return null;

            int bestIndex = -1;
            int bestDiff = int.MaxValue;

            for (int i = 0; i < count; i++)
            {
                int entry = 6 + i * 16;
                if (entry + 16 > ico.Length) break;

                int width = ico[entry];
                int height = ico[entry + 1];
                if (width == 0) width = 256;
                if (height == 0) height = 256;

                int diff = Math.Abs(width - size) + Math.Abs(height - size);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0) return null;

            int bestEntry = 6 + bestIndex * 16;
            int dataSize = BitConverter.ToInt32(ico, bestEntry + 8);
            int dataOffset = BitConverter.ToInt32(ico, bestEntry + 12);
            if (dataSize <= 0 || dataOffset <= 0 || dataOffset + dataSize > ico.Length) return null;

            int headerSize = 6 + 16;
            var result = new byte[headerSize + dataSize];

            // ICONDIR：保留数量为 1
            result[0] = 0;
            result[1] = 0;
            result[2] = 1;
            result[3] = 0;
            result[4] = 1;
            result[5] = 0;

            // ICONDIRENTRY：整条复制后修正数据偏移
            Array.Copy(ico, bestEntry, result, 6, 16);
            byte[] offsetBytes = BitConverter.GetBytes(headerSize);
            Array.Copy(offsetBytes, 0, result, 6 + 12, 4);

            // 帧数据
            Array.Copy(ico, dataOffset, result, headerSize, dataSize);
            return result;
        }
    }
}

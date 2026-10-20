using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RestReminder
{
    /// <summary>
    /// 开机自启：通过 HKCU\Software\Microsoft\Windows\CurrentVersion\Run 注册表项实现，
    /// 无需管理员权限，随当前用户登录启动。
    /// </summary>
    public static class AutoStart
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "RestReminder";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return false;
                    object value = key.GetValue(ValueName);
                    if (value == null) return false;

                    // 校验注册表里的路径是否仍是当前 exe：程序被移动/重命名后，
                    // 旧项会指向不存在的文件，这里视为“未启用”，启动时会重新写入正确路径。
                    string stored = value.ToString().Trim().Trim('"');
                    string exe = Application.ExecutablePath;
                    if (string.IsNullOrEmpty(exe)) return true;
                    return string.Equals(stored, exe, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null) return;
                    if (enabled)
                    {
                        string exe = Application.ExecutablePath;
                        if (string.IsNullOrEmpty(exe)) return;
                        // 带引号防止路径含空格
                        key.SetValue(ValueName, "\"" + exe + "\"");
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }
            }
            catch
            {
                // 注册表写入失败时静默（例如被策略限制）
            }
        }
    }
}

using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace DesktopPet
{
    /// <summary>
    /// 开机自启：往 HKCU\...\CurrentVersion\Run 写一条。
    /// 写 HKCU 不需要管理员权限，也不会污染其他用户。
    /// </summary>
    public static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "DesktopPet";

        // .NET Framework 没有 Environment.ProcessPath（那是 .NET 6+ 的 API）
        private static string ExePath
        {
            get { return Process.GetCurrentProcess().MainModule.FileName; }
        }

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                    {
                        var s = key?.GetValue(ValueName) as string;
                        return s != null && s.Trim('"').Equals(ExePath, StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch { return false; }
            }
        }

        public static void SetEnabled(bool on)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null) return;

                    if (on) key.SetValue(ValueName, "\"" + ExePath + "\"");
                    else key.DeleteValue(ValueName, throwOnMissingValue: false);
                }
            }
            catch { /* 被安全软件拦了也不至于崩 */ }
        }
    }
}

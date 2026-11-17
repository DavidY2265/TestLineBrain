using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopPet.LineBrain.Ai
{
    public static class NativeCredentialManager
    {
        // 定义 Windows 凭据类型 (CRED_TYPE_GENERIC = 1 表示通用凭据，即我们用来存 AI Key 的类型)
        private const uint CRED_TYPE_GENERIC = 1;

        // P/Invoke 声明 Windows 原生 API
        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref CREDENTIAL userCredential, uint flags);

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string targetName, uint type, uint flags, out IntPtr credentialPtr);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr credentialPtr);

        // 原生 C 结构体的 C# 映射
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        /// <summary>
        /// 保存凭据（例如：存入 AI Key）
        /// </summary>
        public static bool SaveCredential(string targetName, string userName, string password)
        {
            // 用 UTF-16 和凭据管理器 / cmdkey 保持一致。写 UTF-8 的话，
            // 程序自己存自己读没问题，但在凭据管理器里点「显示」会看到乱码，
            // 别人手工改过一次之后程序又读不回来了。
            byte[] passwordBytes = Encoding.Unicode.GetBytes(password);
            IntPtr unmanagedBytes = Marshal.AllocCoTaskMem(passwordBytes.Length);
            Marshal.Copy(passwordBytes, 0, unmanagedBytes, passwordBytes.Length);

            CREDENTIAL credential = new CREDENTIAL
            {
                TargetName = targetName,
                UserName = userName,
                CredentialBlob = unmanagedBytes,
                CredentialBlobSize = (uint)passwordBytes.Length,
                Type = CRED_TYPE_GENERIC, // 使用通用凭据类型
                Persist = 3 // 永久保存
            };

            bool success = CredWrite(ref credential, 0);

            // 清理非托管内存，防止内存泄漏
            Marshal.FreeCoTaskMem(unmanagedBytes);
            return success;
        }

        /// <summary>
        /// 读取凭据。读不到返回 null，原因写进 LastError。
        ///
        /// 必须是**通用凭据**（控制面板 → 凭据管理器 → Windows 凭据 → 添加普通凭据，
        /// 或者 cmdkey /generic:）。
        ///
        /// 存成「Windows 凭据」那种（类型 Domain Password）是读不出来的：
        /// CredRead 能查到这条记录，但 CredentialBlobSize 永远是 0 ——
        /// **Windows 不把域密码交给应用程序**，那是留给系统做网络认证的，
        /// 这是安全设计不是 bug，改代码绕不过去，只能换成通用凭据重存一次。
        /// </summary>
        public static string? ReadCredential(string targetName)
        {
            LastError = 0;
            IntPtr credentialPtr = IntPtr.Zero;

            try
            {
                bool success = CredRead(targetName, CRED_TYPE_GENERIC, 0, out credentialPtr);
                if (!success || credentialPtr == IntPtr.Zero)
                {
                    // 1168 = ERROR_NOT_FOUND，最常见的原因是存错成「Windows 凭据」了
                    LastError = Marshal.GetLastWin32Error();
                    return null;
                }

                CREDENTIAL credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);

                // 密码在 CredentialBlob 里，**不在 UserName 里**。
                // UserName 是凭据管理器那个「用户名」框，填的是 AI_API_KEY1 这种标签，
                // 拿它当 key 返回的话程序会一路正常跑下去、最后被 API 401 掉，
                // 比直接读不到还难查。
                if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                {
                    LastError = -1;              // 有这条记录，但密码是空的
                    return null;
                }

                byte[] blob = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
                return Decode(blob);
            }
            finally
            {
                if (credentialPtr != IntPtr.Zero)
                {
                    CredFree(credentialPtr);
                }
            }
        }

        /// <summary>上一次读取失败的 Win32 错误码。0 = 没出错，-1 = 找到了但密码是空的。</summary>
        public static int LastError;

        /// <summary>读失败时给人看的一句话。</summary>
        public static string LastErrorText()
        {
            if (LastError == 0)
            {
                return "";
            }
            if (LastError == 1168)
            {
                return "凭据管理器里没有这条通用凭据（1168）。"
                     + "注意「Windows 凭据」和「普通凭据」是两个库，存错地方就是这个错。";
            }
            if (LastError == -1)
            {
                return "找到了这条记录，但密码读出来是空的。"
                     + "多半它是「Windows 凭据」（Domain Password 类型）—— "
                     + "Windows 不把这类密码交给应用程序，得改存成普通凭据。";
            }
            return "CredRead 失败，Win32 错误 " + LastError;
        }

        /// <summary>
        /// 把密码字节还原成字符串。
        ///
        /// **凭据管理器和 cmdkey 写进去的是 UTF-16**（实测：8 个 ASCII 字符占 16 字节，
        /// 字节序列 41-00-42-00…）。按 UTF-8 解会得到 "A\0B\0C\0D\0" 这种夹着空字符的串 ——
        /// 长度看着差不多、打印出来也像那么回事，但拿去当 API key 一定失败。
        ///
        /// 两种都认：偶数长度且第二个字节是 0 就当 UTF-16。
        /// API key 是纯 ASCII，UTF-8 编出来不可能有 0 字节，所以这个判据不会误判。
        /// </summary>
        private static string Decode(byte[] blob)
        {
            if (blob.Length >= 2 && blob.Length % 2 == 0 && blob[1] == 0)
            {
                return Encoding.Unicode.GetString(blob);
            }
            return Encoding.UTF8.GetString(blob);
        }
    }
}

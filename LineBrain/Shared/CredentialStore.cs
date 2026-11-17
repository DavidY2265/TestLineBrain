using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopPet.LineBrain
{
    /// <summary>
    /// 从 Windows 凭据管理器取密码。数据库密码放这儿，不放源码也不放配置文件。
    ///
    /// ------------------------------------------------------------
    /// 必须是「普通凭据」（Generic），不能是「Windows 凭据」
    /// ------------------------------------------------------------
    /// 这两个是不同的库，而且用途根本不同：
    ///
    ///   · Windows 凭据（Domain Password）—— 给**系统**用的。你存一条是为了让
    ///     Windows 替你登录某台机器、某个共享盘，密码全程在 LSA 里，应用看不见。
    ///     CredRead 能查到这条记录，但 CredentialBlobSize 永远是 0。
    ///     这是安全设计不是 bug，改代码绕不过去。
    ///
    ///   · 普通凭据（Generic）—— 给**应用**用的。程序得拿到那串字符自己去用。
    ///     数据库密码、API key 都属于这种。
    ///
    /// 建一条：
    ///     cmdkey /generic:LineBrainDb /user:dbuser /pass
    /// （/pass 后面不跟值，它会提示输入，密码不会留在命令历史里）
    ///
    /// 或者：控制面板 → 凭据管理器 → Windows 凭据 → **「添加普通凭据」**，
    /// 不是「添加 Windows 凭据」。
    ///
    /// 安全性：普通凭据走 DPAPI，按**当前用户 + 当前机器**加密。
    /// exe 拷到别人机器上读不出来，换个 Windows 账号也读不出来。
    /// </summary>
    public static class CredentialStore
    {
        private const uint CredTypeGeneric = 1;
        private const int ErrorNotFound = 1168;

        /// <summary>存到本机，重启还在。跟着用户走，别的账号读不到。</summary>
        private const uint CredPersistLocalMachine = 2;

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string targetName, uint type, uint flags, out IntPtr credentialPtr);

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr credentialPtr);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public uint Flags;
            public uint Type;
            [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
            [MarshalAs(UnmanagedType.LPWStr)] public string Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            [MarshalAs(UnmanagedType.LPWStr)] public string TargetAlias;
            [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
        }

        /// <summary>
        /// 取密码。取不到返回 null，并把原因记进 DataProblems。
        /// </summary>
        public static string? Read(string targetName)
        {
            IntPtr pointer = IntPtr.Zero;

            try
            {
                bool ok = CredRead(targetName, CredTypeGeneric, 0, out pointer);
                if (!ok || pointer == IntPtr.Zero)
                {
                    DataProblems.Add(Explain(targetName, Marshal.GetLastWin32Error()));
                    return null;
                }

                CREDENTIAL credential = Marshal.PtrToStructure<CREDENTIAL>(pointer);

                // 密码在 CredentialBlob 里，**不在 UserName 里**。
                // UserName 是凭据管理器那个「用户名」框，填的是标签不是密码；
                // 拿它当密码返回的话，程序会一路跑到连库才失败，比读不到还难查。
                if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                {
                    DataProblems.Add("Windows 凭据「" + targetName + "」里的密码是空的。"
                                   + "多半它被存成了「Windows 凭据」而不是「普通凭据」—— "
                                   + "前者的密码 Windows 不交给应用程序，要重新存一次。");
                    return null;
                }

                byte[] blob = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
                return Decode(blob);
            }
            catch (Exception ex)
            {
                DataProblems.Add("读 Windows 凭据「" + targetName + "」出错：" + ex.Message);
                return null;
            }
            finally
            {
                if (pointer != IntPtr.Zero)
                {
                    CredFree(pointer);
                }
            }
        }

        /// <summary>
        /// 存一条**普通凭据**。有同名的就覆盖。
        ///
        /// 写 UTF-16，和凭据管理器、cmdkey 保持一致 —— 写 UTF-8 的话程序自己存自己读
        /// 没问题，但在凭据管理器里点「显示」会是乱码，别人手工改过一次之后就读不回来了。
        /// </summary>
        public static bool Write(string targetName, string userName, string password)
        {
            byte[] blob = Encoding.Unicode.GetBytes(password);
            IntPtr buffer = Marshal.AllocCoTaskMem(blob.Length);

            try
            {
                Marshal.Copy(blob, 0, buffer, blob.Length);

                CREDENTIAL credential = new CREDENTIAL();
                credential.Type = CredTypeGeneric;
                credential.TargetName = targetName;
                credential.UserName = userName;
                credential.CredentialBlob = buffer;
                credential.CredentialBlobSize = (uint)blob.Length;
                credential.Persist = CredPersistLocalMachine;

                if (CredWrite(ref credential, 0))
                {
                    return true;
                }

                DataProblems.Add("存 Windows 凭据「" + targetName + "」失败，Win32 错误 "
                               + Marshal.GetLastWin32Error());
                return false;
            }
            catch (Exception ex)
            {
                DataProblems.Add("存 Windows 凭据「" + targetName + "」出错：" + ex.Message);
                return false;
            }
            finally
            {
                // CredWrite 会把内容拷走，所以这里可以马上释放
                Marshal.FreeCoTaskMem(buffer);
            }
        }

        /// <summary>
        /// 这条凭据在不在。**查不到不记 DataProblems** —— 设置界面要问的是
        /// 「他这次得不得输密码」，没存过是个正常状态，不是故障。
        /// 走 Read 的话每打开一次设置窗口就往问题清单里塞一条。
        /// </summary>
        public static bool Exists(string targetName)
        {
            IntPtr pointer = IntPtr.Zero;
            try
            {
                if (!CredRead(targetName, CredTypeGeneric, 0, out pointer))
                {
                    return false;
                }
                if (pointer == IntPtr.Zero)
                {
                    return false;
                }
                CREDENTIAL credential = Marshal.PtrToStructure<CREDENTIAL>(pointer);
                return credential.CredentialBlob != IntPtr.Zero && credential.CredentialBlobSize > 0;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (pointer != IntPtr.Zero)
                {
                    CredFree(pointer);
                }
            }
        }

        private static string Explain(string targetName, int error)
        {
            if (error == ErrorNotFound)
            {
                return "凭据管理器里没有「" + targetName + "」这条普通凭据。"
                     + "建一条：cmdkey /generic:" + targetName + " /user:账号 /pass"
                     + "（注意「Windows 凭据」和「普通凭据」是两个库，存错地方就是这个错）";
            }
            return "读 Windows 凭据「" + targetName + "」失败，Win32 错误 " + error;
        }

        /// <summary>
        /// 密码字节还原成字符串。
        ///
        /// **凭据管理器和 cmdkey 写进去的是 UTF-16**（实测：8 个 ASCII 字符占 16 字节，
        /// 字节序列 41-00-42-00…）。按 UTF-8 解会得到 "A\0B\0C\0D\0" 这种夹空字符的串，
        /// 长度看着差不多、打印出来也像那么回事，但拿去连库一定失败。
        ///
        /// 两种都认：偶数长度且第二个字节是 0 就当 UTF-16。
        /// 密码是 ASCII 的话，UTF-8 编出来不可能有 0 字节，所以这个判据不会误判。
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

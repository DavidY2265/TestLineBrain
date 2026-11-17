using DesktopPet.LineBrain.Ai;

namespace DesktopPet.LineBrain
{
    /// <summary>
    /// 怎么连上 AI：只走 Logitech 公司网关，token 挂在 Authorization: Bearer 上。
    ///
    /// 凭据**只从 Windows 凭据管理器取**（通用凭据 AI_API_KEY），
    /// 不读环境变量、不读文件。建一条：
    ///     cmdkey /generic:AI_API_KEY /user:AI_API_KEY1 /pass
    /// （/pass 后面不跟值，它会提示输入，凭据不会留在命令历史里）
    /// </summary>
    public static class AiAccess
    {
        /// <summary>Windows 通用凭据的目标名。</summary>
        public const string CredentialTarget = "AI_API_KEY";

        /// <summary>
        /// 公司网关。网关发的 token 只有它自己认得，送去别处一定是 401。
        /// </summary>
        private const string GatewayBase = "https://logiq-service.logitech.io/anthropic";

        /// <summary>对话接口的完整地址。</summary>
        public const string MessagesUrl = GatewayBase + "/v1/messages";

        /// <summary>凭据本身。找不到返回空串。</summary>
        public static string Token()
        {
            string? t = NativeCredentialManager.ReadCredential(CredentialTarget);
            if (t == null)
            {
                return "";
            }
            return t.Trim();
        }

        public static bool Exists()
        {
            return Token().Length > 0;
        }

        /// <summary>没能连上的话卡在哪一步。能用就返回空串。</summary>
        public static string Problem()
        {
            if (Exists())
            {
                return "";
            }

            string why = NativeCredentialManager.LastErrorText();
            if (why.Length == 0)
            {
                why = "凭据里存的是空白。";
            }
            return "Windows 通用凭据「" + CredentialTarget + "」读不到：" + why
                 + "建一条：cmdkey /generic:" + CredentialTarget + " /user:AI_API_KEY1 /pass";
        }
    }
}

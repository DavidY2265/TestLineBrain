using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace DesktopPet.LineBrain
{
    /// <summary>对话里的一句话。role 只有 "user" 和 "assistant" 两种。</summary>
    public sealed class ChatTurn
    {
        public string Role = "user";
        public string Text = "";

        public ChatTurn(string role, string text)
        {
            Role = role;
            Text = text;
        }
    }

    /// <summary>调用结果。失败不抛异常 —— 调用方要的是「能不能用」，不是异常栈。</summary>
    public sealed class ClaudeResult
    {
        public bool Ok;
        public string Text = "";
        public string Error = "";
    }

    /// <summary>
    /// 调 Claude API 的最小客户端。
    ///
    /// 为什么是裸 HttpClient 而不是官方 SDK：这个工程要 VS 2019 + .NET Framework 4.8 能打开，
    /// 依赖能少一个是一个。
    /// JSON 也用框架自带的 DataContractJsonSerializer，不装 System.Text.Json。
    ///
    /// 这里只负责发一次请求、把回答的文字捞出来。**它不做任何计算**，
    /// 模型收到的是 FactSheet 已经算完的数，它只能引用。
    /// </summary>
    public static class ClaudeClient
    {
        /// <summary>接口版本。每个请求都要带，值就是这串，别改。</summary>
        private const string Version = "2023-06-01";

        /// <summary>模型。改这里就能换，别的地方不用动。</summary>
        private const string Model = "claude-opus-5";

        /// <summary>
        /// 思考深度。这活儿是「从清单里挑相关的几条、组织成人话」，不是难推理，
        /// 所以不用 high。觉得回答不够透彻就调成 "high"，觉得慢和贵就调成 "low"。
        /// </summary>
        private const string Effort = "medium";

        /// <summary>
        /// 上限给得宽松些：它是「最多用这么多」，不是「一定花这么多」，
        /// 按实际用量计费。给小了会把回答截断在半句话上。
        /// </summary>
        private const int MaxTokens = 8000;

        private static HttpClient? _http;

        /// <summary>
        /// HttpClient 要复用，不能每次 new —— 每 new 一个就占一个 socket，
        /// 连着问几十句之后端口会耗尽。
        /// </summary>
        private static HttpClient Http()
        {
            if (_http == null)
            {
                // .NET Framework 4.8 默认跟随系统设置，但有些机器上组策略把 TLS 1.2
                // 关掉了，那样握手会直接失败。这里显式打开，不依赖系统怎么配。
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                _http = new HttpClient();
                _http.Timeout = TimeSpan.FromSeconds(120);
            }
            return _http;
        }

        /// <summary>
        /// 同步发一次请求。**必须在后台线程调用** —— 在 UI 线程上调会把界面冻住，
        /// 调用方用 BackgroundWorker（见 ChatPanel）。
        /// </summary>
        public static ClaudeResult Send(string apiKey, string systemPrompt,
                                        string context, List<ChatTurn> turns)
        {
            ClaudeResult result = new ClaudeResult();

            if (apiKey.Length == 0)
            {
                result.Error = "没有 API key";
                return result;
            }

            try
            {
                string body = BuildBody(systemPrompt, context, turns);
                Dump(body);

                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, AiAccess.MessagesUrl);
                req.Headers.Add("anthropic-version", Version);

                // 这两个看着可有可无，其实不能省：HttpClient 默认**不发** User-Agent，
                // 而网关前面那层 WAF 见到没有 User-Agent 的请求会直接回一个
                // nginx 的 403 Forbidden —— 长得跟「token 没权限」一模一样，
                // 能让人往认证的方向查半天。老老实实报自己是谁。
                req.Headers.Add("User-Agent", "DesktopPet-LineBrain/1.0");
                req.Headers.Add("Accept", "application/json");

                // 公司网关的 token 挂在 Authorization: Bearer 上。
                // 挂错头（比如 x-api-key）也是 401，看起来和「过期了」一模一样。
                req.Headers.Add("Authorization", "Bearer " + apiKey);

                // 这里**不加** anthropic-beta 头。公司网关后面挂的是 Vertex，
                // 不带也通，乱加 beta 头反而可能被它挡掉。
                // 同理请求体里也没有 fallbacks 字段，网关会挡。

                req.Content = new StringContent(body, new UTF8Encoding(false), "application/json");

                HttpResponseMessage resp = Http().SendAsync(req).GetAwaiter().GetResult();
                string text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (!resp.IsSuccessStatusCode)
                {
                    result.Error = "HTTP " + (int)resp.StatusCode + "　" + ReadErrorMessage(text);
                    return result;
                }

                return ReadAnswer(text);
            }
            catch (Exception ex)
            {
                // 断网、超时、证书、DNS —— 一律当成「这次用不了」，退回本地引擎。
                // 这是演示现场最要紧的一条：网络出问题不能白屏。
                result.Error = Describe(ex);
                return result;
            }
        }

        /// <summary>
        /// 异常说人话，别把 AggregateException 那一串甩给用户。
        /// 超时在 HttpClient 里抛的是 TaskCanceledException（OperationCanceledException 的子类），
        /// 消息是「A task was canceled」，看不出是超时，所以单独认一下。
        /// </summary>
        private static string Describe(Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                return "请求超时";
            }

            Exception inner = ex;
            while (inner.InnerException != null)
            {
                inner = inner.InnerException;
            }
            return inner.Message;
        }

        /// <summary>
        /// 每次发送前把请求原文落一份盘，方便事后核对到底传了什么出去。
        /// 只留最近一次，不累积 —— 这是给人看的，不是日志。
        /// </summary>
        private static void Dump(string body)
        {
            try
            {
                File.WriteAllText(BrainConfig.PathOf("last_request.json"), body,
                                  new UTF8Encoding(true));
            }
            catch
            {
                // 写不进去不影响请求本身
            }
        }

        // =================================================================
        //  请求
        // =================================================================

        /// <summary>
        /// 手搓请求 JSON。结构简单、字段固定，手搓比为它定义一堆 DataContract 类清楚。
        /// 唯一要小心的是字符串转义，那个交给 Escape()。
        /// </summary>
        private static string BuildBody(string systemPrompt, string context, List<ChatTurn> turns)
        {
            StringBuilder b = new StringBuilder();
            b.Append("{");
            b.Append("\"model\":\"").Append(Model).Append("\",");
            b.Append("\"max_tokens\":").Append(MaxTokens).Append(",");
            b.Append("\"output_config\":{\"effort\":\"").Append(Effort).Append("\"},");

            // 注意：这里没有 temperature。新模型把它拿掉了，放了直接 400。
            // 也没有 fallbacks —— 走公司网关会被挡。

            // system 用最朴素的一整个字符串。
            // 网关后面挂的是 Vertex，块数组 + cache_control 这套没在它上面验过，
            // 而「能用」比「省一点钱」要紧。要开缓存等确认网关支持了再说。
            string all = Rules + "\n\n" + systemPrompt;
            if (context.Length > 0)
            {
                all += "\n\n" + context;
            }
            b.Append("\"system\":\"").Append(Escape(all)).Append("\",");

            b.Append("\"messages\":[");
            for (int i = 0; i < turns.Count; i++)
            {
                if (i > 0)
                {
                    b.Append(",");
                }
                b.Append("{\"role\":\"").Append(turns[i].Role).Append("\",");
                b.Append("\"content\":\"").Append(Escape(turns[i].Text)).Append("\"}");
            }
            b.Append("]}");

            return b.ToString();
        }

        /// <summary>
        /// 给模型的硬规矩。
        ///
        /// 头三条是这套东西能不能用的关键：**自洽不是靠请它小心，是靠它手里根本没有
        /// 需要算的数**。事实清单里连中间步骤都摊开写了，它只要照抄。
        /// 第四条同理 —— 缺数据时该让用户补什么，也只能从能力清单里照抄，
        /// 自己编一份听起来合理的列名，用户照着准备一整天、程序不认。
        /// </summary>
        private const string Rules =
@"你是产线智能大脑看板里的解读助手，帮产线工程师读懂本班数据。

【硬规矩，任何情况下都不能破】
1. 只能引用下面【事实清单】里出现过的数字，一个字都不能改。
2. 禁止做任何算术，包括加减、百分比、平均、外推。清单里已经把中间步骤都算好了，
   需要哪一步就引用哪一步。清单里没算过的数，就说这个本地没算，不要自己凑一个。
3. 不确定的不要猜。宁可说「这个本地没有」，也不要给一个看起来合理的数。
4. 用户问到本地没有的东西（别的产线、别的班次、历史数据、产能、批次……），
   **不要只回一句「没有」**。按【能力清单】里「现在没有的东西」那一节回答，
   说清三件事：①现在有的是什么范围　②要答这个问题需要补哪些数据文件
   （文件名、列名、放哪个目录，全部从能力清单照抄，不要自己编）
   ③需要补什么计算模型。语气是「给我这些我就能算」，不是「我做不到」。
5. 能力清单里没写的文件格式和列名，一律不要编。
6. 用户提到一个你不认识的代号、栏位名或指标名（ME、SPC、DPPM 这一类缩写），
   **第一句就要明说本地没有这个东西**，然后才给别的。
   **绝对不要默默换成一个相近的东西来回答** —— 用户会以为你答的就是他问的，
   拿着答非所问的数去做决定。看板只有 eq / mq / te 三栏，别的缩写都不是它们的别名。
   可以问一句「你说的 X 是指某某吗」，也可以顺带给出你确实有的，
   但「本地没有 X」这句必须在最前面，不能省，不能藏在末尾。

【怎么说话】
- 中文。产线工程师看的，直接给判断和依据，不要铺垫。
- 结论先说，再给依据。重要的结论用 **两个星号** 包起来，界面会渲染成加粗。
- 短。一般三五句话说完；问题确实复杂再展开，但不要为了显得认真而注水。
- 指代词（「它呢」「这条呢」「那个」）按上文的话题去理解，不用反问。
  但**一个具体的陌生名词不算指代词** —— 那种走上面第 6 条，先说不认识，不要猜。
- 同一件事有两个口径时（合计 vs 并集、能力 vs 漂移），两个都要说，并说清区别。";

        /// <summary>
        /// JSON 字符串转义。控制字符必须转成 \u00XX，直接塞进去会让整个请求解析失败 ——
        /// 中文报警内容里偶尔混进一个制表符就够炸一次。
        /// </summary>
        private static string Escape(string s)
        {
            StringBuilder b = new StringBuilder(s.Length + 32);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"')
                {
                    b.Append("\\\"");
                }
                else if (c == '\\')
                {
                    b.Append("\\\\");
                }
                else if (c == '\n')
                {
                    b.Append("\\n");
                }
                else if (c == '\r')
                {
                    b.Append("\\r");
                }
                else if (c == '\t')
                {
                    b.Append("\\t");
                }
                else if (c < 0x20)
                {
                    b.Append("\\u").Append(((int)c).ToString("x4"));
                }
                else
                {
                    b.Append(c);
                }
            }
            return b.ToString();
        }

        // =================================================================
        //  响应
        // =================================================================

        [DataContract]
        private sealed class Reply
        {
            [DataMember(Name = "content")]
            public Block[]? Content { get; set; }

            [DataMember(Name = "stop_reason")]
            public string? StopReason { get; set; }
        }

        [DataContract]
        private sealed class Block
        {
            [DataMember(Name = "type")]
            public string? Type { get; set; }

            [DataMember(Name = "text")]
            public string? Text { get; set; }
        }

        [DataContract]
        private sealed class ErrorEnvelope
        {
            [DataMember(Name = "error")]
            public ErrorBody? Error { get; set; }
        }

        [DataContract]
        private sealed class ErrorBody
        {
            [DataMember(Name = "type")]
            public string? Type { get; set; }

            [DataMember(Name = "message")]
            public string? Message { get; set; }
        }

        /// <summary>
        /// 把回答的文字捞出来。content 是个数组，里面除了 text 还可能有 thinking，
        /// 只取 type == "text" 的那些拼起来。
        /// </summary>
        private static ClaudeResult ReadAnswer(string json)
        {
            ClaudeResult result = new ClaudeResult();

            Reply? reply = Parse<Reply>(json);
            if (reply == null)
            {
                result.Error = "响应解析失败";
                return result;
            }

            if (reply.StopReason == "refusal")
            {
                result.Error = "模型拒答了这个请求";
                return result;
            }

            StringBuilder b = new StringBuilder();
            if (reply.Content != null)
            {
                for (int i = 0; i < reply.Content.Length; i++)
                {
                    Block one = reply.Content[i];
                    if (one != null && one.Type == "text" && one.Text != null)
                    {
                        b.Append(one.Text);
                    }
                }
            }

            if (b.Length == 0)
            {
                result.Error = "回答是空的";
                return result;
            }

            result.Ok = true;
            result.Text = b.ToString();
            return result;
        }

        private static string ReadErrorMessage(string json)
        {
            ErrorEnvelope? env = Parse<ErrorEnvelope>(json);
            if (env != null && env.Error != null && env.Error.Message != null)
            {
                return env.Error.Message;
            }

            // 解析不出来就把原文截一段给出去，总比一句「未知错误」有用
            if (json.Length > 200)
            {
                return json.Substring(0, 200);
            }
            return json;
        }

        private static T? Parse<T>(string json) where T : class
        {
            try
            {
                using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(T));
                    return ser.ReadObject(ms) as T;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}

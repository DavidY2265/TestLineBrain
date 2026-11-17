using System;
using DesktopPet.LineBrain;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;

namespace DesktopPet.Ui
{
    /// <summary>
    /// 一次提问要带去后台线程的全部东西。
    /// BackgroundWorker 的 DoWork 跑在别的线程上，不能碰界面，
    /// 所以要用的料得先在 UI 线程打包好一起递过去。
    /// </summary>
    internal sealed class AskJob
    {
        public string Key = "";
        public string System = "";
        public string Context = "";
        public List<ChatTurn> Turns = new List<ChatTurn>();

        public ClaudeResult Result = new ClaudeResult();
    }

    /// <summary>
    /// 追问对话框。
    ///
    /// 两条回答路径：
    /// · **有 key** —— 把本地算好的事实清单 + 能力清单 + 这一轮对话发给 Claude。
    ///   模型只负责听懂问题、挑相关的事实、组织语言；**它手里没有任何需要它算的数**，
    ///   所以说出来的数字必然和上面的表格一致。
    /// · **没 key / 断网 / 超时 / 报错** —— 退回 QaEngine 的本地关键词引擎。
    ///   这条路必须一直留着：演示现场网络出问题不能白屏。
    ///
    /// 退回的时候会在回答末尾注明原因，不假装一切正常 —— 不然突然变笨了没人知道为什么。
    /// </summary>
    public partial class ChatPanel : UserControl
    {
        private BoardTab _topic = BoardTab.Equipment;
        private string _subject = "";

        /// <summary>这一轮对话的来龙去脉，发给模型让它能理解「那MQ呢」这种话。</summary>
        private readonly List<ChatTurn> _turns = new List<ChatTurn>();

        private readonly DispatcherTimer _thinking = new DispatcherTimer();
        private readonly BackgroundWorker _worker = new BackgroundWorker();

        // 走模型一次要十几秒。这么久盯着一句不动的「正在想…」，人会以为它卡死了，
        // 所以让点点动起来 —— 纯粹是让人看出它还活着。
        private readonly DispatcherTimer _dots = new DispatcherTimer();
        private int _dotStep;

        /// <summary>正在「思考」的那条气泡，答案回来了把它的文字换掉。</summary>
        private TextBlock? _pendingBubble;
        private string _pendingAnswer = "";

        /// <summary>本班的全部数据。Start() 的时候由调用方给。</summary>
        private ShiftData _shift = new ShiftData();

        public ChatPanel()
        {
            InitializeComponent();

            _thinking.Interval = TimeSpan.FromMilliseconds(650);
            _thinking.Tick += OnThinkingTick;

            _worker.DoWork += OnWorkerDoWork;
            _worker.RunWorkerCompleted += OnWorkerCompleted;

            _dots.Interval = TimeSpan.FromMilliseconds(400);
            _dots.Tick += OnDotsTick;
        }

        /// <summary>
        /// 设定这次对话聊的是哪一栏、哪个对象。opening 传了就先由 AI 开个头。
        /// </summary>
        public void Start(ShiftData shift, BoardTab topic, string subject, string opening)
        {
            _shift = shift;
            _topic = topic;
            _subject = subject;

            HintText.Text = BuildHint();
            BuildChips();

            if (opening.Length > 0)
            {
                AddAi(opening);
                // 开场白不进 _turns：发给模型的对话必须从 user 那一句开始，
                // 头一条是 assistant 会被接口拒掉
            }
        }

        /// <summary>这一栏这次到底有没有数据。三个标志在 ShiftData 上，这里只是取一下。</summary>
        private bool Available(BoardTab topic)
        {
            if (_shift == null)
            {
                return false;
            }
            if (topic == BoardTab.Equipment)
            {
                return _shift.AlarmsAvailable;
            }
            if (topic == BoardTab.Quality)
            {
                return _shift.FastenAvailable;
            }
            return _shift.TestAvailable;
        }

        private string BuildHint()
        {
            string where = "本栏";
            if (_subject.Length > 0)
            {
                where = "「" + _subject + "」";
            }

            if (!AiAccess.Exists())
            {
                return "现在连不上模型（" + AiAccess.Problem() + "），所以问了也答不了。"
                     + "上面那份报告和看板上的数都是本地算的，不受影响。";
            }

            // 模型连得上，但这一栏没数据 —— 问什么都只能得到「取不到」，
            // 先说清楚，省得人对着输入框琢磨该怎么问才问得出数来。
            if (!Available(_topic))
            {
                // 这一行是普通 TextBlock，不解析 **加粗**，写了就原样打出来
                return "模型连得上，但这一栏这次一条数据都没取到，"
                     + "所以关于本班数字的问题它答不了（也不会编）。"
                     + "倒是可以问它「怎么把数据接上」「这些指标平时该怎么看」这类。";
            }

            // 报警和锁附来自 MySQL，测试来自 UTS log，没有一样是 csv 了。
            // 原文这里写的是「从 csv 算好」—— 那是改数据源之前的话。
            return "接着问 —— 现在聊的是" + where + "。随便怎么问都行，"
                 + "数字由本地规则引擎从" + SourceWord(_topic) + "算好再交给模型组织，"
                 + "模型不参与计算，所以答出来的数和上面必然一致。";
        }

        private static string SourceWord(BoardTab topic)
        {
            if (topic == BoardTab.Test)
            {
                return "原始 UTS log ";
            }
            return "数据库";
        }

        /// <summary>
        /// 对话框上面那一排「建议问题」。只是往输入框里塞一句话，方便点，
        /// 不是什么智能推荐 —— 所以按栏位写死几条就够。
        ///
        /// **但没数据的时候一条都不出。** 报告正文明明写着「一条记录都没读到」，
        /// 底下却摆着「整线到底停了多久？」「哪一条最该先修？」—— 等于请人去问
        /// 一个注定答不出来的问题，还显得程序自己都不知道自己没数据。
        /// </summary>
        private string[] SuggestionsFor(BoardTab topic)
        {
            if (!Available(topic))
            {
                return new string[0];
            }

            if (topic == BoardTab.Quality)
            {
                return new string[] { "哪个孔位最该先处理？", "是共享部件的问题还是单孔的问题？",
                                      "还有多久会跌破下限？", "均值偏了还是分布宽了？" };
            }
            if (topic == BoardTab.Test)
            {
                return new string[] { "良率掉在哪一项？", "有没有测项在漂？",
                                      "哪些测项不该算 Cpk，为什么？", "超规和功能失败要分开查吗？" };
            }
            return new string[] { "整线到底停了多久？", "哪一条最该先修？",
                                  "MA 是怎么算出来的？", "通讯断连为什么要剔除？" };
        }

        private void BuildChips()
        {
            Chips.Children.Clear();

            string[] items = SuggestionsFor(_topic);
            for (int i = 0; i < items.Length; i++)
            {
                Button chip = new Button();
                chip.Content = items[i];
                chip.Style = (Style)FindResource("ChatChip");
                chip.Click += OnChipClick;
                Chips.Children.Add(chip);
            }
        }

        // =================================================================
        //  发问
        // =================================================================

        private void OnChipClick(object sender, RoutedEventArgs e)
        {
            Button chip = (Button)sender;
            Ask(chip.Content.ToString());
        }

        private void OnSendClick(object sender, RoutedEventArgs e)
        {
            Ask(Input.Text);
            Input.Text = "";
            Input.Focus();
        }

        /// <summary>回车发送，Shift+回车换行 —— 和常见聊天窗口一个习惯。</summary>
        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Return)
            {
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                return;                        // 让 TextBox 自己插一个换行
            }

            e.Handled = true;
            Ask(Input.Text);
            Input.Text = "";
        }

        private void Ask(string question)
        {
            if (question == null || question.Trim().Length == 0)
            {
                return;
            }
            if (_worker.IsBusy)
            {
                return;                        // 上一句还在路上，按钮那会儿是灰的
            }

            FinishPending();

            string q = question.Trim();
            AddUser(q);
            _turns.Add(new ChatTurn("user", q));
            _pendingBubble = AddWaiting();

            string key = AiAccess.Token();
            if (key.Length == 0)
            {
                AnswerLocally("");             // 离线模式，不注明原因（提示里已经说了）
                return;
            }

            AskJob job = new AskJob();
            job.Key = key;
            job.System = FactSheet.Cached(_shift);
            job.Context = BuildContext();
            job.Turns = new List<ChatTurn>(_turns);

            SetBusy(true);
            _dotStep = 0;
            _dots.Start();
            _worker.RunWorkerAsync(job);
        }

        /// <summary>
        /// 告诉模型用户此刻在看哪一栏。这一段每个面板都不一样，
        /// 所以它在缓存标记之后 —— 放前面会让每个面板各占一份缓存。
        /// </summary>
        private string BuildContext()
        {
            string text = "【当前语境】用户正在看看板的「" + StatusText.Of(_topic) + "」栏";

            if (_subject.Length > 0)
            {
                text += "，并且点开了「" + _subject + "」这一条的解析";
            }

            text += "。用户说「它」「这条」而没有点名时，默认指的是这个。"
                  + "但用户明确问别的栏（比如在设备报警栏问「那 MQ 呢」「锁附怎么样」）时，"
                  + "就回答那一栏的情况 —— 事实清单里三栏的数都有，不要因为语境是这一栏就说没有。";
            return text;
        }

        // =================================================================
        //  后台请求
        // =================================================================

        /// <summary>
        /// 这个方法跑在后台线程上，**一行界面代码都不能有**。
        /// 用 BackgroundWorker 而不是让 UI 线程直接等：等一次要好几秒，
        /// 那几秒里窗口连重绘都做不了，拖动一下就变白板。
        /// </summary>
        private void OnWorkerDoWork(object? sender, DoWorkEventArgs e)
        {
            AskJob job = (AskJob)e.Argument;
            job.Result = ClaudeClient.Send(job.Key, job.System, job.Context, job.Turns);
            e.Result = job;
        }

        /// <summary>等待期间让省略号动起来，顺便报一下已经等了几秒。</summary>
        private void OnDotsTick(object? sender, EventArgs e)
        {
            if (_pendingBubble == null)
            {
                _dots.Stop();
                return;
            }

            _dotStep++;
            string dots = new string('·', 1 + (_dotStep % 3));
            int seconds = _dotStep * 2 / 5;          // 400ms 一跳

            // 前 5 秒不报秒数 —— 那个区间还在正常范围，报出来反而显得慢
            _pendingBubble.Text = "正在想 " + dots;
            if (seconds >= 5)
            {
                _pendingBubble.Text = "正在想 " + dots + "　已等 " + seconds + " 秒";
            }
        }

        /// <summary>这个回到 UI 线程上跑，可以动界面。</summary>
        private void OnWorkerCompleted(object? sender, RunWorkerCompletedEventArgs e)
        {
            _dots.Stop();
            SetBusy(false);

            if (e.Error != null)
            {
                AnswerLocally(e.Error.Message);
                return;
            }

            AskJob job = (AskJob)e.Result;
            if (!job.Result.Ok)
            {
                AnswerLocally(job.Result.Error);
                return;
            }

            _turns.Add(new ChatTurn("assistant", job.Result.Text));
            ShowAnswer(job.Result.Text);
        }

        /// <summary>
        /// 连不上模型时说的话。
        ///
        /// **不假装能答。** 以前这里有一套本地关键词引擎顶着，问题是它答出来的东西
        /// 和在线时差一大截，人却看不出这一句是「降级」的 —— 那比直接说连不上更坏。
        /// 现在明确讲清楚怎么回事，同时点明看板上的数不受影响。
        /// </summary>
        private void AnswerLocally(string reason)
        {
            string text = "现在连不上模型，这一句我答不了。\n\n"
                        + "**看板上的数不受影响** —— 表格、KPI、小精灵的警告都是本地算的，"
                        + "和有没有网没关系。要看结论，直接看上面那份报告。\n\n"
                        + "原因：" + AiAccess.Problem();
            if (reason.Length > 0)
            {
                text += "（" + reason + "）";
            }

            _pendingAnswer = text;
            _thinking.Start();               // 保留那一下停顿，节奏和在线时一致
        }

        private void SetBusy(bool busy)
        {
            SendButton.IsEnabled = !busy;
            for (int i = 0; i < Chips.Children.Count; i++)
            {
                Button? chip = Chips.Children[i] as Button;
                if (chip != null)
                {
                    chip.IsEnabled = !busy;
                }
            }
        }

        private void OnThinkingTick(object? sender, EventArgs e)
        {
            _thinking.Stop();
            FinishPending();
        }

        private void FinishPending()
        {
            if (_pendingBubble == null || _pendingAnswer.Length == 0)
            {
                return;
            }

            _thinking.Stop();
            ShowAnswer(_pendingAnswer);
        }

        private void ShowAnswer(string text)
        {
            if (_pendingBubble == null)
            {
                AddAi(text);
                return;
            }

            _pendingBubble.Style = (Style)FindResource("ChatAiText");
            FillRich(_pendingBubble, text);

            _pendingBubble = null;
            _pendingAnswer = "";
            ScrollToEnd();
        }

        // =================================================================
        //  气泡
        // =================================================================

        private void AddUser(string text)
        {
            TextBlock t = new TextBlock();
            t.Style = (Style)FindResource("ChatUserText");
            t.Text = text;

            Border b = new Border();
            b.Style = (Style)FindResource("ChatUser");
            b.Child = t;

            Messages.Children.Add(b);
            ScrollToEnd();
        }

        private void AddAi(string text)
        {
            TextBlock t = new TextBlock();
            t.Style = (Style)FindResource("ChatAiText");
            FillRich(t, text);

            Border b = new Border();
            b.Style = (Style)FindResource("ChatAi");
            b.Child = t;

            Messages.Children.Add(b);
            ScrollToEnd();
        }

        private TextBlock AddWaiting()
        {
            TextBlock t = new TextBlock();
            t.Style = (Style)FindResource("ChatWaitText");
            // 在线要等十几秒，离线是瞬间的事，两句话的语气得对得上各自的等待时间
            t.Text = "正在算　·　规则引擎取数中…";
            if (AiAccess.Exists())
            {
                t.Text = "正在想…";
            }

            Border b = new Border();
            b.Style = (Style)FindResource("ChatAi");
            b.Child = t;

            Messages.Children.Add(b);
            ScrollToEnd();
            return t;
        }

        /// <summary>
        /// 把答案里 **两个星号** 包住的部分加粗。
        ///
        /// 结论性的那句话得比周围重，不然一段话读下来抓不到重点。用 Inlines 一段段拼，
        /// 不用 FlowDocument —— 这里是一行行短文字，上那套排版引擎太重了。
        /// </summary>
        private static void FillRich(TextBlock target, string text)
        {
            target.Text = "";
            target.Inlines.Clear();

            string[] parts = text.Split(new string[] { "**" }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0)
                {
                    continue;
                }

                Run run = new Run(parts[i]);
                if (i % 2 == 1)                // 奇数段落在两个星号之间
                {
                    run.FontWeight = FontWeights.Bold;
                    run.Foreground = BrainBrushes.Ink;
                }
                target.Inlines.Add(run);
            }
        }

        /// <summary>
        /// 加完消息滚到底。要等这一轮布局跑完才有新的高度可滚，
        /// 所以挂在 Loaded 优先级上排队，直接调会滚到加消息之前的位置。
        /// </summary>
        private void ScrollToEnd()
        {
            Dispatcher.BeginInvoke(new Action(DoScroll), DispatcherPriority.Loaded);
        }

        private void DoScroll()
        {
            Scroller.ScrollToEnd();
        }
    }
}

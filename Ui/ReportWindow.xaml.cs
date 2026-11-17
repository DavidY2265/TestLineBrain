using System;
using DesktopPet.LineBrain;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;

namespace DesktopPet.Ui
{
    /// <summary>
    /// AI 解读弹窗。上半是报告正文，下半是可以接着问的对话框，中间可以拖。
    ///
    /// 两种用法：
    /// · 三栏的「AI 解读」按钮 —— 正文由各栏的 Report 类从当前数据现拼
    /// · 小精灵报警点开 —— 正文按那条警告现拼一份，分「这是什么 / 数怎么来的 /
    ///   为什么超线 / 建议动作」四段
    ///
    /// 先显示「正在解读…」再换成正文，不是为了装样子：规则引擎算完是瞬间的事，
    /// 语言模型组织表述才是要等的那一段，这里把这个先后关系演出来。
    /// </summary>
    public partial class ReportWindow : Window
    {
        /// <summary>本班的全部数据。报告正文和追问对话框都用它。</summary>
        private readonly ShiftData _shift;

        private readonly BoardTab _topic;
        private readonly Alert? _alert;
        private DispatcherTimer? _timer;

        /// <summary>
        /// 三栏的「AI 解读」按钮走这个。
        ///
        /// **标题不由调用方传。** 原来三个 Pane 各自拼一份标题传进来，
        /// 拼出来的和报告正文里那个标题还不一样（锁附那份甚至写着写死的
        /// 「规格组 A」）—— 同一份报告两个名字。现在统一由各自的 Report 类出。
        /// </summary>
        public ReportWindow(ShiftData shift, BoardTab topic)
        {
            InitializeComponent();
            _shift = shift;

            _topic = topic;
            _alert = null;

            string title = TitleOf(topic);
            Title = title;
            TitleText.Text = title;

            StartGenerating();
        }

        private string TitleOf(BoardTab topic)
        {
            if (topic == BoardTab.Equipment)
            {
                return AlarmReport.Title(_shift);
            }
            if (topic == BoardTab.Quality)
            {
                return FastenReport.Title(_shift);
            }
            return TestReport.Title(_shift);
        }

        /// <summary>小精灵报警点开走这个：正文是这一条警告的解析，不是整栏的报告。</summary>
        public ReportWindow(ShiftData shift, Alert alert)
        {
            InitializeComponent();
            _shift = shift;

            _topic = alert.Topic;
            _alert = alert;

            string title = "［" + StatusText.Of(alert.Level) + "］" + alert.Subject;
            Title = title;
            TitleText.Text = title;

            JumpButton.Visibility = Visibility.Visible;
            Waiting.Text = "正在解读　·　正在把这条警告的判定依据整理成话…";

            StartGenerating();
        }

        private void StartGenerating()
        {
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(900);
            _timer.Tick += OnGenerateTick;
            _timer.Start();
        }

        private void OnGenerateTick(object? sender, EventArgs e)
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= OnGenerateTick;
                _timer = null;
            }

            // 三栏的正文**全部现拼**。
            //
            // 设备报警和锁附这两份原来是取 Reports.xaml 里写死的 FlowDocument，
            // 那是设计阶段的样稿：「本班整线停机 51 min，MA 86.8%」
            // 「A1 撕膜升降动点警报 8 次 28 min」—— 数字全是手敲的。
            // 结果是数据库没连上、一条记录都没读到，点「AI 解读」照样弹出一份
            // 有鼻子有眼的报告，而且和旁边表格里的数对不上。
            // 这正是这个项目最反对的「两份真相」。
            FlowDocument doc;
            if (_alert != null)
            {
                doc = BuildAlertDoc(_alert);
            }
            else if (_topic == BoardTab.Equipment)
            {
                doc = BuildDoc(TitleOf(_topic), AlarmReport.Sections(_shift));
            }
            else if (_topic == BoardTab.Quality)
            {
                doc = BuildDoc(TitleOf(_topic), FastenReport.Sections(_shift));
            }
            else
            {
                doc = BuildDoc(TitleOf(_topic), TestReport.Sections(_shift));
            }

            Viewer.Document = doc;
            Waiting.Visibility = Visibility.Collapsed;
            Viewer.Visibility = Visibility.Visible;

            // 报告出来了才开对话框：先让人读完再问，顺序反了没意义
            string subject = "";
            if (_alert != null)
            {
                subject = _alert.Subject;
            }
            Chat.Start(_shift, _topic, subject, OpeningLine());
        }

        private string OpeningLine()
        {
            if (_alert != null)
            {
                return "上面是这条警告的完整判定过程。哪一步觉得可疑，直接问我。";
            }
            // 没数据的时候不能说「我只会按固定算法现算」—— 现算个什么？
            // 这句话摆在一份写着「一条记录都没读到」的报告下面，只会让人以为
            // 换个问法就能问出数来。
            bool available = _shift.AlarmsAvailable;
            if (_topic == BoardTab.Quality)
            {
                available = _shift.FastenAvailable;
            }
            if (_topic == BoardTab.Test)
            {
                available = _shift.TestAvailable;
            }

            if (!available)
            {
                return "这一栏本班没有数据，所以本班的数我一个都答不上来 —— "
                     + "问我也不会编。想把数据接上，可以问我该怎么弄。";
            }

            return "报告读完有疑问就在下面问。我只会按固定算法从上面那批原始数据现算，"
                 + "所以答出来的数和上面报告里的必然一致。";
        }

        // =================================================================
        //  三栏的报告正文（都是现拼的）
        // =================================================================

        /// <summary>
        /// 标题 + 若干段。三栏共用这一个，所以三份报告长得是一家人，
        /// 排版上看不出区别 —— 区别只在各自的 Report 类里怎么组织内容。
        ///
        /// 「方法与口径」那一段由各自的 Report 类自己写，不在这儿拼死一句 ——
        /// 三栏的数据来源和算法都不一样，共用一句话必然有两栏是错的。
        /// </summary>
        private FlowDocument BuildDoc(string title, List<ReportSection> sections)
        {
            FlowDocument doc = NewDoc();

            AddHead(doc, title,
                    _shift.LineLabel + "　·　" + _shift.ShiftName + " "
                  + _shift.Window.StartText + "–" + _shift.Window.EndText
                  + "　·　数据截至 " + _shift.DataUntil
                  + "　·　规则引擎 v1.0　·　阈值取自 brain.csv");

            for (int i = 0; i < sections.Count; i++)
            {
                AddSection(doc, sections[i].Heading, sections[i].Body);
            }
            return doc;
        }

        // =================================================================
        //  报警解析正文
        // =================================================================

        /// <summary>
        /// 按四段拼一份文档。段落样式直接借报告那套（ReportStyles.xaml 里的 RepH / RepP 等），
        /// 这样报警解析和三份报告长得是一家人，不是两套排版。
        /// </summary>
        private FlowDocument BuildAlertDoc(Alert a)
        {
            FlowDocument doc = NewDoc();

            AddHead(doc, a.Subject,
                    _shift.LineLabel + "　·　" + _shift.ShiftName + " "
                  + _shift.Window.StartText + "–" + _shift.Window.EndText
                  + "　·　" + StatusText.Of(a.Level)
                  + "　·　规则引擎 v1.0　·　阈值取自 brain.csv");

            AddSection(doc, "一、这是什么", a.What);
            AddSection(doc, "二、这些数是怎么来的", a.How);
            AddSection(doc, "三、为什么判它超线", a.Why);
            AddSection(doc, "四、建议动作", a.Do);

            // thresholds.csv 早就并进 brain.csv 了，这句话一直没跟着改。
            // 照着它去找文件会找不到。
            AddFoot(doc, "本条警告里的每一个数值都由本地规则引擎现算，"
                       + "和看板上那张表出自同一次解析，语言模型不参与任何计算。"
                       + "判定用的门槛写在 " + BrainConfig.Folder
                       + " 下的 brain.csv 里，改了门槛这条警告可能就不再出现。");
            return doc;
        }

        // =================================================================
        //  拼文档的零件，报警解析和测试报告共用
        // =================================================================

        private FlowDocument NewDoc()
        {
            FlowDocument doc = new FlowDocument();
            doc.FontFamily = (System.Windows.Media.FontFamily)FindResource("UiFont");
            doc.PagePadding = new Thickness(0);
            doc.TextAlignment = TextAlignment.Left;
            return doc;
        }

        private void AddHead(FlowDocument doc, string title, string meta)
        {
            Paragraph head = new Paragraph();
            head.Style = FindStyle("RepHead");

            Run titleRun = new Run(title);
            titleRun.Style = FindStyle("RepTitle");
            head.Inlines.Add(titleRun);
            head.Inlines.Add(new LineBreak());

            Run metaRun = new Run(meta);
            metaRun.Style = FindStyle("RepMeta");
            head.Inlines.Add(metaRun);

            doc.Blocks.Add(head);
        }

        private void AddFoot(FlowDocument doc, string text)
        {
            Paragraph foot = new Paragraph();
            foot.Style = FindStyle("RepFoot");
            FillRich(foot, text);
            doc.Blocks.Add(foot);
        }

        private void AddSection(FlowDocument doc, string heading, string body)
        {
            if (body == null || body.Length == 0)
            {
                return;
            }

            Paragraph h = new Paragraph();
            h.Style = FindStyle("RepH");
            h.Inlines.Add(new Run(heading));
            doc.Blocks.Add(h);

            Paragraph p = new Paragraph();
            p.Style = FindStyle("RepP");
            FillRich(p, body);
            doc.Blocks.Add(p);
        }

        /// <summary>和对话框里一样，**两个星号**包住的部分加粗。</summary>
        private void FillRich(Paragraph target, string text)
        {
            string[] parts = text.Split(new string[] { "**" }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0)
                {
                    continue;
                }

                Run run = new Run(parts[i]);
                if (i % 2 == 1)
                {
                    run.Style = FindStyle("RepB");
                }
                target.Inlines.Add(run);
            }
        }

        private Style? FindStyle(string key)
        {
            return TryFindResource(key) as Style;
        }

        // =================================================================
        //  窗口
        // =================================================================

        /// <summary>看完这一条，跳到它所在的那一栏看全貌。</summary>
        private void OnJumpClick(object sender, RoutedEventArgs e)
        {
            BrainWindow? board = BrainWindow.Current();
            if (board == null)
            {
                board = new BrainWindow(_shift);
                board.Show();
            }

            board.SelectTab(_topic);
            board.Activate();
        }

        private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 焦点在下面的对话框里时按 Esc 不关窗，那会把刚打了一半的问题一起弄丢
            if (e.Key == Key.Escape && !Chat.IsKeyboardFocusWithin)
            {
                Close();
            }
        }
    }
}

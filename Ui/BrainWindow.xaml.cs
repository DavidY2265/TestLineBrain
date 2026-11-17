using DesktopPet.LineBrain;
using System.Windows;
using System.Windows.Input;

namespace DesktopPet.Ui
{
    /// <summary>
    /// 看板外壳。左键点小狮子出来的就是它。
    /// </summary>
    public partial class BrainWindow : Window
    {
        /// <summary>本班的全部数据。由上层传进来，这个界面只负责显示。</summary>
        private readonly ShiftData _shift;

        private readonly EquipmentPane PaneEq;
        private readonly QualityPane PaneMq;
        private readonly TestPane PaneTe;

        public BrainWindow(ShiftData shift)
        {
            InitializeComponent();
            _shift = shift;

            // 三个面板在这里建，不在 XAML 里 —— 它们要接本班数据，
            // 而 XAML 只会调无参构造。三个面板叠在同一个格子里，靠 Visibility 切。
            PaneEq = new EquipmentPane(_shift);
            PaneMq = new QualityPane(_shift);
            PaneTe = new TestPane(_shift);
            PaneMq.Visibility = Visibility.Collapsed;
            PaneTe.Visibility = Visibility.Collapsed;
            PaneHost.Children.Add(PaneEq);
            PaneHost.Children.Add(PaneMq);
            PaneHost.Children.Add(PaneTe);

            // 放在建完面板之后：IsChecked 会立刻触发 Checked，
            // 那个处理函数要用到 PaneEq 这几个字段。
            TabEq.IsChecked = true;

            // 页签上那个红色小数字原来写死是 31，现在按本班实际报警次数来
            TabEqCount.Text = _shift.AlarmTimes.ToString();

            SubtitleText.Text = _shift.LineLabel + "　·　" + _shift.ShiftName + " "
                              + _shift.Window.StartText + "–" + _shift.Window.EndText
                              + "　·　数据截至 " + _shift.DataUntil;

            ShowDataProblems();
        }

        /// <summary>
        /// 读数据时发现的毛病，挂到标题栏上。
        ///
        /// 这些问题以前只是被 DataProblems 收着，界面上一个字都不显示 ——
        /// 缺了一个文件、限值变更舍弃了半天的数据，看板照样画得出漂亮的表，
        /// 只是那些数是错的。**静默的错数比明显的报错危险得多**，这是全项目一致的原则，
        /// 这里把最后一段接上。
        ///
        /// 只在标题栏放一行摘要，点开看全文 —— 弹个对话框拦在前面太吵，
        /// 而限值变更这种事几乎每天都有。
        /// </summary>
        private void ShowDataProblems()
        {
            if (_shift.Problems.Count == 0)
            {
                return;
            }

            ProblemText.Text = "⚠ 数据有 " + _shift.Problems.Count + " 处问题　点开看";
            ProblemList.ItemsSource = _shift.Problems;
            ProblemFoot.Text = FootFor();
            ProblemChip.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 弹层底下那句「所以现在该干嘛」。
        ///
        /// 光列出问题不够 —— 「db_name / db_user 没填」这条，人看完的下一个念头
        /// 一定是「那我上哪儿填」。答案就在右键菜单里，但没人会想到去那儿找。
        /// </summary>
        private string FootFor()
        {
            if (!_shift.AlarmsAvailable)
            {
                return "数据库还没配：右键小精灵 →「数据库连接…」，填完点「测试连接」，"
                     + "通了再保存，然后重启程序。";
            }
            if (!_shift.TestAvailable)
            {
                return "测试 log 放在 " + _shift.Config.TestLogPath
                     + "，一台测试机一份，原样拷进去就行，然后重启程序。";
            }
            return "这些不影响其余数据 —— 相关的那几行会空着，别的照常算。";
        }

        private void OnProblemChipClick(object sender, MouseButtonEventArgs e)
        {
            ProblemPopup.IsOpen = !ProblemPopup.IsOpen;
        }

        /// <summary>
        /// 现在开着的那个看板，没开返回 null。
        ///
        /// 不缓存成字段：小精灵那边要用、报警解析窗的「打开看板」也要用，
        /// 两处各存一份迟早对不上（一处关了另一处还以为开着，于是开出第二个）。
        /// WPF 自己就维护着窗口列表，问它最准。
        /// </summary>
        public static BrainWindow? Current()
        {
            if (Application.Current == null)
            {
                return null;
            }

            foreach (Window w in Application.Current.Windows)
            {
                BrainWindow? board = w as BrainWindow;
                if (board != null)
                {
                    return board;
                }
            }
            return null;
        }

        /// <summary>切到某一栏。</summary>
        public void SelectTab(BoardTab tab)
        {
            if (tab == BoardTab.Quality)
            {
                TabMq.IsChecked = true;
            }
            else if (tab == BoardTab.Test)
            {
                TabTe.IsChecked = true;
            }
            else
            {
                TabEq.IsChecked = true;
            }
        }

        private void OnTabEqChecked(object sender, RoutedEventArgs e)
        {
            ShowPane(PaneEq);
        }

        private void OnTabMqChecked(object sender, RoutedEventArgs e)
        {
            ShowPane(PaneMq);
        }

        private void OnTabTeChecked(object sender, RoutedEventArgs e)
        {
            ShowPane(PaneTe);
        }

        private void ShowPane(UIElement pane)
        {
            // 三栏一次性全建出来，切页签只改 Visibility，不重建 ——
            // 表都是静态的，留在内存里比每次重建便宜，滚动位置也不会丢
            PaneEq.Visibility = VisibleIf(pane == PaneEq);
            PaneMq.Visibility = VisibleIf(pane == PaneMq);
            PaneTe.Visibility = VisibleIf(pane == PaneTe);
        }

        private static Visibility VisibleIf(bool show)
        {
            if (show)
            {
                return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        /// <summary>
        /// 板面最宽画到多少。21.5 寸全屏是 1920 宽，再宽下去
        /// 品质那张 11 列的表和良率表会被扯开，数字和它的表头对不上眼，
        /// 所以超出的部分两边平分成留白。
        /// </summary>
        private const double BoardMaxWidth = 1560;

        private void OnBoardSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 用 e.NewSize.Width，也就是 Border 连 Padding 一起的外框宽。
            // Padding 改了它不变，所以不会把自己算进死循环。
            double spare = e.NewSize.Width - BoardMaxWidth;
            double side = 0;
            if (spare > 0)
            {
                side = spare / 2;
            }
            Board.Padding = new Thickness(18 + side, 16, 18 + side, 20);
        }

        private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
        {
            // 双击标题栏在全屏和 1120×760 之间切，和普通窗口一个习惯
            if (e.ClickCount == 2)
            {
                if (WindowState == WindowState.Maximized)
                {
                    WindowState = WindowState.Normal;
                }
                else
                {
                    WindowState = WindowState.Maximized;
                }
                return;
            }

            // 全屏时没什么可拖的，拖了反而把窗口拽成一个奇怪的尺寸
            if (WindowState == WindowState.Maximized)
            {
                return;
            }

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
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }
    }
}

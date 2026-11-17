using System;
using DesktopPet.LineBrain;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DesktopPet.Ui
{
    /// <summary>
    /// 设备报警栏。
    ///
    /// 只有一张表：时间轴是它的最后一列，每行自己那条报警画在自己那一行上，
    /// 所以站别和报警内容只写一遍，也不再需要「轴 ↓」那种跳来跳去的联动。
    /// 末行「整线」是各台报警区间的并集，不是上面几行相加。
    /// </summary>
    public partial class EquipmentPane : UserControl
    {
        /// <summary>本班的全部数据。由上层传进来，这个界面只负责显示。</summary>
        private readonly ShiftData _shift;

        /// <summary>汇总表当前按什么排。三个排序按钮改它，然后重建表。</summary>
        private AlarmSortKey _sortKey = AlarmSortKey.Duration;

        public EquipmentPane(ShiftData shift)
        {
            InitializeComponent();
            _shift = shift;

            // 六个 KPI 一个都没写死，全部从数据库那一条条报警记录算出来。
            //
            // 取不到数据的时候**六格要么全是数，要么全是「—」**：连不上库算出来是
            // 「0 条报警 → 停机 0 → MA 100%」，那看着是一条完美产线，没人会怀疑。
            // 只挡住 MA 一格也不行 —— 「MA 显示 —，停机 0 min，报警 0 次」
            // 读起来是「就这一个指标算不出来，其余都是零」，比六格全 100% 更误导。
            // **静默的错数比明显的报错危险得多。**
            if (!_shift.AlarmsAvailable)
            {
                KpiMa.Text = "—";
                KpiMaDesc.Text = "报警数据没取到，不是「本班没报警」。看标题栏的提示";
                KpiUnionRun.Text = "—";
                KpiSumRun.Text = "—";
                KpiOverlapRun.Text = "—";
                KpiCount.Text = "—";
                KpiCountDesc.Text = "数据库没连上，一条报警记录都没读到";
            }
            else
            {
                KpiMa.Text = _shift.Availability.ToString("F1") + "%";
                KpiMaDesc.Text = "门槛 " + _shift.Config.MaGate.ToString("F1")
                               + "%　·　计划时间 " + _shift.SpanMinutes.ToString("F0") + " min";
                KpiUnionRun.Text = _shift.SumUnion.ToString("F0");
                KpiSumRun.Text = _shift.SumAll.ToString("F0");
                KpiOverlapRun.Text = _shift.Overlap.ToString("F1");
                KpiCount.Text = _shift.AlarmTimes.ToString();
                KpiCountDesc.Text = StationCount() + " 台合计　·　= 停机次数";
            }

            FillWorstStation();
            FillNoData();

            CardSubText.Text = "开班 " + _shift.Window.StartText + " – " + _shift.Window.EndText
                             + "　·　" + _shift.SpanMinutes.ToString("F0")
                             + " min　·　末行为整线停机（各台区间并集）";

            SortDur.IsChecked = true;            // 顺手把表建出来
        }

        /// <summary>在监的台数 = 不重复的站别个数。</summary>
        private int StationCount()
        {
            List<string> seen = new List<string>();
            for (int i = 0; i < _shift.Alarms.Count; i++)
            {
                string st = _shift.Alarms[i].Station;
                if (!seen.Contains(st))
                {
                    seen.Add(st);
                }
            }
            return seen.Count;
        }

        /// <summary>
        /// 最差站别：把各条报警按站别归堆，比累计时长。
        /// 这里用的是该站自己那几条的相加，不是并集 —— 问的是「哪台设备最闹」，
        /// 不是「产线停了多久」，两个问题两个口径。
        /// </summary>
        private void FillWorstStation()
        {
            List<string> names = new List<string>();
            List<double> mins = new List<double>();
            List<int> times = new List<int>();

            for (int i = 0; i < _shift.Alarms.Count; i++)
            {
                AlarmGroup g = _shift.Alarms[i];
                if (g.IsNoData)
                {
                    continue;
                }

                int at = names.IndexOf(g.Station);
                if (at < 0)
                {
                    names.Add(g.Station);
                    mins.Add(g.Total);
                    times.Add(g.Count);
                }
                else
                {
                    mins[at] = mins[at] + g.Total;
                    times[at] = times[at] + g.Count;
                }
            }

            if (names.Count == 0)
            {
                KpiWorstSt.Text = "—";
                KpiWorstStDesc.Text = "本班无报警";
                return;
            }

            int top = 0;
            for (int i = 1; i < names.Count; i++)
            {
                if (mins[i] > mins[top])
                {
                    top = i;
                }
            }

            KpiWorstSt.Text = names[top];
            KpiWorstStDesc.Text = times[top] + " 次　·　"
                                + Format.Duration(mins[top]) + " min";
        }

        /// <summary>通讯断连那几条：时长和次数都算出来，它们不进 MA。</summary>
        private void FillNoData()
        {
            double mins = 0;
            int times = 0;
            for (int i = 0; i < _shift.Alarms.Count; i++)
            {
                if (_shift.Alarms[i].IsNoData)
                {
                    mins += _shift.Alarms[i].Total;
                    times += _shift.Alarms[i].Count;
                }
            }

            KpiNoDataRun.Text = Format.Duration(mins);
            KpiNoDataDesc.Text = "通讯断连 " + times + " 次　·　已剔除出 MA";
        }

        // ------------------------------------------------------------ 排序

        private void OnSortDurationChecked(object sender, RoutedEventArgs e)
        {
            _sortKey = AlarmSortKey.Duration;
            BuildSummary(_shift.SortAlarms(_sortKey));
        }

        private void OnSortCountChecked(object sender, RoutedEventArgs e)
        {
            _sortKey = AlarmSortKey.Count;
            BuildSummary(_shift.SortAlarms(_sortKey));
        }

        private void OnSortStationChecked(object sender, RoutedEventArgs e)
        {
            _sortKey = AlarmSortKey.Station;
            BuildSummary(_shift.SortAlarms(_sortKey));
        }

        // ------------------------------------------------------------ 汇总表

        private void BuildSummary(List<AlarmGroup> rows)
        {
            SummaryGrid.Children.Clear();
            SummaryGrid.RowDefinitions.Clear();
            SummaryGrid.ColumnDefinitions.Clear();

            TableKit.AddFixed(SummaryGrid, 52);             // 站别
            TableKit.AddStar(SummaryGrid, 1, 170);          // 报警内容
            TableKit.AddFixed(SummaryGrid, 56);             // 次数
            TableKit.AddFixed(SummaryGrid, 84);             // 累计时长
            TableKit.AddFixed(SummaryGrid, 56);             // 占比
            TableKit.AddFixed(SummaryGrid, 78);             // 单次均值
            TableKit.AddStar(SummaryGrid, 1.7, 240);        // 时间轴

            // 表头 + 各类报警 + 整线 + 刻度
            TableKit.AddRows(SummaryGrid, rows.Count + 3);

            int totalRow = rows.Count + 1;
            int axisRow = rows.Count + 2;

            TableKit.Place(SummaryGrid, TableKit.Cell(this, "站别", "Th"), 0, 0);
            TableKit.Place(SummaryGrid, TableKit.Cell(this, "报警内容", "Th"), 0, 1);
            TableKit.Place(SummaryGrid, TableKit.Cell(this, "次数", "ThRight"), 0, 2);
            TableKit.Place(SummaryGrid, TableKit.Cell(this, "累计时长", "ThRight"), 0, 3);
            TableKit.Place(SummaryGrid, TableKit.Cell(this, "占比", "ThRight"), 0, 4);
            TableKit.Place(SummaryGrid, TableKit.Cell(this, "单次均值", "ThRight"), 0, 5);

            TextBlock axisHead = TableKit.Cell(this, "时间轴　全线共用一根轴", "Th");
            axisHead.Margin = new Thickness(BarLeft, 0, 0, 7);
            TableKit.Place(SummaryGrid, axisHead, 0, 6);

            TableKit.Place(SummaryGrid, TableKit.Rule(this, "HeadLine", 7), 0, 0);

            double total = _shift.Sum(_sortKey);
            double worst = _shift.Peak(_sortKey);

            for (int i = 0; i < rows.Count; i++)
            {
                AlarmGroup r = rows[i];
                int row = i + 1;
                double value = ShiftData.ValueOf(r, _sortKey);
                bool top = !r.IsNoData && Math.Abs(value - worst) < 1e-9;

                // 站别 —— 前面那个点：最贵的那一类标红，数据缺失用灰底描边
                StackPanel st = new StackPanel();
                st.Orientation = Orientation.Horizontal;
                st.Margin = new Thickness(0, 8, 0, 8);

                System.Windows.Shapes.Ellipse dot;
                if (r.IsNoData)
                {
                    dot = TableKit.Dot(BrainBrushes.NoData);
                    dot.Stroke = BrainBrushes.Soft;
                    dot.StrokeThickness = 1;
                }
                else
                {
                    Brush dotColor = BrainBrushes.Soft;
                    if (top)
                    {
                        dotColor = BrainBrushes.Crit;
                    }
                    dot = TableKit.Dot(dotColor);
                }
                st.Children.Add(dot);

                TextBlock stn = new TextBlock();
                stn.Text = r.Station;
                stn.FontSize = 12;
                stn.FontWeight = FontWeights.SemiBold;
                stn.Foreground = (Brush)FindResource("InkBrush");
                stn.VerticalAlignment = VerticalAlignment.Center;
                st.Children.Add(stn);
                TableKit.Place(SummaryGrid, st, row, 0);

                // 报警内容
                if (r.IsNoData)
                {
                    StackPanel nm = new StackPanel();
                    nm.Orientation = Orientation.Horizontal;
                    nm.Margin = new Thickness(0, 8, 0, 8);
                    TextBlock txt = new TextBlock();
                    txt.Text = r.Name;
                    txt.FontSize = 11;
                    txt.Foreground = (Brush)FindResource("SoftBrush");
                    txt.VerticalAlignment = VerticalAlignment.Center;
                    txt.Margin = new Thickness(0, 0, 8, 0);
                    txt.TextTrimming = TextTrimming.CharacterEllipsis;
                    nm.Children.Add(txt);
                    nm.Children.Add(TableKit.Tag(this, "数据缺失", "TagBase"));
                    TableKit.Place(SummaryGrid, nm, row, 1);
                }
                else
                {
                    TextBlock name = TableKit.Cell(this, r.Name, "TdName");
                    name.TextTrimming = TextTrimming.CharacterEllipsis;
                    name.ToolTip = r.Name;
                    TableKit.Place(SummaryGrid, name, row, 1);
                }

                TableKit.Place(SummaryGrid, TableKit.Cell(this, r.Count.ToString(), "TdRight"), row, 2);
                TableKit.Place(SummaryGrid,
                    TableKit.Cell(this, Format.Duration(r.Total) + " min", "TdRight"), row, 3);

                // 通讯断连那条没有占比可言：状态未知，不进分子也不进分母
                string pctText = "—";
                if (!r.IsNoData)
                {
                    pctText = Format.Percent(value, total).ToString("F0") + "%";
                }
                TextBlock pct = TableKit.Cell(this, pctText, "TdRight");
                if (top)
                {
                    pct.Foreground = BrainBrushes.Crit;
                    pct.FontWeight = FontWeights.Bold;
                }
                TableKit.Place(SummaryGrid, pct, row, 4);

                TableKit.Place(SummaryGrid,
                    TableKit.Cell(this, Format.Each(r.Each), "TdSmallRight"), row, 5);

                // 这一行自己那条轴
                List<AlarmSpan> segs = new List<AlarmSpan>();
                for (int k = 0; k < r.Starts.Length; k++)
                {
                    segs.Add(new AlarmSpan(r.Starts[k], r.Each));
                }
                TimelineBar bar = new TimelineBar();
                bar.Height = 15;
                bar.Margin = new Thickness(BarLeft, 8, 0, 8);
                bar.VerticalAlignment = VerticalAlignment.Center;
                bar.IsNoData = r.IsNoData;
                bar.Segments = segs;
                TableKit.Place(SummaryGrid, bar, row, 6);

                // 最后一行下面紧接着整线那条粗线，就不再画细线了
                if (i < rows.Count - 1)
                {
                    TableKit.Place(SummaryGrid, TableKit.Rule(this, "RowLine", 7), row, 0);
                }
            }

            BuildTotalRow(totalRow);
            BuildAxisRow(axisRow);
        }

        /// <summary>
        /// 表尾的整线行。这里的时长是各台报警区间的并集：两台同时停，现场只停了一次。
        /// </summary>
        private void BuildTotalRow(int row)
        {
            TableKit.Place(SummaryGrid, TableKit.Rule(this, "TotalLine", 7), row, 0);

            TableKit.Place(SummaryGrid, TableKit.Cell(this, "整线", "TdName"), row, 0);

            StackPanel name = new StackPanel();
            name.Margin = new Thickness(0, 9, 8, 9);
            TextBlock head = new TextBlock();
            head.Text = "整线停机";
            head.FontSize = 12;
            head.FontWeight = FontWeights.SemiBold;
            head.Foreground = (Brush)FindResource("InkBrush");
            name.Children.Add(head);
            TextBlock note = new TextBlock();
            note.Text = "各台区间并集　·　合计 " + Format.Duration(_shift.SumAll)
                      + " min 中重叠 " + Format.Duration(_shift.Overlap) + " min";
            note.FontSize = 10.5;
            note.Foreground = (Brush)FindResource("SoftBrush");
            note.Margin = new Thickness(0, 2, 0, 0);
            note.TextTrimming = TextTrimming.CharacterEllipsis;
            name.Children.Add(note);
            TableKit.Place(SummaryGrid, name, row, 1);

            TableKit.Place(SummaryGrid,
                TableKit.Cell(this, _shift.AlarmTimes.ToString(), "TdRight"), row, 2);

            TextBlock union = TableKit.Cell(this, _shift.SumUnion.ToString("F0") + " min", "TdRight");
            union.FontWeight = FontWeights.Bold;
            union.Foreground = BrainBrushes.Crit;
            TableKit.Place(SummaryGrid, union, row, 3);

            TableKit.Place(SummaryGrid, TableKit.Cell(this, "—", "TdRight"), row, 4);
            TableKit.Place(SummaryGrid, TableKit.Cell(this, "—", "TdSmallRight"), row, 5);

            TimelineBar bar = new TimelineBar();
            bar.Height = 20;
            bar.Margin = new Thickness(BarLeft, 9, 0, 9);
            bar.VerticalAlignment = VerticalAlignment.Center;
            bar.Segments = _shift.LineDown;
            TableKit.Place(SummaryGrid, bar, row, 6);
        }

        /// <summary>刻度按真实时刻摆：10:00 在 120/386 处，12:00 在 240/386 处。</summary>
        private void BuildAxisRow(int row)
        {
            Grid axis = new Grid();
            axis.Margin = new Thickness(BarLeft, 3, 0, 0);
            // 120 / 120 / 146 是 08:00–10:00、10:00–12:00、12:00–14:26 三段的分钟数，
            // 用 Star 按比例分宽，刻度才落在时间轴上真正对应的位置
            AddAxisColumn(axis, 120);
            AddAxisColumn(axis, 120);
            AddAxisColumn(axis, 146);
            TableKit.Place(axis, TableKit.Cell(this, "08:00", "TlAxis"), 0, 0);
            TableKit.Place(axis, TableKit.Cell(this, "10:00", "TlAxis"), 0, 1);
            TableKit.Place(axis, TableKit.Cell(this, "12:00", "TlAxis"), 0, 2);
            TextBlock last = TableKit.Cell(this, _shift.Window.EndText, "TlAxis");
            last.HorizontalAlignment = HorizontalAlignment.Right;
            TableKit.Place(axis, last, 0, 2);
            TableKit.Place(SummaryGrid, axis, row, 6);
        }

        private void OnAiClick(object sender, RoutedEventArgs e)
        {
            ReportWindow w = new ReportWindow(_shift, BoardTab.Equipment);
            w.Owner = Window.GetWindow(this);
            w.Show();
        }

        /// <summary>刻度行的一列。宽度按分钟数成比例分，刻度才落在对的位置。</summary>
        private static void AddAxisColumn(Grid axis, double minutes)
        {
            ColumnDefinition column = new ColumnDefinition();
            column.Width = new GridLength(minutes, GridUnitType.Star);
            axis.ColumnDefinitions.Add(column);
        }

        /// <summary>轴那一列左边留的空。表头、每条轴、刻度都用它，三者才对得齐。</summary>
        private const double BarLeft = 14;
    }
}

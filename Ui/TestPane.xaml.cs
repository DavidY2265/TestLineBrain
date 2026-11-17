using System;
using DesktopPet.LineBrain;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DesktopPet.Ui
{
    /// <summary>
    /// 测试栏。两张表：上面按测站看良率，下面按测项看能力和漂移。
    ///
    /// 良率、Cpk、漂移比、状态没有一个是填进来的 —— 测站表只存测试数和各类 NG 数，
    /// 测项表只存均值和两个标准差，剩下的全是算的。所以改一个 NG 数，
    /// 站良率、整线良率、对比基线、KPI 会一起动。
    /// </summary>
    public partial class TestPane : UserControl
    {
        /// <summary>本班的全部数据。由上层传进来，这个界面只负责显示。</summary>
        private readonly ShiftData _shift;

        public TestPane(ShiftData shift)
        {
            InitializeComponent();
            _shift = shift;

            FillKpi();
            BuildStationTable();
            BuildItemTable();
        }

        // ------------------------------------------------------------ KPI

        private void FillKpi()
        {
            // 没读到 log 就全部「—」。良率那格算的是 (0 − 0) ÷ 0，
            // 显示出来是 0.00% —— 看着像整线全挂，比空着糟得多。
            if (!_shift.TestAvailable)
            {
                KpiYieldRun.Text = "—";
                KpiYieldDesc.Text = "没读到本班（" + _shift.Window.DateText + "）的测试 log";
                KpiNgRun.Text = "—";
                KpiNgDesc.Text = "不是 0 笔不良，是一笔测试都没读到";
                KpiWorst.Text = "—";
                KpiWorstDesc.Text = "没有测站数据";
                KpiCalcRun.Text = "—";
                KpiCalcTotalRun.Text = "";
                KpiCalcDesc.Text = "没有测项可盘点";
                KpiBelowKey.Text = "Cpk < " + _shift.Config.CpkWarn.ToString("F2");
                KpiBelow.Text = "—";
                KpiBelowDesc.Text = "没有测项可判";
                return;
            }

            KpiYieldRun.Text = _shift.LineYield().ToString("F2");
            KpiYieldDesc.Text = "门槛 " + _shift.Config.YieldGate.ToString("F1")
                              + "%　·　基线 " + Format.Delta(_shift.LineVsBaseline(), 2);

            KpiNgRun.Text = _shift.TestNg().ToString();
            KpiNgDesc.Text = _shift.TestTotal().ToString("N0") + " 笔中　·　超规 "
                           + _shift.TestOverSpec() + " · 功能 " + _shift.TestFuncFail();

            TestStationRow? worst = _shift.WorstStation();
            if (worst == null)
            {
                KpiWorst.Text = "—";
                KpiWorstDesc.Text = "没有测站数据";
            }
            else
            {
                KpiWorst.Text = worst.Name;

                string gateNote = "　·　仍在门槛内";
                if (worst.Yield < _shift.Config.YieldGate)
                {
                    gateNote = "　·　已破门槛";
                }

                KpiWorstDesc.Text = worst.Yield.ToString("F1") + "%　·　"
                                  + Format.Delta(worst.VsBaseline)
                                  + gateNote;
            }

            int calc = _shift.TestCpkCapable;
            int all = _shift.TestTotalItems;
            KpiCalcRun.Text = calc.ToString();
            KpiCalcTotalRun.Text = " / " + all;
            KpiCalcDesc.Text = "不可算 " + (all - calc) + " 项，原因已分类";

            KpiBelowKey.Text = "Cpk < " + _shift.Config.CpkWarn.ToString("F2");
            KpiBelow.Text = _shift.TestBelowWarn().ToString();
            KpiBelowDesc.Text = "其中 " + _shift.TestBelowCrit()
                              + " 项 < " + _shift.Config.CpkCrit.ToString("F2");

            KpiDrift.Text = _shift.TestDrifting().ToString();
            KpiDriftDesc.Text = "σ总 / σ内 > " + _shift.Config.DriftRatio.ToString("F1");
        }

        // ------------------------------------------------------------ 测站表

        private void BuildStationTable()
        {
            StationTitle.Text = "测试良率分析　本班 · " + _shift.TestStations.Count + " 个测站";
            StationSub.Text = "门槛 " + _shift.Config.YieldGate.ToString("F1")
                            + "%　·　基线为同班次前七日均值";

            StationGrid.Children.Clear();
            StationGrid.RowDefinitions.Clear();
            StationGrid.ColumnDefinitions.Clear();

            // 测站名是从 log 档名和 #B 行解析出来的，长短不一，写死宽度会被截掉
            TableKit.AddStar(StationGrid, 1, 140);          // 测站
            TableKit.AddStar(StationGrid, 1, 80);           // 测试数
            TableKit.AddFixed(StationGrid, 70);             // NG
            TableKit.AddFixed(StationGrid, 70);             // 超规
            TableKit.AddFixed(StationGrid, 82);             // 功能失败
            TableKit.AddFixed(StationGrid, 82);             // 未知异常
            TableKit.AddFixed(StationGrid, 88);             // 良率
            TableKit.AddFixed(StationGrid, 96);             // 对比基线

            List<TestStationRow> rows = _shift.TestStations;
            TableKit.AddRows(StationGrid, rows.Count + 2);  // 表头 + 各站 + 整线

            TableKit.Place(StationGrid, TableKit.Cell(this, "测站", "Th"), 0, 0);
            TableKit.Place(StationGrid, TableKit.Cell(this, "测试数", "ThRight"), 0, 1);
            TableKit.Place(StationGrid, TableKit.Cell(this, "NG", "ThRight"), 0, 2);
            TableKit.Place(StationGrid, TableKit.Cell(this, "超规", "ThRight"), 0, 3);
            TableKit.Place(StationGrid, TableKit.Cell(this, "功能失败", "ThRight"), 0, 4);
            TableKit.Place(StationGrid, TableKit.Cell(this, "未知异常", "ThRight"), 0, 5);
            TableKit.Place(StationGrid, TableKit.Cell(this, "良率", "ThRight"), 0, 6);
            TableKit.Place(StationGrid, TableKit.Cell(this, "对比基线", "ThRight"), 0, 7);
            TableKit.Place(StationGrid, TableKit.Rule(this, "HeadLine", 8), 0, 0);

            for (int i = 0; i < rows.Count; i++)
            {
                TestStationRow s = rows[i];
                int row = i + 1;
                bool broken = s.Yield < _shift.Config.YieldGate;

                TableKit.Place(StationGrid, TableKit.Cell(this, s.Name, "TdName"), row, 0);
                TableKit.Place(StationGrid, TableKit.Cell(this, s.Tested.ToString("N0"), "TdRight"), row, 1);
                TableKit.Place(StationGrid, TableKit.Cell(this, s.Ng.ToString(), "TdRight"), row, 2);
                TableKit.Place(StationGrid, TableKit.Cell(this, s.OverSpec.ToString(), "TdRight"), row, 3);

                // 功能失败没有测值，查的东西和超规完全不同，有一笔就该显眼
                bool hasFuncFail = s.FuncFail > 0;
                Brush? funcFailColor = null;
                if (hasFuncFail)
                {
                    funcFailColor = BrainBrushes.Crit;
                }
                TableKit.Place(StationGrid,
                    TableKit.Cell(this, s.FuncFail.ToString(), "TdRight", funcFailColor, hasFuncFail),
                    row, 4);

                TableKit.Place(StationGrid, TableKit.Cell(this, s.Unknown.ToString(), "TdRight"), row, 5);

                Brush yieldColor = BrainBrushes.Ok;
                if (broken)
                {
                    yieldColor = BrainBrushes.Crit;
                }
                TableKit.Place(StationGrid,
                    TableKit.Cell(this, s.Yield.ToString("F1") + "%", "TdRight", yieldColor, true),
                    row, 6);

                // 没破门槛的差值不上色也不加粗 —— 只有真出事的那一行该抢眼
                Brush? deltaColor = null;
                if (broken)
                {
                    deltaColor = BrainBrushes.Crit;
                }
                TableKit.Place(StationGrid,
                    TableKit.Cell(this, Format.Delta(s.VsBaseline), "TdRight", deltaColor, broken),
                    row, 7);

                if (i < rows.Count - 1)
                {
                    TableKit.Place(StationGrid, TableKit.Rule(this, "RowLine", 8), row, 0);
                }
            }

            BuildStationTotal(rows.Count + 1);
        }

        /// <summary>
        /// 整线那一行。良率是按测试数加权算的，不是三个站的良率相加除以三 ——
        /// 测试数不一样，平均一下就把小站的权重放大了。
        /// </summary>
        private void BuildStationTotal(int row)
        {
            TableKit.Place(StationGrid, TableKit.Rule(this, "TotalLine", 8), row, 0);

            TableKit.Place(StationGrid, TableKit.Cell(this, "整线", "TdName"), row, 0);
            TableKit.Place(StationGrid,
                TableKit.Cell(this, _shift.TestTotal().ToString("N0"), "TdRight", BrainBrushes.Ink, true), row, 1);
            TableKit.Place(StationGrid,
                TableKit.Cell(this, _shift.TestNg().ToString(), "TdRight", BrainBrushes.Ink, true), row, 2);
            TableKit.Place(StationGrid,
                TableKit.Cell(this, _shift.TestOverSpec().ToString(), "TdRight", BrainBrushes.Ink, true), row, 3);
            TableKit.Place(StationGrid,
                TableKit.Cell(this, _shift.TestFuncFail().ToString(), "TdRight", BrainBrushes.Ink, true), row, 4);

            int unknown = 0;
            for (int i = 0; i < _shift.TestStations.Count; i++)
            {
                unknown += _shift.TestStations[i].Unknown;
            }
            TableKit.Place(StationGrid,
                TableKit.Cell(this, unknown.ToString(), "TdRight", BrainBrushes.Ink, true), row, 5);

            TableKit.Place(StationGrid,
                TableKit.Cell(this, _shift.LineYield().ToString("F2") + "%", "TdRight",
                              BrainBrushes.Ink, true), row, 6);

            double delta = _shift.LineVsBaseline();
            Brush totalDeltaColor = BrainBrushes.Ok;
            if (delta < 0)
            {
                totalDeltaColor = BrainBrushes.Crit;
            }
            TableKit.Place(StationGrid,
                TableKit.Cell(this, Format.Delta(delta, 2), "TdRight",
                              totalDeltaColor, true), row, 7);
        }

        // ------------------------------------------------------------ 测项表

        private void BuildItemTable()
        {
            int all = _shift.TestTotalItems;
            int calc = _shift.TestCpkCapable;
            ItemSub.Text = "共 " + all + " 项　·　可算 Cpk " + calc + "，不可算 " + (all - calc)
                         + "（单边等于判定 " + _shift.TestSingleSided
                         + " · 离散量 " + _shift.TestDiscrete
                         + " · σ=0 " + _shift.TestZeroSigma
                         + " · 无数值规格 " + _shift.TestNoLimits
                         + "）　·　先列有 NG 的，再列能力低的、在漂的、规格过宽的，此处显示 "
                         + _shift.TestItems.Count + " 项";

            ItemGrid.Children.Clear();
            ItemGrid.RowDefinitions.Clear();
            ItemGrid.ColumnDefinitions.Clear();

            // 测站名从 log 解析出来，长短不一（"FT #13_03" 到 "QUICKSTRIKE-MDA PAIRING #13_07"），
            // 所以这一列不能写死宽度，跟着表一起伸缩
            TableKit.AddStar(ItemGrid, 1, 118);             // 测站
            TableKit.AddStar(ItemGrid, 2, 150);             // 测项
            TableKit.AddFixed(ItemGrid, 86);                // 本班 NG
            TableKit.AddFixed(ItemGrid, 62);                // Cpk
            TableKit.AddFixed(ItemGrid, 70);                // 均值
            TableKit.AddFixed(ItemGrid, 66);                // σ内
            TableKit.AddFixed(ItemGrid, 66);                // σ总
            TableKit.AddFixed(ItemGrid, 116);               // 规格
            TableKit.AddFixed(ItemGrid, 70);                // 近 8 班
            TableKit.AddFixed(ItemGrid, 110);               // 状态

            List<TestItemRow> rows = SortedItems();
            TableKit.AddRows(ItemGrid, rows.Count + 1);

            TableKit.Place(ItemGrid, TableKit.Cell(this, "测站", "Th"), 0, 0);
            TableKit.Place(ItemGrid, TableKit.Cell(this, "测项", "Th"), 0, 1);
            TableKit.Place(ItemGrid, TableKit.Cell(this, "本班 NG", "ThRight"), 0, 2);
            TableKit.Place(ItemGrid, TableKit.Cell(this, "Cpk", "ThRight"), 0, 3);
            TableKit.Place(ItemGrid, TableKit.Cell(this, "均值", "ThRight"), 0, 4);
            TableKit.Place(ItemGrid, TableKit.Cell(this, "σ内", "ThRight"), 0, 5);
            TableKit.Place(ItemGrid, TableKit.Cell(this, "σ总", "ThRight"), 0, 6);
            TableKit.Place(ItemGrid, TableKit.Cell(this, "规格", "ThRight"), 0, 7);
            TableKit.Place(ItemGrid, TableKit.Cell(this, "近 8 班", "ThRight"), 0, 8);
            TableKit.Place(ItemGrid, TableKit.Cell(this, "状态", "ThRight"), 0, 9);
            TableKit.Place(ItemGrid, TableKit.Rule(this, "HeadLine", 10), 0, 0);

            for (int i = 0; i < rows.Count; i++)
            {
                BuildItemRow(rows[i], i + 1, i < rows.Count - 1);
            }
        }

        /// <summary>副标题里写的是「按本班 NG 降序，其后按 Cpk 升序」，那就真按这个排。</summary>
        private List<TestItemRow> SortedItems()
        {
            List<TestItemRow> list = new List<TestItemRow>(_shift.TestItems);
            list.Sort(CompareItems);
            return list;
        }

        private static int CompareItems(TestItemRow a, TestItemRow b)
        {
            if (a.Ng != b.Ng)
            {
                return b.Ng.CompareTo(a.Ng);
            }

            return SortableCpk(a).CompareTo(SortableCpk(b));
        }

        /// <summary>
        /// 排序用的 Cpk。算不出来的给个最大值，让它排到队尾 ——
        /// 那些项没有能力可比，混在中间会让人以为它们「还行」。
        /// </summary>
        private static double SortableCpk(TestItemRow item)
        {
            if (double.IsNaN(item.Cpk))
            {
                return double.MaxValue;
            }
            return item.Cpk;
        }

        private void BuildItemRow(TestItemRow t, int row, bool drawLine)
        {
            TestItemStatus status = t.Status;

            TableKit.Place(ItemGrid, TableKit.Cell(this, t.Station, "TdSmall"), row, 0);
            TableKit.Place(ItemGrid, TableKit.Cell(this, t.Name, "TdName"), row, 1);

            string ngText = "0";
            if (t.Ng != 0)
            {
                ngText = t.Ng + " " + t.NgKind;
            }

            bool ngHot = t.Ng > 0
                       && (t.NgKind == TestItemRow.FunctionFailure || status == TestItemStatus.Failing);
            Brush? ngColor = null;
            if (ngHot)
            {
                ngColor = BrainBrushes.Crit;
            }
            TableKit.Place(ItemGrid,
                TableKit.Cell(this, ngText, "TdRight", ngColor, ngHot), row, 2);

            if (!t.CanCpk)
            {
                // 功能失败没有测值，后面五列全是空的，写一句话解释为什么空，比五个横杠有用
                TableKit.Place(ItemGrid, TableKit.Cell(this, "—", "TdSmallRight"), row, 3);

                TextBlock why = TableKit.Cell(this, "不可算 —— " + t.NotComputableReason, "TdSmall");
                why.TextWrapping = TextWrapping.Wrap;
                Grid.SetColumnSpan(why, 3);
                TableKit.Place(ItemGrid, why, row, 4);

                TableKit.Place(ItemGrid, TableKit.Cell(this, t.SpecText, "TdSmallRight"), row, 7);
            }
            else
            {
                // 规格过宽那种 Cpk 是个没有参考意义的大数（比如 317），
                // 用小一号的字、不上色、不加粗，免得它比真正要看的那几行还抢眼
                string cpkStyle = "TdRight";
                Brush? cpkColor = TableKit.BrushFor(status);
                if (cpkColor == null)
                {
                    cpkColor = BrainBrushes.Ink;
                }
                bool cpkBold = true;

                if (t.IsSpecTooWide)
                {
                    cpkStyle = "TdSmallRight";
                    cpkColor = null;
                    cpkBold = false;
                }

                TableKit.Place(ItemGrid,
                    TableKit.Cell(this, Format.Cpk(t.Cpk, _shift.Config.CpkAbsurd), cpkStyle, cpkColor, cpkBold),
                    row, 3);

                TableKit.Place(ItemGrid, TableKit.Cell(this, t.MeanText, "TdRight"), row, 4);
                TableKit.Place(ItemGrid, TableKit.Cell(this, t.SigmaInText, "TdRight"), row, 5);

                // σ总 只在它和 σ内 拉开到判漂移的程度时才上色 —— 平时它只是个参考数
                Brush? sigmaAllColor = null;
                if (t.IsDrifting)
                {
                    sigmaAllColor = DriftBrush(status);
                }
                TableKit.Place(ItemGrid,
                    TableKit.Cell(this, t.SigmaAllText, "TdRight", sigmaAllColor, t.IsDrifting),
                    row, 6);

                TableKit.Place(ItemGrid, TableKit.Cell(this, t.SpecText, "TdSmallRight"), row, 7);
            }

            if (_shift.HasTrend(t.TrendKey))
            {
                Sparkline spark = new Sparkline();
                spark.Values = _shift.TrendOf(t.TrendKey);
                spark.IsBad = _shift.IsTrendBad(t.TrendKey);
                spark.HorizontalAlignment = HorizontalAlignment.Right;
                spark.VerticalAlignment = VerticalAlignment.Center;
                TableKit.Place(ItemGrid, spark, row, 8);
            }
            else
            {
                TableKit.Place(ItemGrid, TableKit.Cell(this, "—", "TdSmallRight"), row, 8);
            }

            BuildItemStatus(t, status, row);

            if (drawLine)
            {
                TableKit.Place(ItemGrid, TableKit.Rule(this, "RowLine", 10), row, 0);
            }
        }

        /// <summary>
        /// 状态那一格。能力和漂移是两套判据，都中了就叠两个徽标 ——
        /// 只报其中一个会漏掉「Cpk 很漂亮但整批在漂」这种情况。
        /// </summary>
        private void BuildItemStatus(TestItemRow t, TestItemStatus status, int row)
        {
            if (!t.IsDrifting)
            {
                TableKit.Place(ItemGrid,
                    TableKit.Tag(this, StatusText.Of(status), TableKit.TagStyleFor(status)), row, 9);
                return;
            }

            StackPanel stack = new StackPanel();
            stack.HorizontalAlignment = HorizontalAlignment.Right;
            stack.VerticalAlignment = VerticalAlignment.Center;
            stack.Margin = new Thickness(0, 8, 0, 8);

            stack.Children.Add(TableKit.Tag(this, StatusText.Of(status), TableKit.TagStyleFor(status)));

            // 漂移徽标的轻重跟着这一项本身的严重程度走：已经不合格的用红，
            // 能力还好的用黄 —— 那是预警，不是现在的问题
            Border drift = TableKit.Tag(this, "漂移",
                DriftTagStyle(status));
            drift.Margin = new Thickness(0, 3, 0, 0);
            stack.Children.Add(drift);

            TableKit.Place(ItemGrid, stack, row, 9);
        }

        // 漂移徽标和 σ总 那一格的轻重，都跟着这一项本身的严重程度走：
        // 已经不合格的用红，能力还好的用黄 —— 后者是提前预警，不是现在的问题。

        private static string DriftTagStyle(TestItemStatus status)
        {
            if (status == TestItemStatus.Failing)
            {
                return "TagRightCrit";
            }
            return "TagRightWarn";
        }

        private static System.Windows.Media.Brush DriftBrush(TestItemStatus status)
        {
            if (status == TestItemStatus.Failing)
            {
                return BrainBrushes.Crit;
            }
            return BrainBrushes.Warn;
        }

        private void OnAiClick(object sender, RoutedEventArgs e)
        {
            // 三栏的报告正文都是现拼的，标题也由各自的 Report 类出 ——
            // 测站叫什么、有哪些测项每班都不一样，写死的散文第二天就成了假话。
            ReportWindow win = new ReportWindow(_shift, BoardTab.Test);
            win.Owner = Window.GetWindow(this);
            win.Show();
        }
    }
}

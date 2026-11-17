using System;
using DesktopPet.LineBrain;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DesktopPet.Ui
{
    /// <summary>
    /// 锁附过程栏。整张表和六个 KPI 都从 quality.csv 现算出来。
    ///
    /// 孔位那一列一格顶三行（扭力 / 浮高 / 圈数），靠 Grid.SetRowSpan 做，
    /// 和原来 XAML 里写 Grid.RowSpan="3" 是一回事。行数由 csv 有几行决定，
    /// 多监一个孔位不用改代码。
    /// </summary>
    public partial class QualityPane : UserControl
    {
        /// <summary>本班的全部数据。由上层传进来，这个界面只负责显示。</summary>
        private readonly ShiftData _shift;

        public QualityPane(ShiftData shift)
        {
            InitializeComponent();
            _shift = shift;

            FillKpi();
            FillHeader();
            BuildTable();
        }

        // ------------------------------------------------------------ KPI

        private void FillKpi()
        {
            // 和设备报警栏同一个道理：取不到就全部「—」。
            // 「在监 0 个孔位、扭力不良 0 笔、浮高不良 0 笔」读起来是「本班一颗螺丝都没锁错」，
            // 实际是一条记录都没读到。
            if (!_shift.FastenAvailable)
            {
                KpiHoles.Text = "—";
                KpiHolesDesc.Text = NoDataReason();
                KpiLowest.Text = "—";
                KpiLowestDesc.Text = "没有数据，不是没有异常";
                KpiTorque.Text = "—";
                KpiTorqueDesc.Text = "没有数据，不是没有异常";
                KpiTorqueNg.Text = "—";
                KpiHeightNg.Text = "—";
                KpiWindowRun.Text = "—";
                KpiWindowDesc.Text = "没有趋势可外推";
                return;
            }

            KpiHoles.Text = _shift.HoleCount().ToString();
            KpiHolesDesc.Text = HoleBreakdown();

            FastenRow? low = _shift.LowestCapability();
            if (low == null)
            {
                KpiLowest.Text = "—";
                KpiLowestDesc.Text = "没有可算能力指数的量";
            }
            else
            {
                KpiLowest.Text = Format.Cpk(low.Cpk, _shift.Config.CpkAbsurd);
                KpiLowestDesc.Text = low.Station + "-" + low.Hole + " " + low.Measure
                                   + "　" + CapabilityLabel(low);
            }

            FastenRow? torque = _shift.LowestOf("扭力");
            if (torque == null)
            {
                KpiTorque.Text = "—";
                KpiTorqueDesc.Text = "无扭力监测";
            }
            else
            {
                KpiTorque.Text = Format.Cpk(torque.Cpk, _shift.Config.CpkAbsurd);
                KpiTorqueDesc.Text = BelowWarnNote("扭力");
            }

            KpiTorqueNg.Text = _shift.FastenNg("扭力").ToString();
            KpiHeightNg.Text = _shift.FastenNg("浮高").ToString();

            double window = _shift.WarnWindow();
            if (double.IsNaN(window))
            {
                KpiWindowRun.Text = "—";
                KpiWindowDesc.Text = "没有正在下行的关注项";
            }
            else
            {
                KpiWindowRun.Text = window.ToString("F1");
                KpiWindowDesc.Text = "扭力趋势外推至 " + _shift.Config.WarnFloor.ToString("F2");
            }
        }

        /// <summary>
        /// 锁附栏空着的两种原因，说法不一样：表还没建是「这功能还没做」，
        /// 配了表读不出来才是「出问题了」。混着说会让人白跑一趟去查数据库。
        /// </summary>
        private string NoDataReason()
        {
            if (_shift.Config.FastenTable.Length == 0)
            {
                return "锁附表还没建，这一栏暂时接不上数据";
            }
            return "锁附数据没取到，不是「本班全合格」。看标题栏的提示";
        }

        /// <summary>
        /// KPI 说明里那句「Cpk 双边」还是「CPU 单边」。
        /// 双边规格取两侧的小值叫 Cpk，单边上限只有一侧叫 CPU，名字不能混。
        /// </summary>
        private static string CapabilityLabel(FastenRow row)
        {
            if (row.Side == SpecSide.TwoSided)
            {
                return "Cpk 双边";
            }
            return "CPU 单边";
        }

        /// <summary>「A1：工位1 ×1 · 工位3 ×3」那一行，按 csv 里实际有几个孔位数出来。</summary>
        private string HoleBreakdown()
        {
            List<string> stations = new List<string>();
            List<int> counts = new List<int>();
            List<string> seenHoles = new List<string>();

            for (int i = 0; i < _shift.Fasten.Count; i++)
            {
                FastenRow q = _shift.Fasten[i];
                string holeKey = q.Station + q.Hole;
                if (seenHoles.Contains(holeKey))
                {
                    continue;
                }
                seenHoles.Add(holeKey);

                int at = stations.IndexOf(q.Station);
                if (at < 0)
                {
                    stations.Add(q.Station);
                    counts.Add(1);
                }
                else
                {
                    counts[at] = counts[at] + 1;
                }
            }

            string text = "";
            for (int i = 0; i < stations.Count; i++)
            {
                if (i > 0)
                {
                    text += " · ";
                }
                text += stations[i] + " ×" + counts[i];
            }
            return text;
        }

        /// <summary>某个量里有几个孔位已经跌破关注线。</summary>
        private string BelowWarnNote(string measure)
        {
            int below = 0;
            int total = 0;
            for (int i = 0; i < _shift.Fasten.Count; i++)
            {
                FastenRow q = _shift.Fasten[i];
                if (q.Measure != measure || double.IsNaN(q.Cpk))
                {
                    continue;
                }
                total++;
                if (q.Cpk < _shift.Config.CpkWarn)
                {
                    below++;
                }
            }

            if (below == 0)
            {
                return total + " 个孔位全部 ≥ " + _shift.Config.CpkWarn.ToString("F2");
            }
            return below + " / " + total + " 个孔位 < " + _shift.Config.CpkWarn.ToString("F2");
        }

        private void FillHeader()
        {
            CardTitleText.Text = "锁附过程参数　站别 " + StationOf() + " · "
                               + _shift.HoleCount() + " 孔位 × " + MeasureCount() + " 量";

            CardSubText.Text = AddressRange() + "　·　触发式采集，每次锁附一笔　·　"
                             + "规格组 A（同规格，可互比）";
        }

        /// <summary>产线名里的站别前缀，比如「A1 装配线」取「A1」。</summary>
        private string StationOf()
        {
            string line = _shift.LineLabel;
            int space = line.IndexOf(' ');
            if (space > 0)
            {
                return line.Substring(0, space);
            }
            return line;
        }

        private int MeasureCount()
        {
            List<string> seen = new List<string>();
            for (int i = 0; i < _shift.Fasten.Count; i++)
            {
                string m = _shift.Fasten[i].Measure;
                if (!seen.Contains(m))
                {
                    seen.Add(m);
                }
            }
            return seen.Count;
        }

        /// <summary>地址范围，比如 50108–50130。</summary>
        private string AddressRange()
        {
            string lo = "";
            string hi = "";
            for (int i = 0; i < _shift.Fasten.Count; i++)
            {
                string a = _shift.Fasten[i].Address;
                if (a.Length == 0)
                {
                    continue;
                }
                if (lo.Length == 0 || string.CompareOrdinal(a, lo) < 0)
                {
                    lo = a;
                }
                if (hi.Length == 0 || string.CompareOrdinal(a, hi) > 0)
                {
                    hi = a;
                }
            }
            if (lo.Length == 0)
            {
                return "—";
            }
            return lo + "–" + hi;
        }

        // ------------------------------------------------------------ 表

        private void BuildTable()
        {
            ParamGrid.Children.Clear();
            ParamGrid.RowDefinitions.Clear();
            ParamGrid.ColumnDefinitions.Clear();

            TableKit.AddFixed(ParamGrid, 86);               // 孔位
            TableKit.AddFixed(ParamGrid, 48);               // 量
            TableKit.AddFixed(ParamGrid, 62);               // 地址
            TableKit.AddStar(ParamGrid, 1, 130);            // 规格
            TableKit.AddFixed(ParamGrid, 68);               // 均值
            TableKit.AddFixed(ParamGrid, 68);               // σ
            TableKit.AddFixed(ParamGrid, 84);               // 能力指数
            TableKit.AddFixed(ParamGrid, 66);               // 边
            TableKit.AddFixed(ParamGrid, 70);               // 近 8 班
            TableKit.AddFixed(ParamGrid, 74);               // 判定计数
            TableKit.AddFixed(ParamGrid, 64);               // 状态

            List<FastenRow> rows = _shift.Fasten;
            TableKit.AddRows(ParamGrid, rows.Count + 1);

            TableKit.Place(ParamGrid, TableKit.Cell(this, "孔位", "Th"), 0, 0);
            TableKit.Place(ParamGrid, TableKit.Cell(this, "量", "Th"), 0, 1);
            TableKit.Place(ParamGrid, TableKit.Cell(this, "地址", "Th"), 0, 2);
            TableKit.Place(ParamGrid, TableKit.Cell(this, "规格", "ThRight"), 0, 3);
            TableKit.Place(ParamGrid, TableKit.Cell(this, "均值", "ThRight"), 0, 4);
            TableKit.Place(ParamGrid, TableKit.Cell(this, "σ", "ThRight"), 0, 5);
            TableKit.Place(ParamGrid, TableKit.Cell(this, "能力指数", "ThRight"), 0, 6);
            TableKit.Place(ParamGrid, TableKit.Cell(this, "边", "ThRight"), 0, 7);
            TableKit.Place(ParamGrid, TableKit.Cell(this, "近 8 班", "ThRight"), 0, 8);
            TableKit.Place(ParamGrid, TableKit.Cell(this, "判定计数", "ThRight"), 0, 9);
            TableKit.Place(ParamGrid, TableKit.Cell(this, "状态", "ThRight"), 0, 10);
            TableKit.Place(ParamGrid, TableKit.Rule(this, "HeadLine", 11), 0, 0);

            for (int i = 0; i < rows.Count; i++)
            {
                BuildRow(rows, i, i + 1);
            }
        }

        private void BuildRow(List<FastenRow> rows, int index, int row)
        {
            FastenRow q = rows[index];
            CapabilityStatus status = q.Status;

            // 孔位那一格只在这个孔位的第一行画，往下跨完它全部的行
            bool firstOfHole = index == 0
                || rows[index - 1].Station != q.Station
                || rows[index - 1].Hole != q.Hole;

            if (firstOfHole)
            {
                int span = 0;
                for (int k = index; k < rows.Count; k++)
                {
                    if (rows[k].Station != q.Station || rows[k].Hole != q.Hole)
                    {
                        break;
                    }
                    span++;
                }

                StackPanel hole = new StackPanel();
                hole.Margin = new Thickness(0, 10, 10, 8);
                hole.VerticalAlignment = VerticalAlignment.Top;

                TextBlock st = new TextBlock();
                st.Text = q.Station;
                st.FontSize = 12;
                st.FontWeight = FontWeights.SemiBold;
                st.Foreground = BrainBrushes.Ink;
                hole.Children.Add(st);

                TextBlock hl = new TextBlock();
                hl.Text = q.Hole;
                hl.FontSize = 12;
                hl.FontWeight = FontWeights.SemiBold;
                hl.Foreground = BrainBrushes.Ink;
                hole.Children.Add(hl);

                Border mark = TableKit.Tag(this, "组 A", "SideMark");
                mark.Margin = new Thickness(0, 5, 0, 0);
                hole.Children.Add(mark);

                TableKit.PlaceSpan(ParamGrid, hole, row, 0, span);
            }

            TableKit.Place(ParamGrid, TableKit.Cell(this, q.Measure, "TdName"), row, 1);
            TableKit.Place(ParamGrid, TableKit.Cell(this, q.Address, "TdSmall"), row, 2);
            TableKit.Place(ParamGrid, TableKit.Cell(this, q.SpecText, "TdSmallRight"), row, 3);

            // 均值本身就偏得厉害的标红。这和「能力指数低」不是一回事：
            // 能力低可能是均值偏、也可能是分布宽，标在均值上才说明该往哪个方向查。
            Brush? meanColor = null;
            if (MeanUsage(q) > MeanOffsetAlarm)
            {
                meanColor = BrainBrushes.Crit;
            }
            TableKit.Place(ParamGrid,
                TableKit.Cell(this, q.MeanText, "TdRight", meanColor, false),
                row, 4);

            TableKit.Place(ParamGrid, TableKit.Cell(this, q.SigmaText, "TdRight"), row, 5);

            if (q.CanCpk)
            {
                Brush? cpkColor = TableKit.BrushFor(q.Status);
                if (cpkColor == null)
                {
                    cpkColor = BrainBrushes.Ink;
                }
                TableKit.Place(ParamGrid,
                    TableKit.Cell(this, Format.Cpk(q.Cpk, _shift.Config.CpkAbsurd), "TdRight", cpkColor, true),
                    row, 6);
            }
            else
            {
                TableKit.Place(ParamGrid, TableKit.Cell(this, "不适用", "TdSmallRight"), row, 6);
            }

            Border side = TableKit.Tag(this, StatusText.Of(q.Side), "SideMark");
            side.HorizontalAlignment = HorizontalAlignment.Right;
            side.VerticalAlignment = VerticalAlignment.Center;
            TableKit.Place(ParamGrid, side, row, 7);

            // 离散量没有能力指数可画折线，改报异常笔数
            if (_shift.HasTrend(q.TrendKey))
            {
                Sparkline spark = new Sparkline();
                spark.Values = _shift.TrendOf(q.TrendKey);
                spark.IsBad = _shift.IsTrendBad(q.TrendKey);
                spark.HorizontalAlignment = HorizontalAlignment.Right;
                spark.VerticalAlignment = VerticalAlignment.Center;
                TableKit.Place(ParamGrid, spark, row, 8);
            }
            else
            {
                Brush? oddColor = null;
                if (q.NgCount > 0)
                {
                    oddColor = BrainBrushes.Crit;
                }
                TableKit.Place(ParamGrid,
                    TableKit.Cell(this, "异常 " + q.NgCount + " 笔", "TdSmallRight", oddColor, false),
                    row, 8);
            }

            // 离散量这一列画横杠：它的不良笔数已经在「近 8 班」那一格写成「异常 N 笔」了，
            // 这里再写一遍同一个数，读表的人会以为是两个不同的计数。
            //
            // 以前这里分了两种写法（没异常写 0、有异常写横杠），是因为那时候
            // 判定计数和异常笔数在 csv 里是两列，离散量的判定计数确实恒为 0。
            // 现在合成了一个 NgCount，那个区分没有意义了。
            string judged = q.NgCount.ToString();
            if (!q.CanCpk)
            {
                judged = "—";
            }
            TableKit.Place(ParamGrid, TableKit.Cell(this, judged, "TdSmallRight"), row, 9);

            TableKit.Place(ParamGrid,
                TableKit.Tag(this, StatusText.Of(status), TableKit.TagStyleFor(status)), row, 10);

            // 一个孔位的最后一行下面画深一点的线，把孔位和孔位分开
            bool lastOfHole = index == rows.Count - 1
                || rows[index + 1].Station != q.Station
                || rows[index + 1].Hole != q.Hole;

            if (index == rows.Count - 1)
            {
                return;                        // 表尾不画线
            }

            if (lastOfHole)
            {
                TableKit.Place(ParamGrid, TableKit.Rule(this, "HeadLine", 11), row, 0);
            }
            else
            {
                Border thin = TableKit.Rule(this, "RowLine", 10);
                TableKit.Place(ParamGrid, thin, row, 1);
            }
        }

        /// <summary>均值偏到这个程度就标红。0.60 = 已经吃掉允许偏移量的六成。</summary>
        private const double MeanOffsetAlarm = 0.60;

        /// <summary>
        /// 均值把规格吃掉了多少，0 = 正在中心，1 = 已经压在规格线上。
        ///
        /// 双边规格看它离中心多远（除以半宽）；单边上限没有「中心」这回事，
        /// 看的是均值本身占了上限的几成 —— 浮高 0.071 / 0.100 就是吃掉了七成。
        /// </summary>
        private static double MeanUsage(FastenRow q)
        {
            if (q.HasLsl && q.HasUsl)
            {
                double half = (q.Usl - q.Lsl) / 2;
                if (half <= 0)
                {
                    return 0;
                }
                double center = (q.Usl + q.Lsl) / 2;
                return Math.Abs(q.Mean - center) / half;
            }

            if (q.HasUsl && q.Usl > 0)
            {
                return q.Mean / q.Usl;
            }
            return 0;
        }

        private void OnAiClick(object sender, RoutedEventArgs e)
        {
            ReportWindow win = new ReportWindow(_shift, BoardTab.Quality);
            win.Owner = Window.GetWindow(this);
            win.Show();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;

namespace DesktopPet.LineBrain
{
    /// <summary>
    /// 锁附工艺栏「AI 解读」那份报告的正文，**现拼的**。
    ///
    /// 和 <see cref="AlarmReport"/> 同样的来历：原来写死在 Ui\Reports.xaml 里，
    /// 「A1 · 规格组 A · n = 462 / 孔」这些数是设计阶段手敲的，
    /// 表里换了孔位，报告还在讲原来那几个 —— 两份真相。
    ///
    /// 锁附表目前还没建，所以这份报告绝大多数时候只会出「没有数据」那一段。
    /// **这正是它该有的样子**：没数据就说没数据，不是拿一段漂亮的散文顶上。
    /// </summary>
    public static class FastenReport
    {
        public static string Title(ShiftData data)
        {
            if (data.Fasten.Count == 0)
            {
                return "锁附过程解读报告　" + data.LineLabel;
            }
            return "锁附过程解读报告　" + data.LineLabel + " · " + data.HoleCount() + " 孔位";
        }

        public static List<ReportSection> Sections(ShiftData data)
        {
            List<ReportSection> sections = new List<ReportSection>();

            if (!data.FastenAvailable)
            {
                sections.Add(new ReportSection("没有锁附数据", NoData(data)));
                return sections;
            }

            if (data.Fasten.Count == 0)
            {
                sections.Add(new ReportSection("本班没有锁附记录",
                    "数据库连上了，本班（" + data.Window.StartText + "–" + data.Window.EndText
                    + "）在 " + data.Config.FastenTable + " 里**一条锁附记录都没有**。\n\n"
                    + "这和「读不到数据」是两回事：这次是真的查到了，结果是空的。"
                    + "**值得确认锁附机有没有在传数据**。"));
                return sections;
            }

            ReportSection.Add(sections, "结论", Conclusion(data));
            ReportSection.Add(sections, "逐孔位能力", HoleTable(data));
            ReportSection.Add(sections, "是共享部件还是单孔问题", CommonMode(data));
            ReportSection.Add(sections, "趋势与预警窗口", Trend(data));
            ReportSection.Add(sections, "建议动作", Actions(data));
            ReportSection.Add(sections, "方法与口径", Method(data));
            return sections;
        }

        // =================================================================

        private static string NoData(ShiftData data)
        {
            StringBuilder s = new StringBuilder();

            s.Append("本班的锁附数据**一条都没读到**，所以这份报告没有内容可写。\n\n");
            s.Append("**这不是「本班锁附全部合格」。** 孔位数、扭力和浮高的不良笔数、"
                   + "各孔位的 Cpk，现在全都是未知。\n\n");

            if (data.Config.FastenTable.Length == 0)
            {
                s.Append("**原因：锁附表还没建。** brain.csv 里的 fasten_table 是空的 —— "
                       + "这部分功能还没接上，不是出故障了。\n\n"
                       + "要接上需要一张一行一次锁附的表：工位、孔位、量（扭力 / 浮高 / 圈数）、"
                       + "实测值、**PLC 自己的判定**，外加一份规格（上下限、显示位数）。\n\n"
                       + "判定那一列一定要填 PLC 的结论，不要按规格现推 —— "
                       + "PLC 的门槛未必等于图纸规格，两者对不上恰恰是要查的问题，"
                       + "自己推就把这条线索抹掉了。");
            }
            else
            {
                s.Append("配的表是 **" + data.Config.FastenTable + "**。");
                if (data.FastenProblems.Count > 0)
                {
                    s.Append("程序记到的原因：\n");
                    for (int i = 0; i < data.FastenProblems.Count; i++)
                    {
                        s.Append("· " + data.FastenProblems[i] + "\n");
                    }
                }
            }
            return s.ToString();
        }

        private static string Conclusion(ShiftData data)
        {
            StringBuilder s = new StringBuilder();

            s.Append("在监 **" + data.HoleCount() + " 个孔位**。本班扭力判定不良 **"
                   + data.FastenNg("扭力") + " 笔**，浮高判定不良 **"
                   + data.FastenNg("浮高") + " 笔**。\n\n");

            FastenRow? low = data.LowestCapability();
            if (low == null)
            {
                s.Append("本班没有可算能力指数的量 —— 在监的都是离散量（如圈数），"
                       + "或者规格只有单边、样本不够。这不代表过程有问题，"
                       + "只是**能力指数这个工具在这里用不上**，要看判定结果本身。");
                return s.ToString();
            }

            s.Append("能力最低的是 **" + low.Station + "-" + low.Hole + " " + low.Measure
                   + "**，" + Word(low) + " **" + Format.Cpk(low.Cpk, data.Config.CpkAbsurd)
                   + "**（样本 " + low.SampleCount + "，均值 " + low.MeanText
                   + "，σ " + low.SigmaText + "，规格 " + low.SpecText + "）。");

            if (low.Cpk < data.Config.CpkCrit)
            {
                s.Append("**已经低于 " + data.Config.CpkCrit.ToString("F2")
                       + "，这是要停下来查的档位**，不是观察。");
            }
            else if (low.Cpk < data.Config.CpkWarn)
            {
                s.Append("在 " + data.Config.CpkCrit.ToString("F2") + "–"
                       + data.Config.CpkWarn.ToString("F2") + " 之间，属于关注档。");
            }
            else
            {
                s.Append("在 " + data.Config.CpkWarn.ToString("F2") + " 之上，本班没有低能力孔位。");
            }
            return s.ToString();
        }

        private static string HoleTable(ShiftData data)
        {
            StringBuilder s = new StringBuilder();

            for (int i = 0; i < data.Fasten.Count; i++)
            {
                FastenRow q = data.Fasten[i];

                s.Append("· **" + q.Station + "-" + q.Hole + " " + q.Measure + "**　规格 "
                       + q.SpecText + "　n=" + q.SampleCount
                       + "　均值 " + q.MeanText + "　σ " + q.SigmaText);

                if (q.CanCpk)
                {
                    s.Append("　" + Word(q) + " " + Format.Cpk(q.Cpk, data.Config.CpkAbsurd));
                }
                else
                {
                    s.Append("　离散量，不出能力指数");
                }

                if (q.NgCount > 0)
                {
                    s.Append("　**判定不良 " + q.NgCount + " 笔**");
                }
                s.Append("\n");
            }
            return s.ToString();
        }

        /// <summary>
        /// 同一个量里几个孔位一起下行 = 共享部件的问题（气压、扭力枪、程式参数）；
        /// 只有一个孔在漂 = 该孔独有的部件（那一把批头、那一颗螺丝的供料）。
        /// 这个判别决定了修的时候动哪儿，比「哪个孔 Cpk 最低」有用得多。
        /// </summary>
        private static string CommonMode(ShiftData data)
        {
            List<string> measures = new List<string>();
            for (int i = 0; i < data.Fasten.Count; i++)
            {
                string m = data.Fasten[i].Measure;
                if (!measures.Contains(m))
                {
                    measures.Add(m);
                }
            }

            StringBuilder s = new StringBuilder();

            for (int i = 0; i < measures.Count; i++)
            {
                string measure = measures[i];
                int total = 0;
                int falling = 0;
                for (int k = 0; k < data.Fasten.Count; k++)
                {
                    FastenRow q = data.Fasten[k];
                    if (q.Measure != measure || !q.CanCpk)
                    {
                        continue;
                    }
                    total++;
                    if (data.IsTrendBad(q.TrendKey))
                    {
                        falling++;
                    }
                }

                if (total < 2 || falling == 0)
                {
                    continue;
                }

                if (falling >= 2)
                {
                    s.Append("· **" + measure + "：" + total + " 个孔位里有 " + falling
                           + " 个一起在下行。** 这是**共模**，查共享的东西 —— "
                           + "气压、扭力枪本体、程式参数、来料批次。挨个孔去校没用。\n");
                }
                else
                {
                    s.Append("· **" + measure + "：" + total + " 个孔位里只有 1 个在下行。** "
                           + "这是**单孔**问题，查这个孔独有的部件 —— 那一把批头、"
                           + "那一路供料、那个夹具定位。别动共享参数，会把好的带坏。\n");
                }
            }
            return s.ToString();
        }

        private static string Trend(ShiftData data)
        {
            double window = data.WarnWindow();
            if (double.IsNaN(window))
            {
                return "";
            }

            return "还在关注档、并且正在下行的孔位里，按各自近 8 班的最小二乘斜率外推到 "
                 + data.Config.WarnFloor.ToString("F2") + "，**最快的那个还剩 "
                 + window.ToString("F1") + " 班**。\n\n"
                 + "这个数是线性外推，**只在趋势保持不变时成立** —— "
                 + "它的用处是排先后，不是预言哪一班会出事。";
        }

        private static string Actions(ShiftData data)
        {
            StringBuilder s = new StringBuilder();
            int n = 1;

            for (int i = 0; i < data.Fasten.Count; i++)
            {
                FastenRow q = data.Fasten[i];

                if (q.NgCount > 0)
                {
                    s.Append(n + "、**" + q.Station + "-" + q.Hole + " " + q.Measure
                           + "** 本班判定不良 " + q.NgCount + " 笔 —— "
                           + "判定不良是**已经发生的事实**，不是趋势预测，先处理这个。\n");
                    n++;
                    continue;
                }

                if (q.CanCpk && q.Cpk < data.Config.CpkCrit)
                {
                    s.Append(n + "、**" + q.Station + "-" + q.Hole + " " + q.Measure
                           + "** " + Word(q) + " " + Format.Cpk(q.Cpk, data.Config.CpkAbsurd)
                           + "，低于 " + data.Config.CpkCrit.ToString("F2")
                           + " —— 现在还没出不良，但余量已经没了，"
                           + "来料稍微一变就会跑出去。\n");
                    n++;
                }
            }

            if (n == 1)
            {
                return "本班没有判定不良，也没有低于 " + data.Config.CpkCrit.ToString("F2")
                     + " 的孔位。按常规保养走即可。";
            }
            return s.ToString();
        }

        private static string Method(ShiftData data)
        {
            return "锁附记录来自本机 MySQL 的 **" + data.Config.FastenTable + "** 表，"
                 + "取本班窗口（" + data.Window.StartText + "–" + data.Window.EndText + "）内的记录。\n\n"
                 + "**判定不良用的是 PLC 自己的结论，不是拿规格现推的。** "
                 + "PLC 的门槛未必等于图纸规格，两者对不上恰恰是要查的问题。\n\n"
                 + "能力指数用**组内标准差**（σ内 = MR̄ / 1.128）算，"
                 + "双边规格取两侧的小值叫 Cpk，单边只有一侧只能叫能力指数 —— 名字不能混。"
                 + "σ总 / σ内 只用来判漂移（> " + data.Config.DriftRatio.ToString("F2")
                 + " 算漂），不产出任何指标。\n\n"
                 + "门槛写在 brain.csv 里（关注 " + data.Config.CpkWarn.ToString("F2")
                 + "、严重 " + data.Config.CpkCrit.ToString("F2")
                 + "）。**这份报告每次打开都重算**，和看板上那张表同源，语言模型不参与任何计算。";
        }

        /// <summary>双边叫 Cpk，单边只能叫能力指数。名字不一样，别混着说。</summary>
        private static string Word(FastenRow q)
        {
            if (q.Side == SpecSide.TwoSided)
            {
                return "Cpk";
            }
            return "能力指数";
        }
    }
}

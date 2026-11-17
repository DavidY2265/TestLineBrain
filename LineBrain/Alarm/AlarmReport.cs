using System;
using System.Collections.Generic;
using System.Text;

namespace DesktopPet.LineBrain
{
    /// <summary>
    /// 设备报警栏「AI 解读」那份报告的正文，**现拼的**。
    ///
    /// 原来这一份写死在 Ui\Reports.xaml 里，是设计阶段的样稿：
    /// 「本班整线停机 51 min，MA 86.8%」「A1 撕膜升降动点警报 8 次 28 min」——
    /// 全是手敲的假数，跟数据库连没连上毫无关系，连不上也照样弹出来。
    /// 那正是这个项目最反对的「两份真相」：表格里是一套数，报告里是另一套。
    ///
    /// 现在和 <see cref="TestReport"/> 一样，每次打开从当前 ShiftData 重算，
    /// 和看板上那张表、和小精灵的警告同源。
    /// </summary>
    public static class AlarmReport
    {
        public static string Title(ShiftData data)
        {
            return "设备报警解读报告　" + data.LineLabel + " · " + StationCount(data) + " 台";
        }

        private static int StationCount(ShiftData data)
        {
            List<string> seen = new List<string>();
            for (int i = 0; i < data.Alarms.Count; i++)
            {
                string station = data.Alarms[i].Station;
                if (!seen.Contains(station))
                {
                    seen.Add(station);
                }
            }
            return seen.Count;
        }

        public static List<ReportSection> Sections(ShiftData data)
        {
            List<ReportSection> sections = new List<ReportSection>();

            // 没取到数据的时候只出这一段，别的一段都不出。
            // 硬算的话每个数都是 0，拼出来是「停机 0 min、MA 100%、本班运行平稳」，
            // 看着是一份漂亮的报告，实际一个字都不可信。
            if (!data.AlarmsAvailable)
            {
                sections.Add(new ReportSection("没有报警数据", NoData(data)));
                return sections;
            }

            if (data.Alarms.Count == 0)
            {
                sections.Add(new ReportSection("本班没有报警记录",
                    "数据库连上了，本班（" + data.Window.StartText + "–" + data.Window.EndText
                    + "）在 " + data.Config.AlarmTable + " 里**一条报警记录都没有**。\n\n"
                    + "这和「读不到数据」是两回事：这次是真的查到了，结果是空的。"
                    + "整线 MA " + data.Availability.ToString("F1") + "%，全班 "
                    + data.SpanMinutes.ToString("F0") + " min 都算作自动运行。\n\n"
                    + "**值得确认一下采集链路。** 一整班零报警在装配线上不常见，"
                    + "更常见的原因是采集程序没在跑，或者 work_id 里的产线代号和 brain.csv 里配的 "
                    + data.Config.LineName + " 对不上（那样会被产线过滤器全部滤掉）。"));
                return sections;
            }

            // 序号由 ReportSection.Add 自动发 —— 「有没有规律性的」和「算不进去的部分」
            // 是条件出现的，写死序号就会跳号。空正文自动跳过，不用在这里判。
            ReportSection.Add(sections, "结论", Conclusion(data));
            ReportSection.Add(sections, "停机集中在哪", Concentration(data));
            ReportSection.Add(sections, "并集与合计不是一回事", UnionVsSum(data));
            ReportSection.Add(sections, "有没有规律性的", Rhythm(data));
            ReportSection.Add(sections, "算不进去的部分", BlindSpots(data));
            ReportSection.Add(sections, "建议动作", Actions(data));
            ReportSection.Add(sections, "方法与口径", Method(data));
            return sections;
        }

        // =================================================================

        private static string NoData(ShiftData data)
        {
            StringBuilder s = new StringBuilder();

            s.Append("本班的报警数据**一条都没读到**，所以这份报告没有内容可写。\n\n");
            s.Append("**这不是「本班没有报警」。** 停机时长、报警次数、整线 MA 现在全都是未知，"
                   + "不是 0。\n\n");

            if (data.AlarmProblems.Count > 0)
            {
                s.Append("程序记到的原因：\n");
                for (int i = 0; i < data.AlarmProblems.Count; i++)
                {
                    s.Append("· " + data.AlarmProblems[i] + "\n");
                }
                s.Append("\n");
            }

            s.Append("接上去的办法：右键小精灵 → **数据库连接…**，填主机、数据库、账号、密码"
                   + "和报警表名（现在配的是 " + data.Config.AlarmTable + "），"
                   + "先点「测试连接」确认通了再保存，然后**重启程序**。");
            return s.ToString();
        }

        private static string Conclusion(ShiftData data)
        {
            StringBuilder s = new StringBuilder();

            s.Append("本班整线停机 **" + data.SumUnion.ToString("F0") + " min**，整线 MA **"
                   + data.Availability.ToString("F1") + "%**（门槛 "
                   + data.Config.MaGate.ToString("F1") + "%）。");

            if (data.Availability < data.Config.MaGate)
            {
                s.Append("**没到门槛**，差 "
                       + (data.Config.MaGate - data.Availability).ToString("F1") + " 个点。");
            }
            else
            {
                s.Append("在门槛之上。");
            }

            s.Append("\n\n" + data.SpanMinutes.ToString("F0") + " min 的班次，除去停机，"
                   + "实际自动运行 **" + (data.SpanMinutes - data.SumUnion).ToString("F0")
                   + " min**。本班共 " + data.AlarmTimes + " 次报警，涉及 "
                   + StationCount(data) + " 台设备、" + data.Alarms.Count + " 种报警。");
            return s.ToString();
        }

        private static string Concentration(ShiftData data)
        {
            StringBuilder s = new StringBuilder();
            List<AlarmGroup> rows = data.SortAlarms(AlarmSortKey.Duration);

            // 前三条，通讯断连不算 —— 它不进 MA 的分子分母，说它「占停机多少」没有意义
            int shown = 0;
            for (int i = 0; i < rows.Count && shown < 3; i++)
            {
                AlarmGroup g = rows[i];
                if (g.IsNoData)
                {
                    continue;
                }

                double share = Format.Percent(g.Total, data.SumUnion);
                s.Append("· **" + g.Station + " " + g.Name + "**　" + g.Count + " 次　累计 "
                       + Format.Duration(g.Total) + " min　单次均值 " + Format.Each(g.Each)
                       + "　占整线停机 **" + share.ToString("F0") + "%**\n");
                shown++;
            }

            if (shown == 0)
            {
                return "本班只有通讯断连类的记录，没有可归因的停机条目。";
            }

            AlarmGroup? worst = data.WorstAlarm();
            if (worst != null)
            {
                double share = Format.Percent(worst.Total, data.SumUnion);
                s.Append("\n最该先动的是 **" + worst.Station + " " + worst.Name + "**，"
                       + "它一条就占了整线停机的 " + share.ToString("F0") + "%。");

                if (share >= data.Config.AlarmShare)
                {
                    s.Append("超过了 " + data.Config.AlarmShare.ToString("F0")
                           + "% 这条线 —— 修好它，整线 MA 大约能回到 "
                           + ((data.SpanMinutes - data.SumUnion + worst.Total)
                              / data.SpanMinutes * 100.0).ToString("F1")
                           + "%。**这是个上界不是承诺**：它那几段停机可能和别的报警重叠，"
                           + "重叠的部分修了也省不下来。");
                }
            }
            return s.ToString();
        }

        private static string UnionVsSum(ShiftData data)
        {
            StringBuilder s = new StringBuilder();

            s.Append("整线停机取的是各条报警区间的**并集** " + data.SumUnion.ToString("F0")
                   + " min，不是各条时长的**合计** " + data.SumAll.ToString("F0") + " min。\n\n");

            if (data.Overlap > 0.05)
            {
                s.Append("两者差 **" + data.Overlap.ToString("F1")
                       + " min**，这是两台以上同时在报的时间 —— 现场那会儿只停了一次，"
                       + "算两次就把停机算多了。按合计算的话 MA 会变成 "
                       + ((data.SpanMinutes - data.SumAll) / data.SpanMinutes * 100.0).ToString("F1")
                       + "%，比真实情况低 "
                       + (data.Availability
                          - (data.SpanMinutes - data.SumAll) / data.SpanMinutes * 100.0).ToString("F1")
                       + " 个点。");
            }
            else
            {
                s.Append("本班两者**几乎相等**，说明报警之间基本没有重叠，"
                       + "各台是先后出问题而不是一起出问题。");
            }
            return s.ToString();
        }

        /// <summary>
        /// 规律性判据：相邻间隔的极差 ≤ 均值的一半，就是「到点就犯」。
        /// 这种节奏不是人为波动 —— 操作失误不会每 45 分钟准时来一回，
        /// 该查机构不该查人。判据和 AlertScanner 里那条是同一个，不是另立一套。
        /// </summary>
        private static string Rhythm(ShiftData data)
        {
            StringBuilder s = new StringBuilder();

            for (int i = 0; i < data.Alarms.Count; i++)
            {
                AlarmGroup g = data.Alarms[i];
                if (g.IsNoData || g.Count < 3)
                {
                    continue;
                }

                double[] starts = g.Starts;
                double minGap = double.MaxValue;
                double maxGap = 0;
                for (int k = 1; k < starts.Length; k++)
                {
                    double gap = starts[k] - starts[k - 1];
                    if (gap < minGap)
                    {
                        minGap = gap;
                    }
                    if (gap > maxGap)
                    {
                        maxGap = gap;
                    }
                }

                if (maxGap - minGap > g.MeanGap / 2.0)
                {
                    continue;
                }

                s.Append("· **" + g.Station + " " + g.Name + "**　" + g.Count + " 次，"
                       + "发生于开班后第 " + JoinStarts(g) + " 分钟，"
                       + "间隔稳定在 " + minGap.ToString("F0") + "–" + maxGap.ToString("F0")
                       + " min。\n");
            }

            if (s.Length == 0)
            {
                return "";
            }

            s.Insert(0, "下面这几条**到点就犯**（相邻间隔的极差不超过均值的一半）：\n");
            s.Append("\n这种规律性不是操作波动 —— 人为失误不会每隔几十分钟准时来一回。"
                   + "**查机构，别查人**：多半是某个部件到了行程次数就卡、或者热起来才出问题。");
            return s.ToString();
        }

        private static string BlindSpots(ShiftData data)
        {
            StringBuilder s = new StringBuilder();

            double noDataTotal = 0;
            int noDataTimes = 0;
            for (int i = 0; i < data.Alarms.Count; i++)
            {
                if (data.Alarms[i].IsNoData)
                {
                    noDataTotal += data.Alarms[i].Total;
                    noDataTimes += data.Alarms[i].Count;
                }
            }

            if (noDataTimes > 0)
            {
                s.Append("**通讯断连 " + noDataTimes + " 次共 " + Format.Duration(noDataTotal)
                       + " min，已从 MA 的分子分母一并剔除。**\n\n"
                       + "那段时间设备在不在跑没人知道，算成运行会虚高，算成停机会虚低，"
                       + "所以两边都不算 —— MA 的口径变成「在能看见的时间里，自动运行占多少」。\n\n"
                       + "断连时间如果占比很大，这个 MA 的代表性就要打折扣。"
                       + "本班断连占班次的 "
                       + Format.Percent(noDataTotal, data.SpanMinutes).ToString("F1") + "%。");
            }

            if (data.AlarmsNeverRecovered > 0)
            {
                if (s.Length > 0)
                {
                    s.Append("\n\n");
                }
                s.Append("另有 **" + data.AlarmsNeverRecovered + " 条报警到班末还没恢复**，"
                       + "它们的时长按「一直持续到班末」计。真实时长只会更长，"
                       + "所以本班的停机是个**下界**。");
            }

            if (data.AlarmsClipped > 0)
            {
                if (s.Length > 0)
                {
                    s.Append("\n\n");
                }
                s.Append("还有 **" + data.AlarmsClipped + " 条报警跨出了本班窗口**，"
                       + "只算了落在本班里的那一段。上一班烧过来的那一头记在上一班头上，"
                       + "两班的数加起来才是这条报警的全貌。");
            }

            return s.ToString();
        }

        private static string Actions(ShiftData data)
        {
            StringBuilder s = new StringBuilder();
            List<AlarmGroup> rows = data.SortAlarms(AlarmSortKey.Duration);

            int n = 1;
            for (int i = 0; i < rows.Count && n <= 3; i++)
            {
                AlarmGroup g = rows[i];
                if (g.IsNoData)
                {
                    continue;
                }

                s.Append(n + "、**" + g.Station + " " + g.Name + "** —— ");

                if (g.Count >= data.Config.AlarmCount)
                {
                    s.Append("本班犯了 " + g.Count + " 次（≥ " + data.Config.AlarmCount
                           + " 次这条线），次数多但单次只有 " + Format.Each(g.Each)
                           + "，属于**频繁短停**。这种最耗人：每次都要有人过去按一下。"
                           + "查触发条件，别只是复位。");
                }
                else if (g.Each >= 15)
                {
                    // 时长直接用算出来的，不写「十几二十分钟」这种话 ——
                    // 碰上一条 690 min 的，那句话就和紧挨着的数字当场打架。
                    s.Append("单次平均 " + Format.Each(g.Each) + "，属于**长停**，"
                           + "一次就吃掉 " + Format.Duration(g.Each)
                           + " min。这种通常是要拆要换件，"
                           + "备件有没有、换一次要多久，先问清楚。");
                }
                else
                {
                    s.Append(g.Count + " 次累计 " + Format.Duration(g.Total)
                           + " min，占整线停机 "
                           + Format.Percent(g.Total, data.SumUnion).ToString("F0") + "%。");
                }
                s.Append("\n");
                n++;
            }

            if (n == 1)
            {
                return "本班没有可归因的报警条目，先确认采集链路是否正常。";
            }

            // 有没恢复的记录时，上面那几条「长停 / 频繁短停」的定性要打个问号：
            // 一条 recover_time 为空的记录会被算成「一直烧到班末」，
            // 时长看着吓人，但很可能只是采集侧没把恢复写回去。
            // 不提醒的话，会有人拿着这份报告去拆一台其实没坏的机器。
            if (data.AlarmsNeverRecovered > 0)
            {
                s.Append("\n注意：本班有 " + data.AlarmsNeverRecovered
                       + " 条报警没有恢复时间，它们按「一直持续到班末」计时。"
                       + "**如果上面某一条的时长长得离谱，先去库里确认它是真的一直没恢复，"
                       + "还是采集侧漏写了 recover_time** —— 后者很常见，"
                       + "照着它去拆机器就白干了。");
            }
            return s.ToString();
        }

        private static string Method(ShiftData data)
        {
            return "报警记录来自本机 MySQL 的 **" + data.Config.AlarmTable + "** 表，"
                 + "按 work_id 里的产线代号过滤出 " + data.Config.LineName + "，"
                 + "再和本班窗口（" + data.Window.StartText + "–" + data.Window.EndText
                 + "，" + data.SpanMinutes.ToString("F0") + " min）取交集 —— "
                 + "**跨班的报警只算落在本班里的那一段**，不然 MA 会算成负数。\n\n"
                 + "整线停机 = 各条报警区间的**并集**；MA =（班次时长 − 并集）÷ 班次时长。"
                 + "含「" + data.Config.NoDataKeyword + "」的记录从分子分母一并剔除。\n\n"
                 + "门槛写在 brain.csv 里（MA " + data.Config.MaGate.ToString("F1")
                 + "%、单条占比 " + data.Config.AlarmShare.ToString("F0")
                 + "%、次数 " + data.Config.AlarmCount + " 次），改了门槛判定跟着变。"
                 + "**这份报告每次打开都重算**，和看板上那张表出自同一次解析，"
                 + "语言模型不参与任何计算。";
        }

        private static string JoinStarts(AlarmGroup g)
        {
            string text = "";
            double[] starts = g.Starts;
            for (int i = 0; i < starts.Length; i++)
            {
                if (i > 0)
                {
                    text += " / ";
                }
                text += starts[i].ToString("F0");
            }
            return text;
        }
    }
}

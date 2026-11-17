using System;
using System.Collections.Generic;

namespace DesktopPet.LineBrain
{
    /// <summary>
    /// 把查出来的报警记录算成看板上的东西：聚类 → 裁到班次 → 求并集 → 算 MA。
    ///
    /// **一个数都不从数据库里读现成的。** 次数、累计时长、单次均值、占比、
    /// 整线停机、稼动率全是从那一条条原始记录算出来的 ——
    /// 所以改一条记录，表、时序图、KPI、AI 的回答会一起变。
    /// </summary>
    public static class AlarmAnalyzer
    {
        /// <summary>报警算完的结果。</summary>
        public sealed class AlarmResult
        {
            public readonly List<AlarmGroup> Groups = new List<AlarmGroup>();

            /// <summary>整线停机 = 各台报警区间的**并集**。不是各行相加。</summary>
            public readonly List<AlarmSpan> LineDown = new List<AlarmSpan>();

            public double SumAll;          // 各条时长简单相加，含重复计时
            public double SumUnion;        // 并集后的整线停机。MA 用的是这个
            public double Overlap;         // 被重复算掉的那部分 = SumAll − SumUnion
            public double Availability;    // 整线 MA，百分数

            /// <summary>本班报警次数。通讯断连状态未知，不算停机也就不算次数。</summary>
            public int Times;

            public int ClippedCount;       // 有几条被班次窗口裁过
            public int NeverRecoveredCount;
        }


        /// <summary>
        /// 原始记录 → 报警汇总。
        /// </summary>
        public static AlarmResult AnalyzeAlarms(List<AlarmRecord> records, ShiftWindow window,
                                                BrainConfig config)
        {
            AlarmResult result = new AlarmResult();

            GroupByKind(records, window, config, result);
            ComputeLineDown(result, window);
            return result;
        }

        /// <summary>
        /// 聚类：「站别 + 报警内容」相同的归成一类。
        ///
        /// 数据库里是一行一次发生。次数、累计时长、单次均值、发生节奏全由这里算出来 ——
        /// 这些都是机械换算，让导数据的人先算一遍只会多一道出错的机会，
        /// 而且算错了界面上照样画得出漂亮的图。
        /// </summary>
        private static void GroupByKind(List<AlarmRecord> records, ShiftWindow window,
                                        BrainConfig config, AlarmResult result)
        {
            Dictionary<string, AlarmGroup> byKind = new Dictionary<string, AlarmGroup>(StringComparer.Ordinal);

            for (int i = 0; i < records.Count; i++)
            {
                AlarmRecord one = records[i];
                if (one.Station.Length == 0 && one.Message.Length == 0)
                {
                    continue;
                }

                // **裁到班次窗口。** 实测有单次 36 小时、跨三天的报警，
                // 不裁的话整线停机会超过班次长度，MA 算出来是负的。
                double startMinute;
                double duration;
                if (!window.Clip(one.Start, one.End, out startMinute, out duration))
                {
                    continue;              // 完全落在班外
                }

                if (one.Start < window.Start || one.End > window.End)
                {
                    result.ClippedCount++;
                }
                if (one.NeverRecovered)
                {
                    result.NeverRecoveredCount++;
                }

                string key = one.Station + "|" + one.Message;
                AlarmGroup group;
                if (!byKind.TryGetValue(key, out group))
                {
                    group = new AlarmGroup();
                    group.Index = result.Groups.Count;
                    group.Station = one.Station;
                    group.Name = one.Message;
                    group.IsNoData = IsNoData(one.Message, config);
                    byKind[key] = group;
                    result.Groups.Add(group);
                }
                group.Add(startMinute, duration);
            }

            // 间隔规律的判断依赖先后顺序，聚完必须排一遍
            for (int i = 0; i < result.Groups.Count; i++)
            {
                result.Groups[i].SortByTime();
                if (!result.Groups[i].IsNoData)
                {
                    result.Times += result.Groups[i].Count;
                }
            }

            if (result.ClippedCount > 0)
            {
                DataProblems.Add("有 " + result.ClippedCount + " 条报警跨出了本班窗口，"
                               + "已按交集裁到 " + window.StartText + "–" + window.EndText
                               + " 之内（不裁的话整线停机会超过班次长度）");
            }
            if (result.NeverRecoveredCount > 0)
            {
                DataProblems.Add("有 " + result.NeverRecoveredCount
                               + " 条报警的 recover_time 是空的（还没恢复），已按持续到班末计算");
            }
        }

        /// <summary>
        /// 通讯断连：设备在不在跑没人知道。既不算运行也不算停机，
        /// MA 的分子分母里一起剔除 —— 把「状态未知」算成「在跑」或者「停了」都是编数据。
        /// </summary>
        private static bool IsNoData(string message, BrainConfig config)
        {
            if (config.NoDataKeyword.Length == 0)
            {
                return false;
            }
            return message.IndexOf(config.NoDataKeyword, StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// 整线停机的**并集**算法，以及 MA。这是全项目最容易搞错的一处口径。
        ///
        /// 两台设备同时在报，现场只停了一次 —— 所以不能把各条时长相加，
        /// 要把所有区间摊平、排序、能接上的并起来。
        /// </summary>
        private static void ComputeLineDown(AlarmResult result, ShiftWindow window)
        {
            // 摊平成一条条区间，通讯断连不参与
            List<AlarmSpan> flat = new List<AlarmSpan>();
            for (int i = 0; i < result.Groups.Count; i++)
            {
                AlarmGroup g = result.Groups[i];
                if (g.IsNoData)
                {
                    continue;
                }

                for (int k = 0; k < g.Occurrences.Count; k++)
                {
                    AlarmSpan one = g.Occurrences[k];
                    flat.Add(one);
                    result.SumAll += one.Duration;
                }
            }

            flat.Sort(CompareSpanStart);

            for (int i = 0; i < flat.Count; i++)
            {
                AlarmSpan e = flat[i];

                if (result.LineDown.Count > 0)
                {
                    AlarmSpan last = result.LineDown[result.LineDown.Count - 1];
                    if (e.Start <= last.End)
                    {
                        // 接得上，并进上一段
                        double newEnd = Math.Max(last.End, e.End);
                        last.Duration = newEnd - last.Start;
                        result.LineDown[result.LineDown.Count - 1] = last;
                        continue;
                    }
                }
                result.LineDown.Add(e);
            }

            for (int i = 0; i < result.LineDown.Count; i++)
            {
                result.SumUnion += result.LineDown[i].Duration;
            }

            result.Overlap = result.SumAll - result.SumUnion;

            double span = window.SpanMinutes;
            if (span > 0)
            {
                result.Availability = (span - result.SumUnion) / span * 100.0;
            }
        }

        private static int CompareSpanStart(AlarmSpan a, AlarmSpan b)
        {
            return a.Start.CompareTo(b.Start);
        }
    }
}

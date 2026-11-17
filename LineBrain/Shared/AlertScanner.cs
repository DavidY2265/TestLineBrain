using System;
using System.Collections.Generic;

namespace DesktopPet.LineBrain
{
    /// <summary>
    /// 扫一遍三栏的数，把超过警戒线的挑出来，按严重度排队。
    ///
    /// 输出的每条 Alert 有五样东西：气泡里那一句（Short），
    /// 和点开之后的四段（这是什么 / 数是怎么来的 / 为什么判它超线 / 建议动作）。
    /// 四段都是**从数据现拼的**，不是写死的模板 —— 换一批数据，说法跟着变。
    ///
    /// 所有判定用的门槛都来自 brain.csv，改一条门槛，这里扫出来的东西立刻不一样。
    /// </summary>
    public static class AlertScanner
    {
        /// <summary>
        /// 扫完排队。**紧急的排前面，同级按超线幅度。** 最该先看的先冒泡 ——
        /// 小精灵一次只说一件事，顺序错了等于没报。
        /// </summary>
        public static List<Alert> Scan(ShiftData data)
        {
            List<Alert> list = new List<Alert>();

            // 没取到数据的栏不扫。
            //
            // 不扫不是为了省事 —— 是因为空数据算出来的是「MA 100%、不良 0 笔」，
            // 拿去和门槛比，结论必然是「没问题」。一条警告都不冒，
            // 看着就和「本班一切正常」一模一样。
            // 数据没取到这件事本身由标题栏的⚠和右键菜单去说，不归警告队列管。
            if (data.AlarmsAvailable)
            {
                ScanEquipment(data, list);
            }
            if (data.FastenAvailable)
            {
                ScanQuality(data, list);
            }
            if (data.TestAvailable)
            {
                ScanTest(data, list);
            }

            list.Sort(CompareAlerts);
            return list;
        }

        private static int CompareAlerts(Alert a, Alert b)
        {
            if (a.IsCrit != b.IsCrit)
            {
                if (a.IsCrit)
                {
                    return -1;
                }
                return 1;
            }
            return b.Severity.CompareTo(a.Severity);
        }

        // ------------------------------------------------------------ 设备报警

        private static void ScanEquipment(ShiftData data, List<Alert> list)
        {
            double gate = data.Config.MaGate;
            double ma = data.Availability;

            if (ma < gate)
            {
                Alert a = new Alert();
                a.Level = AlertLevel.Critical;
                a.Topic = BoardTab.Equipment;
                a.Subject = "整线 MA";
                a.Severity = (gate - ma) / gate;
                a.Short = "整线 MA " + ma.ToString("F1") + "%，没到 " + gate.ToString("F0") + "%";

                a.What = "整线稼动率（MA）本班 " + ma.ToString("F1") + "%，门槛是 "
                       + gate.ToString("F1") + "%，差 " + (gate - ma).ToString("F1") + " 个点。";

                a.How = "MA =（班次 " + data.SpanMinutes.ToString("F0") + " min − 整线停机 "
                      + data.SumUnion.ToString("F0") + " min）÷ " + data.SpanMinutes.ToString("F0")
                      + " min。停机取的是各台报警区间的**并集** "
                      + data.SumUnion.ToString("F0") + " min，不是各条相加的 "
                      + data.SumAll.ToString("F0") + " min —— 两台同时在报的那 "
                      + data.Overlap.ToString("F1") + " min，现场只停了一次。";

                a.Why = "通讯断连那几条不参与计算：那段时间设备在不在跑没人知道，"
                      + "算进分母会把 MA 做假，算进分子会冤枉设备，所以分子分母里一并剔除。";

                AlarmGroup? worst = data.WorstAlarm();
                if (worst != null)
                {
                    double share = worst.Total / data.SumUnion * 100;
                    a.Do = "先修 " + worst.Station + " " + worst.Name + "：它一条就占了整线停机的 "
                         + share.ToString("F0") + "%。把它压下去，MA 立刻能回来大半。";
                }
                else
                {
                    a.Do = "本班没有可归因的报警条目，先确认采集链路是否正常。";
                }

                list.Add(a);
            }

            double shareLimit = data.Config.AlarmShare;
            double countLimit = data.Config.AlarmCount;

            for (int i = 0; i < data.Alarms.Count; i++)
            {
                AlarmGroup g = data.Alarms[i];
                if (g.IsNoData || data.SumUnion <= 0)
                {
                    continue;
                }

                double share = g.Total / data.SumUnion * 100;
                bool overShare = share >= shareLimit;
                bool overCount = g.Count >= countLimit;
                if (!overShare && !overCount)
                {
                    continue;
                }

                Alert a = new Alert();
                if (overShare) { a.Level = AlertLevel.Critical; } else { a.Level = AlertLevel.Watch; }
                a.Topic = BoardTab.Equipment;
                a.Subject = g.Station + " " + g.Name;
                // 严重度一律归一化成「超出门槛多少 ÷ 门槛」，两条规则才能放一起比
                a.Severity = (g.Count - countLimit) / countLimit;
                if (overShare)
                {
                    a.Severity = (share - shareLimit) / shareLimit;
                }

                a.Short = BuildAlarmShort(data, g, share, overShare);

                a.What = g.Station + " 的「" + g.Name + "」本班报了 " + g.Count + " 次，累计 "
                       + Format.Duration(g.Total) + " min，单次平均 "
                       + Format.Each(g.Each) + "。";

                a.How = "次数是这条报警在本班出现的记录条数，累计时长 = 次数 × 单次时长 = "
                      + g.Count + " × " + Format.Each(g.Each) + "。占比 "
                      + share.ToString("F0") + "% 的分母是整线停机 "
                      + data.SumUnion.ToString("F0") + " min（并集），不是各条相加。";

                a.Why = BuildAlarmWhy(data, g, share, overShare, overCount, shareLimit, countLimit);
                a.Do = BuildAlarmDo(data, g, overShare);

                list.Add(a);
            }
        }

        private static string BuildAlarmShort(ShiftData data, AlarmGroup g, double share, bool overShare)
        {
            string who = g.Station + " " + ShortName(data, g.Name);
            if (overShare)
            {
                return who + " 占了整线停机 " + share.ToString("F0") + "%";
            }
            return who + " 报了 " + g.Count + " 次，本班最频繁";
        }

        /// <summary>
        /// 报警内容原文太长，气泡里放不下。取下划线后面那段 —— 前面是单元号，
        /// 后面才是毛病本身，「St4单元_撕膜升降动点警报」里真正要看的是「撕膜升降动点警报」。
        /// </summary>
        private static string ShortName(ShiftData data, string name)
        {
            int bar = name.IndexOf('_');
            string tail = name;
            if (bar >= 0)
            {
                tail = name.Substring(bar + 1);
            }
            if (tail.Length > 10)
            {
                tail = tail.Substring(0, 10);
            }
            return tail;
        }

        private static string BuildAlarmWhy(ShiftData data, AlarmGroup g, double share, bool overShare,
                                            bool overCount, double shareLimit, double countLimit)
        {
            string why = "";
            if (overShare)
            {
                why = "它占整线停机 " + share.ToString("F0") + "%，超过了 "
                    + shareLimit.ToString("F0") + "% 这条线。";
            }
            if (overCount)
            {
                if (why.Length > 0)
                {
                    why += "同时，";
                }
                why += "它发生了 " + g.Count + " 次，达到了 " + countLimit.ToString("F0") + " 次这条线。";
            }

            // 间隔稳定说明是机构本身的问题，不是操作波动 —— 这是派工方向的分水岭
            double gap = g.MeanGap;
            if (g.Count >= 4 && gap > 0)
            {
                string rhythm = "间隔忽长忽短，更像是随机因素。";
                if (IsRegular(data, g))
                {
                    rhythm = "间隔相当稳定 —— 这种规律性不是操作波动，"
                           + "是机构到点就出问题，该查机械不该查人。";
                }
                why += "另外，它平均每 " + gap.ToString("F0") + " 分钟犯一回，" + rhythm;
            }
            return why;
        }

        /// <summary>间隔的最大值和最小值差不超过均值的一半，就算「到点就犯」。</summary>
        private static bool IsRegular(ShiftData data, AlarmGroup g)
        {
            if (g.Starts.Length < 3)
            {
                return false;
            }

            double min = double.MaxValue;
            double max = 0;
            for (int i = 1; i < g.Starts.Length; i++)
            {
                double gap = g.Starts[i] - g.Starts[i - 1];
                if (gap < min)
                {
                    min = gap;
                }
                if (gap > max)
                {
                    max = gap;
                }
            }
            double mean = g.MeanGap;
            if (mean <= 0)
            {
                return false;
            }
            return (max - min) <= mean * 0.5;
        }

        private static string BuildAlarmDo(ShiftData data, AlarmGroup g, bool overShare)
        {
            // 单次很短但次数很多 = 问题在犯的频率，不在单次时长，别当故障去修
            if (g.Each < 0.5 && g.Count >= 5)
            {
                return "单次只有 " + Format.Each(g.Each)
                     + "，说明处理动作本身很快，问题在犯的频率不在单次时长。"
                     + "该查的是来料一致性和料仓容量设计，不要当成设备故障去修机器。";
            }

            if (overShare)
            {
                string how = "先调出这几次的前后动作记录，找共同触发条件。";
                if (IsRegular(data, g))
                {
                    how = "间隔规律，做一次完整机构点检：导轨、气缸、限位、原点回归。";
                }
                return "它是本班停机的主要来源，优先派修。" + how;
            }

            return "次数偏多但单次不长，先观察下一班是否延续；若延续，按机构问题升级处理。";
        }

        // ------------------------------------------------------------ 锁附过程

        private static void ScanQuality(ShiftData data, List<Alert> list)
        {
            for (int i = 0; i < data.Fasten.Count; i++)
            {
                FastenRow q = data.Fasten[i];
                CapabilityStatus status = q.Status;
                if (status == CapabilityStatus.Normal)
                {
                    continue;
                }

                Alert a = new Alert();
                if (status == CapabilityStatus.Urgent) { a.Level = AlertLevel.Critical; } else { a.Level = AlertLevel.Watch; }
                a.Topic = BoardTab.Quality;
                a.Subject = q.Title;

                if (!q.CanCpk)
                {
                    // 离散量没有能力指数可比，用「均值偏离规格中心的比例」当严重度：
                    // 0 = 正在中心，1 = 已经压在规格线上，和别的规则同一个量纲
                    a.Severity = MeanOffset(data, q);
                    a.Short = q.Station + "-" + q.Hole + " " + q.Measure + " 异常 " + q.NgCount + " 笔";
                    a.What = q.Title + " 本班判出 " + q.NgCount + " 笔异常。规格 " + q.SpecText
                           + "，本班均值 " + q.MeanText + "。";
                    a.How = "圈数是准离散量，σ 由编码器分辨率主导，算出来的 Cpk 是个看着很好"
                          + "但没有信息量的数，所以这一项不出能力指数，改用判定笔数看。";
                    a.Why = "规格中心是 " + ((q.Lsl + q.Usl) / 2).ToString("F1") + " 圈，本班均值 "
                          + q.MeanText + "。" + OverTurnHint(data, q);
                    a.Do = "圈数偏多配合同孔浮高偏高，是螺丝没坐实的典型特征。"
                         + "查该孔沉孔毛刺、治具定位和这批螺丝来料，不要去调电批。";
                }
                else
                {
                    double line = data.Config.CpkWarn;
                    if (status == CapabilityStatus.Urgent)
                    {
                        line = data.Config.CpkCrit;
                    }
                    a.Severity = (line - q.Cpk) / line;
                    a.Short = q.Station + "-" + q.Hole + " " + q.Measure + " "
                            + CapabilityWord(data, q) + " " + q.Cpk.ToString("F2")
                            + "，破 " + line.ToString("F2") + " 了";

                    a.What = q.Title + " 的" + CapabilityWord(data, q) + "是 " + q.Cpk.ToString("F2")
                           + "，规格 " + q.SpecText + "，本班均值 " + q.MeanText
                           + "，σ " + q.SigmaText + "。";

                    a.How = BuildCpkHow(data, q);
                    a.Why = BuildQualityWhy(data, q, line);
                    a.Do = BuildQualityDo(data, q);
                }

                list.Add(a);
            }
        }

        /// <summary>均值把规格吃掉了多少，0 = 正在中心，1 = 已经压在规格线上。</summary>
        private static double MeanOffset(ShiftData data, FastenRow q)
        {
            if (q.HasLsl && q.HasUsl)
            {
                double half = (q.Usl - q.Lsl) / 2;
                if (half <= 0)
                {
                    return 0;
                }
                return Math.Abs(q.Mean - (q.Usl + q.Lsl) / 2) / half;
            }
            if (q.HasUsl && q.Usl > 0)
            {
                return q.Mean / q.Usl;
            }
            return 0;
        }

        /// <summary>双边规格叫 Cpk，单边上限叫 CPU —— 名字不一样，别混着说。</summary>
        private static string CapabilityWord(ShiftData data, FastenRow q)
        {
            if (q.HasLsl && q.HasUsl)
            {
                return "Cpk";
            }
            if (q.HasUsl)
            {
                return "CPU";
            }
            return "CPL";
        }

        private static string BuildCpkHow(ShiftData data, FastenRow q)
        {
            string how = CapabilityWord(data, q) + " 是现算的，不是存进来的：";

            if (q.HasUsl && q.HasLsl)
            {
                how += "上限侧 (" + q.Usl.ToString("F3").TrimEnd('0').TrimEnd('.') + " − " + q.MeanText
                     + ") ÷ (3 × " + q.SigmaText + ") = " + q.Cpu.ToString("F2")
                     + "，下限侧 (" + q.MeanText + " − " + q.Lsl.ToString("F3").TrimEnd('0').TrimEnd('.')
                     + ") ÷ (3 × " + q.SigmaText + ") = " + q.Cpl.ToString("F2")
                     + "，取小的那个 = " + q.Cpk.ToString("F2") + "。";
            }
            else if (q.HasUsl)
            {
                how += "只有上限，(" + q.Usl.ToString("F3").TrimEnd('0') + " − " + q.MeanText
                     + ") ÷ (3 × " + q.SigmaText + ") = " + q.Cpk.ToString("F2") + "。";
            }

            how += "改均值或改 σ，这个数会跟着变；它不是单独存的一列。";
            return how;
        }

        private static string BuildQualityWhy(ShiftData data, FastenRow q, double line)
        {
            string why = q.Cpk.ToString("F2") + " 低于 " + line.ToString("F2") + "。";

            // 同组里找一个正常的做基准 —— 能力低到底是均值偏了还是分布宽了，
            // 一个数看不出来，得和同规格的好孔位比
            FastenRow? baseRow = FindBaseline(data, q);
            if (baseRow != null)
            {
                why += "拿同规格组里正常的 " + baseRow.Station + "-" + baseRow.Hole + "（"
                     + baseRow.Cpk.ToString("F2") + "）当基准比：它的均值 " + baseRow.MeanText
                     + "、σ " + baseRow.SigmaText + "，这一孔是 " + q.MeanText + " / " + q.SigmaText + "。";
                why += SplitCause(data, q, baseRow);
            }

            // 共模判别：同工位其他孔位是不是一起在掉
            int together = CountFalling(data, q);
            if (together >= 2)
            {
                why += " " + q.Station + " 上有 " + (together + 1) + " 个孔位的" + q.Measure
                     + "在同步下行，而别的工位没有 —— 三个一起漂、别人不动，"
                     + "指向它们共享的部件（同一把电批、同一路供气），不是各孔自己的问题。";
            }
            else if (together == 0)
            {
                why += " 同工位其余孔位都平，只有这一孔在动 —— 只有一个漂，"
                     + "指向该孔独有的部件：沉孔、治具，或这批螺丝。";
            }

            return why;
        }

        /// <summary>均值偏移和分布变宽各占多少。两种成因的处置完全不同，必须拆开。</summary>
        private static string SplitCause(ShiftData data, FastenRow q, FastenRow baseRow)
        {
            if (q.Sigma <= 0 || baseRow.Sigma <= 0)
            {
                return "";
            }

            // 假设性计算：用这一行的均值，配基准行的 σ。
            // 从基准掉到这个假想值 = 均值偏移的贡献；再掉到实际值 = σ 变宽的贡献。
            double ifOnlyMeanMoved = CpkCalculator.Index(q.Mean, baseRow.Sigma,
                                                      q.Lsl, q.Usl, q.HasLsl, q.HasUsl);

            double byMean = baseRow.Cpk - ifOnlyMeanMoved;
            double bySigma = ifOnlyMeanMoved - q.Cpk;

            if (byMean <= 0 && bySigma <= 0)
            {
                return "";
            }

            string text = "差距里，均值下移贡献 " + Math.Max(0, byMean).ToString("F2")
                        + "，σ 变宽贡献 " + Math.Max(0, bySigma).ToString("F2") + "。";

            if (bySigma > 0.1 && byMean > 0.1)
            {
                text += "两种成因并存 —— 均值偏移校准一次就能拉回，σ 变宽不行，那是磨损或松动。";
            }
            else if (bySigma > byMean)
            {
                text += "主要是分布变宽，校准没用，查磨损和松动。";
            }
            else
            {
                text += "主要是均值偏了，校准一次应该能拉回来。";
            }
            return text;
        }

        /// <summary>同一个量里，能力最好的那个孔位，拿来当同组基准。</summary>
        private static FastenRow? FindBaseline(ShiftData data, FastenRow q)
        {
            FastenRow? best = null;
            for (int i = 0; i < data.Fasten.Count; i++)
            {
                FastenRow other = data.Fasten[i];
                if (other == q || other.Measure != q.Measure || double.IsNaN(other.Cpk))
                {
                    continue;
                }
                if (other.Cpk < data.Config.CpkWarn)
                {
                    continue;                 // 自己也不合格的不能当基准
                }
                if (best == null || other.Cpk > best.Cpk)
                {
                    best = other;
                }
            }
            return best;
        }

        /// <summary>同工位、同一个量，除了它自己还有几个也在下行。</summary>
        private static int CountFalling(ShiftData data, FastenRow q)
        {
            int n = 0;
            for (int i = 0; i < data.Fasten.Count; i++)
            {
                FastenRow other = data.Fasten[i];
                if (other == q || other.Station != q.Station || other.Measure != q.Measure)
                {
                    continue;
                }
                if (data.HasTrend(other.TrendKey) && data.TrendSlope(other.TrendKey) < -0.01)
                {
                    n++;
                }
            }
            return n;
        }

        private static string BuildQualityDo(ShiftData data, FastenRow q)
        {
            int together = CountFalling(data, q);

            if (together >= 2)
            {
                return "按共享部件派工：校准 " + q.Station + " 的电批输出，复查该路供气压力与接头。"
                     + "这一处动作会同时影响该工位的几个孔位，不要一个孔一个孔地调。";
            }

            if (q.Measure == "浮高")
            {
                return "只影响这一孔，查沉孔毛刺、治具定位和这批螺丝来料。"
                     + "不要连带去调电批 —— 电批是三个孔共用的，为一个孔动它会把另外两个也带偏。";
            }

            double slope = Math.Round(data.TrendSlope(q.TrendKey), 2);
            if (slope < 0)
            {
                double shifts = (q.Cpk - data.Config.WarnFloor) / -slope;
                return "按每班 " + slope.ToString("F2") + " 的斜率外推到 "
                     + data.Config.WarnFloor.ToString("F2") + "，还有 " + shifts.ToString("F1")
                     + " 个班。这段时间产出仍然合格 —— 这就是可以从容处理的窗口，别等它跌破了再动。";
            }

            return "本班取 100 笔重测确认，未达 " + data.Config.CpkWarn.ToString("F2") + " 则升级。";
        }

        private static string OverTurnHint(ShiftData data, FastenRow q)
        {
            double center = (q.Lsl + q.Usl) / 2;
            if (q.Mean > center)
            {
                return "圈数偏多：拧得更多，说明螺丝没有在该停的位置停住。";
            }
            return "圈数偏少：螺丝可能提前到位或提前判停。";
        }

        // ------------------------------------------------------------ 测试

        private static void ScanTest(ShiftData data, List<Alert> list)
        {
            double gate = data.Config.YieldGate;
            int lineTested = data.TestTotal();

            for (int i = 0; i < data.TestStations.Count; i++)
            {
                TestStationRow s = data.TestStations[i];
                if (s.Yield >= gate)
                {
                    continue;
                }

                Alert a = new Alert();
                a.Level = AlertLevel.Critical;
                a.Topic = BoardTab.Test;
                a.Subject = s.Name;
                a.Severity = (gate - s.Yield) / gate;
                a.Short = s.Name + " 良率 " + s.Yield.ToString("F1") + "%，跌破 "
                        + gate.ToString("F0") + " 门槛";

                a.What = s.Name + " 本班良率 " + s.Yield.ToString("F1") + "%，门槛 "
                       + gate.ToString("F1") + "%，基线 " + s.Baseline.ToString("F1")
                       + "%，比基线差 " + Math.Abs(s.VsBaseline).ToString("F1") + " 个点。";

                a.How = "良率 =（测试数 " + s.Tested + " − NG " + s.Ng + "）÷ " + s.Tested
                      + "。NG 里超规 " + s.OverSpec + " 笔、功能失败 " + s.FuncFail
                      + " 笔、未知异常 " + s.Unknown + " 笔。";

                a.Why = BuildStationWhy(data, s, lineTested);
                a.Do = BuildStationDo(data, s);

                list.Add(a);
            }

            for (int i = 0; i < data.TestItems.Count; i++)
            {
                TestItemRow t = data.TestItems[i];
                Alert? a = TestItemAlert(data, t, lineTested);
                if (a != null)
                {
                    list.Add(a);
                }
            }
        }

        private static string BuildStationWhy(ShiftData data, TestStationRow s, int lineTested)
        {
            string why = "";

            if (s.FuncFail > 0 && s.OverSpec > 0)
            {
                why = "它的 " + s.Ng + " 笔 NG 是两件事，不要合并处理："
                    + s.OverSpec + " 笔超规（有测值，能算能力，查参数漂移）、"
                    + s.FuncFail + " 笔功能失败（FailValue 为空，根本没量到值，查通讯与接触）。"
                    + "处置动作完全不同，并案会让两边都查不清。";
            }

            // 找出这个站里拖累最大的那一项 —— 是单点问题还是整体劣化，决定派几个人
            TestItemRow? top = null;
            for (int i = 0; i < data.TestItems.Count; i++)
            {
                TestItemRow t = data.TestItems[i];
                if (t.Station != s.Name)
                {
                    continue;
                }
                if (top == null || t.YieldLoss(lineTested) > top.YieldLoss(lineTested))
                {
                    top = t;
                }
            }

            if (top != null && top.YieldLoss(lineTested) > 0)
            {
                double all = Math.Abs(data.LineVsBaseline());
                double mine = top.YieldLoss(lineTested);
                double pct = Format.Percent(mine, all);
                why += " 整线掉的 " + all.ToString("F2") + " 个点里，"
                     + top.Name + " 一项就占了 " + mine.ToString("F2") + " 点（"
                     + pct.ToString("F0") + "%）。这不是整体性劣化，是单点问题，只该派一个人。";
            }

            return why;
        }

        private static string BuildStationDo(ShiftData data, TestStationRow s)
        {
            if (s.FuncFail > 0)
            {
                return "分两条线派：超规那部分按 SeqNum 原始顺序调出本班全部测值看漂移形态；"
                     + "功能失败那 " + s.FuncFail + " 笔没有测值，查线材、连接器接触与枚举超时设定。";
            }
            return "调出本班该站全部 NG 的测项分布，确认是集中在一项还是散在多项 —— "
                 + "集中就是单点问题，散开才需要整站排查。";
        }

        private static Alert? TestItemAlert(ShiftData data, TestItemRow t, int lineTested)
        {
            TestItemStatus status = t.Status;
            bool bad = status == TestItemStatus.Failing || status == TestItemStatus.NeedsWork
                    || t.NgKind == TestItemRow.FunctionFailure;
            if (!bad && !t.IsDrifting)
            {
                return null;
            }

            Alert a = new Alert();
            a.Topic = BoardTab.Test;
            a.Subject = t.Station + " " + t.Name;

            if (t.NgKind == TestItemRow.FunctionFailure)
            {
                a.Level = AlertLevel.Critical;
                // 换算成它吃掉了整线良率的几个点，这样才和别的规则可比
                a.Severity = Format.Percent(t.Ng, lineTested);
                a.Short = t.Station + " " + t.Name + " " + t.Ng + " 笔没量到值";
                a.What = t.Station + " 的 " + t.Name + " 本班 " + t.Ng + " 笔功能失败。";
                a.How = "这一项没有 Cpk：FailValue 为空，这一步根本没量到数（通讯 / 无回应 / 超时），"
                      + "没有数字可以做能力统计。它只进良率分母，不进 Cpk 样本。";
                a.Why = "有测值的超规和没测值的功能失败是两类问题。前者是参数漂了，"
                      + "后者是根本没连上，查的东西完全不同。";
                a.Do = "查 USB 线材、连接器接触与枚举超时设定。不要和同站的超规问题并案。";
                return a;
            }

            double cpk = t.Cpk;
            if (!double.IsNaN(cpk) && !t.IsSpecTooWide && cpk < data.Config.CpkWarn)
            {
                double line = data.Config.CpkWarn;
                if (cpk < data.Config.CpkCrit)
                {
                    line = data.Config.CpkCrit;
                }
                if (cpk < data.Config.CpkCrit) { a.Level = AlertLevel.Critical; } else { a.Level = AlertLevel.Watch; }
                a.Severity = (line - cpk) / line;
                a.Short = t.Station + " " + t.Name + " Cpk " + cpk.ToString("F2")
                        + "，破 " + line.ToString("F2");
            }
            else if (t.IsDrifting)
            {
                a.Level = AlertLevel.Watch;
                a.Severity = t.Drift - data.Config.DriftRatio;
                a.Short = t.Station + " " + t.Name + " 在漂，σ总/σ内 " + t.Drift.ToString("F2");
            }
            else
            {
                return null;
            }

            a.What = t.Station + " 的 " + t.Name + "：Cpk " + Format.Cpk(cpk, data.Config.CpkAbsurd)
                   + "，规格 " + t.SpecText + "，均值 " + t.MeanText
                   + "，σ内 " + t.SigmaInText + "，σ总 " + t.SigmaAllText
                   + OverSpecNote(data, t);

            a.How = "Cpk 用组内标准差算：上限侧 " + Fmt(data, t.Cpu) + "、下限侧 " + Fmt(data, t.Cpl)
                  + "，取小的 = " + Format.Cpk(cpk, data.Config.CpkAbsurd)
                  + "。漂移比是另一套判据，σ总 ÷ σ内 = " + Fmt(data, t.Drift) + "。";

            a.Why = BuildItemWhy(data, t, lineTested);
            a.Do = BuildItemDo(data, t);
            return a;
        }

        /// <summary>
        /// 零不良也要明说「本班零不良」，不能省成空白。
        /// 空白会让人以为这一项没查过，而它其实是查过了、没问题 —— 两种情况差别很大。
        /// </summary>
        private static string OverSpecNote(ShiftData data, TestItemRow item)
        {
            if (item.Ng > 0)
            {
                return "，本班 " + item.Ng + " 笔超规。";
            }
            return "，本班零不良。";
        }

        private static string BuildItemWhy(ShiftData data, TestItemRow t, int lineTested)
        {
            string why = "";

            if (t.IsDrifting)
            {
                why = "σ总 / σ内 = " + Fmt(data, t.Drift) + "，超过 " + data.Config.DriftRatio.ToString("F1")
                    + "，判定过程漂移。合起来的含义很具体：**设备的短期能力不差，"
                    + "是整批从头到尾在往上爬**。";
                if (!double.IsNaN(t.Cpk) && t.Cpk >= data.Config.CpkWarn)
                {
                    why += " 光看 Cpk " + t.Cpk.ToString("F2") + " 什么问题都没有 —— "
                         + "只有把两个标准差摆在一起才看得见。本班零不良，属于提前预警。";
                }
            }
            else
            {
                why = "漂移比 " + Fmt(data, t.Drift) + " 在 " + data.Config.DriftRatio.ToString("F1")
                    + " 以下，**没有漂移**，是能力本身就偏低。这类靠拉回中心值或压窄分布改善，"
                    + "查异常点没用 —— 和有漂移的那种正好相反。";
            }

            if (t.Ng > t.BaselineNg && lineTested > 0)
            {
                double loss = t.YieldLoss(lineTested);
                double all = Math.Abs(data.LineVsBaseline());
                why += " 它让整线良率掉了 " + loss.ToString("F2") + " 个点（本班 " + t.Ng
                     + " 笔，基线 " + t.BaselineNg + " 笔）";
                if (all > 0)
                {
                    why += "，占全部劣化 " + all.ToString("F2") + " 点的 "
                         + (loss / all * 100).ToString("F0") + "%";
                }
                why += "。";
            }

            return why;
        }

        private static string BuildItemDo(ShiftData data, TestItemRow t)
        {
            if (t.IsDrifting)
            {
                return "按 SeqNum 原始顺序调出本班全部测值，看漂移形态：**单调上升**就查温升与老化，"
                     + "**分层跳变**就对时间戳找换治具 / 换批 / 换班的断点。"
                     + "要找的是漂移源，不是去改设备或调设定值。";
            }
            return "能力本身偏低，看均值离哪一侧规格近就往哪边拉，"
                 + "或者压窄分布。查单个异常点对这类没有帮助。";
        }

        private static string Fmt(ShiftData data, double v)
        {
            if (double.IsNaN(v))
            {
                return "—";
            }
            return v.ToString("F2");
        }
    }
}

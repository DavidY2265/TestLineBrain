using System;
using System.Collections.Generic;
using System.Text;

namespace DesktopPet.LineBrain
{
    /// <summary>
    /// 发给语言模型的那两份清单。
    ///
    /// **事实清单** —— 本地规则引擎已经算完的每一个数，连中间步骤都摊开写。
    /// 模型手里没有一个需要它动手算的数，它能做的只有挑出相关的几条、用人话讲出来。
    /// 自洽是这么保证的：不是靠提示词请它别算错，是让它物理上没有算错的机会。
    ///
    /// **能力清单** —— 程序认得哪些文件、每个文件要哪几列、有哪些计算模型、
    /// 以及**已知缺什么**。缺数据的时候，模型从这里挑该让用户补什么，同样只能照抄。
    /// 让它自己编一份听起来合理的列名，用户照着准备一整天、程序不认，
    /// 这和让它算错数是同一类错误：发明了本该照抄的东西。
    /// </summary>
    public static class FactSheet
    {
        private static ShiftData? _cachedFor;
        private static string _cached = "";

        /// <summary>
        /// 拼一次存着反复用。同一份 ShiftData 拼出来的清单是不变的，
        /// 每问一句重拼一遍纯属浪费（里面要跑一遍 AlertScanner.Scan）。
        ///
        /// 缓存认的是**对象本身**：刷新之后 ShiftData 是个新对象，
        /// 这里自然就重拼了 —— 不用记得去手动作废，也就不会出现
        /// 「表刷新了、AI 还在说老数据」。
        /// </summary>
        public static string Cached(ShiftData data)
        {
            if (!ReferenceEquals(_cachedFor, data))
            {
                _cached = Build(data);
                _cachedFor = data;
            }
            return _cached;
        }

        /// <summary>
        /// 两份清单拼在一起，当系统提示词的主体发过去。
        /// 同一个班次里它是不变的，所以可以走 prompt 缓存，多轮追问只算一次钱。
        /// </summary>
        public static string Build(ShiftData data)
        {
            StringBuilder s = new StringBuilder();
            AppendFacts(data, s);
            s.AppendLine();
            AppendCapability(data, s);
            return s.ToString();
        }

        // =================================================================
        //  事实清单
        // =================================================================

        private static void AppendFacts(ShiftData data, StringBuilder s)
        {
            s.AppendLine("# 事实清单");
            s.AppendLine("# 以下每个数都由本地规则引擎算出，与看板上的表格同源。");
            s.AppendLine("# 你只能引用，不能改写，不能自己做任何算术。");
            s.AppendLine();

            AppendShift(data, s);
            AppendEquipment(data, s);
            AppendQuality(data, s);
            AppendTest(data, s);
            AppendAlerts(data, s);
            AppendThresholds(data, s);
        }

        private static void AppendShift(ShiftData data, StringBuilder s)
        {
            // 看板有三栏，每栏有个内部代号。用户会拿代号当简称用（「那MQ呢」），
            // 不说清楚的话模型会把 MQ 当成某个不认识的指标，然后老老实实回答「没有这个数」——
            // 答得诚恳，但答错了问题。
            s.AppendLine("## 看板的三栏（用户可能用代号或简称指代）");
            s.AppendLine("- **eq** = 「设备报警」栏，也叫报警、停机、稼动、MA");
            s.AppendLine("- **mq** = 「锁附工艺」栏，也叫锁附、过程品质、扭力浮高圈数、Cpk");
            s.AppendLine("- **te** = 「测试」栏，也叫测试良率、FCT、CAL、测项");
            s.AppendLine("用户说「那 MQ 呢」「TE 怎么样」，问的就是对应那一栏的情况。");

            // 原来这里写死「三栏的数下面全都有，直接答」。有栏取不到数的时候，
            // 这句话本身就是假的，而且它出现在清单最前面，会盖过后面那些
            //「没取到」的说明 —— 模型先读到「全都有」，就会去把空栏当成零。
            List<string> missing = new List<string>();
            if (!data.AlarmsAvailable)
            {
                missing.Add("eq（设备报警）");
            }
            if (!data.FastenAvailable)
            {
                missing.Add("mq（锁附工艺）");
            }
            if (!data.TestAvailable)
            {
                missing.Add("te（测试）");
            }

            if (missing.Count == 0)
            {
                s.AppendLine("三栏的数下面全都有，直接答。");
            }
            else
            {
                s.AppendLine("**但这一次 " + string.Join("、", missing.ToArray())
                           + " 没取到数据**，那几栏下面只有「没取到」的说明，没有任何数字。"
                           + "被问到就说取不到，别把空栏当成零。");
            }
            s.AppendLine();

            s.AppendLine("## 班次");
            s.AppendLine("产线 " + data.LineLabel
                       + "　班次 " + data.ShiftName
                       + " " + data.Window.StartText + "–" + data.Window.EndText
                       + "　班次长度 " + data.SpanMinutes.ToString("F0") + " min"
                       + "　数据截至 " + data.DataUntil);
            s.AppendLine("注意：本地只有这一条产线、这一个班次的数据。");
            s.AppendLine();
        }

        private static void AppendEquipment(ShiftData data, StringBuilder s)
        {
            s.AppendLine("## 设备报警（来源：本机 MySQL 的报警表，看板「设备报警」栏）");

            // 取不到就**一个数都不给**。
            //
            // 原来这里不管取没取到都照写「整线停机 0 min、报警 0 次、MA 100%」——
            // 那三个数在没取到数据时全是假的，而模型没有别的依据，只能当真，
            // 于是回答里出现「本班设备运行平稳、稼动率满分」。
            // 提示词里叮嘱「没有就说没有」没用：清单里白纸黑字写着 100%，
            // 那对模型来说就是有。**唯一可靠的办法是不写那些数。**
            if (!data.AlarmsAvailable)
            {
                s.AppendLine("**报警数据没取到。**");
                s.AppendLine("原因见下面「数据问题」一节 —— 通常是数据库还没配、连不上，"
                           + "或者表名写错。");
                s.AppendLine();
                s.AppendLine("注意：**这不等于「本班没有报警」。** 这一栏的停机时长、报警次数、"
                           + "整线 MA 现在一个都不知道。被问到设备、停机、稼动、MA 的时候，"
                           + "直接回答「取不到报警数据」并说明原因，"
                           + "**不要给数字、不要说产线运行正常、也不要拿测试栏的数据去推断**。");
                s.AppendLine();
                return;
            }

            s.AppendLine("整线停机（各台报警区间的并集）= " + data.SumUnion.ToString("F0") + " min");
            s.AppendLine("各条报警时长相加 = " + data.SumAll.ToString("F0")
                       + " min，其中重叠 " + data.Overlap.ToString("F1")
                       + " min（两台同时在报，现场只停了一次）");
            s.AppendLine("报警次数 = " + data.AlarmTimes + " 次（通讯断连不计）");
            s.AppendLine("整线 MA = (" + data.SpanMinutes.ToString("F0") + " − "
                       + data.SumUnion.ToString("F0") + ") ÷ " + data.SpanMinutes.ToString("F0")
                       + " = " + data.Availability.ToString("F1") + "%"
                       + "，门槛 " + data.Config.MaGate.ToString("F1") + "%");
            s.AppendLine("实际自动运行 = " + (data.SpanMinutes - data.SumUnion).ToString("F0") + " min");
            s.AppendLine();
            s.AppendLine("逐条：");

            List<AlarmGroup> rows = data.SortAlarms(AlarmSortKey.Duration);
            for (int i = 0; i < rows.Count; i++)
            {
                AlarmGroup g = rows[i];

                if (g.IsNoData)
                {
                    s.AppendLine("- [通讯断连] " + g.Station + " " + g.Name
                               + "　" + g.Count + " 次　" + Format.Duration(g.Total) + " min"
                               + "　状态未知，已从 MA 的分子分母一并剔除，不算运行也不算停机");
                    continue;
                }

                double share = Format.Percent(g.Total, data.SumUnion);
                s.Append("- " + g.Station + " " + g.Name
                       + "　" + g.Count + " 次　累计 " + Format.Duration(g.Total) + " min"
                       + "　单次 " + Format.Each(g.Each)
                       + "　占整线停机 " + share.ToString("F0") + "%");

                if (g.Count >= 2)
                {
                    s.Append("　平均间隔 " + g.MeanGap.ToString("F0") + " min");
                    s.Append("　发生于开班后第 " + JoinStarts(data, g) + " 分钟");
                }
                s.AppendLine();
            }
            s.AppendLine();
        }

        /// <summary>
        /// 双边规格叫「Cpk」，单边的只能叫「能力指数」—— 名字不一样，别混着说。
        /// 原文这里是个三元表达式，条件和两个分支挤在一行里，读的时候要把括号数清楚。
        /// </summary>
        private static string CapabilityWord(ShiftData data, FastenRow q)
        {
            if (q.Side == SpecSide.TwoSided)
            {
                return "Cpk";
            }
            return "能力指数";
        }

        private static string JoinStarts(ShiftData data, AlarmGroup g)
        {
            string text = "";
            for (int i = 0; i < g.Starts.Length; i++)
            {
                if (i > 0)
                {
                    text += "/";
                }
                text += g.Starts[i].ToString("F0");
            }
            return text;
        }

        private static void AppendQuality(ShiftData data, StringBuilder s)
        {
            s.AppendLine("## 锁附过程（来源：本机 MySQL 的锁附表，看板「锁附工艺」栏）");

            // 同 AppendEquipment：取不到就一个数都不给。
            // 「在监 0 个孔位、不良 0 笔」读起来像「全部合格」，是最坏的一种假数据。
            if (!data.FastenAvailable)
            {
                s.AppendLine("**锁附数据没取到。**");
                if (data.Config.FastenTable.Length == 0)
                {
                    s.AppendLine("brain.csv 里还没配 fasten_table —— 锁附表的结构尚未确定，"
                               + "这部分功能还没接上。");
                }
                else
                {
                    s.AppendLine("原因见下面「数据问题」一节。");
                }
                s.AppendLine();
                s.AppendLine("注意：**这不等于「本班锁附全部合格」。** 孔位数、扭力/浮高不良笔数、"
                           + "各孔位的 Cpk 现在一个都不知道。被问到锁附、扭力、浮高、圈数、"
                           + "过程能力的时候，直接回答「取不到锁附数据」，"
                           + "**不要给数字，也不要说过程稳定**。");
                s.AppendLine();
                return;
            }

            s.AppendLine("在监 " + data.HoleCount() + " 个孔位");
            s.AppendLine("本班扭力判定不良 " + data.FastenNg("扭力")
                       + " 笔，浮高判定不良 " + data.FastenNg("浮高") + " 笔");

            double window = data.WarnWindow();
            if (!double.IsNaN(window))
            {
                s.AppendLine("预警窗口 " + window.ToString("F1") + " 班"
                           + "（还在关注档且正在下行的孔位里，按各自斜率外推到 "
                           + data.Config.WarnFloor.ToString("F2") + "，最快的那个还剩几班）");
            }
            s.AppendLine();
            s.AppendLine("逐行（能力指数含两侧中间值，取小的那个为 Cpk）：");

            for (int i = 0; i < data.Fasten.Count; i++)
            {
                FastenRow q = data.Fasten[i];
                s.Append("- " + q.Station + "-" + q.Hole + " " + q.Measure
                       + "　规格 " + q.SpecText
                       + "　均值 " + q.MeanText + "　σ " + q.SigmaText);

                if (q.CanCpk)
                {
                    if (!double.IsNaN(q.Cpu))
                    {
                        s.Append("　CPU " + q.Cpu.ToString("F2"));
                    }
                    if (!double.IsNaN(q.Cpl))
                    {
                        s.Append("　CPL " + q.Cpl.ToString("F2"));
                    }
                    s.Append("　" + CapabilityWord(data, q) + " " + q.Cpk.ToString("F2"));
                    s.Append("　" + StatusText.Of(q.Side));
                    s.Append("　本班采 " + q.SampleCount + " 笔，PLC 判不良 " + q.NgCount + " 笔");
                }
                else
                {
                    s.Append("　离散量不出能力指数（σ 由编码器分辨率主导，算出来没有信息量）"
                           + "　本班采 " + q.SampleCount + " 笔，PLC 判异常 " + q.NgCount + " 笔");
                }

                s.Append("　状态 " + StatusText.Of(q.Status));

                if (data.HasTrend(q.TrendKey))
                {
                    double slope = Math.Round(data.TrendSlope(q.TrendKey), 2);
                    s.Append("　近8班斜率 " + slope.ToString("F2") + "/班"
                           + "（" + data.TrendFirst(q.TrendKey).ToString("F2")
                           + " → " + data.TrendLast(q.TrendKey).ToString("F2") + "）");
                }
                s.AppendLine();
            }
            s.AppendLine();
        }

        private static void AppendTest(ShiftData data, StringBuilder s)
        {
            int total = data.TestTotal();

            s.AppendLine("## 测试（来源 data\\testlogs\\ 下的原始 UTS log，看板「测试」栏）");

            // 一个测站都没有的时候，良率算式是 (0 − 0) ÷ 0，打出来是「整线良率 0.00%」。
            // 那是个假数，而且方向比报警栏更吓人 —— 看着像整线全挂。
            if (!data.TestAvailable)
            {
                s.AppendLine("**本班没读到测试 log。**");
                s.AppendLine("目录 " + data.Config.TestLogPath
                           + " 里没有本班日期（" + data.Window.DateText + "）的文件，"
                           + "或者文件里没有一笔测试落在本班时间内。原因见「数据问题」一节。");
                s.AppendLine();
                s.AppendLine("注意：**这不等于「良率 0%」，也不等于「本班没测」。** "
                           + "良率、NG 笔数、各测站、各测项的 Cpk 现在一个都不知道。"
                           + "被问到测试、良率、FCT、CAL、测项的时候，直接回答「没读到本班的测试 log」，"
                           + "**不要给数字**。");
                s.AppendLine();
                return;
            }

            s.AppendLine("整线良率 = (" + total + " − " + data.TestNg() + ") ÷ " + total
                       + " = " + data.LineYield().ToString("F2") + "%"
                       + "（按测试数加权，不是三站平均）");
            s.AppendLine("整线基线 " + data.LineBaseline().ToString("F2")
                       + "%（同样加权），差 "
                       + Format.Delta(data.LineVsBaseline(), 2));
            s.AppendLine("本班 NG " + data.TestNg() + " 笔 = 超规 " + data.TestOverSpec()
                       + " + 功能失败 " + data.TestFuncFail() + " + 未知异常 "
                       + (data.TestNg() - data.TestOverSpec() - data.TestFuncFail()));
            s.AppendLine("测站良率门槛 " + data.Config.YieldGate.ToString("F1") + "%");
            s.AppendLine();

            s.AppendLine("测站：");
            for (int i = 0; i < data.TestStations.Count; i++)
            {
                TestStationRow t = data.TestStations[i];
                s.AppendLine("- " + t.Name + "　测试 " + t.Tested + " 笔　NG " + t.Ng
                           + "（超规 " + t.OverSpec + " 功能失败 " + t.FuncFail
                           + " 未知 " + t.Unknown + "）"
                           + "　良率 " + t.Yield.ToString("F1") + "%"
                           + "　基线 " + t.Baseline.ToString("F2") + "%"
                           + "　差 " + Format.Delta(t.VsBaseline)
                           + GateNote(data, t));
            }
            s.AppendLine();

            s.AppendLine("测项（Cpk 用组内 σ 算；σ总 只用来判漂移，不产出任何指标）：");
            for (int i = 0; i < data.TestItems.Count; i++)
            {
                TestItemRow t = data.TestItems[i];
                s.Append("- " + t.Station + " " + t.Name
                       + "　本班 NG " + t.Ng + NgKindNote(data, t)
                       + "　基线 NG " + t.BaselineNg);

                if (t.CanCpk)
                {
                    s.Append("　规格 " + t.SpecText + "　均值 " + t.MeanText
                           + "　σ内 " + t.SigmaInText + "　σ总 " + t.SigmaAllText
                           + "　CPU " + t.Cpu.ToString("F2") + "　CPL " + t.Cpl.ToString("F2")
                           + "　Cpk " + Format.Cpk(t.Cpk, data.Config.CpkAbsurd)
                           + "　漂移比 σ总/σ内 " + t.Drift.ToString("F2")
                           + DriftNote(data, t));
                }
                else
                {
                    s.Append("　无测值，不可算 Cpk（FailValue 为空：通讯/无回应/超时），"
                           + "只进良率分母，不进 Cpk 样本");
                }

                double loss = t.YieldLoss(total);
                if (loss > 0)
                {
                    s.Append("　拖累整线良率 " + loss.ToString("F2") + " pt");
                }
                s.AppendLine("　状态 " + StatusText.Of(t.Status));
            }
            s.AppendLine();

            s.AppendLine("测项盘点：共 " + data.TestTotalItems + " 项，可算 Cpk "
                       + data.TestCpkCapable + " 项；不可算 "
                       + (data.TestTotalItems - data.TestCpkCapable) + " 项"
                       + "（单边等于判定 " + data.TestSingleSided
                       + "，离散量 " + data.TestDiscrete
                       + "，σ=0 " + data.TestZeroSigma + "）。"
                       + "这 " + (data.TestTotalItems - data.TestCpkCapable)
                       + " 项不是漏算，是不该算，不进分母。");
            s.AppendLine("上面只列出要看的那几项；其余测项的逐项明细本地没有展开，不要编。");
            s.AppendLine();
        }

        // 下面三个都是「这一行后面要不要跟一句补充」。原文是嵌在长字符串拼接里的
        // 三元表达式，一行里既有条件又有两个分支，读的时候要先把括号数清楚。

        /// <summary>破了门槛才提一句。没破就什么都不说 —— 没事的行不该占篇幅。</summary>
        private static string GateNote(ShiftData data, TestStationRow station)
        {
            if (station.Yield < data.Config.YieldGate)
            {
                return "　已破门槛";
            }
            return "";
        }

        /// <summary>NG 类型：超规 / 功能失败。零不良的行没有类型，留空。</summary>
        private static string NgKindNote(ShiftData data, TestItemRow item)
        {
            if (item.NgKind.Length > 0)
            {
                return " " + item.NgKind;
            }
            return "";
        }

        /// <summary>
        /// 漂移判没判。两种都要写出来 —— 只在判了的时候才提，
        /// 会让模型以为没提到的那些是「不知道」而不是「查过了，没有」。
        /// </summary>
        private static string DriftNote(ShiftData data, TestItemRow item)
        {
            if (item.IsDrifting)
            {
                return "（已判漂移）";
            }
            return "（未判漂移）";
        }

        private static void AppendAlerts(ShiftData data, StringBuilder s)
        {
            List<Alert> alerts = AlertScanner.Scan(data);
            s.AppendLine("## 本班警告（数值超过 brain.csv 里的线，共 " + alerts.Count + " 条）");

            for (int i = 0; i < alerts.Count; i++)
            {
                Alert a = alerts[i];
                s.AppendLine("- [" + StatusText.Of(a.Level) + "] " + a.Subject + "：" + a.Short);
                if (a.Why.Length > 0)
                {
                    s.AppendLine("  判定依据：" + a.Why);
                }
                if (a.Do.Length > 0)
                {
                    s.AppendLine("  建议动作：" + a.Do);
                }
            }
            s.AppendLine();
        }

        private static void AppendThresholds(ShiftData data, StringBuilder s)
        {
            s.AppendLine("## 判定门槛（brain.csv，用户可改）");
            s.AppendLine("能力指数：< " + data.Config.CpkWarn.ToString("F2") + " 关注，< "
                       + data.Config.CpkCrit.ToString("F2") + " 紧急，≥ "
                       + data.Config.CpkGood.ToString("F2") + " 优秀，> "
                       + data.Config.CpkAbsurd.ToString("F0") + " 说明规格开太松、这个数没有参考意义");
            s.AppendLine("测站良率门槛 " + data.Config.YieldGate.ToString("F1") + "%");
            s.AppendLine("漂移：σ总/σ内 > " + data.Config.DriftRatio.ToString("F1"));
            s.AppendLine("整线 MA 门槛 " + data.Config.MaGate.ToString("F1") + "%");
            s.AppendLine("单条报警：占整线停机 ≥ " + data.Config.AlarmShare.ToString("F0")
                       + "%，或发生 ≥ " + data.Config.AlarmCount.ToString("F0") + " 次，就提醒");
            s.AppendLine("趋势外推目标下限 " + data.Config.WarnFloor.ToString("F2"));
            s.AppendLine();
        }

        // =================================================================
        //  能力清单
        // =================================================================

        private static void AppendCapability(ShiftData data, StringBuilder s)
        {
            s.AppendLine("# 能力清单");
            s.AppendLine("# 缺数据时，从这里照抄文件名、列名和路径告诉用户该准备什么。");
            s.AppendLine("# 不要自己发明列名或文件格式 —— 用户照着准备了，程序不认。");
            s.AppendLine();

            s.AppendLine("## 数据从哪来（三个来源，不是一个）");
            s.AppendLine();

            s.AppendLine("**报警和螺丝锁附 —— 本机 MySQL，直连。**");
            s.AppendLine("  库 " + data.Config.DbName + " @ " + data.Config.DbHost
                       + "，报警表 " + data.Config.AlarmTable);
            s.AppendLine("  报警表的列：device_id（站别）、work_id（里面第 3 段是产线）、"
                       + "alarm_msg（报警内容）、insert_time（发生）、recover_time（恢复，可能为空）");
            if (data.Config.FastenTable.Length == 0)
            {
                s.AppendLine("  **螺丝锁附的表还没有**，所以锁附那一栏现在是空的。"
                           + "要能回答锁附相关的问题，需要一张一行一次锁附的表："
                           + "工位、孔位、量（扭力/浮高/圈数）、实测值、PLC 判定，"
                           + "外加一份规格（上下限、显示位数）。");
            }
            s.AppendLine();

            s.AppendLine("**测试 —— 测试机的原始 UTS log，一台机器一份。**");
            s.AppendLine("  目录 " + data.Config.TestLogPath);
            s.AppendLine("  原样拷进去就行，不用预处理。程序自己切段、处理程式重启、"
                       + "判三类不良、逐测项算能力。规格（上下限）在 log 的 U / L 两行里自带。");
            s.AppendLine();

            s.AppendLine("**历史 —— 两份小 csv，log 和数据库里都没有的东西。**");
            s.AppendLine("  test_baseline.csv　列：测站, 基线良率");
            s.AppendLine("  test_trend.csv　　列：测站, 测项, 基线NG, 近8班（8 个值分号隔开）");
            s.AppendLine();

            s.AppendLine("**配置 —— brain.csv**（产线、两班起止、数据库连接、全部警戒值），"
                       + "在 " + BrainConfig.Folder + "。改完重启生效。");
            s.AppendLine();

            s.AppendLine("## 已有的计算模型");
            s.AppendLine("- 报警区间并集与整线停机、整线 MA（通讯断连从分子分母一并剔除）");
            s.AppendLine("- 报警规律性判别（间隔的极差 ≤ 均值的一半 = 机构到点就犯，该查机械不查人）");
            s.AppendLine("- 能力指数 CPU/CPL/Cpk，双边取小；离散量不出指数，改用组合规则判读");
            s.AppendLine("- 同规格组内对比，把差距拆成「均值偏移贡献」和「σ 变宽贡献」两部分");
            s.AppendLine("- 共模判别：同一个量里几个孔位一起下行 = 共享部件问题，只有一个漂 = 该孔独有部件");
            s.AppendLine("- 近 8 班最小二乘斜率，以及按斜率外推到目标下限还剩几班（预警窗口）");
            s.AppendLine("- 测试良率按测试数加权（站良率不能相加，也不能取平均）");
            s.AppendLine("- 单项对整线良率的拖累 =（本班 NG − 基线 NG）÷ 整线测试数");
            s.AppendLine("- 漂移比 σ总/σ内，与能力指数是两套独立判据，任一项都不得只报其中一个");
            s.AppendLine("- 阈值判定与警告排队（严重度按归一化的超线比例，不是原始计数）");
            s.AppendLine();

            AppendGaps(data, s);
        }

        /// <summary>
        /// 已知缺口。这几类问题是可以预见的，与其让模型临场组织，
        /// 不如把「缺什么数据 + 缺什么算法」提前写清楚，它照抄就行。
        /// </summary>
        private static void AppendGaps(ShiftData data, StringBuilder s)
        {
            s.AppendLine("## 现在没有的东西（被问到这几类，按这里写的回答）");
            s.AppendLine();

            // 这两条排在最前面。下面几条是「这个程序本来就还没做」，
            // 这两条是「本来做好了，但这台机器这次没取到」—— 后者更容易被当成
            // 「一切正常」，也更该先看见。
            if (!data.AlarmsAvailable)
            {
                s.AppendLine("### 本班的报警数据（这次没取到）");
                s.AppendLine("停机时长、报警次数、整线 MA、逐条报警明细，**本次一个都没有**。"
                           + "不是 0，是不知道。原因见「数据问题」一节。"
                           + "回答时说明取不到和原因，不要给数字，不要下「设备正常」的结论。");
                s.AppendLine();
            }
            if (!data.FastenAvailable)
            {
                s.AppendLine("### 本班的锁附数据（这次没取到）");
                s.AppendLine("孔位、扭力/浮高/圈数的实测与判定、各孔位能力指数，"
                           + "**本次一个都没有**。不是全合格，是不知道。"
                           + "回答时说明取不到和原因，不要给数字，不要下「过程稳定」的结论。");
                s.AppendLine();
            }
            if (!data.TestAvailable)
            {
                s.AppendLine("### 本班的测试数据（这次没读到）");
                s.AppendLine("良率、NG 笔数与三类拆分、各测站、各测项的 Cpk，"
                           + "**本次一个都没有**。不是 0%，是不知道。"
                           + "回答时说明没读到 log 和原因，不要给数字。");
                s.AppendLine();
            }

            s.AppendLine("### 别的产线（隔壁线体、B 线、整厂对比）");
            s.AppendLine("缺数据：本地只有 " + data.LineLabel + " 一条线。"
                       + "需要按同样的格式，在 " + BrainConfig.Folder
                       + " 下为每条线建一个子目录（如 A2\\），放同名的那一整套 csv。");
            s.AppendLine("缺算法：现在的规则引擎是「单线单班」口径。跨线要补两块 ——"
                       + "①可比性判定：两条线的规格组、设备型号、程式版本一致才可比，"
                       + "否则能力指数不能直接比大小；"
                       + "②跨线加权：整线 MA 和良率按各自产量加权，不是两条线取平均。");
            s.AppendLine();

            s.AppendLine("### 别的班次、别的日期、历史趋势");
            s.AppendLine("缺数据：本地只有 " + data.ShiftName + " "
                       + data.Window.StartText + "–" + data.Window.EndText + " 这一班。"
                       + "近 8 班只有 quality_trend.csv / test_trend.csv 里那一列能力指数，没有历史的报警和良率明细。"
                       + "需要按班次建子目录（如 2026-09-21-夜班\\），每班一套同样的文件。");
            s.AppendLine("缺算法：需要补跨班聚合与班次间对比（同班次比同班次，白班和夜班不直接比）。");
            s.AppendLine();

            s.AppendLine("### 产能、节拍、OEE");
            s.AppendLine("缺数据：现在没有产量和节拍。需要一份 output.csv："
                       + "时段, 投入数, 产出数, 理论节拍s, 实际节拍s。");
            s.AppendLine("缺算法：OEE = 时间稼动率 × 性能稼动率 × 良品率。"
                       + "现在只有第一项（MA）和第三项（良率）能算，性能稼动率要靠节拍数据。");
            s.AppendLine();

            s.AppendLine("### 物料批次、追溯、供应商");
            s.AppendLine("缺数据：需要一份 lot.csv：批次号, 物料, 供应商, 上线时间, 数量。");
            s.AppendLine("缺算法：需要补批次与不良的关联分析（按批次切分良率，比较批次间差异）。");
            s.AppendLine();

            s.AppendLine("### 人员、工单、成本");
            s.AppendLine("缺数据：完全没有接入。");
            s.AppendLine("缺算法：没有。这类问题超出本看板的范围，"
                       + "建议直接说不在本系统的职责内，不要给准备数据的建议。");
            s.AppendLine();
        }
    }
}

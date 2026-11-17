using System;
using System.Collections.Generic;
using System.Text;

namespace DesktopPet.LineBrain
{
    /// <summary>报告里的一段：一个小标题 + 一段正文。正文里 **两个星号** 包住的会加粗。</summary>
    public sealed class ReportSection
    {
        public string Heading = "";
        public string Body = "";

        public ReportSection(string heading, string body)
        {
            Heading = heading;
            Body = body;
        }

        private static readonly string[] Ordinals =
        {
            "一", "二", "三", "四", "五", "六", "七", "八", "九", "十"
        };

        /// <summary>
        /// 往报告里加一段，**序号自动发**，正文是空的就整段不加。
        ///
        /// 序号不能写死在小标题里：有几段是条件出现的（本班没有规律性报警，
        /// 「有没有规律性的」那段就不该出现），写死的话读者会看到
        /// 「三、」后面直接跟「五、」，以为自己漏了一段或者程序出错了。
        /// 这里按实际加进去的段数发号，中间跳过谁都不影响。
        /// </summary>
        public static void Add(List<ReportSection> list, string heading, string body)
        {
            if (body == null || body.Length == 0)
            {
                return;
            }

            string number = "";
            if (list.Count < Ordinals.Length)
            {
                number = Ordinals[list.Count] + "、";
            }
            list.Add(new ReportSection(number + heading, body));
        }
    }

    /// <summary>
    /// 测试栏「AI 解读」那份报告的正文，**现拼的**。
    ///
    /// 数据来自产线上真实的 UTS log，测站叫什么、有哪些测项、哪一项出问题，
    /// **每一班都不一样**。写死一段散文，第二天就变成假话 ——
    /// 表里写着 PAIRING #13_07 的 Standby Current，报告还在讲 FCT-2，
    /// 这正是这个项目最反对的「两份真相」。
    ///
    /// 三栏里这一份是最早改成现拼的，另外两份（<see cref="AlarmReport"/>、
    /// <see cref="FastenReport"/>）曾经是 Reports.xaml 里写死的样稿，
    /// 后来照着这一份的样子也改成了现拼。
    ///
    /// 放在 Test\ 而不是界面那边，是为了让它挨着产出这些数的代码 ——
    /// 改了统计口径，顺手就能看见这份报告里哪句话要跟着改。
    /// </summary>
    public static class TestReport
    {
        public static string Title(ShiftData data)
        {
            return "测试良率解读报告　" + StationList(data);
        }

        public static string StationList(ShiftData data)
        {
            if (data.TestStations.Count == 0)
            {
                return "（没有测试 log）";
            }

            string text = "";
            for (int i = 0; i < data.TestStations.Count; i++)
            {
                if (i > 0)
                {
                    text += " / ";
                }
                text += data.TestStations[i].Name;
            }
            return text;
        }

        public static List<ReportSection> Sections(ShiftData data)
        {
            List<ReportSection> sections = new List<ReportSection>();

            if (data.TestStations.Count == 0)
            {
                sections.Add(new ReportSection("没有测试数据",
                    "本班的测试 log 一份都没读到。把测试机上的 UTS log 拷进 "
                    + data.Config.TestLogPath + "（一台机器一份），重启程序。\n\n"
                    + "**在此之前这一栏的所有数字都是空的** —— 不是良率 100%，是没有数据。"));
                return sections;
            }

            ReportSection.Add(sections, "结论", Conclusion(data));
            ReportSection.Add(sections, "测站良率", StationTable(data));
            ReportSection.Add(sections, "不良分成三类，处置完全不同", FailKinds(data));
            ReportSection.Add(sections, "能力与漂移", Capability(data));
            ReportSection.Add(sections, "建议动作", Actions(data));
            ReportSection.Add(sections, "方法与口径", Method(data));
            return sections;
        }

        // =================================================================

        private static string Conclusion(ShiftData data)
        {
            StringBuilder s = new StringBuilder();

            int total = data.TestTotal();
            double yield = data.LineYield();
            double delta = data.LineVsBaseline();

            s.Append("整线良率 **" + yield.ToString("F2") + "%**（"
                   + total.ToString("N0") + " 笔中不良 " + data.TestNg() + " 笔），"
                   + "对比基线 **" + Format.Delta(delta, 2) + "**。"
                   + "整线良率是按各站测试数**加权**得到的，不是几个站的良率取平均。\n\n");

            TestItemRow? worst = data.WorstItem();
            if (worst != null && worst.YieldLoss(total) > 0)
            {
                double loss = worst.YieldLoss(total);
                s.Append("拖累最大的是 **" + worst.Station + " 的 " + worst.Name
                       + "，一项就占了 " + loss.ToString("F2") + " 点**"
                       + "（本班判 " + worst.Ng + " 笔，基线 " + worst.BaselineNg + " 笔）。\n\n");

                if (delta >= 0)
                {
                    s.Append("整线对比基线还是正的，所以这一项的损失被别的项目补回来了。");
                }
                else if (loss > -delta)
                {
                    // 单项损失比整线净劣化还大，说明别的项目本班比基线好。
                    // 这时候算「占百分之多少」会算出 141% 这种话，不能这么写。
                    s.Append("注意这一项的损失（" + loss.ToString("F2")
                           + " 点）**比整线的净劣化（" + (-delta).ToString("F2")
                           + " 点）还大** —— 意思是其余项目本班反而比基线好一些，"
                           + "把它的一部分补回来了。别因为整线只掉了 " + (-delta).ToString("F2")
                           + " 点就觉得问题不大。");
                }
                else
                {
                    double share = loss / -delta * 100;
                    s.Append("占全部劣化的 **" + share.ToString("F0") + "%**。");
                    if (share > 70)
                    {
                        s.Append("**这不是整体性劣化，是单点问题，只该派一个人。**");
                    }
                }
            }
            else
            {
                s.Append("没有哪一项对良率造成明显拖累。");
            }
            return s.ToString();
        }

        private static string StationTable(ShiftData data)
        {
            StringBuilder s = new StringBuilder();
            double gate = data.Config.YieldGate;

            int broken = 0;
            for (int i = 0; i < data.TestStations.Count; i++)
            {
                if (data.TestStations[i].Yield < gate)
                {
                    broken++;
                }
            }

            s.Append("门槛 " + gate.ToString("F1") + "%，");
            if (broken == 0)
            {
                s.Append("**没有站破门槛**。\n\n");
            }
            else
            {
                s.Append("**" + broken + " 个站破门槛**。\n\n");
            }

            for (int i = 0; i < data.TestStations.Count; i++)
            {
                TestStationRow station = data.TestStations[i];
                s.Append("· " + station.Name + "　" + station.Yield.ToString("F2") + "%"
                       + "（" + station.Tested.ToString("N0") + " 笔，不良 " + station.Ng + "）"
                       + "　基线 " + station.Baseline.ToString("F2") + "%"
                       + "　" + Format.Delta(station.VsBaseline));

                if (station.Yield < gate)
                {
                    s.Append("　**已破门槛**");
                }
                s.Append("\n");
            }
            return s.ToString();
        }

        private static string FailKinds(ShiftData data)
        {
            int over = data.TestOverSpec();
            int func = data.TestFuncFail();
            int unknown = data.TestNg() - over - func;

            StringBuilder s = new StringBuilder();
            s.Append("本班 " + data.TestNg() + " 笔不良：**超规 " + over
                   + " 笔 · 功能失败 " + func + " 笔 · 未知异常 " + unknown + " 笔**。\n\n");

            s.Append("· **超规** —— 量到数字了，只是超出 U / L。这一项能算能力指数，查的是参数漂移。\n");
            s.Append("· **功能失败** —— FailValue 是空的，根本没量到值（通讯 / 无回应 / 超时）。"
                   + "没有数字可做 Cpk，查的是线材、连接器、超时设定。\n");
            s.Append("· **未知异常** —— ErrorCode 是 E-00000 或者在 E 行里查不到，"
                   + "UTS 没说是哪一项，只能翻 log。\n\n");

            s.Append("**三类合并成一个「NG 数」就查不清了** —— 处置动作完全不同，"
                   + "并案会让每一边都查不到底。");
            return s.ToString();
        }

        private static string Capability(ShiftData data)
        {
            StringBuilder s = new StringBuilder();

            s.Append("Cpk 一律用**组内标准差 σ内**（相邻两笔的平均移动全距 ÷ 1.128），"
                   + "代表设备短期、不受漂移影响时的能力。"
                   + "**σ总只用来判漂移，不产出任何指标** —— σ总 / σ内 超过 "
                   + data.Config.DriftRatio.ToString("F1") + " 就判过程在漂。\n\n");

            for (int i = 0; i < data.TestItems.Count; i++)
            {
                TestItemRow item = data.TestItems[i];
                s.Append("· **" + item.Station + " " + item.Name + "**　");

                if (!item.CanCpk)
                {
                    s.Append("不可算 —— " + item.NotComputableReason);
                    if (item.Ng > 0)
                    {
                        s.Append("。本班判 " + item.Ng + " 笔" + item.NgKind);
                    }
                    s.Append("\n");
                    continue;
                }

                s.Append("Cpk " + Format.Cpk(item.Cpk, data.Config.CpkAbsurd)
                       + "　μ=" + item.MeanText + "　σ内=" + item.SigmaInText
                       + "　规格 " + item.SpecText);

                if (item.IsSpecTooWide)
                {
                    s.Append("　**规格过宽**，Cpk 超过 " + data.Config.CpkAbsurd.ToString("F0")
                           + " 已经没有参考意义，它不代表这台机很棒，只代表规格开得太松，"
                           + "要检讨的是规格设定");
                }
                else if (item.Cpk < data.Config.CpkCrit)
                {
                    s.Append("　**低于 " + data.Config.CpkCrit.ToString("F2") + "**");
                }
                else if (item.Cpk < data.Config.CpkWarn)
                {
                    s.Append("　低于 " + data.Config.CpkWarn.ToString("F2"));
                }

                if (item.IsDrifting)
                {
                    s.Append("　**σ总/σ内 = " + item.Drift.ToString("F2") + "，过程在漂**"
                           + "：设备短期能力不差，是整批从头到尾在动。"
                           + "要找的是漂移源（温升、老化、供电），不是去改设定值");
                }

                if (item.Ng > 0)
                {
                    s.Append("　本班判 " + item.Ng + " 笔" + item.NgKind
                           + "（基线 " + item.BaselineNg + "）");
                }
                s.Append("\n");
            }
            return s.ToString();
        }

        private static string Actions(ShiftData data)
        {
            StringBuilder s = new StringBuilder();
            int total = data.TestTotal();
            int n = 0;

            TestItemRow? worst = data.WorstItem();
            if (worst != null && worst.YieldLoss(total) > 0)
            {
                n++;
                s.Append(n + ". **" + worst.Station + " " + worst.Name + "** —— "
                       + "按 SeqNum 原始顺序调出本班全部实测值看漂移形态："
                       + "**单调上升**查温升与老化，**分层跳变**就对时间戳找换治具 / 换批 / 换班的断点。"
                       + "这一项占本班良率损失的大头，其余可以往后排。\n");
            }

            for (int i = 0; i < data.TestItems.Count; i++)
            {
                TestItemRow item = data.TestItems[i];
                if (item.NgKind != TestItemRow.FunctionFailure)
                {
                    continue;
                }
                n++;
                s.Append(n + ". **" + item.Station + " " + item.Name + " " + item.Ng
                       + " 笔功能失败** —— 无测值，查线材、连接器接触与超时设定。"
                       + "**和上面那条不要并案**，并案会让两边都查不清。\n");
            }

            for (int i = 0; i < data.TestItems.Count; i++)
            {
                TestItemRow item = data.TestItems[i];
                if (!item.IsDrifting || item.Ng > 0)
                {
                    continue;
                }
                n++;
                s.Append(n + ". **" + item.Station + " " + item.Name
                       + " 本班零不良，但 σ总/σ内 = " + item.Drift.ToString("F2")
                       + " 已越线** —— 光看 Cpk（" + Format.Cpk(item.Cpk, data.Config.CpkAbsurd)
                       + "）什么问题都没有，这是提前预警不是现在的问题。\n");
            }

            if (n == 0)
            {
                return "本班测试没有需要立刻处置的项。";
            }
            return s.ToString();
        }

        private static string Method(ShiftData data)
        {
            StringBuilder s = new StringBuilder();

            int all = data.TestTotalItems;
            int can = data.TestCpkCapable;

            s.Append("**数据来源**　" + data.Config.TestLogPath + " 下的原始 UTS log，"
                   + "一台测试机一份。本班 " + data.TestStations.Count + " 份，"
                   + "按 Test_Start_Time 裁到 " + data.Window.StartText + "–" + data.Window.EndText
                   + " 这个窗口。整份 log 覆盖的是一整天，班外那部分不算在本班里。\n\n");

            s.Append("**测项覆盖**　" + all + " 项中 " + can + " 项可算 Cpk。"
                   + "不可算 " + (all - can) + " 项，原因已分类：单边等于判定 "
                   + data.TestSingleSided + " 项（U 空、L 是期望值，"
                   + "如版本号 / 长度 / 旗标）、离散量 "
                   + data.TestDiscrete + " 项（不重复值 < 5）、σ = 0 共 "
                   + data.TestZeroSigma + " 项、无数值规格 "
                   + data.TestNoLimits + " 项。"
                   + "**这些不是漏算，是不该算**，不进分母。\n\n");

            s.Append("**算法**　σ内 = MR̄ / 1.128（I-MR 管制图，子组大小 2 的 d2 常数）。"
                   + "MR 吃顺序，全程使用 log 的 SeqNum 原始行序，未做任何重排 —— "
                   + "顺序一乱 σ内 就是错的。σ总 = 样本标准差，只用于判漂移。\n\n");

            s.Append("**程式重启**　一份 log 里程式重启过的话，会切成好几段，"
                   + "每段自带一套 U / L / E。从最后一段往前比，"
                   + "**限值不一致的那些段整段舍弃** —— 程式改过了，混着算就是错的。"
                   + "舍弃了多少会报在看板标题栏上，不会悄悄丢。");

            return s.ToString();
        }
    }
}

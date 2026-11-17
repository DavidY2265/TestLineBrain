using System;
using System.Collections.Generic;

namespace DesktopPet.LineBrain
{
    // =====================================================================
    //  枚举 —— 判定用它，显示走 StatusText
    // =====================================================================

    /// <summary>看板的三栏。</summary>
    public enum BoardTab
    {
        Equipment,      // 设备报警
        Quality,        // 锁附工艺
        Test,           // 测试
    }

    /// <summary>报警汇总表按什么排。</summary>
    public enum AlarmSortKey
    {
        Duration,       // 按累计时长
        Count,          // 按次数
        Station,        // 按站别
    }

    /// <summary>
    /// 警告的轻重。**只有两档是故意的** —— 三档以上人就开始纠结
    /// 「这算中还是算高」，而这个判断对「现在要不要去处理」没有帮助。
    /// </summary>
    public enum AlertLevel
    {
        Watch,          // 关注：还有时间，排进计划
        Critical,       // 紧急：现在就得处理
    }

    /// <summary>锁附一行的状态。</summary>
    public enum CapabilityStatus
    {
        Normal,
        Watch,
        Urgent,
    }

    /// <summary>测项的状态。比锁附多两档，因为测试有「规格开太松」这种情况。</summary>
    public enum TestItemStatus
    {
        Good,           // 优秀
        Pass,           // 合格
        NeedsWork,      // 需改善
        Failing,        // 不合格
        Urgent,         // 紧急（功能失败，根本没量到值）
        SpecTooWide,    // 规格过宽，Cpk 已经没有参考意义
        NotComputable,  // 不可算
    }

    /// <summary>
    /// 规格是单边还是双边。这个区别很要紧：双边的叫 Cpk，单边上限的叫 CPU，
    /// 名字不一样，不能混着说。
    /// </summary>
    public enum SpecSide
    {
        TwoSided,
        UpperOnly,
        LowerOnly,
        Discrete,       // 离散量，不出能力指数
    }

    /// <summary>小精灵说话的优先级。</summary>
    public enum SpeakPriority
    {
        Normal,         // 3 秒内说过话就闭嘴，防刷屏
        Force,          // 必定说（点击、睡醒、报警）
    }

    /// <summary>气泡长什么样。</summary>
    public enum BubbleStyle
    {
        Chat,           // 闲聊：橙色描边
        Alert,          // 报警：红色描边，加「本班警告」和「点我看 AI 解析」两行
    }

    /// <summary>
    /// 枚举 → 界面上显示的中文。
    ///
    /// **判定和显示必须分开。** 要是让 Status 直接返回 "紧急" 这个中文串，
    /// 代码里就会到处 `if (status == "紧急")`；哪天想把文案改成「立即处理」，
    /// 一改就把判定全改坏，而且编译器一声不吭。
    /// 现在判定认枚举，显示走这里，改文案动不了逻辑。
    /// </summary>
    public static class StatusText
    {
        public static string Of(CapabilityStatus status)
        {
            switch (status)
            {
                case CapabilityStatus.Urgent: return "紧急";
                case CapabilityStatus.Watch: return "关注";
                default: return "正常";
            }
        }

        public static string Of(TestItemStatus status)
        {
            switch (status)
            {
                case TestItemStatus.Good: return "优秀";
                case TestItemStatus.Pass: return "合格";
                case TestItemStatus.NeedsWork: return "需改善";
                case TestItemStatus.Failing: return "不合格";
                case TestItemStatus.Urgent: return "紧急";
                case TestItemStatus.SpecTooWide: return "规格过宽";
                default: return "不可算";
            }
        }

        public static string Of(SpecSide side)
        {
            switch (side)
            {
                case SpecSide.TwoSided: return "双边";
                case SpecSide.UpperOnly: return "单边 U";
                case SpecSide.LowerOnly: return "单边 L";
                default: return "离散";
            }
        }

        public static string Of(AlertLevel level)
        {
            if (level == AlertLevel.Critical)
            {
                return "紧急";
            }
            return "关注";
        }

        public static string Of(BoardTab tab)
        {
            switch (tab)
            {
                case BoardTab.Quality: return "锁附工艺";
                case BoardTab.Test: return "测试";
                default: return "设备报警";
            }
        }
    }

    // =====================================================================
    //  报警
    // =====================================================================

    /// <summary>数据库里一行原始报警记录。查出来什么样就是什么样，不做任何加工。</summary>
    public sealed class AlarmRecord
    {
        public string Station = "";        // device_id，一号机…六号机
        public string Message = "";        // alarm_msg
        public DateTime Start;             // insert_time，报警发生
        public DateTime End;               // recover_time，恢复
        public bool NeverRecovered;        // recover_time 是空的
        public string Line = "";           // work_id 里切出来的产线
    }

    /// <summary>一次报警发生：开班后第几分钟起、持续几分钟。</summary>
    public struct AlarmSpan
    {
        public double Start;
        public double Duration;

        public AlarmSpan(double start, double duration)
        {
            Start = start;
            Duration = duration;
        }

        public double End
        {
            get { return Start + Duration; }
        }
    }

    /// <summary>
    /// 一类报警 ——「站别 + 报警内容」相同的若干次发生，聚在一起。
    ///
    /// **数据库里是一行一次发生，不是一行一类。** 聚类在 AlarmAnalyzer 里做。
    /// 下面的 Count / Total / Each / Starts **全是算出来的**，没有一个是填的字段。
    /// 改一条发生记录，表里的次数、时长、占比、时序图位置会一起变。
    /// </summary>
    public sealed class AlarmGroup
    {
        public int Index;
        public string Station = "";
        public string Name = "";

        /// <summary>
        /// 每一次发生。**每次时长可以不一样** —— 真实报警本来就不是每次都停一样久。
        /// </summary>
        public readonly List<AlarmSpan> Occurrences = new List<AlarmSpan>();

        /// <summary>通讯断连。设备状态未知，既不算运行也不算停机，分子分母里一起剔除。</summary>
        public bool IsNoData;

        public int Count
        {
            get { return Occurrences.Count; }
        }

        public double Total
        {
            get
            {
                double sum = 0;
                for (int i = 0; i < Occurrences.Count; i++)
                {
                    sum += Occurrences[i].Duration;
                }
                return sum;
            }
        }

        /// <summary>单次**平均**时长。表里「单次均值」那一列就是它。</summary>
        public double Each
        {
            get
            {
                if (Occurrences.Count == 0)
                {
                    return 0;
                }
                return Total / Occurrences.Count;
            }
        }

        /// <summary>每次发生的时刻（开班后第几分钟），按先后排好。</summary>
        public double[] Starts
        {
            get
            {
                double[] starts = new double[Occurrences.Count];
                for (int i = 0; i < Occurrences.Count; i++)
                {
                    starts[i] = Occurrences[i].Start;
                }
                return starts;
            }
        }

        /// <summary>
        /// 相邻两次的平均间隔。规律性判据：某条报警每 40–55 分钟准时犯一回，
        /// 这种节奏不是操作波动，是机构到点就出问题。
        /// </summary>
        public double MeanGap
        {
            get
            {
                if (Occurrences.Count < 2)
                {
                    return 0;
                }
                double first = Occurrences[0].Start;
                double last = Occurrences[Occurrences.Count - 1].Start;
                return (last - first) / (Occurrences.Count - 1);
            }
        }

        public void Add(double startMinute, double durationMinute)
        {
            Occurrences.Add(new AlarmSpan(startMinute, durationMinute));
        }

        /// <summary>按时刻排序。间隔判断依赖顺序。</summary>
        public void SortByTime()
        {
            Occurrences.Sort(CompareStart);
        }

        private static int CompareStart(AlarmSpan a, AlarmSpan b)
        {
            return a.Start.CompareTo(b.Start);
        }
    }

    // =====================================================================
    //  螺丝锁附
    // =====================================================================

    /// <summary>
    /// 一次锁附的一个量：拧完这颗螺丝，扭力/浮高/圈数各量到多少。
    ///
    /// **判定是 PLC 自己给的，不是程序按规格算的。** 这一条很要紧：
    /// PLC 的判定门槛未必等于图纸规格（现场调过、或者程式里写死的是另一套）。
    /// 如果程序自己按规格推判定，这个不一致就永远暴露不出来 ——
    /// 而它恰恰是「实测越线笔数和正态外推对不上」这类问题的根源。
    /// </summary>
    public sealed class FastenSample
    {
        public string Station = "";
        public string Hole = "";
        public string Measure = "";        // 扭力 / 浮高 / 圈数
        public double Value;
        public bool IsNg;                  // PLC 的判定
    }

    /// <summary>
    /// 一个孔位的一个量。
    ///
    /// **均值、σ、能力指数、不良笔数、状态，一个都不存，全是从 Samples 算的。**
    /// 存了就会出现「表里 1.08、AI 说 1.1」这种两份数打架的事。
    /// </summary>
    public sealed class FastenRow
    {
        /// <summary>
        /// 阈值从这里读。构造时必须给 —— 不给就没法判「关注」还是「紧急」，
        /// 做成构造参数是为了让人忘不掉。
        /// </summary>
        public readonly BrainConfig Config;

        public FastenRow(BrainConfig config)
        {
            Config = config;
        }

        // ---- 规格 ----
        public string Station = "";
        public string Hole = "";
        public string Measure = "";
        public string Address = "";        // PLC 点位
        public string SpecText = "";
        public double Lsl;
        public double Usl;
        public bool HasLsl;
        public bool HasUsl;
        public bool CanCpk;                // 离散量（圈数）为 false

        /// <summary>
        /// 显示小数位 = 量测分辨率，属于规格的一部分，不该由代码猜 ——
        /// 0.0128 和 31.8 的有效位数差三档，任何「按数量级推断」都会在某一行猜错。
        /// </summary>
        public int MeanDigits = 2;
        public int SigmaDigits = 3;

        // ---- 实测 ----
        public readonly List<FastenSample> Samples = new List<FastenSample>();

        /// <summary>近 8 班趋势的键。由「工位|孔位|量」拼出来，不从数据里读。</summary>
        public string TrendKey
        {
            get { return Station + "|" + Hole + "|" + Measure; }
        }

        public string Title
        {
            get { return Station + "-" + Hole + " " + Measure; }
        }

        public int SampleCount
        {
            get { return Samples.Count; }
        }

        public double Mean
        {
            get
            {
                if (Samples.Count == 0)
                {
                    return 0;
                }
                double sum = 0;
                for (int i = 0; i < Samples.Count; i++)
                {
                    sum += Samples[i].Value;
                }
                return sum / Samples.Count;
            }
        }

        /// <summary>
        /// 样本标准差（除以 n−1）。这批锁附记录是从产线抽出来的样本不是全体，
        /// 所以用样本公式。
        /// </summary>
        public double Sigma
        {
            get
            {
                if (Samples.Count < 2)
                {
                    return 0;
                }

                double mean = Mean;
                double squares = 0;
                for (int i = 0; i < Samples.Count; i++)
                {
                    double diff = Samples[i].Value - mean;
                    squares += diff * diff;
                }
                return Math.Sqrt(squares / (Samples.Count - 1));
            }
        }

        /// <summary>
        /// PLC 判 NG 的笔数。**不是程序按规格数出来的** —— 两者对不上恰恰是要查的问题。
        /// </summary>
        public int NgCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Samples.Count; i++)
                {
                    if (Samples[i].IsNg)
                    {
                        n++;
                    }
                }
                return n;
            }
        }

        public string MeanText
        {
            get { return Mean.ToString("F" + MeanDigits); }
        }

        public string SigmaText
        {
            get { return Sigma.ToString("F" + SigmaDigits); }
        }

        public double Cpu
        {
            get { return CpkCalculator.Upper(Mean, Sigma, Usl, HasUsl); }
        }

        public double Cpl
        {
            get { return CpkCalculator.Lower(Mean, Sigma, Lsl, HasLsl); }
        }

        public double Cpk
        {
            get
            {
                if (!CanCpk)
                {
                    return double.NaN;
                }
                return CpkCalculator.Index(Mean, Sigma, Lsl, Usl, HasLsl, HasUsl);
            }
        }

        /// <summary>表里「边」那一列。由有没有上下限推出来，不是填的。</summary>
        public SpecSide Side
        {
            get
            {
                if (!CanCpk)
                {
                    return SpecSide.Discrete;
                }
                if (HasLsl && HasUsl)
                {
                    return SpecSide.TwoSided;
                }
                if (HasUsl)
                {
                    return SpecSide.UpperOnly;
                }
                return SpecSide.LowerOnly;
            }
        }

        /// <summary>离散量看 NG 笔数，其余看能力指数踩了哪条线。</summary>
        public CapabilityStatus Status
        {
            get
            {
                if (!CanCpk)
                {
                    if (NgCount > 0)
                    {
                        return CapabilityStatus.Urgent;
                    }
                    return CapabilityStatus.Normal;
                }

                double capability = Cpk;
                if (capability < Config.CpkCrit)
                {
                    return CapabilityStatus.Urgent;
                }
                if (capability < Config.CpkWarn)
                {
                    return CapabilityStatus.Watch;
                }
                return CapabilityStatus.Normal;
            }
        }
    }

    // =====================================================================
    //  测试
    // =====================================================================

    /// <summary>一个测试站的良率。NG 和良率都是算的。</summary>
    public sealed class TestStationRow
    {
        public string Name = "";
        public int Tested;
        public int OverSpec;               // 超规：有测值
        public int FuncFail;               // 功能失败：根本没量到值
        public int Unknown;                // 未知异常：ErrorCode 查不到对应列
        public double Baseline;            // 同班次前七日良率 %

        public int Ng
        {
            get { return OverSpec + FuncFail + Unknown; }
        }

        public double Yield
        {
            get
            {
                if (Tested <= 0)
                {
                    return 0;
                }
                return (Tested - Ng) / (double)Tested * 100.0;
            }
        }

        public double VsBaseline
        {
            get { return Yield - Baseline; }
        }
    }

    /// <summary>
    /// 一个测项。
    ///
    /// Cpk 用组内标准差，漂移比用组内和总体两个标准差的比 ——
    /// **这是两套判据，任何一项都不得只报其中一个。**
    /// </summary>
    public sealed class TestItemRow
    {
        public const string FunctionFailure = "功能失败";

        public readonly BrainConfig Config;

        public TestItemRow(BrainConfig config)
        {
            Config = config;
        }

        public string Station = "";
        public string Name = "";
        public int Ng;
        public string NgKind = "";         // 超规 / 功能失败 / 空
        public int BaselineNg;             // 同班次前七日这一项平均判几笔

        public double Mean;
        public double SigmaIn;             // 组内，算能力用
        public double SigmaAll;            // 总体，只用来判漂移，不产出任何指标

        // 显示用原文，位数即分辨率
        public string MeanText = "";
        public string SigmaInText = "";
        public string SigmaAllText = "";
        public string SpecText = "";

        public double Lsl;
        public double Usl;
        public bool HasLsl;
        public bool HasUsl;
        public bool CanCpk;

        /// <summary>算不出能力指数的话，为什么。五种原因对应五种处置。</summary>
        public string NotComputableReason = "";

        /// <summary>近 8 班趋势的键，由「测站|测项」拼出来。</summary>
        public string TrendKey
        {
            get { return Station + "|" + Name; }
        }

        public double Cpu
        {
            get { return CpkCalculator.Upper(Mean, SigmaIn, Usl, HasUsl); }
        }

        public double Cpl
        {
            get { return CpkCalculator.Lower(Mean, SigmaIn, Lsl, HasLsl); }
        }

        public double Cpk
        {
            get
            {
                if (!CanCpk)
                {
                    return double.NaN;
                }
                return CpkCalculator.Index(Mean, SigmaIn, Lsl, Usl, HasLsl, HasUsl);
            }
        }

        /// <summary>σ总 / σ内。超过门槛说明整批在漂，不是设备短期能力不行。</summary>
        public double Drift
        {
            get
            {
                if (SigmaIn <= 0 || SigmaAll <= 0)
                {
                    return double.NaN;
                }
                return SigmaAll / SigmaIn;
            }
        }

        public bool IsDrifting
        {
            get
            {
                double d = Drift;
                return !double.IsNaN(d) && d > Config.DriftRatio;
            }
        }

        /// <summary>规格开太松的时候，Cpk 这个数本身就没有参考意义了。</summary>
        public bool IsSpecTooWide
        {
            get
            {
                double c = Cpk;
                return !double.IsNaN(c) && c > Config.CpkAbsurd;
            }
        }

        public TestItemStatus Status
        {
            get
            {
                if (NgKind == FunctionFailure)
                {
                    return TestItemStatus.Urgent;
                }
                if (IsSpecTooWide)
                {
                    return TestItemStatus.SpecTooWide;
                }
                if (!CanCpk)
                {
                    return TestItemStatus.NotComputable;
                }

                double capability = Cpk;
                if (capability < Config.CpkCrit)
                {
                    return TestItemStatus.Failing;
                }
                if (capability < Config.CpkWarn)
                {
                    return TestItemStatus.NeedsWork;
                }
                if (capability >= Config.CpkGood)
                {
                    return TestItemStatus.Good;
                }
                return TestItemStatus.Pass;
            }
        }

        /// <summary>
        /// 这一项让整线良率掉了几个点。分母是**整线**测试数不是本站的 ——
        /// 一项拖累的是整条线的良率。
        /// </summary>
        public double YieldLoss(int lineTested)
        {
            if (lineTested <= 0)
            {
                return 0;
            }
            return (Ng - BaselineNg) / (double)lineTested * 100.0;
        }
    }

    // =====================================================================
    //  警告
    // =====================================================================

    /// <summary>
    /// 一条警告。
    ///
    /// Short 是小精灵气泡里那一句，必须一行读得完 —— 它的任务是让人决定要不要点开，
    /// 不是把事情说清楚。说清楚是 What / How / Why / Do 四段的事。
    /// </summary>
    public sealed class Alert
    {
        public AlertLevel Level = AlertLevel.Watch;
        public BoardTab Topic = BoardTab.Equipment;
        public string Subject = "";
        public string Short = "";

        /// <summary>
        /// 超线幅度，排队用。
        ///
        /// 必须是**归一化的比例**（超出门槛多少 ÷ 门槛），不能是原始计数。
        /// 「4 笔不良」和「Cpk 低了 0.34」量纲差得远，直接比大小毫无意义，
        /// 前者会仅仅因为数字大就永远排最前面。
        /// </summary>
        public double Severity;

        public string What = "";
        public string How = "";
        public string Why = "";
        public string Do = "";

        public bool IsCrit
        {
            get { return Level == AlertLevel.Critical; }
        }
    }

    // =====================================================================
    //  数字怎么写成界面上那样
    // =====================================================================

    /// <summary>
    /// 纯排版，不做任何计算。单独放这儿是因为界面、报告、离线问答、
    /// 发给模型的事实清单都要用同一套写法 —— 同一个数在四个地方长得不一样，
    /// 人会以为是四个不同的数。
    /// </summary>
    public static class Format
    {
        /// <summary>
        /// 占比，百分数。分母是 0 就返回 0 —— 这个守卫必须有：
        /// 本班一条报警都没有时整线停机就是 0，除下去得到 NaN，
        /// 然后 NaN 一路传到界面上显示成「非数字%」。
        /// </summary>
        public static double Percent(double part, double whole)
        {
            if (whole <= 0)
            {
                return 0;
            }
            return part / whole * 100;
        }

        /// <summary>累计时长：10 分钟以上不要小数，以下留一位。</summary>
        public static string Duration(double minutes)
        {
            if (minutes >= 10)
            {
                return minutes.ToString("F0");
            }
            return minutes.ToString("F1");
        }

        /// <summary>单次均值写成 3m30s 这种，19 秒和 3 分半一眼能分开。</summary>
        public static string Each(double minutes)
        {
            int total = (int)Math.Round(minutes * 60);
            return (total / 60) + "m" + (total % 60).ToString("00") + "s";
        }

        /// <summary>能力指数。离散量之类算不出来的画成「不适用」。</summary>
        public static string Cpk(double value, double absurdGate)
        {
            if (double.IsNaN(value))
            {
                return "不适用";
            }
            if (value > absurdGate)
            {
                return value.ToString("F0");    // 317 这种数写成 317.00 反而更假
            }
            return value.ToString("F2");
        }

        /// <summary>
        /// 对比基线那一列，正数要带加号，不然看不出是好了还是坏了。
        ///
        /// digits 默认 1 位。整线那一行要传 2 —— 它是几个站加权出来的，
        /// −0.95 写成 −1.0 会让人以为整整掉了一个点，
        /// 而下面「某一项贡献 0.92」就接不上了。
        /// </summary>
        public static string Delta(double points, int digits = 1)
        {
            string sign = "−";
            if (points >= 0)
            {
                sign = "+";
            }
            return sign + Math.Abs(points).ToString("F" + digits) + " pt";
        }
    }
}

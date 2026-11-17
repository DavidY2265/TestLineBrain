using System;
using System.Collections.Generic;

namespace DesktopPet.LineBrain
{
    /// <summary>这一列为什么算不出能力指数。<see cref="Ok"/> 就是能算。</summary>
    public enum CpkGate
    {
        Ok,
        NoNumericLimits,      // U 和 L 都不是数字，这一列压根没有数值规格
        SingleSidedEquals,    // 只有 L 是数字 —— 那是「等于判定」，不是下限
        Discrete,             // 不重复值 < 5，离散量不是连续量测
        ZeroSigma,            // 所有实测值一模一样，σ = 0
        TooFewSamples         // 不足两笔，算不了标准差
    }

    /// <summary>
    /// 一列数算完的结果。
    ///
    /// Cpk、σ、中心值这些都在这儿一次给全，调用方不用再算 —— 一算就会出现
    /// 「表里 1.08、报告里 1.1」这种两份数打架的事。
    /// </summary>
    public sealed class CpkResult
    {
        public CpkGate Gate = CpkGate.NoNumericLimits;

        public int Count;                 // 有效读数几笔
        public double Mean;               // 过程均值
        public double SigmaWithin;        // 组内标准差，Cpk 用它
        public double SigmaOverall;       // 总体标准差，只用来判漂移

        public bool HasUpper;
        public bool HasLower;
        public double Upper;
        public double Lower;

        /// <summary>原始文字里出现过的最大小数位 = 量具的分辨率，界面按它显示。</summary>
        public int ValueDigits;

        public bool CanCompute
        {
            get { return Gate == CpkGate.Ok; }
        }

        /// <summary>
        /// **规格中心值 (U + L) / 2。**
        ///
        /// 有什么用：和过程均值一比，就知道「偏了多少、往哪边偏」。
        /// Cpk 低有两种原因 —— 均值偏离中心，或者分布太宽。
        /// 光看 Cpk 分不出是哪一种，而这两种的处置完全不同：
        /// 偏心校准一次就能拉回，分布宽不行，那是磨损或松动。
        /// 单边规格没有中心值，返回 NaN。
        /// </summary>
        public double SpecCenter
        {
            get
            {
                if (!HasUpper || !HasLower)
                {
                    return double.NaN;
                }
                return (Upper + Lower) / 2;
            }
        }

        /// <summary>过程均值偏离规格中心多少。正数偏上，负数偏下。</summary>
        public double MeanOffset
        {
            get
            {
                double center = SpecCenter;
                if (double.IsNaN(center))
                {
                    return double.NaN;
                }
                return Mean - center;
            }
        }

        public double Cpu
        {
            get { return CpkCalculator.Upper(Mean, SigmaWithin, Upper, HasUpper); }
        }

        public double Cpl
        {
            get { return CpkCalculator.Lower(Mean, SigmaWithin, Lower, HasLower); }
        }

        public double Cpk
        {
            get
            {
                if (!CanCompute)
                {
                    return double.NaN;
                }
                return CpkCalculator.Index(Mean, SigmaWithin, Lower, Upper, HasLower, HasUpper);
            }
        }

        /// <summary>σ总 / σ内。超过门槛说明整批在漂，不是设备短期能力不行。</summary>
        public double DriftRatio
        {
            get
            {
                if (SigmaWithin <= 0 || SigmaOverall <= 0)
                {
                    return double.NaN;
                }
                return SigmaOverall / SigmaWithin;
            }
        }

        /// <summary>算不出来时给人看的一句话。五种原因对应五种处置，不能笼统写「不可算」。</summary>
        public string GateText()
        {
            if (Gate == CpkGate.Ok)
            {
                return "";
            }
            if (Gate == CpkGate.SingleSidedEquals)
            {
                return "只有 L 没有 U。这种列的 L 是「期望值」不是数值下限，"
                     + "属于等于判定（版本号 / 长度 / 旗标 / 状态码），做 Cpk 没有意义";
            }
            if (Gate == CpkGate.Discrete)
            {
                return "不重复值少于 5 个，是离散量（旗标 / 计数）不是连续量测，做 Cpk 没有意义";
            }
            if (Gate == CpkGate.ZeroSigma)
            {
                return "所有实测值完全相同，σ = 0，Cpk 除不出来";
            }
            if (Gate == CpkGate.TooFewSamples)
            {
                return "本班这一列不足两笔有效读数，算不了标准差";
            }
            return "U 和 L 都不是数字，这一列没有数值规格。"
                 + "判不良多半是 FailValue 为空 —— 根本没量到值（通讯 / 无回应 / 超时）";
        }
    }

    /// <summary>
    /// 制程能力计算。**全项目算 Cpk 只走这一处。**
    ///
    /// 把一列实测值和上下限交给它，回来的是均值、两个标准差、Cpk、规格中心值，
    /// 以及「这一列该不该算」的判定。
    ///
    /// ------------------------------------------------------------
    /// 用哪个标准差 —— 这一条决定数字大小，别搞错
    /// ------------------------------------------------------------
    ///   Cpk 用**组内标准差** σ内 = MR̄ / 1.128，只看相邻两笔的差，
    ///   代表设备在短期、不受漂移影响时的能力。这是教科书定义。
    ///
    ///   另外算一个**总体标准差** σ总（全部数据的离散），但它**不产出任何指标**，
    ///   只有一个用途：判断有没有漂移。
    ///
    ///     假设前 200 台都量到 44.5，后 200 台都量到 45.5。
    ///     相邻两笔差异很小 → σ内 很小 → Cpk 很漂亮。
    ///     但整批从头到尾偏移了一大截 → σ总 很大。
    ///     **σ总 明显大于 σ内 = 设备本身稳，但过程在漂。**
    ///
    ///   只看 Cpk 会漏掉这件事，还以为没问题。
    ///
    /// ------------------------------------------------------------
    /// σ内 吃顺序
    /// ------------------------------------------------------------
    /// 移动全距算的是**相邻两笔**的差，所以顺序不能动。传进来的值必须是
    /// log 里的原始行序（SeqNum 顺序）。谁要是先排个序再传进来，σ内 就是错的，
    /// 而且**错得很好看** —— 排完序相邻差变小，Cpk 反而更漂亮。
    ///
    /// ------------------------------------------------------------
    /// 三道闸门（对着真实 UTS log 验出来的，不是拍脑袋定的）
    /// ------------------------------------------------------------
    /// 1. U 和 L 都得是数字。「U 空 + L 是数字」的那种，L 是**期望值**不是下限：
    ///        QS_USB_Exist   L=0        要等于 0，量到 1 就不良，不是「至少 0」
    ///        Bincode_Length L=8        长度必须是 8
    ///    实测扫过：这样的列全是版本号 / 长度 / 旗标 / 状态码，没有一列是连续量测。
    /// 2. 不重复值 ≥ 5。万一有列漏网（U/L 都填了数字其实是离散判定），再挡一次。
    /// 3. σ > 0。
    /// </summary>
    public static class CpkCalculator
    {
        /// <summary>I-MR 管制图，子组大小为 2 时的 d2 常数。</summary>
        private const double D2ForSubgroupTwo = 1.128;

        /// <summary>不重复值少于这个数就当离散量。</summary>
        private const int MinDistinctValues = 5;

        /// <summary>小到这个程度就当 0。</summary>
        private const double AlmostZero = 0.0000000001;

        /// <summary>
        /// 主入口。
        /// </summary>
        /// <param name="values">这一列所有有效读数，**必须是原始顺序**</param>
        /// <param name="upperText">U 行的原文，不是数字就当没有上限</param>
        /// <param name="lowerText">L 行的原文</param>
        /// <param name="valueDigits">原始文字里的最大小数位，界面显示用</param>
        public static CpkResult Compute(List<double> values, string upperText, string lowerText,
                                        int valueDigits)
        {
            CpkResult result = new CpkResult();
            result.ValueDigits = valueDigits;

            double upper;
            double lower;
            result.HasUpper = CsvHelper.TryNumber(upperText, out upper);
            result.HasLower = CsvHelper.TryNumber(lowerText, out lower);
            result.Upper = upper;
            result.Lower = lower;

            if (values == null)
            {
                result.Gate = CpkGate.TooFewSamples;
                return result;
            }
            result.Count = values.Count;

            // 不足两笔就什么都不算，均值也不算 —— 一笔的「均值」不是统计量，
            // 印在表上会被当成一个有意义的数。
            if (values.Count >= 2)
            {
                result.Mean = MeanOf(values);
                result.SigmaOverall = SigmaOverallOf(values, result.Mean);
                result.SigmaWithin = SigmaWithinOf(values);
            }

            result.Gate = JudgeGate(result, values);
            return result;
        }

        private static CpkGate JudgeGate(CpkResult r, List<double> values)
        {
            if (values.Count < 2)
            {
                return CpkGate.TooFewSamples;
            }
            if (!r.HasUpper && !r.HasLower)
            {
                return CpkGate.NoNumericLimits;
            }
            if (!r.HasUpper || !r.HasLower)
            {
                return CpkGate.SingleSidedEquals;
            }
            if (CountDistinct(values) < MinDistinctValues)
            {
                return CpkGate.Discrete;
            }
            if (r.SigmaWithin < AlmostZero || r.SigmaOverall < AlmostZero)
            {
                return CpkGate.ZeroSigma;
            }
            return CpkGate.Ok;
        }

        // =================================================================
        //  公式
        // =================================================================

        /// <summary>上限那一侧，(USL − μ) / 3σ。没有上限或 σ 不是正数返回 NaN。</summary>
        public static double Upper(double mean, double sigma, double upper, bool hasUpper)
        {
            if (!hasUpper || sigma <= 0)
            {
                return double.NaN;
            }
            return (upper - mean) / (3 * sigma);
        }

        /// <summary>下限那一侧，(μ − LSL) / 3σ。</summary>
        public static double Lower(double mean, double sigma, double lower, bool hasLower)
        {
            if (!hasLower || sigma <= 0)
            {
                return double.NaN;
            }
            return (mean - lower) / (3 * sigma);
        }

        /// <summary>
        /// 两侧取小的那个。单边规格就是那一边本身。两边都没有返回 NaN。
        ///
        /// 也用来做**假设性计算** ——「拿这个孔的均值配基准孔的 σ，能力会是多少」，
        /// 用来把差距拆成「均值偏了」和「分布宽了」两部分。这两种成因的处置完全不同。
        /// </summary>
        public static double Index(double mean, double sigma,
                                   double lower, double upper, bool hasLower, bool hasUpper)
        {
            double cpu = Upper(mean, sigma, upper, hasUpper);
            double cpl = Lower(mean, sigma, lower, hasLower);

            if (double.IsNaN(cpu))
            {
                return cpl;
            }
            if (double.IsNaN(cpl))
            {
                return cpu;
            }
            return Math.Min(cpu, cpl);
        }

        // =================================================================
        //  统计
        // =================================================================

        public static double MeanOf(List<double> values)
        {
            double sum = 0;
            for (int i = 0; i < values.Count; i++)
            {
                sum += values[i];
            }
            return sum / values.Count;
        }

        /// <summary>总体（样本）标准差，除以 n−1。只用来判漂移。</summary>
        public static double SigmaOverallOf(List<double> values, double mean)
        {
            double squares = 0;
            for (int i = 0; i < values.Count; i++)
            {
                double diff = values[i] - mean;
                squares += diff * diff;
            }
            return Math.Sqrt(squares / (values.Count - 1));
        }

        /// <summary>组内标准差 = 平均移动全距 / 1.128。**顺序敏感，不能先排序。**</summary>
        public static double SigmaWithinOf(List<double> values)
        {
            double sum = 0;
            for (int i = 1; i < values.Count; i++)
            {
                double gap = values[i] - values[i - 1];
                if (gap < 0)
                {
                    gap = -gap;
                }
                sum += gap;
            }
            return sum / (values.Count - 1) / D2ForSubgroupTwo;
        }

        /// <summary>数到够 5 个就提前返回 —— 只关心够不够，不关心到底几个。</summary>
        private static int CountDistinct(List<double> values)
        {
            List<double> seen = new List<double>();

            for (int i = 0; i < values.Count; i++)
            {
                bool already = false;
                for (int j = 0; j < seen.Count; j++)
                {
                    if (seen[j] == values[i])
                    {
                        already = true;
                        break;
                    }
                }

                if (!already)
                {
                    seen.Add(values[i]);
                }
                if (seen.Count >= MinDistinctValues)
                {
                    return seen.Count;
                }
            }
            return seen.Count;
        }
    }
}

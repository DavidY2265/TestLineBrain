using System;
using System.Collections.Generic;
using System.Globalization;

namespace DesktopPet.LineBrain
{
    /// <summary>不良的三种类型。**处置动作完全不同，不能合并成一个「NG 数」。**</summary>
    public enum TestFailKind
    {
        /// <summary>超规：量到数字了，只是超出 U / L。能算能力指数，查参数漂移。</summary>
        OverSpec,

        /// <summary>功能失败：FailValue 是空的，根本没量到值。查线材、接触、超时设定。</summary>
        FunctionFailure,

        /// <summary>未知异常：ErrorCode 是 E-00000 或在 E 行里查不到。只能翻 log。</summary>
        Unknown
    }

    /// <summary>一笔不良。</summary>
    public sealed class TestFail
    {
        public TestFailKind Kind;
        public int ColumnIndex = -1;       // 反查到的列；-1 = 查不到
        public string ColumnName = "";
        public string Comment = "";        // 不良项的描述，UTS 自己写的
    }

    /// <summary>
    /// 从读进来的 log 里**判断和提取**：哪几台不良、属于哪一类、每个测项的能力如何。
    ///
    /// 和 <see cref="TestLogReader"/> 分开是因为这是两件事：
    /// 那边管「这份文件怎么切成段、哪一段有效」，这边管「这些行里有什么」。
    /// 换了 log 格式改那边，换了判读口径改这边。
    ///
    /// 算数一律交给 <see cref="CpkCalculator"/>，这里不自己算任何统计量。
    /// </summary>
    public static class TestLogExtract
    {
        private const string NoErrorCode = "E-00000";

        /// <summary>测项表最多显示几行。超过这个数，表就没法一眼看完了。</summary>
        private const int MaxItemRows = 10;

        // =================================================================
        //  判不良
        // =================================================================

        /// <summary>
        /// E 行的码 → 列号。
        ///
        /// E 行本身就是对照表：第 N 格写的码，就是第 N 列判不良时要报的码。
        /// 某一笔在第 140 列判不良，UTS 就把 E 行第 140 格的码抄进这一行的 ErrorCode。
        /// 反过来在 E 行里找这个码在第几格，就还原出是哪一列了。
        /// </summary>
        public static Dictionary<string, int> BuildCodeIndex(TestLogBlock block)
        {
            Dictionary<string, int> index = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int i = 0; i < block.ColumnCount; i++)
            {
                string code = CsvHelper.At(block.ErrorCodeRow, i).Trim();
                if (!code.StartsWith("E-", StringComparison.Ordinal))
                {
                    continue;
                }
                if (!index.ContainsKey(code))
                {
                    index[code] = i;       // 同一个码万一出现两次，认第一个
                }
            }
            return index;
        }

        /// <summary>
        /// 判一行。不是不良返回 null。
        ///
        /// 判据是 **Status == "F"**，不是「Comment 有没有内容」。
        /// 实测有 Status=F 但 Comment 为空的行（某份 260806 的 Seq 410）——
        /// 漏掉它的话「NG = 超规 + 功能失败 + 未知异常」这个恒等式就不成立，良率会虚高。
        /// </summary>
        public static TestFail? Classify(TestLogBlock block, string[] row,
                                         Dictionary<string, int> codeIndex)
        {
            if (CsvHelper.At(row, TestLogReader.StatusCell).Trim() != TestLogReader.StatusFail)
            {
                return null;
            }

            TestFail fail = new TestFail();
            fail.Comment = CsvHelper.At(row, TestLogReader.CommentCell).Trim();

            string code = CsvHelper.At(row, TestLogReader.ErrorCodeCell).Trim();
            string failValue = CsvHelper.At(row, TestLogReader.FailValueCell).Trim();

            if (code.Length == 0 || code == NoErrorCode)
            {
                fail.Kind = TestFailKind.Unknown;
                return fail;
            }

            int column;
            if (!codeIndex.TryGetValue(code, out column))
            {
                fail.Kind = TestFailKind.Unknown;     // 码查不到，同样算未知异常
                return fail;
            }

            fail.ColumnIndex = column;
            fail.ColumnName = block.ColumnName(column);

            if (failValue.Length > 0)
            {
                fail.Kind = TestFailKind.OverSpec;
            }
            else
            {
                fail.Kind = TestFailKind.FunctionFailure;
            }
            return fail;
        }

        // =================================================================
        //  裁到本班
        // =================================================================

        /// <summary>
        /// 只留 Test_Start_Time 落在本班窗口里的行。
        ///
        /// **顺序原封不动** —— 组内标准差算的是相邻两笔的差，吃顺序，重排一次就全错。
        /// 时刻读不出来的行按「保留」处理：宁可多算几台，也别因为某台的时间戳写坏了
        /// 就把它从良率分母里悄悄抹掉。
        /// </summary>
        public static TestLogBlock TrimToShift(TestLogBlock full, ShiftWindow window)
        {
            TestLogBlock trimmed = new TestLogBlock();
            trimmed.Index = full.Index;
            trimmed.HeaderLineNumber = full.HeaderLineNumber;
            trimmed.Header = full.Header;
            trimmed.UpperRow = full.UpperRow;
            trimmed.LowerRow = full.LowerRow;
            trimmed.ErrorCodeRow = full.ErrorCodeRow;
            trimmed.BrandRow = full.BrandRow;

            for (int r = 0; r < full.DataRows.Count; r++)
            {
                string clock = CsvHelper.At(full.DataRows[r], TestLogReader.StartTimeCell).Trim();
                double minutes = window.MinutesAfterStart(clock);

                if (double.IsNaN(minutes) || minutes < window.SpanMinutes)
                {
                    trimmed.DataRows.Add(full.DataRows[r]);
                }
            }
            return trimmed;
        }

        // =================================================================
        //  提取一列的所有测量值
        // =================================================================

        /// <summary>
        /// 把某一列所有能转成数字的读数收出来，顺便记下原始文字里的最大小数位
        /// （= 仪器的输出分辨率，界面按它显示几位）。
        ///
        /// **不排序** —— 交给 CpkCalculator 的必须是 log 的原始行序。
        /// </summary>
        public static List<double> CollectValues(TestLogBlock block, int column, out int digits)
        {
            List<double> values = new List<double>();
            digits = 0;

            for (int r = 0; r < block.DataRows.Count; r++)
            {
                string text = CsvHelper.At(block.DataRows[r], column).Trim();
                double value;
                if (!CsvHelper.TryNumber(text, out value))
                {
                    continue;
                }
                values.Add(value);

                int d = CsvHelper.DecimalsOf(text);
                if (d > digits)
                {
                    digits = d;
                }
            }
            return values;
        }

        // =================================================================
        //  产出测站表和测项表
        // =================================================================

        /// <summary>提取的结果。一份 log 出一个测站，外加它那些测项。</summary>
        public sealed class TestExtractResult
        {
            public readonly List<TestStationRow> Stations = new List<TestStationRow>();
            public readonly List<TestItemRow> Items = new List<TestItemRow>();

            // 全测程的测项盘点。**每一列都过一遍数出来的**，不是从表里那十行反推的。
            public int TotalItems;
            public int CpkCapable;
            public int SingleSided;        // 不可算：只有 L，那是等于判定
            public int Discrete;           // 不可算：不重复值 < 5
            public int ZeroSigma;          // 不可算：σ = 0
            public int NoLimits;           // 不可算：U 和 L 都不是数字
        }

        /// <summary>
        /// 把本班那几份 log 提取成测站表和测项表。
        /// </summary>
        public static TestExtractResult Extract(List<TestLogFile> logs, ShiftWindow window,
                                                BrainConfig config)
        {
            TestExtractResult result = new TestExtractResult();

            for (int i = 0; i < logs.Count; i++)
            {
                ExtractOne(logs[i], window, config, result);
            }

            TrimItemTable(result, config);
            return result;
        }

        private static void ExtractOne(TestLogFile log, ShiftWindow window,
                                       BrainConfig config, TestExtractResult result)
        {
            if (log.Effective == null)
            {
                return;
            }

            TestLogBlock shift = TrimToShift(log.Effective, window);

            if (log.LimitChanged)
            {
                DataProblems.Add(log.StationName + "：程式重启后限值有变更，已舍弃前段 "
                               + log.DroppedUnits + " 台（共 " + log.DroppedBlocks + " 段）");
            }

            if (shift.DataRows.Count == 0)
            {
                DataProblems.Add(log.StationName + "：本班时间窗口（" + window.StartText
                               + "–" + window.EndText + "）内一台都没有，整份 log 都在班外");
                return;
            }

            // ---- 测站：三类不良各几笔 ----
            TestStationRow station = new TestStationRow();
            station.Name = log.StationName;
            station.Tested = shift.DataRows.Count;

            Dictionary<string, int> codes = BuildCodeIndex(shift);
            Dictionary<int, int> overSpecByColumn = new Dictionary<int, int>();
            Dictionary<int, int> funcFailByColumn = new Dictionary<int, int>();

            for (int r = 0; r < shift.DataRows.Count; r++)
            {
                TestFail? fail = Classify(shift, shift.DataRows[r], codes);
                if (fail == null)
                {
                    continue;
                }

                if (fail.Kind == TestFailKind.Unknown)
                {
                    station.Unknown++;
                    continue;
                }

                if (fail.Kind == TestFailKind.OverSpec)
                {
                    station.OverSpec++;
                    Bump(overSpecByColumn, fail.ColumnIndex);
                }
                else
                {
                    station.FuncFail++;
                    Bump(funcFailByColumn, fail.ColumnIndex);
                }
            }
            result.Stations.Add(station);

            // ---- 测项：逐列算能力 ----
            for (int c = 0; c < shift.ColumnCount; c++)
            {
                if (!shift.IsTestItem(c))
                {
                    continue;              // E 行没给码 = 不是测项，是 log 自己的字段
                }

                int digits;
                List<double> values = CollectValues(shift, c, out digits);
                CpkResult stats = CpkCalculator.Compute(values,
                    CsvHelper.At(shift.UpperRow, c).Trim(),
                    CsvHelper.At(shift.LowerRow, c).Trim(),
                    digits);

                CountMeta(result, stats);

                int overSpec;
                overSpecByColumn.TryGetValue(c, out overSpec);
                int funcFail;
                funcFailByColumn.TryGetValue(c, out funcFail);

                // 没判过不良、又算不出能力的列，放进表里只是噪声
                if (overSpec == 0 && funcFail == 0 && !stats.CanCompute)
                {
                    continue;
                }

                result.Items.Add(BuildItem(log, shift, c, stats, overSpec, funcFail, config));
            }
        }

        private static void Bump(Dictionary<int, int> counter, int key)
        {
            int had;
            counter.TryGetValue(key, out had);
            counter[key] = had + 1;
        }

        private static TestItemRow BuildItem(TestLogFile log, TestLogBlock shift, int column,
                                             CpkResult stats, int overSpec, int funcFail,
                                             BrainConfig config)
        {
            TestItemRow item = new TestItemRow(config);
            item.Station = log.StationName;
            item.Name = shift.ColumnName(column);
            item.Ng = overSpec + funcFail;

            // 一列同时有超规和功能失败时报「超规」：超规那部分有测值、能算能力，
            // 报成功能失败会把能力信息整个藏起来。
            if (overSpec > 0)
            {
                item.NgKind = "超规";
            }
            else if (funcFail > 0)
            {
                item.NgKind = TestItemRow.FunctionFailure;
            }

            item.Mean = stats.Mean;
            item.SigmaIn = stats.SigmaWithin;
            item.SigmaAll = stats.SigmaOverall;

            // 显示位数跟着仪器的输出分辨率走。σ 和均值用同一个位数 ——
            // σ 是这些读数的离散程度，印得比读数本身还细是在展示浮点噪声。
            item.MeanText = Fixed(stats.Mean, stats.ValueDigits);
            item.SigmaInText = Fixed(stats.SigmaWithin, stats.ValueDigits);
            item.SigmaAllText = Fixed(stats.SigmaOverall, stats.ValueDigits);

            item.HasLsl = stats.HasLower;
            item.Lsl = stats.Lower;
            item.HasUsl = stats.HasUpper;
            item.Usl = stats.Upper;
            item.CanCpk = stats.CanCompute;
            item.NotComputableReason = stats.GateText();

            // 规格那一列直接用 log 里 U / L 两行的原文，位数跟着数据走
            string lowerText = CsvHelper.At(shift.LowerRow, column).Trim();
            string upperText = CsvHelper.At(shift.UpperRow, column).Trim();
            if (stats.HasLower && stats.HasUpper)
            {
                item.SpecText = lowerText + " – " + upperText;
            }
            else
            {
                item.SpecText = "—";
            }

            return item;
        }

        private static string Fixed(double value, int digits)
        {
            if (digits < 0)
            {
                digits = 0;
            }
            if (digits > 8)
            {
                digits = 8;                // 再多就是在印浮点噪声了
            }
            return value.ToString("F" + digits, CultureInfo.InvariantCulture);
        }

        private static void CountMeta(TestExtractResult r, CpkResult stats)
        {
            r.TotalItems++;

            if (stats.Gate == CpkGate.Ok)
            {
                r.CpkCapable++;
            }
            else if (stats.Gate == CpkGate.SingleSidedEquals)
            {
                r.SingleSided++;
            }
            else if (stats.Gate == CpkGate.Discrete)
            {
                r.Discrete++;
            }
            else if (stats.Gate == CpkGate.ZeroSigma)
            {
                r.ZeroSigma++;
            }
            else
            {
                r.NoLimits++;
            }
        }

        // =================================================================
        //  测项表只放得下十行，选哪十行
        // =================================================================

        /// <summary>
        /// 一份 log 有一百多个测项，表里放得下十行，所以「放哪十行」本身就是一次判断。
        /// 按发现的性质分档，**每一档对应一种不同的处置**：
        ///
        ///   0  本班判过不良      现在就要处置
        ///   1  能力指数低于关注线  还没出事，但快了
        ///   2  σ总 / σ内 越线     Cpk 好看，整批却在漂 —— 不摆在一起看根本发现不了
        ///   3  规格过宽          Cpk 几百，该检讨的是规格不是设备
        ///   4  其余              没事
        ///
        /// 只按 Cpk 排的话第 2 档永远上不了榜（它们的 Cpk 往往是全场最好的几个），
        /// 而那恰恰是这个看板想让人看见的东西。
        /// </summary>
        private static void TrimItemTable(TestExtractResult result, BrainConfig config)
        {
            List<TestItemRow> all = new List<TestItemRow>(result.Items);
            all.Sort(new ItemTableOrder(config));

            result.Items.Clear();
            for (int i = 0; i < all.Count && i < MaxItemRows; i++)
            {
                result.Items.Add(all[i]);
            }
        }

        /// <summary>分档排序。写成类而不是 lambda，调用处一眼看得出排序规则在哪。</summary>
        private sealed class ItemTableOrder : IComparer<TestItemRow>
        {
            private readonly BrainConfig _config;

            public ItemTableOrder(BrainConfig config)
            {
                _config = config;
            }

            public int Compare(TestItemRow a, TestItemRow b)
            {
                int tierA = TierOf(a);
                int tierB = TierOf(b);
                if (tierA != tierB)
                {
                    return tierA.CompareTo(tierB);
                }

                // 同一档里：有不良的按笔数多的在前，其余按能力指数低的在前
                if (tierA == 0 && a.Ng != b.Ng)
                {
                    return b.Ng.CompareTo(a.Ng);
                }

                double left = Sortable(a);
                double right = Sortable(b);
                if (left != right)
                {
                    return left.CompareTo(right);
                }
                return string.CompareOrdinal(a.Name, b.Name);
            }

            private int TierOf(TestItemRow item)
            {
                if (item.Ng > 0)
                {
                    return 0;
                }
                if (item.IsSpecTooWide)
                {
                    return 3;
                }
                if (item.CanCpk && item.Cpk < _config.CpkWarn)
                {
                    return 1;
                }
                if (item.IsDrifting)
                {
                    return 2;
                }
                return 4;
            }

            /// <summary>
            /// 算不出能力的排到后面 —— 它们不是「能力很差」是「不该算」，
            /// 混在最低的那几个里会让人以为出事了。
            /// </summary>
            private static double Sortable(TestItemRow item)
            {
                double capability = item.Cpk;
                if (double.IsNaN(capability))
                {
                    return double.MaxValue;
                }
                return capability;
            }
        }
    }
}

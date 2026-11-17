using System;
using System.Collections.Generic;
using System.IO;

namespace DesktopPet.LineBrain
{
    /// <summary>
    /// UTS log 里的一「段」= 一次程式启动。
    ///
    /// 一份 log 的结构：
    ///
    ///   SeqNum,Status,Comment,ErrorLine,ErrorCode,FailValue,Test_Start_Time,...   ← 表头
    ///   ,T,...        测试机时间戳 / MAC / 作业系统
    ///   ,U,...        每一列的**上限**
    ///   ,L,...        每一列的**下限，或者期望值**
    ///   ,E,...        每一列专属的 ErrorCode
    ///   ,#S / #P / #B / #D,...   机台名 / 程式路径 / 客户机种测程 / 版本
    ///   1,P,...       从这里开始是测试资料，一行 = 一台产品
    ///   2,P,...
    ///   ...
    ///   SeqNum,...    **程式重启了**，表头和这八行元数据又来一遍
    ///
    /// 实测 71 份样例里有 20 份是多段的，最多一份 9 段。
    /// </summary>
    public sealed class TestLogBlock
    {
        public int Index;                  // 第几段，从 1 开始
        public int HeaderLineNumber;       // 表头在原档案第几行，方便回查

        public string[]? Header;
        public string[]? UpperRow;         // U 行
        public string[]? LowerRow;         // L 行
        public string[]? ErrorCodeRow;     // E 行
        public string[]? BrandRow;         // #B 行：客户 / 机种 / 测程 / 阶段 / 厂区

        /// <summary>测试资料行，一行 = 一台产品。Status 是 P 或 F。</summary>
        public readonly List<string[]> DataRows = new List<string[]>();

        public int ColumnCount
        {
            get
            {
                if (Header == null)
                {
                    return 0;
                }
                return Header.Length;
            }
        }

        public string ColumnName(int index)
        {
            string name = CsvHelper.At(Header, index).Trim();
            if (name.Length > 0)
            {
                return name;
            }
            return "第" + (index + 1) + "列";
        }

        /// <summary>
        /// 这一列是不是一个「可判定的测项」。
        ///
        /// 判据是 E 行有没有给它分配错误码。E 行本身就是一张对照表：第 N 格写的码，
        /// 就是第 N 列这一项判不良时要报的码。开头 SeqNum / Status / Comment 那几列
        /// 是 log 自己的字段，不是测项，E 行那几格是空的。
        /// 实测：172 列的那份，163 列有码 —— 正好是 172 减去开头 9 个字段列。
        ///
        /// 反过来不成立：有码不代表这一列真会被判定，有些只是暂存变量，
        /// 它的码一辈子不会出现。所以只能用 ErrorCode 正查列，
        /// 不能用「哪一列是空的」去猜不良项 —— 那样会猜错。
        /// </summary>
        public bool IsTestItem(int index)
        {
            return CsvHelper.At(ErrorCodeRow, index).Trim()
                            .StartsWith("E-", StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 一份 UTS log 读出来的样子。
    ///
    /// 重点在 <see cref="Effective"/>：**限值一致的那些段合并出来的一段**。
    /// 下游只看它，不用管这份文件重启过几次。
    /// </summary>
    public sealed class TestLogFile
    {
        public string FileName = "";

        public string Model = "";          // 机种，如 QUICKSTRIKE-MDA
        public string Program = "";        // 测程，如 FT / PAIRING / HAPTIC-L
        public string Machine = "";        // 机台号，如 13_03
        public string DateText = "";       // 档名里的日期，如 260808

        /// <summary>表里显示的站名，由 <see cref="TestLogReader.AssignStationNames"/> 统一分配。</summary>
        public string StationName = "";

        public readonly List<TestLogBlock> Blocks = new List<TestLogBlock>();

        /// <summary>限值一致的那些段并起来的结果。一段都没有时为 null。</summary>
        public TestLogBlock? Effective;

        public bool LimitChanged;          // 重启之后限值改过
        public int DroppedBlocks;
        public int DroppedUnits;

        /// <summary>变了哪几列。舍弃数据这件事必须让人看见。</summary>
        public readonly List<string> ChangedColumns = new List<string>();
    }

    /// <summary>
    /// 读测试机的原始 UTS log：切段 → 处理程式重启 → 认身份 → 判不良。
    ///
    /// **解析规则移植自 `C:\Users\dyin3\source\repos\Sample\UtsAnalyzer`，一条都没自己发明。**
    /// 拿 71 份真实样例和那个项目逐项对过数，47000+ 项零差异。
    /// 动这个文件之前先把那个对数程序跑起来（见 HANDOVER）。
    ///
    /// ------------------------------------------------------------
    /// 重启怎么处理（整个读取里最要紧的一条）
    /// ------------------------------------------------------------
    /// 重启之后那一套 U / L / E 有可能和重启之前不一样。实测到过：
    /// 某份 260804 的 log 重启后 L 行有 9 列变了。
    ///
    /// 规则：**从最后一段往前比 U / L / E 三行**
    ///   一样   → 同一套程式，收进来一起算
    ///   不一样 → 从这里往前全部舍弃（程式改过了，混着算就是错的）
    ///
    /// 舍弃了多少必须报出来。悄悄丢掉一半数据，界面上照样是一个漂亮的良率。
    /// </summary>
    public static class TestLogReader
    {
        private const string HeaderFirstCell = "SeqNum";

        public const string StatusPass = "P";
        public const string StatusFail = "F";

        private const string MetaUpper = "U";
        private const string MetaLower = "L";
        private const string MetaErrorCode = "E";
        private const string MetaBrand = "#B";

        private const string NoErrorCode = "E-00000";

        // log 开头几个固定字段的列号
        public const int StatusCell = 1;
        public const int CommentCell = 2;
        public const int ErrorCodeCell = 4;
        public const int FailValueCell = 5;
        public const int StartTimeCell = 6;

        // =================================================================
        //  扫目录
        // =================================================================

        /// <summary>
        /// 读某一天的全部 log。<paramref name="wantedDate"/> 留空就取目录里最新的那一天。
        ///
        /// 留空是常态：抓取代码每班把当天的 log 拷进来，程序自己认最新的就行 ——
        /// 要人每班去改一次日期的地方，迟早忘。
        /// </summary>
        public static List<TestLogFile> Load(string folder, string wantedDate)
        {
            List<TestLogFile> kept = new List<TestLogFile>();

            if (!Directory.Exists(folder))
            {
                DataProblems.Add("没有测试 log 目录：" + folder + "，测试栏会是空的");
                return kept;
            }

            string[] paths = Directory.GetFiles(folder, "*.csv");
            if (paths.Length == 0)
            {
                DataProblems.Add("测试 log 目录是空的：" + folder
                               + "，把测试机上的 UTS log 拷进去，一台机器一份");
                return kept;
            }

            Array.Sort(paths, StringComparer.Ordinal);

            List<TestLogFile> all = new List<TestLogFile>();
            for (int i = 0; i < paths.Length; i++)
            {
                TestLogFile? one = ReadFile(paths[i]);
                if (one != null && one.Effective != null)
                {
                    all.Add(one);
                }
            }

            string date = wantedDate;
            if (date.Length == 0)
            {
                date = LatestDate(all);
            }

            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].DateText == date)
                {
                    kept.Add(all[i]);
                }
            }

            if (kept.Count == 0)
            {
                DataProblems.Add("测试 log 目录里没有 " + date + " 这一天的文件");
            }

            AssignStationNames(kept);
            return kept;
        }

        private static string LatestDate(List<TestLogFile> all)
        {
            string latest = "";
            for (int i = 0; i < all.Count; i++)
            {
                if (string.CompareOrdinal(all[i].DateText, latest) > 0)
                {
                    latest = all[i].DateText;
                }
            }
            return latest;
        }

        // =================================================================
        //  读一份
        // =================================================================

        public static TestLogFile? ReadFile(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            TestLogFile log = new TestLogFile();
            log.FileName = Path.GetFileName(path);

            // 拿裸的行。CsvHelper 不需要知道什么是 SeqNum ——
            // 「哪一行是表头、哪一行是元数据」这套规矩是 UTS 独有的，写在下面。
            List<string[]> rows = CsvHelper.ReadRows(path);
            if (rows.Count == 0)
            {
                return null;
            }

            SplitIntoBlocks(rows, log);
            BuildEffective(log);
            ResolveIdentity(log);
            return log;
        }

        private static void SplitIntoBlocks(List<string[]> rows, TestLogFile log)
        {
            TestLogBlock? current = null;

            for (int i = 0; i < rows.Count; i++)
            {
                string[] cells = rows[i];
                string first = CsvHelper.At(cells, 0).Trim();
                string second = CsvHelper.At(cells, 1).Trim();

                // ---- 表头行 = 开新的一段 ----
                if (first == HeaderFirstCell)
                {
                    current = new TestLogBlock();
                    current.Index = log.Blocks.Count + 1;
                    current.HeaderLineNumber = i + 1;
                    current.Header = cells;
                    log.Blocks.Add(current);
                    continue;
                }

                if (current == null)
                {
                    continue;              // 表头还没出现就有内容，格式不对
                }

                // ---- 元数据行：第一格空，第二格是 T / U / L / E / #S / #P / #B / #D ----
                if (first.Length == 0)
                {
                    if (second == MetaUpper)
                    {
                        current.UpperRow = cells;
                    }
                    else if (second == MetaLower)
                    {
                        current.LowerRow = cells;
                    }
                    else if (second == MetaErrorCode)
                    {
                        current.ErrorCodeRow = cells;
                    }
                    else if (second == MetaBrand)
                    {
                        current.BrandRow = cells;
                    }
                    continue;              // T / #S / #P / #D 用不到
                }

                // ---- 测试资料行 ----
                if (second == StatusPass || second == StatusFail)
                {
                    current.DataRows.Add(cells);
                }
            }
        }

        private static void BuildEffective(TestLogFile log)
        {
            if (log.Blocks.Count == 0)
            {
                return;
            }

            int last = log.Blocks.Count - 1;
            TestLogBlock newest = log.Blocks[last];

            // 从倒数第二段往前找，找到第一个「限值不一样」的位置
            int firstKept = last;
            for (int i = last - 1; i >= 0; i--)
            {
                TestLogBlock older = log.Blocks[i];

                bool same = RowsEqual(older.UpperRow, newest.UpperRow)
                         && RowsEqual(older.LowerRow, newest.LowerRow)
                         && RowsEqual(older.ErrorCodeRow, newest.ErrorCodeRow);

                if (same)
                {
                    firstKept = i;
                    continue;
                }

                log.LimitChanged = true;
                CollectChangedColumns(older, newest, log.ChangedColumns);
                break;
            }

            for (int i = 0; i < firstKept; i++)
            {
                log.DroppedBlocks++;
                log.DroppedUnits += log.Blocks[i].DataRows.Count;
            }

            // 留下来的这几段限值完全一致，合并之后不会算错
            TestLogBlock merged = new TestLogBlock();
            merged.Index = 0;                        // 0 = 合并出来的
            merged.HeaderLineNumber = log.Blocks[firstKept].HeaderLineNumber;
            merged.Header = newest.Header;
            merged.UpperRow = newest.UpperRow;
            merged.LowerRow = newest.LowerRow;
            merged.ErrorCodeRow = newest.ErrorCodeRow;
            merged.BrandRow = newest.BrandRow;

            for (int i = firstKept; i <= last; i++)
            {
                List<string[]> rows = log.Blocks[i].DataRows;
                for (int r = 0; r < rows.Count; r++)
                {
                    merged.DataRows.Add(rows[r]);
                }
            }

            log.Effective = merged;
        }

        /// <summary>两行逐格比。长度不同时短的那边当空串。</summary>
        private static bool RowsEqual(string[]? a, string[]? b)
        {
            if (a == null && b == null)
            {
                return true;
            }

            int lengthA = 0;
            if (a != null)
            {
                lengthA = a.Length;
            }

            int lengthB = 0;
            if (b != null)
            {
                lengthB = b.Length;
            }

            int max = Math.Max(lengthA, lengthB);

            for (int i = 0; i < max; i++)
            {
                if (!string.Equals(CsvHelper.At(a, i).Trim(),
                                   CsvHelper.At(b, i).Trim(), StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        private static void CollectChangedColumns(TestLogBlock older, TestLogBlock newer,
                                                  List<string> output)
        {
            int max = Math.Max(newer.ColumnCount, older.ColumnCount);

            for (int i = 0; i < max; i++)
            {
                string oldUpper = CsvHelper.At(older.UpperRow, i).Trim();
                string newUpper = CsvHelper.At(newer.UpperRow, i).Trim();
                string oldLower = CsvHelper.At(older.LowerRow, i).Trim();
                string newLower = CsvHelper.At(newer.LowerRow, i).Trim();
                string oldCode = CsvHelper.At(older.ErrorCodeRow, i).Trim();
                string newCode = CsvHelper.At(newer.ErrorCodeRow, i).Trim();

                bool changed = !string.Equals(oldUpper, newUpper, StringComparison.Ordinal)
                            || !string.Equals(oldLower, newLower, StringComparison.Ordinal)
                            || !string.Equals(oldCode, newCode, StringComparison.Ordinal);

                if (!changed)
                {
                    continue;
                }

                output.Add(newer.ColumnName(i)
                         + "　U " + OrEmpty(oldUpper) + " → " + OrEmpty(newUpper)
                         + "　L " + OrEmpty(oldLower) + " → " + OrEmpty(newLower));
            }
        }

        private static string OrEmpty(string text)
        {
            if (text.Length == 0)
            {
                return "(空)";
            }
            if (text.Length > 18)
            {
                return text.Substring(0, 18) + "…";
            }
            return text;
        }

        // =================================================================
        //  认身份
        // =================================================================

        private const int BrandModelCell = 3;
        private const int BrandProgramCell = 4;

        /// <summary>
        /// 这份 log 是哪台机器、跑什么机种、什么测程、哪一天。
        ///
        ///     CP&amp;G_QUICKSTRIKE-MDA_FT_MP__260808_13_03.csv
        ///     └客户┘└── 机种 ──┘└测程┘└阶段┘  └日期┘└机台┘
        ///
        /// **机种和测程优先读文件里的 `#B` 行**，档名只用来取日期和机台号。
        /// 理由：测程名里有空格和横杠（"HAPTIC L"、"HAPTIC-WEIGHT-CHECK"），
        /// 靠切档名迟早切错；#B 行是一格一个值，71 份样例全都有。
        /// </summary>
        private static void ResolveIdentity(TestLogFile log)
        {
            ParseFileName(log);

            if (log.Effective != null && log.Effective.BrandRow != null)
            {
                string model = CsvHelper.At(log.Effective.BrandRow, BrandModelCell).Trim();
                string program = CsvHelper.At(log.Effective.BrandRow, BrandProgramCell).Trim();

                if (model.Length > 0)
                {
                    log.Model = model;
                }
                if (program.Length > 0)
                {
                    log.Program = program;
                }
            }
        }

        private static void ParseFileName(TestLogFile log)
        {
            string name = Path.GetFileNameWithoutExtension(log.FileName);

            int split = name.IndexOf("__", StringComparison.Ordinal);
            if (split < 0)
            {
                log.Program = name;          // 档名不是预期格式，整个当测程名
                return;
            }

            string[] left = name.Substring(0, split).Split('_');
            if (left.Length >= 2)
            {
                log.Model = left[1];
            }
            if (left.Length >= 3)
            {
                log.Program = left[2];
            }

            string right = name.Substring(split + 2);
            int underscore = right.IndexOf('_');
            if (underscore < 0)
            {
                log.DateText = right;
                return;
            }

            log.DateText = right.Substring(0, underscore);
            log.Machine = right.Substring(underscore + 1);
        }

        /// <summary>
        /// 分配表里显示的站名。
        ///
        /// 默认 `测程 #机台` —— 短，一眼能认。一条产线一个班只跑一个机种，
        /// 机种写在站名里纯属重复。
        ///
        /// 但样例里出现过同一天同一台机器跑两个机种（260808 的 PAIRING#13_07
        /// 既有 BAZOOKA2-MMC-MDA 又有 QUICKSTRIKE-MDA）。那种情况下两份 log
        /// 会挤进同一个站名，良率被合成一行，看不出是两回事。所以撞车时补上机种。
        /// </summary>
        public static void AssignStationNames(List<TestLogFile> logs)
        {
            Dictionary<string, List<string>> modelsByShortName =
                new Dictionary<string, List<string>>(StringComparer.Ordinal);

            for (int i = 0; i < logs.Count; i++)
            {
                string shortName = ShortName(logs[i]);
                List<string> models;
                if (!modelsByShortName.TryGetValue(shortName, out models))
                {
                    models = new List<string>();
                    modelsByShortName[shortName] = models;
                }
                if (!models.Contains(logs[i].Model))
                {
                    models.Add(logs[i].Model);
                }
            }

            for (int i = 0; i < logs.Count; i++)
            {
                string shortName = ShortName(logs[i]);
                if (modelsByShortName[shortName].Count > 1)
                {
                    logs[i].StationName = logs[i].Model + " " + shortName;
                }
                else
                {
                    logs[i].StationName = shortName;
                }
            }
        }

        private static string ShortName(TestLogFile log)
        {
            if (log.Machine.Length == 0)
            {
                return log.Program;
            }
            return log.Program + " #" + log.Machine;
        }

    }
}

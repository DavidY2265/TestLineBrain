using System;
using System.Collections.Generic;

namespace DesktopPet.LineBrain
{
    /// <summary>
    /// **一个班的全部数据，算完装在这里。**
    ///
    /// 界面、警告扫描、发给模型的事实清单，拿的都是同一个 ShiftData 对象 ——
    /// 所以表里的数、小精灵嘴里的数、AI 回答里的数不可能对不上。
    ///
    /// ------------------------------------------------------------
    /// 为什么要有这一层，不让三个界面各自去调计算
    /// ------------------------------------------------------------
    /// 三栏各调一次的话：同一份 log 会被解析三遍（一份一百多列几千行，不便宜），
    /// 而且「整线良率」这种跨栏的数会被算出两个版本。
    /// 这里只做一件事：**跑一遍、装好、发下去。**
    ///
    /// ------------------------------------------------------------
    /// 刷新就是重新 Load() 一个新对象
    /// ------------------------------------------------------------
    /// 没有任何静态可变状态，所以不存在「刷新前要记得清哪几个变量」的问题。
    /// 旧的那个对象没人引用了，GC 自己收走。
    /// </summary>
    public sealed class ShiftData
    {
        public BrainConfig Config = new BrainConfig();
        public ShiftWindow Window = new ShiftWindow();

        /// <summary>数据取到几点为止。就是「现在」，或者班末（班已经过完的话）。</summary>
        public string DataUntil = "";

        /// <summary>这一轮取数发现的毛病，挂到标题栏上。</summary>
        public List<string> Problems = new List<string>();

        // ---------------------------------------------------------- 报警
        public List<AlarmGroup> Alarms = new List<AlarmGroup>();
        public List<AlarmSpan> LineDown = new List<AlarmSpan>();
        public double SumAll;
        public double SumUnion;
        public double Overlap;
        public double Availability;
        public int AlarmTimes;

        /// <summary>
        /// 报警数据到底取到了没有。
        ///
        /// **没取到不等于「本班没报警」。** 数据库没配、连不上、表名写错，
        /// 算出来都是「0 条报警 → 整线停机 0 → MA 100%」，界面上看着是完美产线。
        /// 这是这个项目最反对的那种错：静默的错数比明显的报错危险得多。
        /// 所以单独记一个标志，界面拿它决定是显示数字还是显示「—」。
        /// </summary>
        public bool AlarmsAvailable;

        /// <summary>取报警时记下的问题。报告里交代「为什么没数据」要用，只含这一段的。</summary>
        public List<string> AlarmProblems = new List<string>();

        /// <summary>有几条报警跨出了本班窗口、被裁掉了一头。</summary>
        public int AlarmsClipped;

        /// <summary>有几条到班末还没恢复。有的话，本班停机时长是个下界。</summary>
        public int AlarmsNeverRecovered;

        // ---------------------------------------------------------- 锁附
        public List<FastenRow> Fasten = new List<FastenRow>();

        /// <summary>
        /// 锁附数据取到了没有。理由和 <see cref="AlarmsAvailable"/> 一样：
        /// 空列表既可能是「本班没锁附记录」，也可能是表还没建、连不上。
        /// </summary>
        public bool FastenAvailable;

        /// <summary>取锁附时记下的问题。</summary>
        public List<string> FastenProblems = new List<string>();

        // ---------------------------------------------------------- 测试
        public List<TestStationRow> TestStations = new List<TestStationRow>();
        public List<TestItemRow> TestItems = new List<TestItemRow>();
        public int TestTotalItems;
        public int TestCpkCapable;
        public int TestSingleSided;
        public int TestDiscrete;
        public int TestZeroSigma;
        public int TestNoLimits;

        /// <summary>
        /// 本班到底读到测试 log 没有。
        ///
        /// 这一栏不存在「连不上」的问题（它读的是文件），但空了一样会骗人：
        /// 没有测站时良率算式是 (0 − 0) ÷ 0，打印出来是「整线良率 0.00%」——
        /// 和报警栏的「MA 100%」是同一种错，只是方向反了，看着像整线全挂。
        /// </summary>
        public bool TestAvailable;

        /// <summary>近 8 班的能力指数，画表格里那列小折线。键是 TrendKey。</summary>
        public Dictionary<string, double[]> Trends = new Dictionary<string, double[]>(StringComparer.Ordinal);

        public string LineLabel
        {
            get { return Config.LineLabel; }
        }

        public string ShiftName
        {
            get { return Window.Name; }
        }

        public double SpanMinutes
        {
            get { return Window.SpanMinutes; }
        }

        // =================================================================
        //  跑一遍
        // =================================================================

        /// <summary>
        /// 取数并算完。<paramref name="when"/> 不传就用「现在」，决定算哪一班。
        /// </summary>
        public static ShiftData Load()
        {
            return Load(DateTime.Now);
        }

        public static ShiftData Load(DateTime when)
        {
            DataProblems.Clear();          // 这一轮从零开始，不带上一轮的问题

            ShiftData data = new ShiftData();
            data.Config = BrainConfig.Load();
            data.Window = data.Config.ShiftAt(when);

            DateTime until = when;
            if (until > data.Window.End)
            {
                until = data.Window.End;
            }
            data.DataUntil = until.ToString("HH:mm");

            data.LoadAlarms();
            data.LoadFasten();
            data.LoadTest();
            data.LoadTrends();

            data.Problems = DataProblems.Snapshot();
            return data;
        }

        /// <summary>
        /// 取这一段期间新记下的问题。
        ///
        /// 三栏各自的报告要交代「为什么没数据」，直接把整份 DataProblems 倒出来是不行的 ——
        /// 报警报告里会混进 test_baseline.csv 找不到测站之类的话，读的人会以为
        /// 那就是报警取不到的原因，白跑一趟。
        /// </summary>
        private static List<string> ProblemsSince(int before)
        {
            List<string> mine = new List<string>();
            List<string> all = DataProblems.Snapshot();
            for (int i = before; i < all.Count; i++)
            {
                mine.Add(all[i]);
            }
            return mine;
        }

        private void LoadAlarms()
        {
            int problemsBefore = DataProblems.Count;
            List<AlarmRecord> records = AlarmDatabase.ReadAlarms(Config, Window);

            // 取数过程中一条问题都没新增，才算「真的取到了」。
            // 查出来 0 条是合法结果（本班确实没报警），连不上也是 0 条 —— 两者必须分开。
            AlarmsAvailable = DataProblems.Count == problemsBefore;
            AlarmProblems = ProblemsSince(problemsBefore);
            AlarmAnalyzer.AlarmResult r = AlarmAnalyzer.AnalyzeAlarms(records, Window, Config);

            Alarms = r.Groups;
            LineDown = r.LineDown;
            SumAll = r.SumAll;
            SumUnion = r.SumUnion;
            Overlap = r.Overlap;
            Availability = r.Availability;
            AlarmTimes = r.Times;

            // 这两个数解读报告要用：裁过的说明有报警跨班，没恢复的说明停机是个下界。
            // 它们本来只进了 DataProblems，报告拿不到。
            AlarmsClipped = r.ClippedCount;
            AlarmsNeverRecovered = r.NeverRecoveredCount;
        }

        private void LoadFasten()
        {
            int problemsBefore = DataProblems.Count;
            List<FastenSample> samples = FastenDatabase.ReadFasten(Config, Window);

            // 表都还没配，就谈不上「取到了」。
            // ReadFasten 在这种情况下**故意不记 DataProblems**（功能还没做完不算故障，
            // 不该天天在标题栏挂个⚠），所以光看问题条数会误判成取到了。
            FastenAvailable = Config.FastenTable.Length > 0
                           && DataProblems.Count == problemsBefore;
            FastenProblems = ProblemsSince(problemsBefore);

            Fasten = FastenAnalyzer.AnalyzeFasten(samples, Config);
        }

        private void LoadTest()
        {
            List<TestLogFile> logs = TestLogReader.Load(Config.TestLogPath, Window.DateText);
            TestLogExtract.TestExtractResult r = TestLogExtract.Extract(logs, Window, Config);

            TestStations = r.Stations;
            TestItems = r.Items;
            TestTotalItems = r.TotalItems;
            TestCpkCapable = r.CpkCapable;
            TestSingleSided = r.SingleSided;
            TestDiscrete = r.Discrete;
            TestZeroSigma = r.ZeroSigma;
            TestNoLimits = r.NoLimits;

            // 一个测站都没有 = 今天的 log 没找到，或者找到了但没有一笔落在本班。
            // 这两种都不能拿去算良率，原因 TestLogReader 已经记进 DataProblems。
            TestAvailable = TestStations.Count > 0;

            LoadTestBaseline();
        }

        /// <summary>
        /// 基线良率和基线 NG。**log 里没有这两样** —— 一份 log 就是一天，
        /// 看不到「前七日平均」。所以由两份小 csv 提供，抓取那侧每班更新。
        /// </summary>
        private void LoadTestBaseline()
        {
            CsvTable baseline = CsvHelper.ReadTable(
                BrainConfig.PathOf("test_baseline.csv"), CsvFormat.Commented());

            foreach (CsvRow row in baseline.Rows)
            {
                string name = row.Text("测站");
                if (name.Length == 0)
                {
                    continue;
                }

                TestStationRow? station = FindStation(name);
                if (station == null)
                {
                    // 一份 log 都没读到的时候，基线里**每一个**测站都必然对不上 ——
                    // 那不是三个问题，是「今天没有 log」这一个问题的三个回声。
                    // 照报的话问题清单会被它们撑大，真正的两条原因反而被埋在里面。
                    // 没有 log 这件事 TestLogReader 已经报过一条了，这里闭嘴。
                    if (TestAvailable)
                    {
                        DataProblems.Add("test_baseline.csv 里的测站「" + name
                                       + "」在本班的测试 log 里找不到，这一行的基线没用上");
                    }
                    continue;
                }
                station.Baseline = row.Number("基线良率", 0);
            }

            CsvTable trend = CsvHelper.ReadTable(
                BrainConfig.PathOf("test_trend.csv"), CsvFormat.Commented());

            foreach (CsvRow row in trend.Rows)
            {
                TestItemRow? item = FindItem(row.Text("测站"), row.Text("测项"));
                if (item != null)
                {
                    item.BaselineNg = row.Int("基线NG", 0);
                }
            }
        }

        /// <summary>
        /// 近 8 班的能力指数。最右边那个应当等于本班算出来的值 ——
        /// 折线和数字列是同一件事的两种画法。
        /// </summary>
        private void LoadTrends()
        {
            CsvTable table = CsvHelper.ReadTable(
                BrainConfig.PathOf("test_trend.csv"), CsvFormat.Commented());

            foreach (CsvRow row in table.Rows)
            {
                string key = row.Text("测站") + "|" + row.Text("测项");
                double[] values = row.Numbers("近8班");
                if (values.Length >= 2)
                {
                    Trends[key] = values;
                }
            }
        }

        // =================================================================
        //  报警的聚合查询
        // =================================================================

        /// <summary>按某种口径排序。返回新列表，不动原来那份的顺序。</summary>
        public List<AlarmGroup> SortAlarms(AlarmSortKey key)
        {
            List<AlarmGroup> sorted = new List<AlarmGroup>(Alarms);
            sorted.Sort(new AlarmOrder(key, Config));
            return sorted;
        }

        /// <summary>某种口径下的最大值，画条形图要用它当满格。</summary>
        public double Peak(AlarmSortKey key)
        {
            double peak = 0;
            for (int i = 0; i < Alarms.Count; i++)
            {
                double v = ValueOf(Alarms[i], key);
                if (v > peak)
                {
                    peak = v;
                }
            }
            return peak;
        }

        public double Sum(AlarmSortKey key)
        {
            double sum = 0;
            for (int i = 0; i < Alarms.Count; i++)
            {
                if (!Alarms[i].IsNoData)
                {
                    sum += ValueOf(Alarms[i], key);
                }
            }
            return sum;
        }

        public static double ValueOf(AlarmGroup g, AlarmSortKey key)
        {
            if (key == AlarmSortKey.Count)
            {
                return g.Count;
            }
            return g.Total;
        }

        /// <summary>占整线停机最多的那一类。通讯断连不参与 —— 它根本不算停机。</summary>
        public AlarmGroup? WorstAlarm()
        {
            AlarmGroup? worst = null;
            for (int i = 0; i < Alarms.Count; i++)
            {
                if (Alarms[i].IsNoData)
                {
                    continue;
                }
                if (worst == null || Alarms[i].Total > worst.Total)
                {
                    worst = Alarms[i];
                }
            }
            return worst;
        }

        /// <summary>排序规则写成类，调用处一眼看得出规则在哪。</summary>
        private sealed class AlarmOrder : IComparer<AlarmGroup>
        {
            private readonly AlarmSortKey _key;
            private readonly BrainConfig _config;

            public AlarmOrder(AlarmSortKey key, BrainConfig config)
            {
                _key = key;
                _config = config;
            }

            public int Compare(AlarmGroup a, AlarmGroup b)
            {
                // 通讯断连永远排最后 —— 它不是停机，混在停机榜里会误导
                if (a.IsNoData != b.IsNoData)
                {
                    if (a.IsNoData)
                    {
                        return 1;
                    }
                    return -1;
                }

                if (_key == AlarmSortKey.Station)
                {
                    int byStation = string.CompareOrdinal(a.Station, b.Station);
                    if (byStation != 0)
                    {
                        return byStation;
                    }
                    return b.Total.CompareTo(a.Total);
                }

                double left = ValueOf(a, _key);
                double right = ValueOf(b, _key);
                if (left != right)
                {
                    return right.CompareTo(left);       // 大的在前
                }
                return string.CompareOrdinal(a.Station, b.Station);
            }
        }

        // =================================================================
        //  测试的聚合查询
        // =================================================================

        public int TestTotal()
        {
            int n = 0;
            for (int i = 0; i < TestStations.Count; i++)
            {
                n += TestStations[i].Tested;
            }
            return n;
        }

        public int TestNg()
        {
            int n = 0;
            for (int i = 0; i < TestStations.Count; i++)
            {
                n += TestStations[i].Ng;
            }
            return n;
        }

        public int TestOverSpec()
        {
            int n = 0;
            for (int i = 0; i < TestStations.Count; i++)
            {
                n += TestStations[i].OverSpec;
            }
            return n;
        }

        public int TestFuncFail()
        {
            int n = 0;
            for (int i = 0; i < TestStations.Count; i++)
            {
                n += TestStations[i].FuncFail;
            }
            return n;
        }

        /// <summary>整线良率。**按测试数加权**，不是几个站的良率取平均。</summary>
        public double LineYield()
        {
            int total = TestTotal();
            if (total <= 0)
            {
                return 0;
            }
            return (total - TestNg()) / (double)total * 100.0;
        }

        /// <summary>整线基线也按测试数加权，这样和整线良率同口径，可以直接相减。</summary>
        public double LineBaseline()
        {
            int total = TestTotal();
            if (total <= 0)
            {
                return 0;
            }

            double sum = 0;
            for (int i = 0; i < TestStations.Count; i++)
            {
                sum += TestStations[i].Tested * TestStations[i].Baseline;
            }
            return sum / total;
        }

        public double LineVsBaseline()
        {
            return LineYield() - LineBaseline();
        }

        public TestStationRow? WorstStation()
        {
            TestStationRow? worst = null;
            for (int i = 0; i < TestStations.Count; i++)
            {
                if (worst == null || TestStations[i].Yield < worst.Yield)
                {
                    worst = TestStations[i];
                }
            }
            return worst;
        }

        /// <summary>让整线良率掉得最多的那一项。</summary>
        public TestItemRow? WorstItem()
        {
            int total = TestTotal();
            TestItemRow? worst = null;
            for (int i = 0; i < TestItems.Count; i++)
            {
                if (worst == null || TestItems[i].YieldLoss(total) > worst.YieldLoss(total))
                {
                    worst = TestItems[i];
                }
            }
            return worst;
        }

        /// <summary>能力指数低于关注线的测项有几项。规格过宽那种不算。</summary>
        public int TestBelowWarn()
        {
            int n = 0;
            for (int i = 0; i < TestItems.Count; i++)
            {
                TestItemRow t = TestItems[i];
                if (t.CanCpk && !t.IsSpecTooWide && t.Cpk < Config.CpkWarn)
                {
                    n++;
                }
            }
            return n;
        }

        public int TestBelowCrit()
        {
            int n = 0;
            for (int i = 0; i < TestItems.Count; i++)
            {
                TestItemRow t = TestItems[i];
                if (t.CanCpk && t.Cpk < Config.CpkCrit)
                {
                    n++;
                }
            }
            return n;
        }

        public int TestDrifting()
        {
            int n = 0;
            for (int i = 0; i < TestItems.Count; i++)
            {
                if (TestItems[i].IsDrifting)
                {
                    n++;
                }
            }
            return n;
        }

        private TestStationRow? FindStation(string name)
        {
            for (int i = 0; i < TestStations.Count; i++)
            {
                if (TestStations[i].Name == name)
                {
                    return TestStations[i];
                }
            }
            return null;
        }

        private TestItemRow? FindItem(string station, string name)
        {
            for (int i = 0; i < TestItems.Count; i++)
            {
                if (TestItems[i].Station == station && TestItems[i].Name == name)
                {
                    return TestItems[i];
                }
            }
            return null;
        }

        // =================================================================
        //  锁附的聚合查询
        // =================================================================

        /// <summary>在监孔位数 = 不重复的「工位+孔位」个数。</summary>
        public int HoleCount()
        {
            List<string> seen = new List<string>();
            for (int i = 0; i < Fasten.Count; i++)
            {
                string key = Fasten[i].Station + Fasten[i].Hole;
                if (!seen.Contains(key))
                {
                    seen.Add(key);
                }
            }
            return seen.Count;
        }

        /// <summary>某个量（扭力 / 浮高 / 圈数）本班判了几笔不良。</summary>
        public int FastenNg(string measure)
        {
            int n = 0;
            for (int i = 0; i < Fasten.Count; i++)
            {
                if (Fasten[i].Measure == measure)
                {
                    n += Fasten[i].NgCount;
                }
            }
            return n;
        }

        public FastenRow? LowestCapability()
        {
            FastenRow? low = null;
            for (int i = 0; i < Fasten.Count; i++)
            {
                double c = Fasten[i].Cpk;
                if (double.IsNaN(c))
                {
                    continue;
                }
                if (low == null || c < low.Cpk)
                {
                    low = Fasten[i];
                }
            }
            return low;
        }

        public FastenRow? LowestOf(string measure)
        {
            FastenRow? low = null;
            for (int i = 0; i < Fasten.Count; i++)
            {
                if (Fasten[i].Measure != measure)
                {
                    continue;
                }
                double c = Fasten[i].Cpk;
                if (double.IsNaN(c))
                {
                    continue;
                }
                if (low == null || c < low.Cpk)
                {
                    low = Fasten[i];
                }
            }
            return low;
        }

        /// <summary>
        /// 预警窗口：还在「关注」档（没跌破紧急线）但正在下行的孔位里，
        /// 按各自斜率外推到 warn_floor，最快的那个还剩几班。
        ///
        /// 斜率和能力指数都**先四舍五入到两位再用** —— 报告正文印出来的是
        /// 1.08 和 −0.06，就得按印出来的数外推，不然算出来的班数和报告上对不上。
        /// </summary>
        public double WarnWindow()
        {
            double best = double.NaN;

            for (int i = 0; i < Fasten.Count; i++)
            {
                FastenRow q = Fasten[i];
                double c = q.Cpk;
                if (double.IsNaN(c) || c < Config.CpkCrit || c >= Config.CpkWarn)
                {
                    continue;              // 已经紧急的不用预警，还正常的没有窗口可言
                }

                double slope = Math.Round(TrendSlope(q.TrendKey), 2);
                if (slope >= 0)
                {
                    continue;
                }

                double shifts = (Math.Round(c, 2) - Config.WarnFloor) / -slope;
                if (double.IsNaN(best) || shifts < best)
                {
                    best = shifts;
                }
            }
            return best;
        }

        // =================================================================
        //  近 8 班趋势
        // =================================================================

        /// <summary>取近 8 班的值，没有就返回 null。给小折线用。</summary>
        public double[]? TrendOf(string key)
        {
            double[]? data;
            if (Trends.TryGetValue(key, out data) && data != null && data.Length >= 2)
            {
                return data;
            }
            return null;
        }

        public bool HasTrend(string key)
        {
            double[]? data;
            if (!Trends.TryGetValue(key, out data))
            {
                return false;
            }
            return data != null && data.Length >= 2;
        }

        /// <summary>
        /// 近 8 班的最小二乘斜率，每班变化多少。负数是在往下走。
        /// </summary>
        public double TrendSlope(string key)
        {
            double[]? d;
            if (!Trends.TryGetValue(key, out d) || d == null || d.Length < 2)
            {
                return 0;
            }

            int n = d.Length;
            double meanX = (n - 1) / 2.0;
            double meanY = 0;
            for (int i = 0; i < n; i++)
            {
                meanY += d[i];
            }
            meanY /= n;

            double num = 0;
            double den = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = i - meanX;
                num += dx * (d[i] - meanY);
                den += dx * dx;
            }
            if (den <= 0)
            {
                return 0;
            }
            return num / den;
        }

        public double TrendFirst(string key)
        {
            double[]? d;
            if (!Trends.TryGetValue(key, out d) || d == null || d.Length == 0)
            {
                return double.NaN;
            }
            return d[0];
        }

        public double TrendLast(string key)
        {
            double[]? d;
            if (!Trends.TryGetValue(key, out d) || d == null || d.Length == 0)
            {
                return double.NaN;
            }
            return d[d.Length - 1];
        }

        /// <summary>
        /// 折线画不画成警示色。
        ///
        /// **已经跌破关注线，而且这 8 班真的跌了不止一点点** —— 两个条件缺一不可。
        /// 光低不动的是能力本身偏低（要拉中心值），光跌还没破线的是趋势预警（还有时间），
        /// 两者都不该和「正在恶化且已经出事」画成一个颜色。
        /// </summary>
        public bool IsTrendBad(string key)
        {
            double first = TrendFirst(key);
            double last = TrendLast(key);
            if (double.IsNaN(first) || double.IsNaN(last))
            {
                return false;
            }
            return last < Config.CpkWarn && (first - last) >= Config.TrendDrop;
        }
    }
}

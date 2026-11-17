using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DesktopPet.LineBrain
{
    /// <summary>
    /// 全部配置：产线、两个班次、数据库、警戒值、测试 log 目录。
    ///
    /// **一个文件、一个类。** 以前是 shift.csv 管班次、thresholds.csv 管警戒值，
    /// 两份文件两套读法；现在合成一份 `brain.csv`，全是「名称,值,说明」三列。
    ///
    /// 文件在 `%APPDATA%\DesktopPet\brain.csv`，改完重启生效。
    /// 缺某一行就用内置默认值，不会因为少一行就起不来。
    /// </summary>
    public sealed class BrainConfig
    {
        public const string FileName = "brain.csv";

        // ---------------------------------------------------------- 产线
        public string LineName = "A4-5";
        public string LineLabel = "A4-5 装配线";

        // ---------------------------------------------------------- 班次
        //
        // 白班 08:00–19:30（690 min），夜班 19:30–08:00（750 min）。
        // **两班不等长**，所以 MA 的分母不是一个常数，要按当前是哪一班取。
        public string DayShiftName = "白班";
        public string DayShiftStart = "08:00";
        public string DayShiftEnd = "19:30";
        public string NightShiftName = "夜班";
        public string NightShiftStart = "19:30";
        public string NightShiftEnd = "08:00";

        // ---------------------------------------------------------- 数据库
        public string DbHost = "localhost";
        public int DbPort = 3306;
        public string DbName = "";
        public string DbUser = "";

        /// <summary>
        /// Windows **普通凭据**的目标名，数据库密码从那里取。
        /// 建一条：cmdkey /generic:LineBrainDb /user:账号 /pass
        /// </summary>
        public string DbCredentialTarget = "LineBrainDb";

        public int DbTimeoutSeconds = 10;
        public string AlarmTable = "lm_device_alarm";
        public string FastenTable = "";           // 锁附表还没有，留空

        // ---------------------------------------------------------- 测试 log
        public string TestLogFolder = "testlogs";

        // ---------------------------------------------------------- 报警口径
        /// <summary>
        /// 报警内容里含这个词就当「通讯断连」—— 设备在不在跑没人知道，
        /// 既不算运行也不算停机，MA 的分子分母一起剔除。
        /// 实测这份数据里只有两种写法：检测到_MES通讯断连接、视觉.通讯断连接。
        /// </summary>
        public string NoDataKeyword = "通讯断连";

        // ---------------------------------------------------------- 警戒值
        public double CpkWarn = 1.33;
        public double CpkCrit = 1.00;
        public double CpkGood = 1.60;
        public double CpkAbsurd = 20.0;
        public double YieldGate = 97.0;
        public double DriftRatio = 1.30;
        public double TrendDrop = 0.20;
        public double WarnFloor = 0.80;
        public double MaGate = 90.0;
        public double AlarmShare = 30.0;
        public int AlarmCount = 8;

        // =================================================================
        //  读盘
        // =================================================================

        public static string Folder
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "DesktopPet");
            }
        }

        public static string PathOf(string name)
        {
            return Path.Combine(Folder, name);
        }

        /// <summary>测试 log 的完整目录。</summary>
        public string TestLogPath
        {
            get { return Path.Combine(Folder, TestLogFolder); }
        }

        /// <summary>
        /// 读配置。文件不在就写一份带说明的默认出去，然后用默认值跑。
        /// </summary>
        public static BrainConfig Load()
        {
            string path = PathOf(FileName);
            if (!File.Exists(path))
            {
                CsvHelper.WriteText(path, DefaultText);
            }

            BrainConfig config = new BrainConfig();

            CsvTable table = CsvHelper.ReadTable(path, CsvFormat.Commented());
            if (!table.Require(new string[] { "名称", "值" }))
            {
                return config;               // 表头都不对，全用默认值
            }

            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (CsvRow row in table.Rows)
            {
                string name = row.Text("名称");
                if (name.Length > 0)
                {
                    values[name] = row.Text("值");
                }
            }

            config.ReadInto(values);
            return config;
        }

        private void ReadInto(Dictionary<string, string> v)
        {
            LineName = Text(v, "line", LineName);
            LineLabel = Text(v, "line_label", LineLabel);

            DayShiftName = Text(v, "day_shift_name", DayShiftName);
            DayShiftStart = Text(v, "day_shift_start", DayShiftStart);
            DayShiftEnd = Text(v, "day_shift_end", DayShiftEnd);
            NightShiftName = Text(v, "night_shift_name", NightShiftName);
            NightShiftStart = Text(v, "night_shift_start", NightShiftStart);
            NightShiftEnd = Text(v, "night_shift_end", NightShiftEnd);

            DbHost = Text(v, "db_host", DbHost);
            DbPort = Int(v, "db_port", DbPort);
            DbName = Text(v, "db_name", DbName);
            DbUser = Text(v, "db_user", DbUser);
            DbCredentialTarget = Text(v, "db_credential", DbCredentialTarget);
            DbTimeoutSeconds = Int(v, "db_timeout_sec", DbTimeoutSeconds);
            AlarmTable = Text(v, "alarm_table", AlarmTable);
            FastenTable = Text(v, "fasten_table", FastenTable);

            TestLogFolder = Text(v, "testlog_folder", TestLogFolder);
            NoDataKeyword = Text(v, "nodata_keyword", NoDataKeyword);

            CpkWarn = Number(v, "cpk_warn", CpkWarn);
            CpkCrit = Number(v, "cpk_crit", CpkCrit);
            CpkGood = Number(v, "cpk_good", CpkGood);
            CpkAbsurd = Number(v, "cpk_absurd", CpkAbsurd);
            YieldGate = Number(v, "yield_gate", YieldGate);
            DriftRatio = Number(v, "drift_ratio", DriftRatio);
            TrendDrop = Number(v, "trend_drop", TrendDrop);
            WarnFloor = Number(v, "warn_floor", WarnFloor);
            MaGate = Number(v, "ma_gate", MaGate);
            AlarmShare = Number(v, "alarm_share", AlarmShare);
            AlarmCount = Int(v, "alarm_count", AlarmCount);
        }

        private static string Text(Dictionary<string, string> v, string key, string fallback)
        {
            string found;
            if (v.TryGetValue(key, out found) && found.Trim().Length > 0)
            {
                return found.Trim();
            }
            return fallback;
        }

        private static double Number(Dictionary<string, string> v, string key, double fallback)
        {
            string found;
            if (!v.TryGetValue(key, out found))
            {
                return fallback;
            }

            double parsed;
            if (CsvHelper.TryNumber(found, out parsed))
            {
                return parsed;
            }
            return fallback;
        }

        private static int Int(Dictionary<string, string> v, string key, int fallback)
        {
            return (int)Math.Round(Number(v, key, fallback));
        }

        // =================================================================
        //  存回去
        // =================================================================

        /// <summary>
        /// 把几个值写回 brain.csv。
        ///
        /// **逐行改，不是整份重写。** 那份文件里一多半是注释（每个参数为什么存在、
        /// 填错会怎样），整份重新生成就把注释冲掉了 —— 下一个人打开只剩一堆键值对，
        /// 不知道 nodata_keyword 是干嘛的。
        ///
        /// 只改 <paramref name="values"/> 里点名的那几行，文件里没有的键追加到末尾。
        /// </summary>
        public static bool SaveValues(Dictionary<string, string> values)
        {
            string path = PathOf(FileName);
            if (!File.Exists(path))
            {
                CsvHelper.WriteText(path, DefaultText);
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                DataProblems.Add("读不了 " + FileName + "：" + ex.Message);
                return false;
            }

            List<string> written = new List<string>();
            List<string> output = new List<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart('﻿').Trim();

                // 注释和空行原样留着
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    output.Add(line);
                    continue;
                }

                string[] cells = CsvHelper.SplitLine(trimmed);
                string name = CsvHelper.At(cells, 0).Trim();

                string replacement;
                if (!values.TryGetValue(name, out replacement))
                {
                    output.Add(line);
                    continue;
                }

                // 第三列那句说明留着，只换值
                string note = CsvHelper.At(cells, 2);
                output.Add(CsvHelper.JoinLine(new string[] { name, replacement, note }));
                written.Add(name);
            }

            foreach (KeyValuePair<string, string> pair in values)
            {
                if (!written.Contains(pair.Key))
                {
                    output.Add(CsvHelper.JoinLine(new string[] { pair.Key, pair.Value, "" }));
                }
            }

            StringBuilder text = new StringBuilder();
            for (int i = 0; i < output.Count; i++)
            {
                text.Append(output[i]);
                text.Append("\r\n");
            }
            return CsvHelper.WriteText(path, text.ToString());
        }

        // =================================================================
        //  连接串
        // =================================================================

        /// <summary>
        /// 拼 MySQL 连接串，密码现从 Windows 凭据取。
        /// 取不到返回 null（原因已经记进 DataProblems），调用方据此跳过取数。
        ///
        /// **密码不落在配置文件里，也不留在字段上** —— 用完就扔，
        /// 免得它出现在内存转储或者哪天被谁打日志打出来。
        /// </summary>
        public string? BuildConnectionString()
        {
            if (DbName.Length == 0 || DbUser.Length == 0)
            {
                DataProblems.Add("brain.csv 里的 db_name / db_user 没填，没法连数据库");
                return null;
            }

            string? password = CredentialStore.Read(DbCredentialTarget);
            if (password == null)
            {
                return null;
            }

            return "Server=" + DbHost
                 + ";Port=" + DbPort.ToString(CultureInfo.InvariantCulture)
                 + ";Database=" + DbName
                 + ";User ID=" + DbUser
                 + ";Password=" + password
                 + ";Connection Timeout=" + DbTimeoutSeconds.ToString(CultureInfo.InvariantCulture)
                 + ";Charset=utf8mb4";
        }

        // =================================================================
        //  班次窗口
        // =================================================================

        /// <summary>
        /// 某个时刻属于哪一班，以及那一班的绝对起止。
        /// 不传就用「现在」。
        /// </summary>
        public ShiftWindow ShiftAt(DateTime when)
        {
            TimeSpan dayStart = ShiftWindow.ParseClock(DayShiftStart);
            TimeSpan dayEnd = ShiftWindow.ParseClock(DayShiftEnd);

            if (ShiftWindow.InRange(when.TimeOfDay, dayStart, dayEnd))
            {
                return ShiftWindow.Build(DayShiftName, when, dayStart, dayEnd);
            }

            TimeSpan nightStart = ShiftWindow.ParseClock(NightShiftStart);
            TimeSpan nightEnd = ShiftWindow.ParseClock(NightShiftEnd);
            return ShiftWindow.Build(NightShiftName, when, nightStart, nightEnd);
        }

        // =================================================================
        //  内置默认
        // =================================================================

        private const string DefaultText =
@"# 产线智能大脑（Line Brain）配置。改完重启生效。
# 三列：名称,值,说明。缺哪一行就用程序内置的默认值，不会起不来。
#
名称,值,说明
# ---------------- 产线 ----------------
line,A4-5,看板显示哪条产线。对的是报警表 work_id 字段第 3 段
line_label,A4-5 装配线,标题栏上显示的名字
# ---------------- 班次 ----------------
# 两班不等长：白班 690 min、夜班 750 min。MA 的分母按当前是哪一班取。
day_shift_name,白班,
day_shift_start,08:00,
day_shift_end,19:30,
night_shift_name,夜班,
night_shift_start,19:30,
night_shift_end,08:00,跨零点，程序会自己加一天
# ---------------- 数据库 ----------------
# 密码不写在这里。用命令建一条 Windows 普通凭据：
#     cmdkey /generic:LineBrainDb /user:账号 /pass
# 注意是「普通凭据」不是「Windows 凭据」——后者的密码 Windows 不给应用程序。
db_host,localhost,
db_port,3306,
db_name,,必填
db_user,,必填
db_credential,LineBrainDb,Windows 普通凭据的目标名
db_timeout_sec,10,连不上就别一直等，现场网络抖动很常见
alarm_table,lm_device_alarm,
fasten_table,,螺丝锁附表，还没有先空着
# ---------------- 测试 log ----------------
testlog_folder,testlogs,相对本目录。测试机的 UTS log 原样拷进去，一台机器一份
# ---------------- 报警口径 ----------------
nodata_keyword,通讯断连,报警内容含这个词就当「状态未知」，不算运行也不算停机
# ---------------- 警戒值 ----------------
cpk_warn,1.33,能力指数低于它算「关注」
cpk_crit,1.00,低于它算「紧急」
cpk_good,1.60,高于它算「优秀」
cpk_absurd,20.0,高过它说明规格开太松，这个数已经没有参考意义
yield_gate,97.0,测站良率门槛 %
drift_ratio,1.30,sigma总 / sigma内 超过它判「漂移」
trend_drop,0.20,近 8 班跌幅超过它且已破 cpk_warn，折线画成警示色
warn_floor,0.80,预警窗口外推的目标下限
ma_gate,90.0,整线稼动率门槛 %
alarm_share,30.0,单条报警占整线停机比例 % 超过它就提醒
alarm_count,8,单条报警发生次数达到它就提醒
";
    }

    /// <summary>
    /// 一个具体班次的时间窗口，**带日期的绝对时刻**。
    ///
    /// 为什么要带日期：夜班 19:30–08:00 跨零点，光有「19:30」和「08:00」两个时刻
    /// 没法判断一条 02:00 的报警属于哪一天的夜班。所以这里把它落实成
    /// 2026-09-30 19:30 → 2026-10-01 08:00 这样一对确定的时刻。
    ///
    /// 两班不等长（白班 690 min、夜班 750 min），所以 <see cref="SpanMinutes"/>
    /// 不是常数 —— MA 的分母要从这里取，不能写死。
    /// </summary>
    public sealed class ShiftWindow
    {
        public string Name = "";
        public DateTime Start;
        public DateTime End;

        public string StartText
        {
            get { return Start.ToString("HH:mm"); }
        }

        public string EndText
        {
            get { return End.ToString("HH:mm"); }
        }

        /// <summary>班次长度，分钟。**MA 的分母。**</summary>
        public double SpanMinutes
        {
            get { return (End - Start).TotalMinutes; }
        }

        /// <summary>这一班是哪天的（按开班那一刻算，夜班算在开始那天）。</summary>
        public string DateText
        {
            get { return Start.ToString("yyMMdd"); }
        }

        public bool Contains(DateTime when)
        {
            return when >= Start && when < End;
        }

        /// <summary>
        /// 时刻文字 → 开班后第几分钟。**只看时分秒，不看日期。**
        ///
        /// 给测试 log 用 —— 它的 Test_Start_Time 只有 "08:30:27" 没有日期。
        /// 算出来是负数就加一整天，这样夜班跨零点也对。
        /// 认不出来返回 NaN，调用方自己决定怎么办（返回 0 的话，一个格式写错的文件
        /// 会把所有台安安静静地堆到开班那一刻）。
        /// </summary>
        public double MinutesAfterStart(string clockText)
        {
            if (clockText == null || clockText.Length == 0)
            {
                return double.NaN;
            }

            DateTime parsed;
            if (!DateTime.TryParse(clockText, CultureInfo.InvariantCulture,
                                   DateTimeStyles.None, out parsed))
            {
                return double.NaN;
            }

            double minutes = (parsed.TimeOfDay - Start.TimeOfDay).TotalMinutes;
            if (minutes < 0)
            {
                minutes += 1440;
            }
            return minutes;
        }

        /// <summary>
        /// 把一段绝对时间区间**裁到班次窗口里**，返回开班后第几分钟起、持续几分钟。
        /// 完全落在班外返回 false。
        ///
        /// 这一步是必须的：数据库里有单次几十小时、跨好几天的报警
        /// （实测最长 36.4 小时）。不裁的话整线停机会超过班次长度，MA 算出来是负的。
        /// </summary>
        public bool Clip(DateTime from, DateTime to, out double startMinute, out double duration)
        {
            startMinute = 0;
            duration = 0;

            DateTime a = from;
            DateTime b = to;
            if (a < Start)
            {
                a = Start;
            }
            if (b > End)
            {
                b = End;
            }
            if (b <= a)
            {
                return false;
            }

            startMinute = (a - Start).TotalMinutes;
            duration = (b - a).TotalMinutes;
            return true;
        }

        // ---------------------------------------------------------- 构造

        public static TimeSpan ParseClock(string text)
        {
            TimeSpan parsed;
            if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }
            return TimeSpan.Zero;
        }

        /// <summary>某个时刻在不在这一段里，支持跨零点。</summary>
        public static bool InRange(TimeSpan now, TimeSpan start, TimeSpan end)
        {
            if (end > start)
            {
                return now >= start && now < end;
            }
            return now >= start || now < end;         // 跨零点
        }

        /// <summary>
        /// 把「几点到几点」落实成带日期的一对时刻。
        /// <paramref name="when"/> 是这一班里的任意一个时刻。
        /// </summary>
        public static ShiftWindow Build(string name, DateTime when, TimeSpan start, TimeSpan end)
        {
            ShiftWindow w = new ShiftWindow();
            w.Name = name;

            DateTime day = when.Date;
            DateTime from = day + start;

            // 跨零点的班：当前时刻在零点之后（还没到收班时刻），说明开班是昨天
            if (end <= start && when.TimeOfDay < end)
            {
                from = day.AddDays(-1) + start;
            }

            w.Start = from;
            w.End = from + SpanOf(start, end);
            return w;
        }

        private static TimeSpan SpanOf(TimeSpan start, TimeSpan end)
        {
            TimeSpan span = end - start;
            if (span <= TimeSpan.Zero)
            {
                span += TimeSpan.FromDays(1);          // 跨零点
            }
            return span;
        }
    }
}

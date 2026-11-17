using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DesktopPet.LineBrain
{
    /// <summary>
    /// csv 怎么读、怎么写，全项目只此一处。
    ///
    /// **这个类不认识任何业务格式。** 它不知道什么叫「表头」、什么叫「注释」，
    /// 更不知道什么是 SeqNum —— 这些由调用方通过 <see cref="CsvFormat"/> 告诉它，
    /// 或者干脆用 <see cref="ReadRows"/> 拿到裸的行自己解释。
    ///
    /// 这么分是因为项目里有两种差别极大的 csv：
    ///
    ///   · 规整的表（配置、阈值、导出的数据库表）—— 一行表头 + 若干行数据，
    ///     用 <see cref="ReadTable"/>，按列名取值
    ///   · 测试机的 UTS log —— 一份文件里有好几段，每段自带表头和 T/U/L/E 四行元数据，
    ///     根本不是「一张表」。用 <see cref="ReadRows"/> 拿裸行，
    ///     由 TestLogSource 自己去认段
    ///
    /// 把「怎么切字符」和「这些字符是什么意思」分开，两种格式才能共用一套读写。
    /// </summary>
    public static class CsvHelper
    {
        // =================================================================
        //  最底层：切一行
        // =================================================================

        /// <summary>
        /// 把一行切成若干格，**认引号**。
        ///
        /// 为什么不能直接 line.Split(',')：
        ///
        /// 1. UTS log 里 UTS_HID_Ver 那一列的内容是 `"1, 0, 0, 3"`，里面有三个逗号。
        ///    按逗号硬切，这一行会比表头多出三格，**从这列往后全部错位**，
        ///    读出来张冠李戴 —— 而且不报任何错。
        /// 2. 2026/08 之后的 UTS log 每一格都加了引号。不脱引号的话
        ///    判断 Status == "P" 永远不成立，一台都读不出来。
        /// 3. 数据库导出的报警表，时间字段也是带引号的。
        ///
        /// 规则就是标准 csv 那三条：引号里的逗号不算分隔、引号里连着两个引号代表
        /// 一个真的引号字元、引号本身不进结果。
        /// </summary>
        public static string[] SplitLine(string line)
        {
            List<string> cells = new List<string>();
            StringBuilder current = new StringBuilder();
            bool insideQuote = false;
            int i = 0;

            while (i < line.Length)
            {
                char c = line[i];

                if (insideQuote)
                {
                    if (c == '"')
                    {
                        // 连着两个引号 = 一个真的引号字元
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
                            i += 2;
                            continue;
                        }
                        insideQuote = false;
                        i++;
                        continue;
                    }

                    current.Append(c);
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    insideQuote = true;
                    i++;
                    continue;
                }

                if (c == ',')
                {
                    cells.Add(current.ToString());
                    current.Length = 0;
                    i++;
                    continue;
                }

                current.Append(c);
                i++;
            }

            cells.Add(current.ToString());       // 最后一格后面没有逗号
            return cells.ToArray();
        }

        /// <summary>
        /// 拼一行。含逗号、引号、换行的格子自动加引号。
        /// </summary>
        public static string JoinLine(string[] cells)
        {
            StringBuilder line = new StringBuilder();

            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0)
                {
                    line.Append(',');
                }

                string cell = cells[i];
                if (cell == null)
                {
                    cell = "";
                }

                bool needQuote = cell.IndexOf(',') >= 0
                              || cell.IndexOf('"') >= 0
                              || cell.IndexOf('\n') >= 0
                              || cell.IndexOf('\r') >= 0;

                if (!needQuote)
                {
                    line.Append(cell);
                    continue;
                }

                line.Append('"');
                line.Append(cell.Replace("\"", "\"\""));
                line.Append('"');
            }

            return line.ToString();
        }

        /// <summary>越界取值返回空串，整个数组是 null 也一样。</summary>
        public static string At(string[]? cells, int index)
        {
            if (cells == null || index < 0 || index >= cells.Length)
            {
                return "";
            }
            if (cells[index] == null)
            {
                return "";
            }
            return cells[index];
        }

        // =================================================================
        //  读整份文件
        // =================================================================

        /// <summary>
        /// 整份文件 → 一行一个字符串数组，**一点都不解释**。
        /// 空行会保留（UTS log 靠行号定位，删行会错位）。
        ///
        /// 读不出来返回空列表，并记一条问题 —— 不抛异常。
        /// </summary>
        public static List<string[]> ReadRows(string path)
        {
            List<string[]> rows = new List<string[]>();

            string[] lines;
            if (!TryReadLines(path, out lines))
            {
                return rows;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line == null)
                {
                    line = "";
                }
                rows.Add(SplitLine(line.TrimStart('﻿')));
            }
            return rows;
        }

        /// <summary>
        /// 按 <paramref name="format"/> 说的规矩，读成一张带表头的表。
        /// </summary>
        public static CsvTable ReadTable(string path, CsvFormat format)
        {
            CsvTable table = new CsvTable(Path.GetFileName(path));

            string[] lines;
            if (!TryReadLines(path, out lines))
            {
                return table;
            }

            bool headerDone = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line == null)
                {
                    continue;
                }

                line = line.TrimStart('﻿').Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (format.CommentPrefix.Length > 0
                    && line.StartsWith(format.CommentPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string[] cells = SplitLine(line);
                if (format.TrimFields)
                {
                    for (int k = 0; k < cells.Length; k++)
                    {
                        cells[k] = cells[k].Trim();
                    }
                }

                if (format.HasHeader && !headerDone)
                {
                    table.TakeHeader(cells);
                    headerDone = true;
                    continue;
                }

                table.Rows.Add(new CsvRow(table, cells));
            }

            if (format.HasHeader && !headerDone)
            {
                DataProblems.Add(table.FileName + " 里没有表头（除了注释之外一行内容都没有）");
            }
            return table;
        }

        private static bool TryReadLines(string path, out string[] lines)
        {
            lines = new string[0];

            if (!File.Exists(path))
            {
                DataProblems.Add(Path.GetFileName(path) + " 不存在：" + path);
                return false;
            }

            try
            {
                lines = File.ReadAllLines(path, Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                DataProblems.Add(Path.GetFileName(path) + " 读不出来：" + ex.Message);
                return false;
            }
        }

        // =================================================================
        //  写
        // =================================================================

        /// <summary>
        /// 写盘。**带 UTF-8 BOM** —— 现场要用 Excel 打开这些文件，
        /// 没有 BOM 的话中文列名全是乱码。
        /// </summary>
        public static bool Write(string path, List<string[]> rows)
        {
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < rows.Count; i++)
            {
                text.Append(JoinLine(rows[i]));
                text.Append("\r\n");
            }
            return WriteText(path, text.ToString());
        }

        /// <summary>整段文本直接写（给那些自己拼好内容的地方用，比如内置默认数据）。</summary>
        public static bool WriteText(string path, string text)
        {
            try
            {
                string folder = Path.GetDirectoryName(path);
                if (folder != null && folder.Length > 0)
                {
                    Directory.CreateDirectory(folder);
                }
                File.WriteAllText(path, text, new UTF8Encoding(true));
                return true;
            }
            catch (Exception ex)
            {
                DataProblems.Add("写不了 " + Path.GetFileName(path) + "：" + ex.Message);
                return false;
            }
        }

        // =================================================================
        //  取值（CsvRow 用，也可以单独用）
        // =================================================================

        /// <summary>
        /// 转成数字。一律 InvariantCulture —— csv 里的小数点就是点，
        /// 不能跟着系统区域设置走，装成德语环境的机器上 "1.42" 会被读成 142。
        /// NumberStyles.Any 含指数，所以 "5.41E-03" 也认得。
        /// </summary>
        public static bool TryNumber(string text, out double value)
        {
            value = 0;
            if (text == null)
            {
                return false;
            }

            string trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }
            return double.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>这个数写了几位小数。仪器输出几位，界面就显示几位。</summary>
        public static int DecimalsOf(string text)
        {
            if (text == null)
            {
                return 0;
            }
            if (text.IndexOf('E') >= 0 || text.IndexOf('e') >= 0)
            {
                return 6;              // 指数写法没法数，给个够用的位数
            }

            int dot = text.IndexOf('.');
            if (dot < 0)
            {
                return 0;
            }
            return text.Length - dot - 1;
        }
    }

    /// <summary>
    /// 一份 csv 长什么样 —— **由调用方告诉 CsvHelper，不是 CsvHelper 自己猜**。
    /// </summary>
    public sealed class CsvFormat
    {
        /// <summary>注释行的前缀。空串 = 不认注释，每一行都是数据。</summary>
        public string CommentPrefix = "";

        /// <summary>第一个非注释行是不是表头。</summary>
        public bool HasHeader = true;

        /// <summary>每一格要不要去掉前后空白。</summary>
        public bool TrimFields = true;

        /// <summary>项目自己写的那几份配置：# 是注释，第一个非注释行是表头。</summary>
        public static CsvFormat Commented()
        {
            CsvFormat f = new CsvFormat();
            f.CommentPrefix = "#";
            f.HasHeader = true;
            f.TrimFields = true;
            return f;
        }
    }

    /// <summary>读进来的一张表：一行表头 + 若干行数据。</summary>
    public sealed class CsvTable
    {
        public string FileName = "";
        public readonly List<string> Columns = new List<string>();
        public readonly List<CsvRow> Rows = new List<CsvRow>();

        /// <summary>列名 → 下标。每取一格都要查，用字典比线性找快。</summary>
        private readonly Dictionary<string, int> _index = new Dictionary<string, int>();

        public CsvTable(string fileName)
        {
            FileName = fileName;
        }

        public int IndexOf(string column)
        {
            int at;
            if (_index.TryGetValue(column, out at))
            {
                return at;
            }
            return -1;
        }

        public bool HasColumn(string column)
        {
            return IndexOf(column) >= 0;
        }

        public void TakeHeader(string[] cells)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                string name = cells[i];
                if (name.Length == 0)
                {
                    continue;
                }

                Columns.Add(name);
                if (!_index.ContainsKey(name))
                {
                    _index[name] = i;
                }
            }
        }

        /// <summary>
        /// 声明这张表必须有哪几列，缺了就记一条问题。
        ///
        /// 为什么要单独检查而不是「读不到就当空」：一个缺列的文件会算出一堆 0，
        /// 界面上看起来是真数据，人不会怀疑。**静默的错数比明显的报错危险得多。**
        /// </summary>
        public bool Require(string[] wanted)
        {
            List<string> missing = new List<string>();
            for (int i = 0; i < wanted.Length; i++)
            {
                if (!HasColumn(wanted[i]))
                {
                    missing.Add(wanted[i]);
                }
            }

            if (missing.Count == 0)
            {
                return true;
            }

            DataProblems.Add(FileName + " 缺少这些列：" + string.Join("、", missing.ToArray())
                           + "。实际有的列是：" + string.Join("、", Columns.ToArray()));
            return false;
        }
    }

    /// <summary>表里的一行，**按列名取值**。列名拼错或这行短了，一律返回空/默认值。</summary>
    public sealed class CsvRow
    {
        private readonly CsvTable _table;
        private readonly string[] _cells;

        public CsvRow(CsvTable table, string[] cells)
        {
            _table = table;
            _cells = cells;
        }

        public string Text(string column)
        {
            return CsvHelper.At(_cells, _table.IndexOf(column));
        }

        public double Number(string column, double fallback)
        {
            double value;
            if (CsvHelper.TryNumber(Text(column), out value))
            {
                return value;
            }
            return fallback;
        }

        public int Int(string column, int fallback)
        {
            return (int)Math.Round(Number(column, fallback));
        }

        /// <summary>一格里塞了多个数，分号隔开。比如近 8 班那一列。</summary>
        public double[] Numbers(string column)
        {
            string text = Text(column);
            if (text.Length == 0)
            {
                return new double[0];
            }

            string[] parts = text.Split(';');
            List<double> list = new List<double>();
            for (int i = 0; i < parts.Length; i++)
            {
                double value;
                if (CsvHelper.TryNumber(parts[i], out value))
                {
                    list.Add(value);
                }
            }
            return list.ToArray();
        }
    }
}

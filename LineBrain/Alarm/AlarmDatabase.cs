using System;
using System.Collections.Generic;
using System.Data;
using MySqlConnector;

namespace DesktopPet.LineBrain
{
    /// <summary>
    /// 从本机 MySQL 查本班的报警记录。**只负责取，一个数都不算** ——
    /// 聚类、裁班次、算并集和 MA 全在 <see cref="AlarmAnalyzer"/> 里。
    ///
    /// 分开是因为这两件事变化的原因不同：换了数据库或表结构改这里，
    /// 改了统计口径改那边。
    /// </summary>
    public static class AlarmDatabase
    {
        // 报警表的列名。这是他导出来那份的实际结构：
        //   id, shift, device_id, work_id, alarm_msg, recover_time, insert_time
        private const string ColStation = "device_id";     // 一号机…六号机，这才是「站别」
        private const string ColWorkId = "work_id";        // Factory A#日期#产线#机种#料号#…#班次#…
        private const string ColMessage = "alarm_msg";
        private const string ColStart = "insert_time";     // 报警发生
        private const string ColRecover = "recover_time";  // 恢复；可能是 NULL

        /// <summary>work_id 用 # 分段，第 3 段（下标 2）是产线，如 A4-5 / E952。</summary>
        private const int WorkIdLineSegment = 2;


        /// <summary>
        /// 查本班的报警。
        ///
        /// SQL 的条件是**区间相交**而不是「开始时刻落在班内」：
        ///     insert_time &lt; 班末  且  (recover_time 为空 或 recover_time &gt; 班初)
        /// 这样上一班就开始、一直烧到本班的那种报警也能查到 —— 它确实占用了本班的时间。
        /// 实测数据里有单次 36 小时、跨三天的报警，漏掉它们整线停机会少算一大截。
        ///
        /// 产线过滤放在 C# 里做，不放 SQL：work_id 是个 # 分隔的长串，
        /// 用 SUBSTRING_INDEX 去切既难读又依赖分隔符不变。
        /// </summary>
        public static List<AlarmRecord> ReadAlarms(BrainConfig config, ShiftWindow window)
        {
            List<AlarmRecord> records = new List<AlarmRecord>();

            string? connectionString = config.BuildConnectionString();
            if (connectionString == null)
            {
                return records;              // 原因已经记进 DataProblems
            }

            if (config.AlarmTable.Length == 0)
            {
                DataProblems.Add("brain.csv 里没填 alarm_table，报警栏会是空的");
                return records;
            }

            // 两类都要：
            //   · 已恢复的，只要区间和本班有交集（含上一班就开始、烧进本班的）
            //   · 没恢复的，**必须是本班才开始的**
            //
            // 第二个条件那句 insert_time >= @shiftStart 很要紧。库里有两个月前
            // recover_time 为空的记录（恢复那一笔没写进去），要是也按「还在烧」算，
            // 它会从 7 月一直盖到今天，把整班占满，MA 直接算成 0%。
            // 两个月没恢复的报警不是真烧了两个月，是数据丢了。
            string sql = "SELECT `" + ColStation + "`, `" + ColWorkId + "`, `" + ColMessage
                       + "`, `" + ColStart + "`, `" + ColRecover + "`"
                       + " FROM `" + config.AlarmTable + "`"
                       + " WHERE `" + ColStart + "` < @shiftEnd"
                       + "   AND ( (`" + ColRecover + "` IS NOT NULL AND `" + ColRecover + "` > @shiftStart)"
                       + "      OR (`" + ColRecover + "` IS NULL AND `" + ColStart + "` >= @shiftStart) )"
                       + " ORDER BY `" + ColStart + "`";

            try
            {
                using (MySqlConnection connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    using (MySqlCommand command = new MySqlCommand(sql, connection))
                    {
                        command.CommandTimeout = config.DbTimeoutSeconds;
                        command.Parameters.AddWithValue("@shiftStart", window.Start);
                        command.Parameters.AddWithValue("@shiftEnd", window.End);

                        using (MySqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                AlarmRecord? one = ReadAlarmRow(reader, window);
                                if (one != null)
                                {
                                    records.Add(one);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DataProblems.Add("查报警表失败：" + MySqlAccess.Describe(ex));
                return new List<AlarmRecord>();
            }

            FilterByLine(records, config);
            return records;
        }

        private static AlarmRecord? ReadAlarmRow(MySqlDataReader reader, ShiftWindow window)
        {
            AlarmRecord one = new AlarmRecord();
            one.Station = MySqlAccess.TextOf(reader, 0);
            one.Line = LineFromWorkId(MySqlAccess.TextOf(reader, 1));
            one.Message = MySqlAccess.TextOf(reader, 2);

            if (reader.IsDBNull(3))
            {
                return null;                 // 连发生时刻都没有，这行没法用
            }
            one.Start = reader.GetDateTime(3);

            if (reader.IsDBNull(4))
            {
                // 没恢复 —— 当作一直烧到班末。跳过的话停机时长会少算，
                // 而「到现在还没恢复」恰恰是最该看见的那种。
                //
                // 上面的 SQL 已经保证这类记录是**本班才开始**的，所以
                // 「烧到班末」是合理的上界，不会出现两个月前那条盖满整班的事。
                one.NeverRecovered = true;
                one.End = window.End;
            }
            else
            {
                one.End = reader.GetDateTime(4);
            }

            return one;
        }

        /// <summary>
        /// 只留本条产线的。配置里的 line 对的是 work_id 第 3 段。
        /// 一条都没留下就报出来 —— 多半是 line 填错了，不报的话界面上是干净的 0，
        /// 看着像「本班没报警」。
        /// </summary>
        private static void FilterByLine(List<AlarmRecord> records, BrainConfig config)
        {
            if (config.LineName.Length == 0)
            {
                return;
            }

            List<string> seenLines = new List<string>();
            int before = records.Count;

            for (int i = records.Count - 1; i >= 0; i--)
            {
                string line = records[i].Line;
                if (!seenLines.Contains(line))
                {
                    seenLines.Add(line);
                }
                if (line != config.LineName)
                {
                    records.RemoveAt(i);
                }
            }

            if (records.Count == 0 && before > 0)
            {
                DataProblems.Add("本班查到 " + before + " 条报警，但没有一条属于产线「"
                               + config.LineName + "」。数据里实际有的产线是："
                               + string.Join("、", seenLines.ToArray())
                               + "。检查 brain.csv 里的 line 填对没有。");
            }
        }

        /// <summary>
        /// work_id 长这样：Factory A#2026-06-15#A4-5#Bazooka SE#910-007776#A#8+9L#AD#87229@1228
        /// 第 3 段是产线。段数不够就返回空串。
        /// </summary>
        public static string LineFromWorkId(string workId)
        {
            if (workId == null || workId.Length == 0)
            {
                return "";
            }

            string[] parts = workId.Split('#');
            if (parts.Length <= WorkIdLineSegment)
            {
                return "";
            }
            return parts[WorkIdLineSegment].Trim();
        }
    }
}

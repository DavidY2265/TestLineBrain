using System;
using System.Collections.Generic;
using System.Data;
using MySqlConnector;

namespace DesktopPet.LineBrain
{
    /// <summary>
    /// 从本机 MySQL 查本班的螺丝锁附记录。
    ///
    /// **表结构还没定，现在恒返回空。** 等表有了照 AlarmDatabase 的样子填进来：
    /// 查出来一行一次锁附（工位 / 孔位 / 量 / 实测值 / PLC 判定），
    /// 统计交给 <see cref="FastenAnalyzer"/>。
    /// </summary>
    public static class FastenDatabase
    {


        /// <summary>
        /// 查本班的锁附记录。
        ///
        /// **表结构还没定，所以现在恒返回空。** 等表有了，照 ReadAlarms 的样子
        /// 填进来就行：查出来一行一次锁附（工位 / 孔位 / 量 / 实测值 / PLC 判定），
        /// 统计交给 AlarmFastenAnalyzer。
        ///
        /// 判定那一列要填 **PLC 自己的结论**，不要按规格推 —— PLC 的门槛未必等于
        /// 图纸规格，两者对不上恰恰是要查的问题，自己推就把线索抹掉了。
        /// </summary>
        public static List<FastenSample> ReadFasten(BrainConfig config, ShiftWindow window)
        {
            List<FastenSample> samples = new List<FastenSample>();

            if (config.FastenTable.Length == 0)
            {
                return samples;              // 还没配表，不算问题，不报
            }

            DataProblems.Add("brain.csv 里配了 fasten_table，但锁附表的读取还没实现"
                           + "（表结构未定）。锁附栏会是空的。");
            return samples;
        }
    }
}

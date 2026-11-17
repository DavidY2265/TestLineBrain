using System;
using System.Collections.Generic;

namespace DesktopPet.LineBrain
{
    /// <summary>
    /// 把查出来的锁附记录按「工位 + 孔位 + 量」归组。
    ///
    /// 均值、σ、Cpk、不良笔数都不在这里算 —— 它们是 <see cref="FastenRow"/> 上
    /// **算出来的属性**，改一笔实测值全都会跟着变。这里只管分组。
    /// </summary>
    public static class FastenAnalyzer
    {


        /// <summary>
        /// 原始锁附记录 → 按「工位 + 孔位 + 量」归组。
        ///
        /// 规格（上下限、显示位数）现在没有来源 —— 锁附表还没定，
        /// 规格多半也得从另一张表或者一份 csv 来。等表有了再补。
        /// 现在传进来的是空列表，所以这里返回空。
        /// </summary>
        public static List<FastenRow> AnalyzeFasten(List<FastenSample> samples, BrainConfig config)
        {
            List<FastenRow> rows = new List<FastenRow>();
            if (samples.Count == 0)
            {
                return rows;
            }

            Dictionary<string, FastenRow> byKey = new Dictionary<string, FastenRow>(StringComparer.Ordinal);

            for (int i = 0; i < samples.Count; i++)
            {
                FastenSample s = samples[i];
                string key = s.Station + "|" + s.Hole + "|" + s.Measure;

                FastenRow row;
                if (!byKey.TryGetValue(key, out row))
                {
                    row = new FastenRow(config);
                    row.Station = s.Station;
                    row.Hole = s.Hole;
                    row.Measure = s.Measure;
                    byKey[key] = row;
                    rows.Add(row);
                }
                row.Samples.Add(s);
            }

            return rows;
        }
    }
}

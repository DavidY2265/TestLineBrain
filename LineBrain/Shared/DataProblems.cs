using System.Collections.Generic;

namespace DesktopPet.LineBrain
{
    /// <summary>
    /// 这一轮取数时发现的毛病：文件不在、缺列、连不上数据库、限值变更舍弃了数据……
    ///
    /// 为什么收集起来而不是直接抛异常：产线上开机抛一个异常栈，比什么都不显示更糟。
    /// 所以出了问题照常启动（缺的那部分空着），但把问题挂到看板标题栏上，
    /// 让人一眼看见「数据有问题」，而不是对着一堆 0 以为产线真的没报警。
    ///
    /// **静默的错数比明显的报错危险得多** —— 这条原则在整个项目里是一致的。
    ///
    /// ------------------------------------------------------------
    /// 为什么是静态的，以及为什么必须 Clear
    /// ------------------------------------------------------------
    /// 取数的代码散在 CsvHelper / AlarmSource / TestLogSource 好几处，
    /// 给每个方法都加一个「问题收集器」参数太吵。所以留一个静态的当收集点。
    ///
    /// 代价是它会跨轮累积。所以 <see cref="BrainEngine"/> 每次 Run 的**第一件事**
    /// 就是 Clear，算完再把这一轮的问题装进 ShiftReport。
    /// 忘了 Clear 的后果是：昨天的问题一直挂在标题栏上，而且越挂越多。
    /// </summary>
    public static class DataProblems
    {
        private static readonly List<string> Items = new List<string>();

        public static void Add(string problem)
        {
            if (problem == null || problem.Length == 0)
            {
                return;
            }
            if (Items.Contains(problem))
            {
                return;                  // 同一个问题不重复记
            }
            Items.Add(problem);
        }

        /// <summary>每一轮取数开始前清空。BrainEngine.Run 负责调。</summary>
        public static void Clear()
        {
            Items.Clear();
        }

        public static int Count
        {
            get { return Items.Count; }
        }

        public static bool Any
        {
            get { return Items.Count > 0; }
        }

        /// <summary>拿走这一轮的问题，装进 ShiftReport。</summary>
        public static List<string> Snapshot()
        {
            return new List<string>(Items);
        }
    }
}

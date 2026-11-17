using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace DesktopPet
{
    public sealed class AppConfig
    {
        // ---------- 身份 ----------

        /// <summary>宠物名字。所有台词里的 {name} 会被替换成它。</summary>
        public string PetName { get; set; } = "AI精灵";

        // ---------- 窗口 ----------

        /// <summary>窗口位置，NaN 表示还没保存过，首次启动放到屏幕右下角。</summary>
        public double Left { get; set; } = double.NaN;
        public double Top { get; set; } = double.NaN;

        /// <summary>整体缩放。</summary>
        public double Scale { get; set; } = 1.0;

        // ---------- 行为 ----------

        // 这个程序不启动任何外部进程：左键点小狮子弹的是自带的功能面板
        // （PetPanel.xaml）。老配置文件里残留的 ClickTarget / MenuItems 之类
        // 的元素不用管，XmlSerializer 遇到不认识的元素是直接跳过的。

        /// <summary>闲置多少秒后进入睡眠动画，0 = 不睡。</summary>
        public int SleepAfterSeconds { get; set; } = 60;

        // ---------- 气泡 ----------

        /// <summary>总开关，false = 从不说话。</summary>
        public bool ShowBubbles { get; set; } = true;

        /// <summary>一句话在屏幕上停留几秒。</summary>
        public double BubbleSeconds { get; set; } = 4.0;

        /// <summary>没人理它的时候，每隔多少秒随机唠一句（取区间内随机值）。0 = 不主动说话。</summary>
        public int ChatterMinSeconds { get; set; } = 50;
        public int ChatterMaxSeconds { get; set; } = 150;

        /// <summary>启动后打的招呼。</summary>
        [XmlArray("GreetingLines"), XmlArrayItem("Line")]
        public List<string> GreetingLines { get; set; } = new List<string>();

        /// <summary>鼠标移上来时说的。</summary>
        [XmlArray("HoverLines"), XmlArrayItem("Line")]
        public List<string> HoverLines { get; set; } = new List<string>();

        /// <summary>被点击时说的。</summary>
        [XmlArray("ClickLines"), XmlArrayItem("Line")]
        public List<string> ClickLines { get; set; } = new List<string>();

        /// <summary>闲着没事自己唠的。</summary>
        [XmlArray("IdleLines"), XmlArrayItem("Line")]
        public List<string> IdleLines { get; set; } = new List<string>();

        public string SleepLine { get; set; } = "困了…我先眯一会儿";
        public string WakeLine { get; set; } = "唔…我醒了";

        // =================================================================
        //  默认值
        //
        //  注意：这些默认值故意 *不* 写在属性初始化器里。XmlSerializer 反序列化
        //  集合属性时是往已有集合里 Add，而不是先清空，写在初始化器里会导致每次
        //  读盘都把默认项再追加一遍。所以统一放到 FillDefaults()，只在集合为空
        //  时才填 —— 这同时也解决了老配置文件缺新字段的迁移问题。
        // =================================================================

        private void FillDefaults()
        {
            if (string.IsNullOrWhiteSpace(PetName)) PetName = "AI精灵";
            if (BubbleSeconds <= 0) BubbleSeconds = 4.0;

            if (GreetingLines == null || GreetingLines.Count == 0)
                GreetingLines = new List<string>
                {
                    "你好呀，我是{name}",
                    "{name} 上线啦，点我一下试试",
                };

            if (HoverLines == null || HoverLines.Count == 0)
                HoverLines = new List<string>
                {
                    "我是{name}，有什么可以帮你的吗？",
                    "在的在的～",
                    "点我一下能打开面板哦",
                    "今天也要加油呀",
                };

            if (ClickLines == null || ClickLines.Count == 0)
                ClickLines = new List<string>
                {
                    "好嘞，这就给你打开～",
                    "收到！",
                    "马上就来",
                };

            if (IdleLines == null || IdleLines.Count == 0)
                IdleLines = new List<string>
                {
                    "写代码记得歇会儿眼睛",
                    "喝口水吧～",
                    "拖动我可以换个地方待着",
                    "滚轮能把我调大调小哦",
                    "右键点我有菜单",
                    "有事叫我，没事我就发呆了",
                };
        }

        // =================================================================
        //  读写
        //
        //  用 XmlSerializer 而不是 System.Text.Json：后者在 .NET Framework 上要
        //  额外装 NuGet 包，而 XmlSerializer 是框架自带的，这个工程因此可以做到
        //  零依赖、不用还原包就能编译。
        // =================================================================

        [XmlIgnore]
        public static string ConfigPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DesktopPet", "config.xml");

        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(AppConfig));

        public static AppConfig Load()
        {
            AppConfig? cfg = null;
            try
            {
                if (File.Exists(ConfigPath))
                {
                    using (var reader = new StreamReader(ConfigPath, Encoding.UTF8))
                        cfg = Serializer.Deserialize(reader) as AppConfig;
                }
            }
            catch
            {
                // 配置坏了不要紧，用默认值继续，别让宠物起不来
            }

            bool isNew = cfg == null;
            if (cfg == null) cfg = new AppConfig();

            cfg.FillDefaults();
            if (isNew) cfg.Save();   // 顺手把带注释说明的完整默认配置落盘，方便用户照着改
            return cfg;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));

                var settings = new XmlWriterSettings
                {
                    Indent = true,
                    IndentChars = "  ",
                    Encoding = new UTF8Encoding(false),
                };

                using (var writer = XmlWriter.Create(ConfigPath, settings))
                    Serializer.Serialize(writer, this);
            }
            catch
            {
                // 写不进去就算了，不值得为此弹窗打扰用户
            }
        }
    }
}

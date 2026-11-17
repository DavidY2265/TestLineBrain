using DesktopPet.LineBrain;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DesktopPet.Ui
{
    /// <summary>
    /// 用代码往 Grid 里摆表格的那几个小工具。
    ///
    /// 原来这几个方法是 EquipmentPane 的私有成员 —— 那会儿只有它一张表是代码建的，
    /// 品质和测试两栏的表直接写死在 XAML 里。现在三栏的数都从 csv 来，行数由数据决定，
    /// 三边都得靠代码建表，就把这几个搬出来共用。
    ///
    /// Cell 要 FindResource 才能拿到 BrainTheme 里的样式，而 FindResource 是实例方法，
    /// 所以第一个参数收一个 owner（就是调用它的那个 Pane）。
    /// </summary>
    public static class TableKit
    {
        // ------------------------------------------------------------ 列

        public static void AddFixed(Grid g, double width)
        {
            ColumnDefinition column = new ColumnDefinition();
            column.Width = new GridLength(width);
            g.ColumnDefinitions.Add(column);
        }

        public static void AddStar(Grid g, double weight, double minWidth)
        {
            ColumnDefinition c = new ColumnDefinition();
            c.Width = new GridLength(weight, GridUnitType.Star);
            c.MinWidth = minWidth;
            g.ColumnDefinitions.Add(c);
        }

        /// <summary>加 n 个 Auto 高的行。表格的行高一律由内容决定。</summary>
        public static void AddRows(Grid g, int count)
        {
            for (int i = 0; i < count; i++)
            {
                RowDefinition line = new RowDefinition();
                line.Height = GridLength.Auto;
                g.RowDefinitions.Add(line);
            }
        }

        // ------------------------------------------------------------ 摆放

        public static void Place(Grid g, UIElement el, int row, int column)
        {
            Grid.SetRow(el, row);
            Grid.SetColumn(el, column);
            g.Children.Add(el);
        }

        /// <summary>跨若干行的格子，比如孔位那一列一格顶三行。</summary>
        public static void PlaceSpan(Grid g, UIElement el, int row, int column, int rowSpan)
        {
            Grid.SetRowSpan(el, rowSpan);
            Place(g, el, row, column);
        }

        // ------------------------------------------------------------ 内容

        public static TextBlock Cell(FrameworkElement owner, string text, string styleKey)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.Style = (Style)owner.FindResource(styleKey);
            return t;
        }

        /// <summary>要上色或加粗的格子。颜色只在真有含义的时候给，别处一律中性灰。</summary>
        public static TextBlock Cell(FrameworkElement owner, string text, string styleKey,
                                     Brush? foreground, bool bold)
        {
            TextBlock t = Cell(owner, text, styleKey);
            if (foreground != null)
            {
                t.Foreground = foreground;
            }
            if (bold)
            {
                t.FontWeight = FontWeights.Bold;
            }
            return t;
        }

        /// <summary>行线 / 表头线 / 合计线，横跨整张表。</summary>
        public static Border Rule(FrameworkElement owner, string styleKey, int span)
        {
            Border b = new Border();
            b.Style = (Style)owner.FindResource(styleKey);
            Grid.SetColumnSpan(b, span);
            return b;
        }

        /// <summary>圆角小徽标，状态列那种。</summary>
        public static Border Tag(FrameworkElement owner, string text, string styleKey)
        {
            Border b = new Border();
            b.Style = (Style)owner.FindResource(styleKey);
            TextBlock t = new TextBlock();
            t.Text = text;
            b.Child = t;
            return b;
        }

        // 整套界面只有绿 / 黄 / 红三个颜色有含义，别的状态一律中性灰，不抢眼。
        //
        // 以前这两个方法收的是中文字符串，靠一长串 == 比较来分档 ——
        // 漏了一种状态编译器不会吭声，界面上就悄悄变成灰色。
        // 现在收枚举走 switch，加了新状态忘了配颜色，编译器会提醒。

        public static string TagStyleFor(CapabilityStatus status)
        {
            switch (status)
            {
                case CapabilityStatus.Normal: return "TagRightOk";
                case CapabilityStatus.Watch: return "TagRightWarn";
                case CapabilityStatus.Urgent: return "TagRightCrit";
                default: return "TagRightPlain";
            }
        }

        public static string TagStyleFor(TestItemStatus status)
        {
            switch (status)
            {
                case TestItemStatus.Good: return "TagRightOk";
                case TestItemStatus.Pass: return "TagRightOk";
                case TestItemStatus.NeedsWork: return "TagRightWarn";
                case TestItemStatus.Failing: return "TagRightCrit";
                case TestItemStatus.Urgent: return "TagRightCrit";
                default: return "TagRightPlain";      // 规格过宽、不可算
            }
        }

        /// <summary>状态对应的文字颜色，给数值列用（徽标之外的地方）。null = 用默认色。</summary>
        public static Brush? BrushFor(CapabilityStatus status)
        {
            switch (status)
            {
                case CapabilityStatus.Watch: return BrainBrushes.Warn;
                case CapabilityStatus.Urgent: return BrainBrushes.Crit;
                default: return null;
            }
        }

        public static Brush? BrushFor(TestItemStatus status)
        {
            switch (status)
            {
                case TestItemStatus.NeedsWork: return BrainBrushes.Warn;
                case TestItemStatus.Failing: return BrainBrushes.Crit;
                case TestItemStatus.Urgent: return BrainBrushes.Crit;
                default: return null;
            }
        }

        /// <summary>行首那个小圆点。</summary>
        public static Ellipse Dot(Brush fill)
        {
            Ellipse dot = new Ellipse();
            dot.Width = 7;
            dot.Height = 7;
            dot.Margin = new Thickness(0, 0, 6, 0);
            dot.VerticalAlignment = VerticalAlignment.Center;
            dot.Fill = fill;
            return dot;
        }
    }
}

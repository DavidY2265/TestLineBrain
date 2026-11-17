using System;
using DesktopPet.LineBrain;
using System.Windows;
using System.Windows.Media;

namespace DesktopPet.Ui
{
    /// <summary>
    /// 表格里那一列 56×16 的小折线，画「近 8 班」的走势。
    ///
    /// 它只看形状不看刻度：每次都把这 8 个数的最小值贴到底、最大值顶到头，
    /// 所以一条平线和一条陡降线一眼就能分开，但两行之间的高低不可比 ——
    /// 要比大小看旁边的数字列，这里只回答「在不在动」。
    ///
    /// 直接写成 FrameworkElement 而不是拿 Polyline 拼，是为了能在 XAML 里
    /// 数据由调用方给，控件只管画。
    /// </summary>
    public sealed class Sparkline : FrameworkElement
    {
        private double[]? _values;
        private bool _bad;

        public Sparkline()
        {
            Width = 56;
            Height = 16;
        }

        /// <summary>
        /// 近 8 班的值，由调用方塞进来。
        /// **这个控件不认识 ShiftData** —— 自绘控件只该管怎么画，
        /// 让它自己去翻数据，画图和取数就缠在一起了。
        /// </summary>
        public double[]? Values
        {
            get
            {
                return _values;
            }
            set
            {
                _values = value;
                InvalidateVisual();
            }
        }

        /// <summary>要不要画成警示色。判定在 ShiftData.IsTrendBad，不在这儿。</summary>
        public bool IsBad
        {
            get
            {
                return _bad;
            }
            set
            {
                _bad = value;
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double[]? data = _values;
            if (data == null || data.Length < 2)
            {
                return;
            }

            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 2 || h <= 2)
            {
                return;
            }

            double min = data[0];
            double max = data[0];
            for (int i = 1; i < data.Length; i++)
            {
                if (data[i] < min)
                {
                    min = data[i];
                }
                if (data[i] > max)
                {
                    max = data[i];
                }
            }
            double range = max - min;
            if (range <= 0)
            {
                range = 1;                       // 8 班一个数都没变，画成一条平线
            }

            // 左右各留 1px，线宽 1.4 的笔画才不会被边界切掉
            StreamGeometry geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                for (int i = 0; i < data.Length; i++)
                {
                    double x = i / (double)(data.Length - 1) * (w - 2) + 1;
                    double y = h - 1 - (data[i] - min) / range * (h - 2);
                    if (i == 0)
                    {
                        ctx.BeginFigure(new Point(x, y), false, false);
                    }
                    else
                    {
                        ctx.LineTo(new Point(x, y), true, true);
                    }
                }
            }
            geo.Freeze();

            Brush stroke = BrainBrushes.Soft;
            if (_bad)
            {
                stroke = BrainBrushes.Crit;
            }
            Pen pen = new Pen(stroke, 1.4);
            pen.StartLineCap = PenLineCap.Round;
            pen.EndLineCap = PenLineCap.Round;
            pen.LineJoin = PenLineJoin.Round;
            pen.Freeze();

            dc.DrawGeometry(null, pen, geo);
        }
    }
}

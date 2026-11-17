using System;
using DesktopPet.LineBrain;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace DesktopPet.Ui
{
    /// <summary>
    /// 时序图上的一条。绿底代表这一整班在自动运行，上面按百分比摆出这一类报警
    /// 每次发生的位置和长度。
    ///
    /// 用 OnRender 而不是往 Canvas 里塞一堆 Rectangle：窗口一拉宽 WPF 自己会重画，
    /// 位置按当时的 ActualWidth 重算，不用手写 SizeChanged 去挪那些方块。
    /// </summary>
    public sealed class TimelineBar : FrameworkElement
    {
        private List<AlarmSpan> _segments = new List<AlarmSpan>();
        private bool _isNoData;

        public List<AlarmSpan> Segments
        {
            get
            {
                return _segments;
            }
            set
            {
                _segments = new List<AlarmSpan>();
                if (value != null)
                {
                    _segments = value;
                }
                InvalidateVisual();
            }
        }

        /// <summary>通讯断连那一条画灰色，它不是停机，是状态未知。</summary>
        /// <summary>班次总长，分钟。横轴的满格。由调用方给，控件不认识 ShiftData。</summary>
        public double SpanMinutes = 1;

        public bool IsNoData
        {
            get
            {
                return _isNoData;
            }
            set
            {
                _isNoData = value;
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0)
            {
                return;
            }

            Rect full = new Rect(0.5, 0.5, Math.Max(0, w - 1), Math.Max(0, h - 1));
            Pen hair = new Pen(BrainBrushes.Hair, 1);
            hair.Freeze();

            // 底：整班自动运行
            dc.DrawRoundedRectangle(BrainBrushes.Ok, hair, full, 4, 4);

            // 报警段裁在圆角里面，免得两头顶出去
            RectangleGeometry clip = new RectangleGeometry(new Rect(0, 0, w, h), 4, 4);
            clip.Freeze();
            dc.PushClip(clip);

            Brush fill = BrainBrushes.Crit;
            if (_isNoData)
            {
                fill = BrainBrushes.NoData;
            }
            double least = w * 0.0028;          // 太短的段留一丝宽度，否则 19 秒那种看不见

            for (int i = 0; i < _segments.Count; i++)
            {
                double x = _segments[i].Start / SpanMinutes * w;
                double bw = Math.Max(_segments[i].Duration / SpanMinutes * w, least);
                dc.DrawRectangle(fill, null, new Rect(x, 0, bw, h));
            }

            dc.Pop();
        }
    }
}

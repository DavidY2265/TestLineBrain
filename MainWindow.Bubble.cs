using System;
using DesktopPet.LineBrain;
using System.Collections.Generic;
using DesktopPet.Ui;
using System.Windows.Controls.Primitives;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopPet
{
    /// <summary>说话气泡。闲聊和报警走同一个 Say，区别只在参数。</summary>
    public partial class MainWindow : Window
    {
        // =================================================================
        //  说话气泡
        // =================================================================

        private void SetupBubble()
        {
            Bubble.PlacementTarget = this;

            // 默认的 PlacementMode.Top 是左对齐，这里自己算成水平居中、贴在头顶上方。
            // 返回 PopupPrimaryAxis.Horizontal，贴到屏幕边缘时 WPF 会自动往回收，
            // 不会跑出屏幕外。
            Bubble.CustomPopupPlacementCallback = PlaceBubble;
        }

        private CustomPopupPlacement[] PlaceBubble(Size popupSize, Size targetSize, Point offset)
        {
            return new[]
            {
                new CustomPopupPlacement(
                    new Point((targetSize.Width - popupSize.Width) / 2, -popupSize.Height + 16),
                    PopupPrimaryAxis.Horizontal),
            };
        }

        // 气泡描边的两种颜色。平时是小狮子身上那个橙，报警时换成看板用的警示红 ——
        // 整个程序就这两个地方在表达「这是不是要紧的事」，颜色得和看板对得上。
        private static readonly Brush NormalStroke = MakeBrush("#E8892B");
        private static readonly Brush AlertStroke = MakeBrush("#B3382C");

        private static Brush MakeBrush(string hex)
        {
            SolidColorBrush b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        /// <summary>
        /// 说一句话。闲聊和报警都走这一个方法，区别全在两个参数上。
        ///
        /// 参数用枚举不用 bool：以前的写法是 Say(text, force: true, alert: true)，
        /// 看调用处得先去翻签名才知道两个 true 分别是什么意思。
        /// 现在 Say(text, SpeakPriority.Force, BubbleStyle.Alert) 自己就说清楚了。
        /// </summary>
        private void Say(string? text, SpeakPriority priority, BubbleStyle style)
        {
            if (!_cfg.ShowBubbles || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            // 防刷屏：3 秒内已经说过就闭嘴。点击、睡醒、报警这些必定要回应的走 Force
            if (priority == SpeakPriority.Normal
                && (DateTime.Now - _lastBubble).TotalSeconds < 3)
            {
                return;
            }
            _lastBubble = DateTime.Now;

            BubbleText.Text = text!.Replace("{name}", _cfg.PetName);
            ApplyBubbleStyle(style);

            if (!Bubble.IsOpen)
            {
                Bubble.IsOpen = true;
            }
            Play("BubbleIn");

            // 文字长度变了气泡尺寸就变了，等这一轮布局跑完再重新定位一次
            Dispatcher.BeginInvoke(new Action(ReanchorBubble), DispatcherPriority.Loaded);

            _bubbleTimer.Stop();
            _bubbleTimer.Interval = TimeSpan.FromSeconds(BubbleSeconds(style));
            _bubbleTimer.Start();
        }

        /// <summary>报警气泡换成红描边，另外露出顶上和底下那两行小字。</summary>
        private void ApplyBubbleStyle(BubbleStyle style)
        {
            if (style == BubbleStyle.Alert)
            {
                BubbleBox.BorderBrush = AlertStroke;
                BubbleTail.Stroke = AlertStroke;
                BubbleTag.Visibility = Visibility.Visible;
                BubbleHint.Visibility = Visibility.Visible;

                if (_current != null)
                {
                    BubbleTag.Text = "本班警告　·　" + LineBrain.StatusText.Of(_current.Level);
                }
                return;
            }

            BubbleBox.BorderBrush = NormalStroke;
            BubbleTail.Stroke = NormalStroke;
            BubbleTag.Visibility = Visibility.Collapsed;
            BubbleHint.Visibility = Visibility.Collapsed;
        }

        /// <summary>气泡停留几秒。警告比闲聊长，也更值得多停一会儿让人看完。</summary>
        private double BubbleSeconds(BubbleStyle style)
        {
            if (style == BubbleStyle.Alert)
            {
                return Math.Max(7, _cfg.BubbleSeconds * 1.8);
            }
            return Math.Max(1.5, _cfg.BubbleSeconds);
        }

        private void HideBubble()
        {
            _bubbleTimer.Stop();
            Bubble.IsOpen = false;
        }

        /// <summary>Popup 不会自动跟着宿主窗口跑，抖一下 offset 逼它重新走一遍定位回调。</summary>
        private void ReanchorBubble()
        {
            if (Bubble == null || !Bubble.IsOpen) return;
            Bubble.HorizontalOffset += 1;
            Bubble.HorizontalOffset -= 1;
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            ReanchorBubble();
        }

        private string? Pick(List<string> lines)
        {
            if (lines == null || lines.Count == 0) return null;
            return lines[_rng.Next(lines.Count)];
        }

        private void ScheduleChatter()
        {
            if (_cfg.ChatterMaxSeconds <= 0) { _nextChatter = DateTime.MaxValue; return; }
            int lo = Math.Max(5, _cfg.ChatterMinSeconds);
            int hi = Math.Max(lo + 1, _cfg.ChatterMaxSeconds);
            _nextChatter = DateTime.Now.AddSeconds(_rng.Next(lo, hi));
        }

    }
}

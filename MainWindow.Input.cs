using System;
using DesktopPet.LineBrain;
using System.Collections.Generic;
using DesktopPet.Ui;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopPet
{
    /// <summary>鼠标交互：单击 vs 拖拽的分界、滚轮缩放、右键。</summary>
    public partial class MainWindow : Window
    {
        // =================================================================
        //  鼠标交互
        // =================================================================

        /// <summary>MouseEnter：鼠标移上来时冒一句。</summary>
        private void OnPointerActivity(object sender, MouseEventArgs e)
        {
            Wake();
            Say(Pick(_cfg.HoverLines), SpeakPriority.Normal, BubbleStyle.Chat);
        }

        private void OnLeftDown(object sender, MouseButtonEventArgs e)
        {
            Wake();
            _pressed = true;
            _dragging = false;
            _pressPoint = e.GetPosition(this);
            CaptureMouse();
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            Wake();
            if (!_pressed) return;

            Point p = e.GetPosition(this);
            double dx = p.X - _pressPoint.X;
            double dy = p.Y - _pressPoint.Y;

            // 超过系统拖拽阈值才算拖动，否则算单击 —— 这是"点开软件"和"挪位置"的分界线
            if (!_dragging &&
                (Math.Abs(dx) > SystemParameters.MinimumHorizontalDragDistance ||
                 Math.Abs(dy) > SystemParameters.MinimumVerticalDragDistance))
            {
                _dragging = true;
                HideBubble();   // 拖着走的时候气泡跟不上，直接收起来
            }

            if (_dragging)
            {
                // 窗口跟着走之后，鼠标相对窗口的坐标会自动回到 _pressPoint 附近，所以用增量是稳的
                Left += dx;
                Top += dy;
            }
        }

        private void OnLeftUp(object sender, MouseButtonEventArgs e)
        {
            if (!_pressed) return;
            _pressed = false;
            ReleaseMouseCapture();

            if (_dragging)
            {
                ClampToScreen();
                _cfg.Left = Left;
                _cfg.Top = Top;
                QueueSave();
            }
            else
            {
                Play("Squish");

                // 有没看过的警告时，点小狮子打开的是那条警告的 AI 解析；
                // 没警告才是老行为，开看板。
                LineBrain.Alert? target = _current;
                if (target == null)
                {
                    int next = NextUnopened();
                    if (next >= 0)
                    {
                        target = _alerts[next];
                    }
                }

                if (target != null)
                {
                    // 不再说话：OpenAlert 会把气泡收掉，解析窗弹出来本身就是回应
                    OpenAlert(target);
                }
                else
                {
                    Say(Pick(_cfg.ClickLines), SpeakPriority.Force, BubbleStyle.Chat);
                    TogglePanel();
                }
            }
            _dragging = false;
        }

        /// <summary>滚轮调大小。</summary>
        private void OnWheel(object sender, MouseWheelEventArgs e)
        {
            Wake();
            double step = -0.1;
            if (e.Delta > 0)
            {
                step = 0.1;
            }
            ApplyScale(_cfg.Scale + step);
            ReanchorBubble();
            QueueSave();
        }

        private void ApplyScale(double s)
        {
            _cfg.Scale = Math.Round(Clamp(s, 0.5, 2.5), 2);
            Zoom.ScaleX = Zoom.ScaleY = _cfg.Scale;
        }

        private void OnRightUp(object sender, MouseButtonEventArgs e)
        {
            Wake();
            BuildContextMenu().IsOpen = true;
        }

        // =================================================================
        //  行为：闲置发呆 -> 唠嗑 -> 睡觉
        // =================================================================

        private void OnBrainTick(object? sender, EventArgs e)
        {
            // 有些全屏程序会抢走 topmost，定期重新置顶（带 NOACTIVATE，不抢焦点）
            KeepOnTop();

            // 没人理的时候自己找话说。正在报警时闭嘴 —— 拿闲聊把警告顶掉最糟糕
            if (!_asleep && !_pressed && _current == null && DateTime.Now >= _nextChatter)
            {
                Say(Pick(_cfg.IdleLines), SpeakPriority.Normal, BubbleStyle.Chat);
                ScheduleChatter();
            }

            if (_cfg.SleepAfterSeconds <= 0 || _asleep) return;
            if ((DateTime.Now - _lastActivity).TotalSeconds >= _cfg.SleepAfterSeconds)
                Sleep();
        }

        private void Sleep()
        {
            _asleep = true;
            Say(_cfg.SleepLine, SpeakPriority.Force, BubbleStyle.Chat);
            Play("EyesClose");
            Play("ZzzLoop");
        }

        private void Wake()
        {
            _lastActivity = DateTime.Now;
            if (!_asleep) return;
            _asleep = false;
            Play("EyesOpen");
            Stop("ZzzLoop");
            Play("ZzzHide");
            Say(_cfg.WakeLine, SpeakPriority.Force, BubbleStyle.Chat);
            ScheduleChatter();
        }
    }
}

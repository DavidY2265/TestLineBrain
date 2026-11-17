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
    /// <summary>小精灵这一侧的本班警告：开机扫一遍、定时冒泡、红点计数、点开解析。</summary>
    public partial class MainWindow : Window
    {
        // =================================================================
        //  本班警告
        // =================================================================

        /// <summary>
        /// 扫一遍三栏的数，超警戒线的排成队。
        ///
        /// 第一条等到第 8 秒才报：打完招呼再说正事，一上来就甩警告太突兀。
        /// 之后每 45 秒推下一条，一次只说一件事 —— 一口气报十条等于没报。
        /// </summary>
        private void LoadAlerts()
        {
            // **整个程序唯一一次取数就在这儿。** 连数据库、读测试 log、算完，
            // 装进一个 ShiftData 对象；之后看板、报警解析、AI 追问全都用它，
            // 所以三处的数不可能对不上。
            //
            // 要做「每班刷新」的话，就是在这里重新 Load() 换一个新对象 ——
            // 没有任何要手动清零的累加变量，旧对象 GC 自己收走。
            _shift = LineBrain.ShiftData.Load();

            List<LineBrain.Alert> found = LineBrain.AlertScanner.Scan(_shift);
            for (int i = 0; i < found.Count; i++)
            {
                _alerts.Add(found[i]);
                _shown.Add(false);
                _opened.Add(false);
            }

            UpdateBadge();

            if (_alerts.Count == 0)
            {
                return;
            }

            _alertTimer.Tick += OnAlertTick;
            _alertTimer.Interval = TimeSpan.FromSeconds(8);
            _alertTimer.Start();
        }

        private void OnAlertTick(object? sender, EventArgs e)
        {
            _alertTimer.Interval = TimeSpan.FromSeconds(45);

            // 上一条还挂在气泡上没人理，就先别叠下一条
            if (_current != null && Bubble.IsOpen)
            {
                return;
            }

            int next = NextUnshown();
            if (next < 0)
            {
                _alertTimer.Stop();       // 该说的都说过一遍了，剩下的靠红点和右键菜单
                return;
            }

            LineBrain.Alert a = _alerts[next];

            // 紧急的值得把它叫醒，关注级的等它自己睡够
            if (_asleep)
            {
                if (!a.IsCrit)
                {
                    return;
                }
                Wake();
            }

            _shown[next] = true;
            _current = a;
            Say(a.Short, SpeakPriority.Force, BubbleStyle.Alert);
        }

        /// <summary>还没冒过泡的第一条。</summary>
        private int NextUnshown()
        {
            for (int i = 0; i < _alerts.Count; i++)
            {
                if (!_shown[i])
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>还没被打开看过解析的第一条。点小狮子去的就是它。</summary>
        private int NextUnopened()
        {
            for (int i = 0; i < _alerts.Count; i++)
            {
                if (!_opened[i])
                {
                    return i;
                }
            }
            return -1;
        }

        private int UnopenedCount()
        {
            int n = 0;
            for (int i = 0; i < _opened.Count; i++)
            {
                if (!_opened[i])
                {
                    n++;
                }
            }
            return n;
        }

        private void UpdateBadge()
        {
            int n = UnopenedCount();
            AlertBadge.Visibility = Visibility.Collapsed;
            if (n > 0)
            {
                AlertBadge.Visibility = Visibility.Visible;
            }

            // 红点就那么大，两位数塞不下，超过 9 条一律显示 9+
            AlertBadgeText.Text = n.ToString();
            if (n > 9)
            {
                AlertBadgeText.Text = "9+";
            }
        }

        /// <summary>打开某条警告的 AI 解析，并把它标成读过了（红点上的数字跟着减一）。</summary>
        private void OpenAlert(LineBrain.Alert a)
        {
            for (int i = 0; i < _alerts.Count; i++)
            {
                if (_alerts[i] == a)
                {
                    _shown[i] = true;
                    _opened[i] = true;
                }
            }

            if (_current == a)
            {
                _current = null;
                HideBubble();
            }
            UpdateBadge();

            Ui.ReportWindow win = new Ui.ReportWindow(_shift, a);
            win.Show();
            win.Activate();
        }

        private void OnBlinkTick(object? sender, EventArgs e)
        {
            if (!_asleep) Play("Blink");
            _blinkTimer.Interval = TimeSpan.FromMilliseconds(_rng.Next(2500, 7000));
        }

        private void OnSaveTick(object? sender, EventArgs e)
        {
            _saveTimer.Stop();
            _cfg.Save();
        }

        private void OnBubbleTick(object? sender, EventArgs e)
        {
            // 警告气泡自己收起来之后就不再算「正在报警」，否则小精灵会从此不再闲聊。
            // 红点还在，那条警告也还在右键菜单里，没丢。
            _current = null;
            HideBubble();
        }

        private void OnGreetTick(object? sender, EventArgs e)
        {
            _greetTimer.Stop();   // 只打一次招呼
            Say(Pick(_cfg.GreetingLines), SpeakPriority.Force, BubbleStyle.Chat);
        }

        /// <summary>把配置里的名字同步到标题栏和托盘提示。</summary>
        private void ApplyIdentity()
        {
            Title = _cfg.PetName;
            if (_tray != null) _tray.Text = _cfg.PetName;
        }

        /// <summary>首次启动放右下角，之后回到上次的位置。</summary>
        private void RestorePosition()
        {
            if (double.IsNaN(_cfg.Left) || double.IsNaN(_cfg.Top))
            {
                Rect wa = SystemParameters.WorkArea;
                Left = wa.Right - ActualWidth - 40;
                Top = wa.Bottom - ActualHeight - 20;
            }
            else
            {
                Left = _cfg.Left;
                Top = _cfg.Top;
            }
            ClampToScreen();
        }

        /// <summary>别让宠物整个跑到屏幕外面抓不回来（支持多显示器）。</summary>
        private void ClampToScreen()
        {
            double vl = SystemParameters.VirtualScreenLeft;
            double vt = SystemParameters.VirtualScreenTop;
            double vw = SystemParameters.VirtualScreenWidth;
            double vh = SystemParameters.VirtualScreenHeight;

            Left = Clamp(Left, vl - ActualWidth * 0.25, vl + vw - ActualWidth * 0.75);
            Top = Clamp(Top, vt, vt + vh - ActualHeight * 0.75);
        }

        // .NET Framework 没有 Math.Clamp（那是 .NET Core 2.0+ 的 API）
        private static double Clamp(double v, double lo, double hi)
        {
            if (hi < lo) return lo;
            if (v < lo)
            {
                return lo;
            }
            if (v > hi)
            {
                return hi;
            }
            return v;
        }

    }
}

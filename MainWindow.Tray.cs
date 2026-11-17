using System;
using System.Collections.Generic;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopPet
{
    /// <summary>托盘图标。WPF 没有托盘 API，借 WinForms 的 NotifyIcon。</summary>
    public partial class MainWindow : Window
    {
        // =================================================================
        //  托盘图标（WPF 没有托盘 API，借 WinForms 的 NotifyIcon）
        // =================================================================

        private void SetupTray()
        {
            var strip = new WinForms.ContextMenuStrip();
            strip.Items.Add("显示 / 隐藏", null, OnTrayToggleVisible);
            strip.Items.Add("产线看板", null, OnTrayOpenPanel);
            strip.Items.Add(new WinForms.ToolStripSeparator());
            strip.Items.Add("退出", null, OnTrayQuit);

            _tray = new WinForms.NotifyIcon
            {
                Icon = BuildTrayIcon(),
                Text = _cfg.PetName,   // 鼠标停在托盘图标上显示的提示
                Visible = true,
                ContextMenuStrip = strip,
            };
            _tray.DoubleClick += OnTrayDoubleClick;
        }

        private void OnTrayToggleVisible(object? sender, EventArgs e)
        {
            if (IsVisible)
            {
                HidePet();
            }
            else
            {
                Show();
                Wake();
            }
        }

        private void OnTrayOpenPanel(object? sender, EventArgs e)
        {
            ShowPanel();
        }

        private void OnTrayQuit(object? sender, EventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void OnTrayDoubleClick(object? sender, EventArgs e)
        {
            Show();
            Activate();
            Wake();
        }

        /// <summary>运行时画一个托盘图标，免得还要带一个 .ico 资源文件。</summary>
        private static Drawing.Icon BuildTrayIcon()
        {
            using (var bmp = new Drawing.Bitmap(32, 32))
            {
                using (Drawing.Graphics g = Drawing.Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (var mane = new Drawing.SolidBrush(Drawing.ColorTranslator.FromHtml("#E8892B")))
                    using (var face = new Drawing.SolidBrush(Drawing.ColorTranslator.FromHtml("#FFF3DC")))
                    {
                        g.FillEllipse(mane, 1, 1, 30, 30);
                        g.FillEllipse(face, 7, 9, 18, 16);
                        g.FillEllipse(Drawing.Brushes.Black, 11, 14, 3, 4);
                        g.FillEllipse(Drawing.Brushes.Black, 18, 14, 3, 4);
                    }
                }

                IntPtr h = bmp.GetHicon();
                try
                {
                    // FromHandle 不接管句柄所有权，Clone 出一个独立的再把原句柄销毁，避免泄漏
                    return (Drawing.Icon)Drawing.Icon.FromHandle(h).Clone();
                }
                finally { DestroyIcon(h); }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _blinkTimer.Stop();
            _brainTimer.Stop();
            _bubbleTimer.Stop();
            _greetTimer.Stop();
            _alertTimer.Stop();
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }
            _cfg.Save();
            base.OnClosed(e);
        }
    }
}

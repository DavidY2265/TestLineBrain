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
    /// <summary>产线看板的开关，以及右键菜单。</summary>
    public partial class MainWindow : Window
    {

        // =================================================================
        //  产线看板
        //
        //  看板是个普通的 WPF 窗口（Brain\BrainWindow.xaml），不是外部进程。
        //  原来这里挂的是设置面板，设置已经挪到右键菜单，这个位置让给看板。
        // =================================================================

        /// <summary>
        /// 左键单击：看板没开就开，开着就收起来。
        ///
        /// 「开着没开着」问 BrainWindow.Current() 而不是自己存一个字段：
        /// 报警解析窗上的「打开看板」按钮也会开看板，两处各记一份迟早对不上，
        /// 到时候就会冒出第二个看板。
        /// </summary>
        private void TogglePanel()
        {
            Ui.BrainWindow? board = Ui.BrainWindow.Current();
            if (board != null)
            {
                board.Close();
                return;
            }

            board = new Ui.BrainWindow(_shift);
            board.Show();
            board.Activate();
        }

        internal AppConfig Config
        {
            get { return _cfg; }
        }

        internal void SetBubbles(bool on)
        {
            _cfg.ShowBubbles = on;
            if (on)
            {
                Say("好，我又能说话啦", SpeakPriority.Force, BubbleStyle.Chat);
            }
            else HideBubble();
            QueueSave();
        }

        internal void HidePet()
        {
            HideBubble();
            Hide();
        }

        // =================================================================
        //  右键菜单
        //
        //  设置面板拆掉之后，「让它说话」「开机自动启动」这两个开关没地方去了，
        //  就挂在这儿。菜单每次右键都重建，所以 IsChecked 直接读当前状态就行，
        //  不用另外维护。大小还是滚轮调，名字目前只能改配置文件。
        // =================================================================

        private ContextMenu BuildContextMenu()
        {
            ContextMenu menu = new ContextMenu();
            menu.PlacementTarget = this;

            MenuItem panel = new MenuItem();
            panel.Header = "产线看板";
            panel.Click += OnMenuOpenPanel;
            menu.Items.Add(panel);

            // 「本班警告」：气泡一次只说一条，错过的从这儿翻回来。
            // 每次右键都重建菜单，所以看过没看过直接读当前状态就行。
            if (_alerts.Count > 0)
            {
                MenuItem alerts = new MenuItem();
                alerts.Header = "本班警告（" + _alerts.Count + "）";
                for (int i = 0; i < _alerts.Count; i++)
                {
                    LineBrain.Alert a = _alerts[i];
                    MenuItem one = new MenuItem();
                    string dot = "○ ";
                    if (a.IsCrit)
                    {
                        dot = "● ";
                    }
                    one.Header = dot + a.Short;
                    one.Tag = a;                       // 点的时候从这儿取回是哪一条
                    // 没读过的加粗。读过的回落成普通字重，一眼能看出还剩哪几条
                    one.FontWeight = FontWeights.Bold;
                    if (_opened[i])
                    {
                        one.FontWeight = FontWeights.Normal;
                    }
                    one.Click += OnMenuAlert;
                    alerts.Items.Add(one);
                }
                menu.Items.Add(alerts);
            }

            menu.Items.Add(new Separator());

            // 数据库连不上的时候把这一项标出来 —— 报警栏空着的首要原因就是它没配，
            // 而在菜单里默默躺着一行「数据库连接」，人不会想到要去点。
            MenuItem database = new MenuItem();
            database.Header = "数据库连接…";
            if (!_shift.AlarmsAvailable)
            {
                database.Header = "⚠ 数据库连接…（报警数据没取到）";
            }
            database.Click += OnMenuDatabase;
            menu.Items.Add(database);

            MenuItem bubbles = new MenuItem();
            bubbles.Header = "让它说话";
            bubbles.IsCheckable = true;
            bubbles.IsChecked = _cfg.ShowBubbles;
            bubbles.Click += OnMenuBubbles;
            menu.Items.Add(bubbles);

            MenuItem startup = new MenuItem();
            startup.Header = "开机自动启动";
            startup.IsCheckable = true;
            startup.IsChecked = StartupManager.IsEnabled;
            startup.Click += OnMenuStartup;
            menu.Items.Add(startup);

            menu.Items.Add(new Separator());

            MenuItem hide = new MenuItem();
            hide.Header = "藏起来（托盘里点回来）";
            hide.Click += OnMenuHide;
            menu.Items.Add(hide);

            MenuItem quit = new MenuItem();
            quit.Header = "退出";
            quit.Click += OnMenuQuit;
            menu.Items.Add(quit);

            return menu;
        }

        private void OnMenuOpenPanel(object sender, RoutedEventArgs e)
        {
            ShowPanel();
        }

        private void OnMenuAlert(object sender, RoutedEventArgs e)
        {
            MenuItem item = (MenuItem)sender;
            LineBrain.Alert a = (LineBrain.Alert)item.Tag;
            OpenAlert(a);
        }

        /// <summary>
        /// 打开数据库连接设置。
        ///
        /// 关掉之后**不自动重新取数** —— 本班的数据是开机时一次性算好的，
        /// 中途换掉会让已经开着的看板和小精灵嘴里的数对不上。
        /// 设置窗口里那句「重启程序才会重新取数」说的就是这个。
        /// </summary>
        private void OnMenuDatabase(object sender, RoutedEventArgs e)
        {
            Ui.DbSettingsWindow win = new Ui.DbSettingsWindow();
            win.Show();
            win.Activate();
        }

        private void OnMenuBubbles(object sender, RoutedEventArgs e)
        {
            MenuItem item = (MenuItem)sender;
            SetBubbles(item.IsChecked);
        }

        private void OnMenuStartup(object sender, RoutedEventArgs e)
        {
            MenuItem item = (MenuItem)sender;
            StartupManager.SetEnabled(item.IsChecked);
        }

        private void OnMenuHide(object sender, RoutedEventArgs e)
        {
            HidePet();
        }

        private void OnMenuQuit(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        /// <summary>把看板叫到前台，没开就开一个。</summary>
        private void ShowPanel()
        {
            if (!IsVisible) Show();
            Wake();

            Ui.BrainWindow? board = Ui.BrainWindow.Current();
            if (board == null)
            {
                board = new Ui.BrainWindow(_shift);
                board.Show();
            }
            board.Activate();
        }

    }
}

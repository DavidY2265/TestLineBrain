using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DesktopPet.LineBrain;

namespace DesktopPet.Ui
{
    /// <summary>
    /// 数据库连接的设置界面。右键菜单进来。
    ///
    /// 在这之前，要接上数据库得手改 `brain.csv` 再手敲一条 cmdkey —— 那不叫做完，
    /// 产线上没人会去翻 %APPDATA% 改 csv。
    ///
    /// **密码和其余设置存在两个不同的地方，这是故意的：**
    /// · 主机 / 端口 / 库名 / 账号 / 表名 → brain.csv，明文，可以拷给别人参考
    /// · 密码 → Windows 普通凭据，按当前用户 DPAPI 加密，
    ///   配置文件拷走也带不走密码
    /// </summary>
    public partial class DbSettingsWindow : Window
    {
        private readonly BrainConfig _config;

        public DbSettingsWindow()
        {
            InitializeComponent();

            _config = BrainConfig.Load();

            HostBox.Text = _config.DbHost;
            PortBox.Text = _config.DbPort.ToString(CultureInfo.InvariantCulture);
            DbBox.Text = _config.DbName;
            UserBox.Text = _config.DbUser;
            TableBox.Text = _config.AlarmTable;

            // 密码不回填。**读得到也不该显示** —— 这个窗口可能被截图、
            // 也可能有人路过看见。要改就重新输一次，不改就留空（留空 = 不动已存的那条）。
            PassBox.Password = "";

            // 一进来就告诉他现在是什么状态，别等他点了「测试连接」才知道没配。
            // 没配过和配过但连不上，处理方法完全不一样。
            if (_config.DbName.Length == 0 || _config.DbUser.Length == 0)
            {
                Hint("这台机器还没配过数据库。填完下面几项，先点「测试连接」确认通了再保存。");
            }
        }

        // =================================================================
        //  试连
        // =================================================================

        private void OnTestClick(object sender, RoutedEventArgs e)
        {
            BrainConfig? probe = Collect();
            if (probe == null)
            {
                return;
            }

            // 密码框里有东西就用它试，空着就用已经存好的那条 ——
            // 不然「只想验证一下现有配置」还得把密码再打一遍。
            string typed = PassBox.Password;
            if (typed.Length == 0)
            {
                // 没输、以前也没存过。这属于「还没填完」，不是连接失败 ——
                // 让它掉进 TestConnection 的话，回来那句是「看 brain.csv 和 Windows 凭据」，
                // 而他人就站在替代那两样东西的窗口里，这建议等于把他支走。
                if (!CredentialStore.Exists(probe.DbCredentialTarget))
                {
                    Hint("密码还没输过。这台机器的凭据里也没存过，先在上面填一次。");
                    return;
                }
            }
            else
            {
                if (!CredentialStore.Write(probe.DbCredentialTarget, probe.DbUser, typed))
                {
                    Show(false, "密码存不进 Windows 凭据，没法试连");
                    return;
                }
            }

            TestButton.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;

            string message;
            bool ok = MySqlAccess.TestConnection(probe, out message);

            Mouse.OverrideCursor = null;
            TestButton.IsEnabled = true;

            Show(ok, message);
        }

        // =================================================================
        //  保存
        // =================================================================

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            BrainConfig? probe = Collect();
            if (probe == null)
            {
                return;
            }

            string typed = PassBox.Password;
            if (typed.Length > 0)
            {
                if (!CredentialStore.Write(probe.DbCredentialTarget, probe.DbUser, typed))
                {
                    Show(false, "密码存不进 Windows 凭据");
                    return;
                }
            }

            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            values["db_host"] = probe.DbHost;
            values["db_port"] = probe.DbPort.ToString(CultureInfo.InvariantCulture);
            values["db_name"] = probe.DbName;
            values["db_user"] = probe.DbUser;
            values["alarm_table"] = probe.AlarmTable;

            if (!BrainConfig.SaveValues(values))
            {
                Show(false, "写不了 brain.csv，看看文件是不是被别的程序占着");
                return;
            }

            Show(true, "存好了。重启程序之后报警栏就会去取数。");
        }

        /// <summary>把界面上填的读出来，顺便挡掉几个明显填错的。</summary>
        private BrainConfig? Collect()
        {
            BrainConfig probe = BrainConfig.Load();
            probe.DbHost = HostBox.Text.Trim();
            probe.DbName = DbBox.Text.Trim();
            probe.DbUser = UserBox.Text.Trim();
            probe.AlarmTable = TableBox.Text.Trim();

            int port;
            if (!int.TryParse(PortBox.Text.Trim(), NumberStyles.Integer,
                              CultureInfo.InvariantCulture, out port) || port <= 0 || port > 65535)
            {
                Hint("端口要填 1–65535 之间的数字");
                return null;
            }
            probe.DbPort = port;

            if (probe.DbHost.Length == 0 || probe.DbName.Length == 0 || probe.DbUser.Length == 0)
            {
                Hint("还差几项没填：主机、数据库、账号都要有");
                return null;
            }
            return probe;
        }

        // =================================================================
        //  结果条
        //
        //  三种颜色，**「还没填」和「连不上」必须分开**：
        //  空表单点一下测试就弹红杠，看着像程序坏了，其实只是提醒你填。
        //  这两件事的含义差得远 —— 一个是你还没做完，一个是配置真的有问题。
        // =================================================================

        /// <summary>黄的：表单还没填全，压根没去连库。</summary>
        private void Hint(string message)
        {
            Paint(message,
                  Color.FromRgb(0xFD, 0xF6, 0xE3),    // 底
                  Color.FromRgb(0xE3, 0xC8, 0x8A),    // 边
                  Color.FromRgb(0x84, 0x60, 0x18));   // 字
        }

        /// <summary>绿的成了，红的没成。数据库的原话照抄不改写。</summary>
        private void Show(bool ok, string message)
        {
            if (ok)
            {
                Paint(message,
                      Color.FromRgb(0xE8, 0xF5, 0xEC),
                      Color.FromRgb(0x9E, 0xCF, 0xAE),
                      Color.FromRgb(0x1E, 0x6B, 0x35));
                return;
            }

            Paint(message,
                  Color.FromRgb(0xFD, 0xEC, 0xEC),
                  Color.FromRgb(0xE6, 0xA5, 0xA5),
                  Color.FromRgb(0x9B, 0x2C, 0x2C));
        }

        private void Paint(string message, Color back, Color border, Color text)
        {
            ResultText.Text = message;
            ResultBox.Background = new SolidColorBrush(back);
            ResultBox.BorderBrush = new SolidColorBrush(border);
            ResultText.Foreground = new SolidColorBrush(text);
            ResultBox.BorderThickness = new Thickness(1);
            ResultBox.Visibility = Visibility.Visible;
        }

        // =================================================================
        //  窗口
        // =================================================================

        private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

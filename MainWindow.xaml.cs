using System;
using DesktopPet.LineBrain;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace DesktopPet
{
    public partial class MainWindow : Window
    {
        private AppConfig _cfg = AppConfig.Load();

        // ---- 拖拽状态 ----
        private Point _pressPoint;
        private bool _pressed;
        private bool _dragging;

        // ---- 行为状态 ----
        private DateTime _lastActivity = DateTime.Now;
        private bool _asleep;
        /// <summary>
        /// 本班的全部数据，**整个程序只有这一份**。
        /// 开机取一次，看板、报警队列、AI 追问用的都是它。
        /// 要刷新就重新 ShiftData.Load() 换一个新的 —— 没有要手动清的状态。
        /// </summary>
        private LineBrain.ShiftData _shift = new LineBrain.ShiftData();

        private readonly Random _rng = new Random();

        // ---- 气泡状态 ----
        private DateTime _lastBubble = DateTime.MinValue;
        private DateTime _nextChatter = DateTime.MaxValue;

        private readonly DispatcherTimer _blinkTimer = new DispatcherTimer();
        private readonly DispatcherTimer _brainTimer = new DispatcherTimer();   // 睡/醒判定 + 唠嗑 + 置顶维持
        private readonly DispatcherTimer _saveTimer = new DispatcherTimer();    // 配置保存防抖
        private readonly DispatcherTimer _bubbleTimer = new DispatcherTimer();  // 气泡自动收起
        private readonly DispatcherTimer _greetTimer = new DispatcherTimer();   // 启动后延迟打招呼，只响一次

        private WinForms.NotifyIcon? _tray;

        // ---- 本班警告 ----
        //
        // 开机扫一遍，把超过 brain.csv 里那几条线的数值挑出来，按严重度排队，
        // 隔一会儿冒一条。冒出来的那条记在 _current 里 —— 这时候左键点小狮子
        // 打开的是它的 AI 解析，而不是看板。

        private readonly List<LineBrain.Alert> _alerts = new List<LineBrain.Alert>();

        // 「冒过泡」和「读过解析」要分开记，不然会二选一地出错：
        // 只记一个标志的话，要么同一条反复冒泡，要么气泡一收就当人看过了、红点自己消失。
        private readonly List<bool> _shown = new List<bool>();
        private readonly List<bool> _opened = new List<bool>();

        /// <summary>气泡里正在说的那条警告。null = 现在没在报警，点击走开看板的老路。</summary>
        private LineBrain.Alert? _current;

        private readonly DispatcherTimer _alertTimer = new DispatcherTimer();

        public MainWindow()
        {
            InitializeComponent();
            ShowActivated = false;   // 出现时不抢焦点，不打断用户正在敲的东西
        }

        // =================================================================
        //  启动
        // =================================================================

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ApplyScale(_cfg.Scale);
            RestorePosition();
            SetupBubble();
            SetupTray();
            ApplyIdentity();

            Play("FloatLoop");

            _blinkTimer.Tick += OnBlinkTick;
            _blinkTimer.Interval = TimeSpan.FromSeconds(3);
            _blinkTimer.Start();

            _brainTimer.Tick += OnBrainTick;
            _brainTimer.Interval = TimeSpan.FromSeconds(1.5);
            _brainTimer.Start();

            _saveTimer.Tick += OnSaveTick;
            _saveTimer.Interval = TimeSpan.FromSeconds(1);

            _bubbleTimer.Tick += OnBubbleTick;

            ScheduleChatter();

            // 启动 2.5 秒后打个招呼，别一露面就说话显得突兀
            _greetTimer.Tick += OnGreetTick;
            _greetTimer.Interval = TimeSpan.FromSeconds(2.5);
            _greetTimer.Start();

            LoadAlerts();
        }


        // =================================================================
        //  小工具
        // =================================================================

        private void Play(string key)
        {
            ((Storyboard)Resources[key]).Begin(this, isControllable: true);
        }

        private void Stop(string key)
        {
            ((Storyboard)Resources[key]).Stop(this);
        }

        private void QueueSave()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void KeepOnTop()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }
}

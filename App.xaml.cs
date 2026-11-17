using System.Threading;
using System.Windows;

namespace DesktopPet
{
    public partial class App : Application
    {
        private Mutex? _single;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 单实例：避免开出一窝宠物
            bool isFirst;
            _single = new Mutex(true, @"Local\DesktopPet.SingleInstance", out isFirst);
            if (!isFirst)
            {
                Shutdown();
                return;
            }

            base.OnStartup(e);
            new MainWindow().Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_single != null)
            {
                _single.ReleaseMutex();
                _single.Dispose();
            }
            base.OnExit(e);
        }
    }
}

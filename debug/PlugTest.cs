using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace OnboardLite
{
    // Debug-only: posts a WM_DEVICECHANGE / DBT_DEVNODES_CHANGED to the real window off-screen; with OBL_LOG set,
    // the log should show the device change followed by an immediate rescan and a follow-up one.
    static class PlugTest
    {
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [STAThread]
        static void Main()
        {
            var app = new Application();
            app.Resources = (ResourceDictionary)Program.LoadXaml("theme.xaml");
            Theme.Init();
            Lang.Init();
            var w = new MainWindow { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = 0 };
            int step = 0;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            t.Tick += (s, e) =>
            {
                step++;
                if (step == 2) { Log.W("--- posting WM_DEVICECHANGE"); PostMessage(new WindowInteropHelper(w).Handle, 0x0219, new IntPtr(7), IntPtr.Zero); }
                if (step == 7) { t.Stop(); w.Close(); }
            };
            w.Loaded += (s, e) => t.Start();
            app.Run(w);
        }
    }
}

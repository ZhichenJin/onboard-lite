using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OnboardLite
{
    // Debug-only: triggers the real theme cross-fade off-screen and captures a frame mid-fade.
    static class FadeTest
    {
        [STAThread]
        static void Main(string[] args)
        {
            var app = new Application();
            app.Resources = (ResourceDictionary)Program.LoadXaml("theme.xaml");
            Theme.Init();
            Lang.Init();
            var w = new MainWindow { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = 0 };
            var m = typeof(MainWindow).GetMethod("ToggleTheme", BindingFlags.Instance | BindingFlags.NonPublic);
            int step = 0;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            t.Tick += (s, e) =>
            {
                step++;
                if (step == 5) m.Invoke(w, null);
                if (step == 6) Shot(w, Path.Combine(args[0], "fade-mid.png"));
                if (step == 9) { Shot(w, Path.Combine(args[0], "fade-end.png")); m.Invoke(w, null); }
                if (step == 13) { t.Stop(); w.Close(); }
            };
            w.Loaded += (s, e) => t.Start();
            app.Run(w);
            Console.WriteLine("ok");
        }

        static void Shot(Window w, string file)
        {
            var el = (FrameworkElement)w.Content;
            var bmp = new RenderTargetBitmap((int)el.ActualWidth, (int)el.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(el);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = File.Create(file)) enc.Save(f);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OnboardLite
{
    // Debug-only: runs the real app off-screen (no focus), opens the first device and renders every page
    // in both themes to PNG files. Read-only: it never saves.
    static class Snap
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [STAThread]
        static void Main(string[] args)
        {
            string outDir = args[0];
            Directory.CreateDirectory(outDir);
            var app = new Application();
            app.DispatcherUnhandledException += (s, e) =>
            {
                File.WriteAllText(Path.Combine(outDir, "crash.txt"), e.Exception.ToString(), System.Text.Encoding.UTF8);
                e.Handled = true;
                Environment.Exit(3);
            };
            app.Resources = (ResourceDictionary)Program.LoadXaml("theme.xaml");
            Theme.Init();
            Lang.Init();
            bool startDark = Theme.IsDark, startEn = Lang.En;
            var w = new MainWindow { ShowActivated = false, ShowInTaskbar = false };
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = -30000;
            w.Top = 0;

            var steps = new Queue<Action>();
            Func<string, FrameworkElement> device = n => (FrameworkElement)((FrameworkElement)Get(w, "device")).FindName(n);
            Action<string> nav = n => ((RadioButton)device(n)).IsChecked = true;
            foreach (var pass in new[] { 0, 1, 2 })
            {
                bool dark = pass == 1 ? !startDark : startDark, en = pass == 2 ? !startEn : startEn;
                string t = (dark ? "dark" : "light") + (en ? "-en" : "-zh");
                steps.Enqueue(() =>
                {
                    if (Theme.IsDark != dark) Theme.Toggle();
                    if (Lang.En != en) Lang.Toggle();
                    Call(w, "ShowHome");
                });
                steps.Enqueue(() => Shot(w, Path.Combine(outDir, t + "-1-home.png")));
                steps.Enqueue(() => Call(w, "OpenDevice", ((System.Collections.IList)Get(w, "sessions"))[0]));
                steps.Enqueue(() => Shot(w, Path.Combine(outDir, t + "-2-perf.png")));
                steps.Enqueue(() => nav("NavButtons"));
                steps.Enqueue(() => Shot(w, Path.Combine(outDir, t + "-3-buttons.png")));
                steps.Enqueue(() => nav("NavProfiles"));
                steps.Enqueue(() => Shot(w, Path.Combine(outDir, t + "-4-profiles.png")));
                steps.Enqueue(() => nav("NavInfo"));
                steps.Enqueue(() => Shot(w, Path.Combine(outDir, t + "-5-info.png")));
            }
            steps.Enqueue(() => { if (Theme.IsDark != startDark) Theme.Toggle(); if (Lang.En != startEn) Lang.Toggle(); w.Close(); });

            int waited = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            timer.Tick += (s, e) =>
            {
                if (((System.Collections.IList)Get(w, "sessions")).Count == 0)
                {
                    if (++waited > 40) { Console.WriteLine("no device"); timer.Stop(); w.Close(); }
                    return;
                }
                if (steps.Count == 0) { timer.Stop(); return; }
                steps.Dequeue()();
            };
            w.Loaded += (s, e) => timer.Start();
            app.Run(w);
        }

        static object Get(object o, string name) { return o.GetType().GetField(name, Any).GetValue(o); }
        static void Call(object o, string name, params object[] args) { o.GetType().GetMethod(name, Any).Invoke(o, args); }

        static void Shot(Window w, string file)
        {
            var el = (FrameworkElement)w.Content;
            el.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)el.ActualWidth, (int)el.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(el);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = File.Create(file)) enc.Save(f);
            Console.WriteLine("saved " + file);
        }
    }
}

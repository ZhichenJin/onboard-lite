using System;
using System.Collections;
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
    // Debug-only: renders the lighting page off-screen for a device that has no LEDs by flagging it as
    // 0x8071 in memory. Nothing is saved, so the mouse is never written.
    static class LightSnap
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
            var w = new MainWindow { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = 0 };

            Session session = null;
            Action<LightEffect> set = fx =>
            {
                session.Current.SetLighting(fx, 0x8071);
                Call(w, "RenderSection", false);
                Call(w, "UpdateSaveBar");
            };
            var steps = new Queue<Action>();
            steps.Enqueue(() =>
            {
                ((DispatcherTimer)Get(w, "tick")).Stop();
                session = FakeSession(args[1]);
                ((IList)Get(w, "sessions")).Add(session);
                Call(w, "OpenDevice", session);
                ((RadioButton)((FrameworkElement)Get(w, "device")).FindName("NavLight")).IsChecked = true;
            });
            steps.Enqueue(() => Shot(w, Path.Combine(outDir, "light-1-default.png")));
            steps.Enqueue(() => set(new LightEffect { Id = LightEffect.Fixed, R = 0x2D, G = 0x6B, B = 0xFF }));
            steps.Enqueue(() => Shot(w, Path.Combine(outDir, "light-2-fixed.png")));
            steps.Enqueue(() => set(new LightEffect { Id = LightEffect.Breathing, R = 0xFF, G = 0x3D, B = 0xAE, Period = 4000, Intensity = 80 }));
            steps.Enqueue(() => Shot(w, Path.Combine(outDir, "light-3-breathing.png")));
            steps.Enqueue(() => { Theme.Toggle(); set(new LightEffect { Id = LightEffect.Cycling, Period = 6000, Intensity = 100 }); });
            steps.Enqueue(() => Shot(w, Path.Combine(outDir, "light-4-cycling-othertheme.png")));
            steps.Enqueue(() => { Lang.Toggle(); });
            steps.Enqueue(() => Shot(w, Path.Combine(outDir, "light-5-cycling-otherlang.png")));
            steps.Enqueue(() =>
            {
                if (Theme.IsDark != startDark) Theme.Toggle();
                if (Lang.En != startEn) Lang.Toggle();
                session.Revert();
                w.Close();
            });

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            timer.Tick += (o, e) =>
            {
                if (steps.Count == 0) { timer.Stop(); return; }
                steps.Dequeue()();
            };
            w.Loaded += (o, e) => timer.Start();
            app.Run(w);
            Console.WriteLine("done");
        }


        // A device built from sector dumps of the real mouse, flagged as having 0x8071 lighting. No HID handle.
        static Session FakeSession(string dumps)
        {
            var dev = (MouseDevice)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(MouseDevice));
            dev.Name = "PRO X Wireless (RGB preview)"; dev.UnitId = ""; dev.Firmware = "";
            dev.Format = 4; dev.ProfileCount = 5; dev.OobCount = 1; dev.ButtonCount = 5; dev.SectorSize = 255;
            dev.DpiMin = 100; dev.DpiMax = 25600; dev.DpiStep = 50; dev.RatesMs = new[] { 8, 4, 2, 1 };
            dev.LightFeature = 0x8071;
            typeof(MouseDevice).GetField("Wireless").SetValue(dev, true);
            var s = new Session(dev, "preview#1");
            var dir = File.ReadAllBytes(Path.Combine(dumps, "dir.bin"));
            s.Dir = (byte[])dir.Clone(); s.DirBase = dir; s.DirClean = (byte[])dir.Clone();
            for (int i = 1; i <= 5; i++)
            {
                var raw = File.ReadAllBytes(Path.Combine(dumps, "prof_000" + i + ".bin"));
                s.Originals[i] = raw;
                s.Profiles.Add(new Profile { Sector = i, Base = raw, Raw = (byte[])raw.Clone() });
            }
            s.Active = 1; s.DpiIndex = 0; s.Mode = 1; s.Selected = 0;
            s.Battery = new BatteryState { Percent = 72 };
            s.Loaded = true;
            return s;
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
            Console.WriteLine("saved " + Path.GetFileName(file));
        }
    }
}

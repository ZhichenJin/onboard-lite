using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Threading;

[assembly: AssemblyTitle("Onboard Lite")]
[assembly: AssemblyProduct("Onboard Lite")]
[assembly: AssemblyDescription("Lightweight onboard memory editor for Logitech G mice")]
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

namespace OnboardLite
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                app.Resources = (ResourceDictionary)LoadXaml("theme.xaml");
                Theme.Init();
                Lang.Init();
                app.DispatcherUnhandledException += (s, e) =>
                {
                    MessageBox.Show(e.Exception.Message, "Onboard Lite", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.Handled = true;
                };
                app.Run(new MainWindow());
            }
            catch (Exception ex)
            {
                MessageBox.Show(Lang.T("启动失败：") + ex.Message, "Onboard Lite", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static object LoadXaml(string name)
        {
            using (var s = typeof(Program).Assembly.GetManifestResourceStream("OnboardLite." + name))
                return XamlReader.Load(s);
        }
    }

    sealed class FailedDevice
    {
        public string Name, Reason;
    }

    // Shell: title bar, theme switch, home page with one card per device, discovery and polling.
    // The device editor (second-level page) lives in DevicePage.cs.
    sealed partial class MainWindow : Window
    {
        const string SunIcon = "M12,8 A4,4 0 1 1 11.99,8 Z M12,2 V4 M12,20 V22 M4.9,4.9 L6.3,6.3 M17.7,17.7 L19.1,19.1 M2,12 H4 M20,12 H22 M4.9,19.1 L6.3,17.7 M17.7,6.3 L19.1,4.9";
        const string MoonIcon = "M20,14.5 A8,8 0 1 1 9.5,4 A6.5,6.5 0 0 0 20,14.5 Z";

        readonly FrameworkElement shell, home, device, brand;
        readonly ContentControl pageHost;
        readonly Border toast;
        readonly TextBlock toastText, homeNote;
        readonly Button btnBack, btnTheme, btnMax, btnLang;
        readonly Path themeIcon, maxIcon;
        readonly TranslateTransform langShift;
        readonly TextBlock langZh, langEn;
        readonly WrapPanel cards;
        readonly DispatcherTimer tick, toastTimer;

        List<Session> sessions = new List<Session>();
        Dictionary<string, FailedDevice> failed = new Dictionary<string, FailedDevice>();
        List<HidInfo> silent = new List<HidInfo>();
        bool scanning, ticking, firstScan = true;
        int tickCount;
        DispatcherTimer plugTimer;
        Session cur;

        public MainWindow()
        {
            Title = "Onboard Lite";
            Width = 1060; Height = 740; MinWidth = 920; MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
            FontSize = 14;
            UseLayoutRounding = true;
            SetResourceReference(BackgroundProperty, "Bg");
            SetResourceReference(ForegroundProperty, "Text");
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 56,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });

            shell = Load("shell.xaml");
            home = Load("home.xaml");
            device = Load("device.xaml");
            Content = shell;

            pageHost = F<ContentControl>(shell, "PageHost");
            toast = F<Border>(shell, "Toast");
            toastText = F<TextBlock>(shell, "ToastText");
            brand = F<FrameworkElement>(shell, "Brand");
            btnBack = F<Button>(shell, "BtnBack");
            btnTheme = F<Button>(shell, "BtnTheme");
            btnMax = F<Button>(shell, "BtnMax");
            themeIcon = F<Path>(shell, "ThemeIcon");
            btnLang = F<Button>(shell, "BtnLang");
            langShift = (TranslateTransform)F<Border>(shell, "LangThumb").RenderTransform;
            langZh = F<TextBlock>(shell, "LangZh");
            langEn = F<TextBlock>(shell, "LangEn");
            maxIcon = F<Path>(shell, "MaxIcon");
            cards = F<WrapPanel>(home, "Cards");
            homeNote = F<TextBlock>(home, "HomeNote");

            var btnMin = F<Button>(shell, "BtnMin");
            var btnClose = F<Button>(shell, "BtnClose");
            foreach (var b in new[] { btnBack, btnLang, btnTheme, btnMin, btnMax, btnClose }) WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            btnMin.Click += (s, e) => WindowState = WindowState.Minimized;
            btnMax.Click += (s, e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            btnClose.Click += (s, e) => Close();
            btnBack.Click += (s, e) => ShowHome();
            btnTheme.Click += (s, e) => ToggleTheme();
            btnLang.Click += (s, e) => Lang.Toggle();
            F<Button>(home, "BtnRescan").Click += (s, e) => Refresh(true);
            StateChanged += (s, e) =>
            {
                bool max = WindowState == WindowState.Maximized;
                shell.Margin = max ? new Thickness(7) : new Thickness(0);
                maxIcon.Data = Geometry.Parse(max ? "M2.5,0.5 H9.5 V7.5 M0.5,2.5 H7.5 V9.5 H0.5 Z" : "M0.5,0.5 H9.5 V9.5 H0.5 Z");
            };
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control && cur != null) { Save(); e.Handled = true; }
                else if (e.Key == Key.F5) { Refresh(true); e.Handled = true; }
            };

            Theme.Changed += () => UpdateThemeIcon(true);
            Lang.Changed += OnLanguageChanged;
            UpdateThemeIcon(false);
            UpdateLangPill(false);
            InitDevicePage();
            ShowHome();

            toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
            toastTimer.Tick += (s, e) => { toastTimer.Stop(); toast.Visibility = Visibility.Collapsed; };
            tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            tick.Tick += (s, e) => Tick();
            Loaded += (s, e) => { Refresh(false); tick.Start(); };
            SourceInitialized += (s, e) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook(WndProc);
            plugTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            plugTimer.Tick += (s, e) =>
            {
                // Scan right after the plug event settles, then once more for devices that start slowly.
                plugTimer.Stop();
                Refresh(false);
                var again = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
                again.Tick += (o, a) => { again.Stop(); Refresh(false); };
                again.Start();
            };
            Closing += OnClosing;
            Closed += (s, e) => { foreach (var x in sessions) { try { x.Dispose(); } catch { } } };
        }

        // ---------- helpers ----------

        static FrameworkElement Load(string name) { return (FrameworkElement)Program.LoadXaml(name); }
        static T F<T>(FrameworkElement root, string name) where T : class { return (T)root.FindName(name); }
        Style St(string key) { return (Style)FindResource(key); }

        static T Brushed<T>(T element, DependencyProperty property, string key) where T : FrameworkElement
        {
            element.SetResourceReference(property, key);
            return element;
        }

        TextBlock Txt(string text, string style, Thickness? margin = null)
        {
            var t = new TextBlock { Text = text, Style = St(style), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            if (margin.HasValue) t.Margin = margin.Value;
            return t;
        }

        Border Pill(string text, string fg, string bg)
        {
            var b = Brushed(new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3, 9, 3), VerticalAlignment = VerticalAlignment.Center }, Border.BackgroundProperty, bg);
            b.Child = Brushed(new TextBlock { Text = text, FontSize = 12 }, TextBlock.ForegroundProperty, fg);
            return b;
        }

        static FrameworkElement Glyph(double h, string brush)
        {
            var g = new Grid { Width = h * 0.64, Height = h, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            double stroke = Math.Max(1.5, h * 0.075);
            g.Children.Add(Brushed(new Border { CornerRadius = new CornerRadius(h * 0.32), BorderThickness = new Thickness(stroke) }, Border.BorderBrushProperty, brush));
            g.Children.Add(Brushed(new Border
            {
                Width = stroke, Height = h * 0.22, CornerRadius = new CornerRadius(stroke / 2),
                VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, h * 0.2, 0, 0)
            }, Border.BackgroundProperty, brush));
            return g;
        }

        bool Confirm(string text)
        {
            return MessageBox.Show(this, text, "Onboard Lite", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        void Toast(string text, bool error = false)
        {
            toastText.Text = text;
            toastText.SetResourceReference(TextBlock.ForegroundProperty, error ? "Danger" : "Text");
            toast.Visibility = Visibility.Visible;
            toastTimer.Stop();
            toastTimer.Start();
        }

        // Snapshot the current look, switch the palette underneath, then fade the snapshot out,
        // so dark <-> light blends over ~0.4 s instead of flashing.
        void ToggleTheme()
        {
            var host = (Grid)shell;
            Image cover = null;
            try
            {
                var toDevice = PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice;
                double w = host.ActualWidth, h = host.ActualHeight;
                var shot = new RenderTargetBitmap((int)Math.Ceiling(w * toDevice.M11), (int)Math.Ceiling(h * toDevice.M22),
                    96 * toDevice.M11, 96 * toDevice.M22, PixelFormats.Pbgra32);
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen()) dc.DrawRectangle(new VisualBrush(host), null, new Rect(0, 0, w, h));
                shot.Render(dv);
                cover = new Image { Source = shot, Stretch = Stretch.Fill, IsHitTestVisible = false };
                Grid.SetRowSpan(cover, 2);
                Panel.SetZIndex(cover, 100);
                host.Children.Add(cover);
            }
            catch { cover = null; }

            Theme.Toggle();
            if (cover == null) return;
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            fade.Completed += (s, e) => host.Children.Remove(cover);
            cover.BeginAnimation(OpacityProperty, fade);
        }

        // The new icon spins and fades in with the cross-fade.
        void UpdateThemeIcon(bool animate)
        {
            themeIcon.Data = Geometry.Parse(Theme.IsDark ? SunIcon : MoonIcon);
            btnTheme.ToolTip = Theme.IsDark ? Lang.T("切换到亮色") : Lang.T("切换到暗色");
            if (!animate) return;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            themeIcon.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-120, 0, TimeSpan.FromMilliseconds(450)) { EasingFunction = ease });
            themeIcon.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)));
        }

        // Slides the 文 | ENG thumb under the active language.
        void UpdateLangPill(bool animate)
        {
            double x = Lang.En ? 38 : 0;
            if (animate) langShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            else langShift.X = x;
            langZh.SetResourceReference(TextBlock.ForegroundProperty, Lang.En ? "Faint" : "Text");
            langEn.SetResourceReference(TextBlock.ForegroundProperty, Lang.En ? "Text" : "Faint");
            btnLang.ToolTip = Lang.En ? "切换到中文" : "Switch to English";
        }

        void OnLanguageChanged()
        {
            UpdateLangPill(true);
            UpdateThemeIcon(false);
            if (cur == null) RenderHome();
            else RefreshDevicePage();
        }

        static string PathOf(string key) { return key.Substring(0, key.LastIndexOf('#')); }

        // ---------- navigation ----------

        void ShowHome()
        {
            if (cur != null && cur.Busy) return;
            cur = null;
            pageHost.Content = home;
            btnBack.Visibility = Visibility.Collapsed;
            brand.Visibility = Visibility.Visible;
            RenderHome();
        }

        void OpenDevice(Session s)
        {
            cur = s;
            pageHost.Content = device;
            brand.Visibility = Visibility.Collapsed;
            btnBack.Visibility = Visibility.Visible;
            EnterDevice();
        }

        // ---------- discovery & polling ----------

        async void Refresh(bool user)
        {
            if (scanning) return;
            scanning = true;
            Log.W("refresh start");
            if (user) Toast(Lang.T("正在扫描设备…"));
            var skip = new HashSet<string>(sessions.Select(s => s.Key).Concat(failed.Keys));
            DiscoveryResult r = null;
            try { r = await Task.Run(() => Discovery.Run(skip)); }
            catch (Exception ex) { Log.W("refresh error " + ex); if (user) Toast(Lang.T("扫描失败：") + ex.Message, true); }
            scanning = false;
            Log.W(r == null ? "refresh end (no result)" : "refresh end: new " + r.Sessions.Count + ", failed " + r.Failed.Count + ", silent " + r.Silent.Count);
            firstScan = false;
            if (r == null) { if (cur == null) RenderHome(); return; }

            foreach (var s in r.Sessions)
            {
                var session = s;
                s.Dev.Io.Lost += () => Dispatcher.BeginInvoke(new Action(() => OnLost(session)));
                sessions.Add(s);
            }
            foreach (var kv in r.Failed) failed[kv.Key] = new FailedDevice { Name = NameFromKey(kv.Key), Reason = kv.Value };
            foreach (var key in failed.Keys.ToList()) if (!r.Paths.Contains(PathOf(key))) failed.Remove(key);
            silent = r.Silent;
            if (cur == null) RenderHome();
            if (user) Toast(sessions.Count == 0 ? Lang.T("没有找到可设置的设备") : Lang.T("已找到 ") + sessions.Count + Lang.T(" 个设备"));
        }

        // Windows broadcasts WM_DEVICECHANGE / DBT_DEVNODES_CHANGED to top-level windows whenever a device
        // is plugged in or removed; rescan shortly after instead of waiting for the next poll.
        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_DEVICECHANGE = 0x0219, DBT_DEVNODES_CHANGED = 0x0007, DBT_DEVICEARRIVAL = 0x8000, DBT_DEVICEREMOVECOMPLETE = 0x8004;
            if (msg == WM_DEVICECHANGE)
            {
                int ev = wParam.ToInt32();
                if (ev == DBT_DEVNODES_CHANGED || ev == DBT_DEVICEARRIVAL || ev == DBT_DEVICEREMOVECOMPLETE)
                {
                    Log.W("device change " + ev);
                    plugTimer.Stop();
                    plugTimer.Start();
                }
            }
            return IntPtr.Zero;
        }

        static string NameFromKey(string key)
        {
            var info = HidApi.Enumerate(MouseDevice.LogitechVid).FirstOrDefault(i => i.Path == PathOf(key));
            return info != null && info.Product.Length > 0 ? info.Product : Lang.T("罗技设备");
        }

        async void Tick()
        {
            Log.W("tick" + (ticking ? " (skipped)" : "") + (cur != null ? " device page" : ""));
            if (ticking) return;
            ticking = true;
            try
            {
                tickCount++;
                if (cur != null)
                {
                    await PollCurrent();
                    if (tickCount % 3 == 0) Refresh(false);   // keep finding newly plugged devices for the home page
                    return;
                }
                var list = sessions.Where(s => s.Loaded && !s.Busy).ToList();
                var results = await Task.Run(() => list.Select(s => s.Poll()).ToList());
                for (int i = 0; i < list.Count; i++) list[i].Apply(results[i]);
                if (cur == null) RenderHome();
                Refresh(false);
            }
            finally { ticking = false; }
        }

        void OnLost(Session s)
        {
            if (!sessions.Contains(s)) return;
            sessions.Remove(s);
            try { s.Dispose(); } catch { }
            if (cur == s)
            {
                s.Busy = false;
                ShowHome();
                Toast(Lang.T("“") + s.Dev.Name + Lang.T("”已断开"), true);
            }
            else if (cur == null) RenderHome();
        }

        void OnClosing(object sender, CancelEventArgs e)
        {
            if (sessions.Any(s => s.Busy)) { e.Cancel = true; Toast(Lang.T("正在写入设备，请稍候…")); return; }
            if (sessions.Any(s => s.HasChanges) && !Confirm(Lang.T("有未保存的更改，确定退出吗？"))) e.Cancel = true;
        }

        // ---------- home page ----------

        // One card per physical device: a mouse seen both by cable and receiver shares a unit id.
        List<Session> VisibleSessions()
        {
            return sessions
                .GroupBy(s => s.Dev.UnitId.Length > 0 ? s.Dev.UnitId : s.Key)
                .SelectMany(g => g.Any(s => s.HasChanges)
                    ? g.Where(s => s.HasChanges)
                    : g.OrderBy(s => s.Offline).ThenBy(s => s.Dev.Wireless).Take(1))
                .ToList();
        }

        void RenderHome()
        {
            cards.Children.Clear();
            foreach (var s in VisibleSessions()) cards.Children.Add(DeviceCard(s));
            foreach (var f in failed.Values) cards.Children.Add(InfoCard(f.Name, f.Reason, Lang.T("不支持"), "Faint", "Surface2"));

            var used = new HashSet<string>(sessions.Select(s => s.Path).Concat(failed.Keys.Select(PathOf)));
            var sleeping = silent.Where(i => !used.Contains(i.Path)).ToList();
            homeNote.Text = "";
            if (cards.Children.Count == 0)
            {
                if (firstScan) cards.Children.Add(Placeholder(Lang.T("正在查找设备…"), ""));
                else if (sleeping.Count > 0)
                    foreach (var i in sleeping) cards.Children.Add(InfoCard(Lang.T("接收器上的设备"), Lang.T("正在休眠，动一下鼠标即可唤醒"), Lang.T("休眠中"), "Muted", "Surface2"));
                else cards.Children.Add(Placeholder(Lang.T("没有找到设备"), Lang.T("用数据线或 LIGHTSPEED 接收器连接后会自动出现")));
            }
            else if (sleeping.Count > 0) homeNote.Text = Lang.T("另有 ") + sleeping.Count + Lang.T(" 个接收器上的设备在休眠，动一下即可唤醒。");
        }

        Button CardShell(bool enabled)
        {
            return new Button { Style = St("CardButton"), Width = 316, Height = 184, Margin = new Thickness(0, 0, 18, 18), IsEnabled = enabled };
        }

        FrameworkElement CardBody(FrameworkElement glyph, Border status, string name, string sub, FrameworkElement bottom)
        {
            var g = new Grid();
            for (int i = 0; i < 4; i++) g.RowDefinitions.Add(new RowDefinition { Height = i == 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            var top = new DockPanel();
            if (status != null) { DockPanel.SetDock(status, Dock.Right); status.VerticalAlignment = VerticalAlignment.Top; top.Children.Add(status); }
            var icon = Brushed(new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(13), HorizontalAlignment = HorizontalAlignment.Left, Child = glyph }, Border.BackgroundProperty, "AccentSoft");
            top.Children.Add(icon);
            g.Children.Add(top);

            var title = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            title.Children.Add(new TextBlock { Text = name, Style = St("H2"), FontSize = 17, TextTrimming = TextTrimming.CharacterEllipsis });
            title.Children.Add(Txt(sub, "Hint", new Thickness(0, 5, 0, 0)));
            Grid.SetRow(title, 2);
            g.Children.Add(title);
            if (bottom != null)
            {
                bottom.Margin = new Thickness(0, 14, 0, 0);
                Grid.SetRow(bottom, 3);
                g.Children.Add(bottom);
            }
            return g;
        }

        FrameworkElement DeviceCard(Session s)
        {
            bool usable = s.Loaded && s.Dev.Editable;
            Border status;
            if (!usable) status = Pill(Lang.T("暂不支持"), "Faint", "Surface2");
            else if (s.Offline) status = Pill(Lang.T("休眠中"), "Muted", "Surface2");
            else if (s.HasChanges) status = Pill(Lang.T("未保存"), "Amber", "AmberSoft");
            else if (s.Mode == 2) status = Pill(Lang.T("主机模式"), "Amber", "AmberSoft");
            else status = Pill(Lang.T("在线"), "Good", "GoodSoft");

            var bottom = new DockPanel();
            if (s.Battery != null && s.Battery.Percent >= 0)
            {
                var bat = BatteryMini(s.Battery);
                DockPanel.SetDock(bat, Dock.Right);
                bottom.Children.Add(bat);
            }
            bottom.Children.Add(Txt(usable ? Summary(s) : Lang.T("配置格式 ") + s.Dev.Format + Lang.T("，本工具支持格式 1–5"), "Hint"));

            var btn = CardShell(usable);
            btn.Content = CardBody(Glyph(26, usable ? "Accent" : "Faint"), status, s.Dev.Name,
                s.Dev.Wireless ? Lang.T("无线 · LIGHTSPEED 接收器") : Lang.T("有线 · USB"), bottom);
            if (usable) btn.Click += (o, e) => OpenDevice(s);
            return btn;
        }

        string Summary(Session s)
        {
            var p = s.ActiveProfile;
            if (p == null || s.Mode == 2) return s.Mode == 2 ? Lang.T("由主机软件控制") : "";
            var dpis = Profile.ReadDpis(p.Base);
            int idx = s.DpiIndex >= 0 && s.DpiIndex < dpis.Count ? s.DpiIndex : Math.Min(p.Base[1], Math.Max(0, dpis.Count - 1));
            string dpi = dpis.Count > 0 ? dpis[idx] + " DPI" : "";
            int rate = p.Base[0];
            return s.Title(p) + " · " + dpi + (rate > 0 && rate < 0xFF ? " · " + (1000 / rate) + " Hz" : "");
        }

        FrameworkElement BatteryMini(BatteryState b)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var track = Brushed(new Border { Width = 34, Height = 6, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center }, Border.BackgroundProperty, "Surface2");
            var fill = new Grid();
            fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1, b.Percent), GridUnitType.Star) });
            fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, 100 - b.Percent), GridUnitType.Star) });
            fill.Children.Add(Brushed(new Border { CornerRadius = new CornerRadius(3) }, Border.BackgroundProperty, b.Percent <= 15 && !b.Charging ? "Danger" : "Good"));
            track.Child = fill;
            sp.Children.Add(track);
            sp.Children.Add(Txt((b.Charging ? Lang.T("充电 ") : "") + b.Percent + "%", "Hint", new Thickness(8, 0, 0, 0)));
            return sp;
        }

        FrameworkElement InfoCard(string name, string sub, string status, string fg, string bg)
        {
            var btn = CardShell(false);
            btn.Content = CardBody(Glyph(26, "Faint"), Pill(status, fg, bg), name, sub, null);
            return btn;
        }

        FrameworkElement Placeholder(string title, string sub)
        {
            var g = new Grid { Width = 316, Height = 184, Margin = new Thickness(0, 0, 18, 18) };
            g.Children.Add(Brushed(new Rectangle { RadiusX = 18, RadiusY = 18, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 } }, Shape.StrokeProperty, "Track"));
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(24) };
            sp.Children.Add(new TextBlock { Text = title, Style = St("H2"), HorizontalAlignment = HorizontalAlignment.Center });
            if (sub.Length > 0) sp.Children.Add(new TextBlock { Text = sub, Style = St("Hint"), Margin = new Thickness(0, 8, 0, 0), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
            g.Children.Add(sp);
            return g;
        }
    }
}

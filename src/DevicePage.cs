using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace OnboardLite
{
    // Second-level page for one device: left menu (性能 / 按键 / 配置管理 / 设备信息), section content, save bar.
    sealed partial class MainWindow
    {
        static readonly string[] ButtonNamesZh = { "左键", "右键", "中键", "后侧键", "前侧键" };
        static readonly string[] ButtonNamesEn = { "Left", "Right", "Middle", "Back side", "Front side" };

        TextBlock devName, devSub, batteryText, sectionTitle, sectionSub, bannerText, saveText, navFoot, dpiNow;
        Border devGlyph, batteryTrack, banner, saveBar;
        FrameworkElement batteryRow;
        StackPanel profileBar;
        ContentControl sectionHost;
        ScrollViewer sectionScroll;
        Button bannerBtn, btnSave, btnRevert;
        Ellipse saveDot;
        RadioButton navPerf, navLight;
        Border lightPreview;
        DispatcherTimer savedTimer;
        string section = "perf", barMarks = "";
        bool building, justSaved;
        Action bannerAction;

        void InitDevicePage()
        {
            devName = F<TextBlock>(device, "DevName");
            devSub = F<TextBlock>(device, "DevSub");
            batteryText = F<TextBlock>(device, "BatteryText");
            sectionTitle = F<TextBlock>(device, "SectionTitle");
            sectionSub = F<TextBlock>(device, "SectionSub");
            bannerText = F<TextBlock>(device, "BannerText");
            saveText = F<TextBlock>(device, "SaveText");
            navFoot = F<TextBlock>(device, "NavFoot");
            devGlyph = F<Border>(device, "DevGlyph");
            batteryTrack = F<Border>(device, "BatteryTrack");
            banner = F<Border>(device, "Banner");
            saveBar = F<Border>(device, "SaveBar");
            batteryRow = F<FrameworkElement>(device, "BatteryRow");
            profileBar = F<StackPanel>(device, "ProfileBar");
            sectionHost = F<ContentControl>(device, "SectionHost");
            sectionScroll = F<ScrollViewer>(device, "SectionScroll");
            bannerBtn = F<Button>(device, "BannerBtn");
            btnSave = F<Button>(device, "BtnSave");
            btnRevert = F<Button>(device, "BtnRevert");
            saveDot = F<Ellipse>(device, "SaveDot");
            navPerf = F<RadioButton>(device, "NavPerf");
            navLight = F<RadioButton>(device, "NavLight");

            foreach (var name in new[] { "NavPerf", "NavButtons", "NavLight", "NavProfiles", "NavInfo" })
            {
                var rb = F<RadioButton>(device, name);
                rb.Checked += (s, e) =>
                {
                    if (building) return;
                    section = (string)((RadioButton)s).Tag;
                    RenderSection(true);
                };
            }
            btnSave.Click += (s, e) => Save();
            btnRevert.Click += (s, e) => Revert();
            bannerBtn.Click += (s, e) => { if (bannerAction != null) bannerAction(); };
            savedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            savedTimer.Tick += (s, e) => { savedTimer.Stop(); justSaved = false; UpdateSaveBar(); };
        }

        void EnterDevice()
        {
            section = "perf";
            justSaved = false;
            building = true;
            navPerf.IsChecked = true;
            navLight.Visibility = cur.Dev.LightFeature != 0 ? Visibility.Visible : Visibility.Collapsed;
            building = false;
            barMarks = "";
            RenderDevCard();
            RenderProfileBar(true);
            RenderBanner();
            RenderSection(true);
            UpdateSaveBar();
        }

        // Redraws everything on the device page, e.g. after a language switch.
        void RefreshDevicePage()
        {
            barMarks = "";
            RenderDevCard();
            RenderProfileBar(true);
            RenderBanner();
            RenderSection(false);
            UpdateSaveBar();
        }

        // ---------- chrome around the sections ----------

        void RenderDevCard()
        {
            var s = cur;
            if (s == null) return;
            devGlyph.Child = Glyph(26, "Accent");
            devName.Text = s.Dev.Name;
            devSub.Text = (s.Dev.Wireless ? Lang.T("无线 · 接收器") : Lang.T("有线 · USB")) + (s.Offline ? Lang.T(" · 休眠中") : "");
            var b = s.Battery;
            batteryRow.Visibility = b != null && b.Percent >= 0 ? Visibility.Visible : Visibility.Collapsed;
            if (b != null && b.Percent >= 0)
            {
                var fill = new Grid();
                fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1, b.Percent), GridUnitType.Star) });
                fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, 100 - b.Percent), GridUnitType.Star) });
                fill.Children.Add(Brushed(new Border { CornerRadius = new CornerRadius(3) }, Border.BackgroundProperty, b.Percent <= 15 && !b.Charging ? "Danger" : "Good"));
                batteryTrack.Child = fill;
                batteryText.Text = (b.Charging ? Lang.T("充电中 ") : b.Full ? Lang.T("已充满 ") : "") + b.Percent + "%";
            }
            navFoot.Text = s.Mode == 2 ? Lang.T("主机模式：设置由 G HUB 等软件实时控制") : Lang.T("板载模式：设置保存在设备里，换电脑也生效");
        }

        void RenderBanner()
        {
            var s = cur;
            bannerAction = null;
            bannerBtn.Visibility = Visibility.Collapsed;
            if (s.Mode == 2)
            {
                bannerText.Text = Lang.T("设备处于主机模式，由 G HUB 等软件实时控制，板载配置暂不生效。");
                bannerBtn.Content = Lang.T("切换到板载模式");
                bannerBtn.Visibility = Visibility.Visible;
                bannerAction = SwitchToOnboard;
            }
            else if (s.Offline) bannerText.Text = Lang.T("设备在休眠或信号中断。动一下鼠标唤醒后才能保存。");
            else { banner.Visibility = Visibility.Collapsed; return; }
            banner.Visibility = Visibility.Visible;
        }

        void RenderProfileBar(bool force)
        {
            var s = cur;
            string marks = section + "|" + string.Join(",", s.Profiles.Select((p, i) =>
                (i == s.Selected ? "s" : "") + (s.Enabled(p) ? "e" : "") + (p.Dirty ? "d" : "") + (p.Sector == s.Active ? "a" : "") + s.Title(p)));
            if (!force && marks == barMarks) return;
            barMarks = marks;

            building = true;
            profileBar.Children.Clear();
            profileBar.Children.Add(Txt(Lang.T("配置"), "Hint", new Thickness(0, 0, 4, 0)));
            for (int i = 0; i < s.Profiles.Count; i++)
            {
                var p = s.Profiles[i];
                int index = i;
                bool on = s.Enabled(p), live = p.Sector == s.Active && s.Mode != 2;
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(new TextBlock { Text = (i + 1).ToString(), FontWeight = FontWeights.SemiBold });
                if (live) content.Children.Add(Dot("Good"));
                if (p.Dirty) content.Children.Add(Dot("Amber"));
                var rb = new RadioButton
                {
                    Style = St("Pill"), GroupName = "profile", Content = content, IsChecked = i == s.Selected, Opacity = on ? 1 : 0.5,
                    ToolTip = s.Title(p) + (on ? "" : Lang.T(" · 未启用")) + (live ? Lang.T(" · 正在使用") : "") + (p.Dirty ? Lang.T(" · 有未保存的更改") : "")
                };
                rb.Checked += (o, e) => { if (!building) SelectProfile(index); };
                profileBar.Children.Add(rb);
            }
            building = false;
        }

        Ellipse Dot(string brush)
        {
            return Brushed(new Ellipse { Width = 6, Height = 6, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }, Shape.FillProperty, brush);
        }

        void RenderSection(bool scrollTop)
        {
            var s = cur;
            if (s == null) return;
            bool perProfile = section == "perf" || section == "buttons" || section == "light";
            profileBar.Visibility = perProfile ? Visibility.Visible : Visibility.Collapsed;
            RenderProfileBar(true);
            string editing = Lang.T("正在编辑：") + s.Title(s.Current);
            FrameworkElement content;
            switch (section)
            {
                case "buttons":
                    sectionTitle.Text = Lang.T("按键");
                    sectionSub.Text = Lang.T("为每个按键指定功能 · ") + editing;
                    content = ButtonsSection();
                    break;
                case "light":
                    sectionTitle.Text = Lang.T("灯光");
                    sectionSub.Text = Lang.T("灯光效果与颜色 · ") + editing;
                    content = LightSection();
                    break;
                case "profiles":
                    sectionTitle.Text = Lang.T("配置管理");
                    sectionSub.Text = Lang.T("设备最多保存 ") + s.Profiles.Count + Lang.T(" 套配置，“切换配置”按键会在已启用的配置之间循环");
                    content = ProfilesSection();
                    break;
                case "info":
                    sectionTitle.Text = Lang.T("设备信息");
                    sectionSub.Text = Lang.T("连接、固件与板载内存");
                    content = InfoSection();
                    break;
                default:
                    sectionTitle.Text = Lang.T("性能");
                    sectionSub.Text = Lang.T("DPI、回报率与移动 · ") + editing;
                    content = PerfSection();
                    break;
            }
            sectionHost.Content = content;
            if (scrollTop) sectionScroll.ScrollToTop();
        }

        Border Card(string title, string sub, FrameworkElement right, FrameworkElement body)
        {
            var card = new Border { Style = St("Card"), Margin = new Thickness(0, 0, 0, 16) };
            var sp = new StackPanel();
            if (title != null)
            {
                var head = new DockPanel();
                if (right != null)
                {
                    DockPanel.SetDock(right, Dock.Right);
                    right.VerticalAlignment = VerticalAlignment.Center;
                    head.Children.Add(right);
                }
                var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                titles.Children.Add(new TextBlock { Text = title, Style = St("H2") });
                if (sub != null) titles.Children.Add(Txt(sub, "Hint", new Thickness(0, 5, 16, 0)));
                head.Children.Add(titles);
                sp.Children.Add(head);
            }
            if (body != null)
            {
                if (title != null) body.Margin = new Thickness(0, 18, 0, 0);
                sp.Children.Add(body);
            }
            card.Child = sp;
            return card;
        }

        Border Divider()
        {
            return Brushed(new Border { Height = 1, Margin = new Thickness(0, 12, 0, 12) }, Border.BackgroundProperty, "Line");
        }

        Border Badge(string text)
        {
            var b = Brushed(new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left }, Border.BackgroundProperty, "Surface2");
            b.Child = Brushed(new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, TextBlock.ForegroundProperty, "Muted");
            return b;
        }

        // ---------- 性能 ----------

        FrameworkElement PerfSection()
        {
            var s = cur;
            var p = s.Current;
            var d = s.Dev;
            var stack = new StackPanel();

            var rows = new StackPanel();
            BuildDpiRows(rows, p);
            dpiNow = Txt("", "Hint");
            UpdateDpiNow();
            stack.Children.Add(Card(Lang.T("DPI 档位"),
                Lang.T("范围 ") + d.DpiMin + "–" + d.DpiMax + (d.DpiList == null ? Lang.T("，步进 ") + d.DpiStep : "") + Lang.T("，最多 ") + Profile.MaxDpi + Lang.T(" 档。可以拖动滑杆或直接输入数值"),
                dpiNow, rows));

            var seg = new UniformGrid { Rows = 1 };
            building = true;
            foreach (var ms in d.RatesMs)
            {
                int m = ms;
                var rb = new RadioButton { Style = St("Seg"), GroupName = "rate", Content = (1000 / m) + " Hz", IsChecked = p.RateMs == m };
                rb.Checked += (o, e) => { if (building) return; cur.Current.RateMs = m; Changed(); };
                seg.Children.Add(rb);
            }
            building = false;
            stack.Children.Add(Card(Lang.T("回报率"), Lang.T("每秒向电脑报告位置的次数，越高延迟越低、耗电越多"), null, seg));

            var snap = new CheckBox { Style = St("Switch"), IsChecked = p.AngleSnapping };
            snap.Click += (o, e) => { cur.Current.AngleSnapping = snap.IsChecked == true; Changed(); };
            stack.Children.Add(Card(Lang.T("角度捕捉"), Lang.T("把略微倾斜的移动修正成水平或垂直直线，FPS 游戏一般建议关闭"), snap, null));
            return stack;
        }

        void UpdateDpiNow()
        {
            var s = cur;
            if (dpiNow == null || s == null) return;
            var p = s.Current;
            if (p == null || s.Mode == 2 || p.Sector != s.Active || s.Offline) { dpiNow.Text = ""; return; }
            var saved = Profile.ReadDpis(p.Base);
            dpiNow.Text = s.DpiIndex >= 0 && s.DpiIndex < saved.Count ? Lang.T("设备当前：第 ") + (s.DpiIndex + 1) + Lang.T(" 档 · ") + saved[s.DpiIndex] : "";
        }

        void BuildDpiRows(StackPanel rows, Profile p)
        {
            building = true;
            rows.Children.Clear();
            var dpis = p.Dpis;
            for (int i = 0; i < dpis.Count; i++) rows.Children.Add(DpiRow(rows, p, dpis, i));
            if (dpis.Count == 0) rows.Children.Add(Txt(Lang.T("这个配置没有 DPI 档位"), "Hint", new Thickness(0, 0, 0, 8)));

            var foot = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var legend = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            legend.Children.Add(Brushed(new Ellipse { Width = 10, Height = 10, StrokeThickness = 2.5, VerticalAlignment = VerticalAlignment.Center }, Shape.StrokeProperty, "Accent"));
            legend.Children.Add(Txt(Lang.T("默认档位"), "Hint", new Thickness(6, 0, 18, 0)));
            var tag = Brushed(new Border { Width = 18, Height = 15, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center }, Border.BorderBrushProperty, "Amber");
            tag.SetResourceReference(Border.BackgroundProperty, "AmberSoft");
            tag.Child = Brushed(new TextBlock { Text = "S", FontSize = 9, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, TextBlock.ForegroundProperty, "Amber");
            legend.Children.Add(tag);
            legend.Children.Add(Txt(Lang.T("按住 DPI Shift 键时使用"), "Hint", new Thickness(6, 0, 0, 0)));
            if (dpis.Count < Profile.MaxDpi)
            {
                var add = new Button { Style = St("Mini"), Content = Lang.T("+ 添加档位") };
                add.Click += (o, e) =>
                {
                    var list = p.Dpis;
                    list.Add(cur.Dev.Snap(list.Count > 0 ? Math.Min(cur.Dev.DpiMax, list[list.Count - 1] * 2) : 800));
                    p.SetDpis(list);
                    BuildDpiRows(rows, p);
                    Changed();
                };
                DockPanel.SetDock(add, Dock.Right);
                foot.Children.Add(add);
            }
            foot.Children.Add(legend);
            rows.Children.Add(Divider());
            rows.Children.Add(foot);
            building = false;
        }

        FrameworkElement DpiRow(StackPanel rows, Profile p, List<int> dpis, int i)
        {
            var d = cur.Dev;
            var g = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            foreach (var w in new[] { 30.0, 0, 100, 46, 38 })
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = w == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });

            var def = new RadioButton { Style = St("Dot"), GroupName = "dpi-default", IsChecked = p.DefaultIndex == i, ToolTip = Lang.T("设为默认档位（开机或切换到此配置时使用）"), VerticalAlignment = VerticalAlignment.Center };
            def.Checked += (o, e) => { if (building) return; p.DefaultIndex = i; Changed(); };

            var slider = new Slider { Minimum = Math.Log(d.DpiMin), Maximum = Math.Log(d.DpiMax), Value = Math.Log(dpis[i]), Margin = new Thickness(10, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            var box = new TextBox { Text = dpis[i].ToString(), HorizontalContentAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, MaxLength = 5 };
            bool sync = false;
            slider.ValueChanged += (o, e) =>
            {
                if (building || sync) return;
                int v = d.Snap((int)Math.Round(Math.Exp(slider.Value)));
                sync = true;
                box.Text = v.ToString();
                sync = false;
                SetDpi(p, i, v);
            };
            Action commit = () =>
            {
                if (sync) return;
                int v;
                if (!int.TryParse(box.Text.Trim(), out v)) v = i < p.Dpis.Count ? p.Dpis[i] : d.DpiMin;
                v = d.Snap(v);
                sync = true;
                box.Text = v.ToString();
                slider.Value = Math.Log(v);
                sync = false;
                SetDpi(p, i, v);
            };
            box.LostKeyboardFocus += (o, e) => commit();
            box.KeyDown += (o, e) => { if (e.Key == Key.Enter) commit(); };
            box.PreviewTextInput += (o, e) => { e.Handled = !e.Text.All(char.IsDigit); };

            var shift = new RadioButton { Style = St("ShiftTag"), GroupName = "dpi-shift", Content = "S", IsChecked = p.ShiftIndex == i, ToolTip = Lang.T("按住 DPI Shift 键时切换到这一档"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            shift.Checked += (o, e) => { if (building) return; p.ShiftIndex = i; Changed(); };

            var del = new Button { Style = St("Mini"), Content = "✕", ToolTip = Lang.T("删除这一档"), VerticalAlignment = VerticalAlignment.Center, Visibility = dpis.Count > 1 ? Visibility.Visible : Visibility.Hidden };
            del.Click += (o, e) =>
            {
                var list = p.Dpis;
                if (list.Count <= 1) return;
                int d0 = p.DefaultIndex, s0 = p.ShiftIndex;
                list.RemoveAt(i);
                p.SetDpis(list);
                p.DefaultIndex = d0 == i ? 0 : d0 > i ? d0 - 1 : d0;
                p.ShiftIndex = s0 == i ? 0 : s0 > i ? s0 - 1 : s0;
                BuildDpiRows(rows, p);
                Changed();
            };

            Grid.SetColumn(slider, 1);
            Grid.SetColumn(box, 2);
            Grid.SetColumn(shift, 3);
            Grid.SetColumn(del, 4);
            g.Children.Add(def);
            g.Children.Add(slider);
            g.Children.Add(box);
            g.Children.Add(shift);
            g.Children.Add(del);
            return g;
        }

        void SetDpi(Profile p, int i, int value)
        {
            var list = p.Dpis;
            if (i >= list.Count || list[i] == value) return;
            list[i] = value;
            p.SetDpis(list);
            Changed();
        }

        // ---------- 灯光 ----------

        static readonly int[] LightIds = { LightEffect.Off, LightEffect.Fixed, LightEffect.Breathing, LightEffect.Cycling };
        static readonly string[] LightNamesZh = { "关闭", "常亮", "呼吸", "色彩循环" };
        static readonly string[] LightNamesEn = { "Off", "Static", "Breathing", "Color cycle" };
        static readonly string[] Swatches = { "#FF2D2D", "#FF7A00", "#FFD000", "#3DFF5A", "#00E5FF", "#2D6BFF", "#8A3DFF", "#FF3DAE", "#FFFFFF" };

        // One effect per profile, written to every lighting slot, the way OnboardMemoryManager does it.
        FrameworkElement LightSection()
        {
            var s = cur;
            var fx = s.Current.GetLighting();
            var stack = new StackPanel();

            var seg = new UniformGrid { Rows = 1 };
            building = true;
            for (int i = 0; i < LightIds.Length; i++)
            {
                int id = LightIds[i];
                var rb = new RadioButton { Style = St("Seg"), GroupName = "light-effect", Content = (Lang.En ? LightNamesEn : LightNamesZh)[i], IsChecked = fx.Id == id };
                rb.Checked += (o, e) =>
                {
                    if (building) return;
                    var next = cur.Current.GetLighting();
                    if (!next.Known) next = new LightEffect();
                    next.Id = id;
                    ApplyLight(next);
                    RenderSection(false);
                };
                seg.Children.Add(rb);
            }
            building = false;
            string sub = fx.Id == LightEffect.Unset ? Lang.T("当前为设备默认灯效，选择一种效果后保存即可写入")
                : !fx.Known ? Lang.T("当前是本工具不支持编辑的灯效（例如 G HUB 设置的特殊效果），选择一种效果会替换它")
                : Lang.T("整套配置使用同一种灯效，作用于设备的所有灯区");
            lightPreview = new Border { Width = 132, Height = 46, CornerRadius = new CornerRadius(12) };
            UpdateLightPreview(fx);
            stack.Children.Add(Card(Lang.T("灯光效果"), sub, lightPreview, seg));

            if (fx.HasColor) stack.Children.Add(Card(Lang.T("颜色"), null, null, ColorPicker(fx)));
            if (fx.HasSpeed)
            {
                var sliders = new StackPanel();
                sliders.Children.Add(SliderRow(Lang.T("速度"), 1000, 20000, 500, fx.Period, v => (v / 1000.0).ToString("0.0") + Lang.T(" 秒 / 周期"),
                    v => { var f = cur.Current.GetLighting(); f.Period = v; ApplyLight(f); UpdateLightPreview(f); }));
                sliders.Children.Add(Divider());
                sliders.Children.Add(SliderRow(Lang.T("亮度"), 10, 100, 5, fx.Intensity, v => v + "%",
                    v => { var f = cur.Current.GetLighting(); f.Intensity = v; ApplyLight(f); UpdateLightPreview(f); }));
                stack.Children.Add(Card(Lang.T("速度与亮度"), null, null, sliders));
            }
            string foot = (s.Dev.LightFeature == 0x8071 ? Lang.T("设备进入省电状态时，会自动换成一半亮度的同款灯效。") : "")
                + Lang.T("灯光和其他设置一样，点“保存到设备”后生效。");
            stack.Children.Add(Txt(foot, "Hint", new Thickness(4, 0, 0, 0)));
            return stack;
        }

        void ApplyLight(LightEffect fx)
        {
            cur.Current.SetLighting(fx, cur.Dev.LightFeature);
            Changed();
        }

        static string Hex(byte r, byte g, byte b) { return "#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2"); }

        static Color FromHue(double h)
        {
            double x = 1 - Math.Abs((h / 60) % 2 - 1);
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = 1; g = x; }
            else if (h < 120) { r = x; g = 1; }
            else if (h < 180) { g = 1; b = x; }
            else if (h < 240) { g = x; b = 1; }
            else if (h < 300) { r = x; b = 1; }
            else { r = 1; b = x; }
            return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        static double HueOf(byte r, byte g, byte b)
        {
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            if (d == 0) return 0;
            double h = max == r ? ((g - b) / d) % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            h *= 60;
            return h < 0 ? h + 360 : h;
        }

        FrameworkElement ColorPicker(LightEffect fx)
        {
            var panel = new StackPanel();
            string now = Hex(fx.R, fx.G, fx.B);
            var swatches = new WrapPanel();
            building = true;
            foreach (var hex in Swatches)
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                var rb = new RadioButton
                {
                    Style = St("Swatch"), GroupName = "swatch", Background = new SolidColorBrush(c), ToolTip = hex,
                    IsChecked = string.Equals(hex, now, StringComparison.OrdinalIgnoreCase)
                };
                rb.Checked += (o, e) => { if (!building) SetLightColor(c, true); };
                swatches.Children.Add(rb);
            }
            building = false;
            panel.Children.Add(swatches);

            var row = new Grid { Margin = new Thickness(0, 18, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            var hue = new Slider { Style = St("HueSlider"), Value = HueOf(fx.R, fx.G, fx.B), Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = Lang.T("自定义颜色") };
            var box = new TextBox { Text = now, MaxLength = 7, VerticalAlignment = VerticalAlignment.Center };
            bool sync = false;
            hue.ValueChanged += (o, e) =>
            {
                if (building || sync) return;
                var c = FromHue(hue.Value);
                sync = true;
                box.Text = Hex(c.R, c.G, c.B);
                sync = false;
                SetLightColor(c, false);
            };
            Action commit = () =>
            {
                if (sync) return;
                string text = box.Text.Trim();
                Color c;
                try { c = (Color)ColorConverter.ConvertFromString(text.StartsWith("#") ? text : "#" + text); }
                catch
                {
                    var f = cur.Current.GetLighting();
                    box.Text = Hex(f.R, f.G, f.B);
                    return;
                }
                SetLightColor(c, true);
            };
            box.LostKeyboardFocus += (o, e) => commit();
            box.KeyDown += (o, e) => { if (e.Key == Key.Enter) commit(); };
            Grid.SetColumn(box, 1);
            row.Children.Add(hue);
            row.Children.Add(box);
            panel.Children.Add(row);
            return panel;
        }

        // rerender = false while dragging the hue slider, so the drag is not interrupted.
        void SetLightColor(Color c, bool rerender)
        {
            var fx = cur.Current.GetLighting();
            if (fx.R == c.R && fx.G == c.G && fx.B == c.B) return;
            fx.R = c.R;
            fx.G = c.G;
            fx.B = c.B;
            ApplyLight(fx);
            if (rerender) RenderSection(false);
            else UpdateLightPreview(fx);
        }

        FrameworkElement SliderRow(string label, int min, int max, int step, int value, Func<int, string> format, Action<int> changed)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            var name = new TextBlock { Text = label, Style = St("Body"), VerticalAlignment = VerticalAlignment.Center };
            var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            var shown = Txt(format((int)slider.Value), "Hint");
            shown.HorizontalAlignment = HorizontalAlignment.Right;
            slider.ValueChanged += (o, e) =>
            {
                int v = (int)(Math.Round(slider.Value / step) * step);
                shown.Text = format(v);
                if (!building) changed(v);
            };
            Grid.SetColumn(slider, 1);
            Grid.SetColumn(shown, 2);
            g.Children.Add(name);
            g.Children.Add(slider);
            g.Children.Add(shown);
            return g;
        }

        // A small animated swatch: breathing pulses, cycling walks the hue wheel over one period.
        void UpdateLightPreview(LightEffect fx)
        {
            var box = lightPreview;
            if (box == null) return;
            box.BeginAnimation(OpacityProperty, null);
            box.Opacity = 1;
            box.Effect = null;
            box.Child = null;
            if (fx.Id == LightEffect.Fixed || fx.Id == LightEffect.Breathing || fx.Id == LightEffect.Cycling)
            {
                var color = Color.FromRgb(fx.R, fx.G, fx.B);
                var brush = new SolidColorBrush(color);
                box.Background = brush;
                var glow = new System.Windows.Media.Effects.DropShadowEffect { Color = color, BlurRadius = 26, ShadowDepth = 0, Opacity = 0.75 };
                box.Effect = glow;
                double peak = fx.Id == LightEffect.Fixed ? 1 : Math.Max(0.3, fx.Intensity / 100.0);
                box.Opacity = peak;
                if (fx.Id == LightEffect.Breathing)
                {
                    var pulse = new DoubleAnimation(peak, 0.12, TimeSpan.FromMilliseconds(Math.Max(300, fx.Period / 2)))
                    {
                        AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                    };
                    box.BeginAnimation(OpacityProperty, pulse);
                }
                else if (fx.Id == LightEffect.Cycling)
                {
                    var walk = new ColorAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(Math.Max(500, fx.Period)), RepeatBehavior = RepeatBehavior.Forever };
                    for (int i = 0; i <= 6; i++)
                        walk.KeyFrames.Add(new LinearColorKeyFrame(FromHue((i * 60) % 360), KeyTime.FromPercent(i / 6.0)));
                    brush.BeginAnimation(SolidColorBrush.ColorProperty, walk);
                    glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.ColorProperty, walk);
                }
            }
            else
            {
                box.SetResourceReference(Border.BackgroundProperty, "Surface2");
                box.Child = Brushed(new TextBlock
                {
                    Text = fx.Id == LightEffect.Off ? (Lang.En ? "Off" : "已关闭") : fx.Id == LightEffect.Unset ? Lang.T("设备默认") : Lang.T("其他灯效"),
                    FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }, TextBlock.ForegroundProperty, "Muted");
            }
        }

        // ---------- 按键 ----------

        FrameworkElement ButtonsSection()
        {
            var s = cur;
            var p = s.Current;
            var list = new StackPanel();
            int n = Math.Min(s.Dev.ButtonCount, Profile.MaxButtons);
            building = true;
            for (int i = 0; i < n; i++)
            {
                int index = i;
                if (i > 0) list.Children.Add(Divider());
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
                row.Children.Add(Badge((i + 1).ToString()));
                var label = new TextBlock { Text = i < ButtonNamesZh.Length ? (Lang.En ? ButtonNamesEn : ButtonNamesZh)[i] : Lang.T("按键 ") + (i + 1), Style = St("Body"), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(label, 1);
                row.Children.Add(label);
                var combo = new ComboBox();
                FillActions(combo, p.GetButton(i));
                combo.SelectionChanged += (o, e) => OnActionPicked(combo, index);
                Grid.SetColumn(combo, 2);
                row.Children.Add(combo);
                list.Children.Add(row);
            }
            building = false;
            var stack = new StackPanel();
            stack.Children.Add(Card(null, null, null, list));
            stack.Children.Add(Txt(Lang.T("每个配置至少要保留一个“左键”。键盘按键支持组合键，例如 Ctrl + C。"), "Hint", new Thickness(4, 0, 0, 0)));
            return stack;
        }

        void FillActions(ComboBox combo, byte[] current)
        {
            ComboBoxItem selected = null;
            if (Actions.Find(current) == null)
            {
                selected = new ComboBoxItem { Content = Actions.Describe(current), Tag = current };
                combo.Items.Add(selected);
            }
            string group = null;
            foreach (var a in Actions.Catalog)
            {
                if (a.Group != group) combo.Items.Add(new ComboBoxItem { Content = Lang.T(a.Group), IsEnabled = false, FontSize = 11.5 });
                group = a.Group;
                var item = new ComboBoxItem { Content = Lang.T(a.Label), Tag = a.Code };
                if (selected == null && a.Code.SequenceEqual(current)) selected = item;
                combo.Items.Add(item);
            }
            combo.Items.Add(new ComboBoxItem { Content = Lang.T("键盘按键…"), Tag = "key" });
            combo.SelectedItem = selected;
        }

        void OnActionPicked(ComboBox combo, int index)
        {
            if (building) return;
            var item = combo.SelectedItem as ComboBoxItem;
            var s = cur;
            var p = s == null ? null : s.Current;
            if (item == null || p == null) return;
            bool viaKey = item.Tag as string == "key";
            var code = viaKey ? KeyCapture.Ask(this) : item.Tag as byte[];
            if (code == null) { Dispatcher.BeginInvoke(new Action(() => RenderSection(false))); return; }

            // Never leave a profile without a primary click.
            bool keepsLeft = Enumerable.Range(0, Math.Min(s.Dev.ButtonCount, Profile.MaxButtons))
                .Any(i => (i == index ? code : p.GetButton(i)).SequenceEqual(Actions.LeftClick));
            if (!keepsLeft)
            {
                Toast(Lang.T("每个配置至少要有一个按键保留为“左键”"), true);
                Dispatcher.BeginInvoke(new Action(() => RenderSection(false)));
                return;
            }
            p.SetButton(index, code);
            Changed();
            if (viaKey) Dispatcher.BeginInvoke(new Action(() => RenderSection(false)));
        }

        // ---------- 配置管理 ----------

        FrameworkElement ProfilesSection()
        {
            var s = cur;
            var list = new StackPanel();
            for (int i = 0; i < s.Profiles.Count; i++)
            {
                var p = s.Profiles[i];
                int index = i;
                if (i > 0) list.Children.Add(Divider());
                var row = new DockPanel();

                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var enabled = new CheckBox { Style = St("Switch"), Content = Lang.T("启用"), IsChecked = s.Enabled(p), Margin = new Thickness(16, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                enabled.Click += (o, e) => ToggleEnabled(p, enabled);
                var use = new Button { Style = St("Mini"), Content = Lang.T("使用"), IsEnabled = s.CanActivate(p) && p.Sector != s.Active, ToolTip = Lang.T("让设备立即切换到这个配置") };
                use.Click += (o, e) => { cur.Selected = index; ActivateSelected(true); };
                var reset = new Button { Style = St("Mini"), Content = Lang.T("恢复出厂"), ToolTip = Lang.T("载入设备内置的出厂设置（保存后生效）") };
                reset.Click += (o, e) => FactoryReset(p);
                actions.Children.Add(enabled);
                actions.Children.Add(use);
                actions.Children.Add(reset);
                DockPanel.SetDock(actions, Dock.Right);
                row.Children.Add(actions);

                var badge = Badge((i + 1).ToString());
                badge.Margin = new Thickness(0, 0, 16, 0);
                row.Children.Add(badge);

                var nameBox = new Grid { Width = 240, VerticalAlignment = VerticalAlignment.Center };
                var tb = new TextBox { Text = p.Name, MaxLength = Profile.NameChars, ToolTip = Lang.T("配置名称（最多 24 个字符）") };
                var hint = Brushed(new TextBlock { Text = Lang.T("配置 ") + (i + 1), IsHitTestVisible = false, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = p.Name.Length == 0 ? Visibility.Visible : Visibility.Collapsed }, TextBlock.ForegroundProperty, "Faint");
                tb.TextChanged += (o, e) =>
                {
                    hint.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                    p.Name = tb.Text;
                    Changed();
                };
                nameBox.Children.Add(tb);
                nameBox.Children.Add(hint);
                row.Children.Add(nameBox);

                var tags = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
                if (p.Sector == s.Active && s.Mode != 2) tags.Children.Add(Pill(Lang.T("正在使用"), "Good", "GoodSoft"));
                if (p.Blank) tags.Children.Add(Pill(Lang.T("空"), "Muted", "Surface2"));
                row.Children.Add(tags);
                list.Children.Add(row);
            }
            var stack = new StackPanel();
            stack.Children.Add(Card(null, null, null, list));
            stack.Children.Add(Txt(Lang.T("至少要保留一个启用的配置。改名、启用和恢复出厂都要点“保存到设备”后才会写入。"), "Hint", new Thickness(4, 0, 0, 0)));
            return stack;
        }

        void ToggleEnabled(Profile p, CheckBox box)
        {
            var s = cur;
            bool on = box.IsChecked == true;
            if (!on && s.Profiles.Count(s.Enabled) <= 1)
            {
                box.IsChecked = true;
                Toast(Lang.T("至少要保留一个启用的配置"), true);
                return;
            }
            ProfileDirectory.SetEnabled(s.Dir, p.Sector, on);
            Changed();
        }

        // ---------- 设备信息 ----------

        FrameworkElement InfoSection()
        {
            var s = cur;
            var d = s.Dev;
            var list = new StackPanel();
            Action<string, string, FrameworkElement> add = (k, v, extra) =>
            {
                if (list.Children.Count > 0) list.Children.Add(Divider());
                var row = new DockPanel();
                if (extra != null) { DockPanel.SetDock(extra, Dock.Right); row.Children.Add(extra); }
                var key = Txt(k, "Hint");
                key.Width = 120;
                key.FontSize = 13.5;
                row.Children.Add(key);
                row.Children.Add(new TextBlock { Text = v, Style = St("Body"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
                list.Children.Add(row);
            };

            add(Lang.T("名称"), d.Name, null);
            add(Lang.T("连接方式"), d.Wireless ? Lang.T("无线（LIGHTSPEED 接收器）") : Lang.T("有线（USB）"), null);
            if (d.Firmware.Length > 0) add(Lang.T("固件版本"), d.Firmware, null);
            if (d.UnitId.Length > 0) add(Lang.T("序列号"), d.UnitId, null);
            if (s.Battery != null && s.Battery.Percent >= 0)
                add(Lang.T("电量"), s.Battery.Percent + "%" + (s.Battery.Charging ? Lang.T("，充电中") : s.Battery.Full ? Lang.T("，已充满") : ""), null);
            Button toOnboard = null;
            if (s.Mode == 2)
            {
                toOnboard = new Button { Style = St("Ghost"), Content = Lang.T("切换到板载模式") };
                toOnboard.Click += (o, e) => SwitchToOnboard();
            }
            add(Lang.T("工作模式"), s.Mode == 2 ? Lang.T("主机模式（由软件实时控制）") : Lang.T("板载模式（使用设备里保存的配置）"), toOnboard);
            add(Lang.T("板载内存"), Lang.T("格式 ") + d.Format + " · " + d.ProfileCount + Lang.T(" 个配置槽 · 每扇区 ") + d.SectorSize + Lang.T(" 字节"), null);
            add(Lang.T("按键数量"), d.ButtonCount.ToString(), null);
            add(Lang.T("DPI 范围"), d.DpiMin + "–" + d.DpiMax + (d.DpiList == null ? Lang.T("，步进 ") + d.DpiStep : ""), null);
            add(Lang.T("回报率"), string.Join(" / ", d.RatesMs.Select(ms => (1000 / ms).ToString())) + " Hz", null);
            var open = new Button { Style = St("Ghost"), Content = Lang.T("打开文件夹") };
            open.Click += (o, e) => OpenBackups();
            add(Lang.T("备份"), Lang.T("每次运行后第一次保存前，会自动备份设备里原来的配置"), open);
            return Card(null, null, null, list);
        }

        void OpenBackups()
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OnboardLite", "backups");
            try
            {
                Directory.CreateDirectory(dir);
                Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch (Exception ex) { Toast(Lang.T("打不开备份文件夹：") + ex.Message, true); }
        }

        // ---------- editing ----------

        void Changed()
        {
            UpdateSaveBar();
            if (cur != null && (section == "perf" || section == "buttons" || section == "light")) RenderProfileBar(false);
        }

        void UpdateSaveBar()
        {
            var s = cur;
            if (s == null) return;
            if (s.HasChanges)
            {
                justSaved = false;
                var names = s.Profiles.Where(p => p.Dirty).Select(s.Title).ToList();
                if (!Crc.SameBody(s.Dir, s.DirBase)) names.Add(Lang.T("启用状态"));
                saveText.Text = s.Busy ? Lang.T("正在写入设备…") : Lang.T("有未保存的更改：") + string.Join(Lang.T("、"), names);
                saveDot.SetResourceReference(Shape.FillProperty, "Amber");
                btnSave.Visibility = btnRevert.Visibility = Visibility.Visible;
                btnSave.IsEnabled = btnRevert.IsEnabled = !s.Busy;
                saveBar.Visibility = Visibility.Visible;
            }
            else if (justSaved)
            {
                saveText.Text = Lang.T("已保存到设备");
                saveDot.SetResourceReference(Shape.FillProperty, "Good");
                btnSave.Visibility = btnRevert.Visibility = Visibility.Collapsed;
                saveBar.Visibility = Visibility.Visible;
            }
            else saveBar.Visibility = Visibility.Collapsed;
        }

        void SelectProfile(int index)
        {
            cur.Selected = index;
            RenderSection(false);
            ActivateSelected(false);
        }

        // Switches the device to the selected profile when it already knows that profile as enabled.
        async void ActivateSelected(bool explicitly)
        {
            var s = cur;
            var p = s.Current;
            if (s.Busy || p.Sector == s.Active) return;
            if (!s.CanActivate(p))
            {
                if (explicitly) Toast(Lang.T("这个配置还没保存为启用状态，保存后才能切换"), true);
                return;
            }
            try
            {
                int act = -1, idx = -1;
                await Task.Run(() => { s.Dev.SetActiveSector(p.Sector); act = s.Dev.GetActiveSector(); idx = s.Dev.GetDpiIndex(); });
                s.Active = act;
                s.DpiIndex = idx;
                if (cur != s) return;
                RenderProfileBar(true);
                if (section == "profiles") RenderSection(false);
                UpdateDpiNow();
                if (explicitly) Toast(Lang.T("已切换到“") + s.Title(p) + Lang.T("”"));
            }
            catch (Exception ex) { Toast(Lang.T("切换配置失败：") + ex.Message, true); }
        }

        void Revert()
        {
            if (cur == null || cur.Busy) return;
            cur.Revert();
            RenderSection(false);
            UpdateSaveBar();
            Toast(Lang.T("已撤销所有未保存的更改"));
        }

        async void FactoryReset(Profile p)
        {
            var s = cur;
            if (s == null || s.Busy) return;
            if (!Confirm(Lang.T("把“") + s.Title(p) + Lang.T("”恢复为设备内置的出厂设置？\nDPI、回报率、按键和名称都会重置，点“保存到设备”后才会写入。"))) return;
            try
            {
                var rom = await Task.Run(() => s.Dev.ReadFactoryProfile(p.Sector));
                p.Raw = rom;
                Toast(Lang.T("已载入出厂设置，保存后生效"));
            }
            catch (Exception ex) { Toast(Lang.T("读取出厂设置失败：") + ex.Message, true); }
            if (cur == s) { RenderSection(false); UpdateSaveBar(); }
        }

        async void SwitchToOnboard()
        {
            var s = cur;
            if (s == null || s.Busy) return;
            var p = s.Current;
            try
            {
                int m = -1, act = -1;
                await Task.Run(() =>
                {
                    s.Dev.SetMode(1);
                    if (p != null && s.CanActivateIgnoringMode(p)) s.Dev.SetActiveSector(p.Sector);
                    m = s.Dev.GetMode();
                    act = s.Dev.GetActiveSector();
                });
                s.Mode = m;
                s.Active = act;
                Toast(Lang.T("已切换到板载模式"));
            }
            catch (Exception ex) { Toast(Lang.T("切换失败：") + ex.Message, true); }
            if (cur == s) { RenderDevCard(); RenderBanner(); RenderSection(false); }
        }

        async void Save()
        {
            var s = cur;
            if (s == null || s.Busy) return;
            btnSave.Focus();   // commits a DPI value still being typed
            if (!s.HasChanges) return;
            s.Busy = true;
            sectionHost.IsEnabled = profileBar.IsEnabled = btnBack.IsEnabled = false;
            UpdateSaveBar();
            var result = await Task.Run(() => s.Save());
            s.Busy = false;
            sectionHost.IsEnabled = profileBar.IsEnabled = btnBack.IsEnabled = true;
            if (cur != s) return;
            if (result.Error != null) Toast(Lang.T("保存失败：") + result.Error.Message, true);
            else
            {
                justSaved = true;
                savedTimer.Stop();
                savedTimer.Start();
                if (result.BackupDir != null) Toast(Lang.T("已保存。原配置已备份，可在“设备信息”里打开备份文件夹"));
            }
            RenderDevCard();
            RenderSection(false);
            UpdateSaveBar();
        }

        async Task PollCurrent()
        {
            var s = cur;
            if (s == null || s.Busy || !s.Loaded) return;
            var r = await Task.Run(() => s.Poll());
            if (s.Busy) return;
            bool moved = r.Ok && (r.Active != s.Active || r.Mode != s.Mode);
            bool wasOffline = s.Offline;
            s.Apply(r);
            if (cur != s) return;
            RenderDevCard();
            if (moved || wasOffline != s.Offline)
            {
                RenderBanner();
                if (section == "profiles" || section == "info") RenderSection(false);
                else RenderProfileBar(true);
            }
            UpdateDpiNow();
        }
    }
}

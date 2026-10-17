using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OnboardLite
{
    // Small modal that records one key (optionally with modifiers) as a HID keyboard binding.
    sealed class KeyCapture : Window
    {
        readonly TextBlock shown;
        readonly Button ok;
        int mods, usage;

        public static byte[] Ask(Window owner)
        {
            var w = new KeyCapture { Owner = owner };
            return w.ShowDialog() == true ? Actions.KeyCode(w.mods, w.usage) : null;
        }

        KeyCapture()
        {
            Title = Lang.T("键盘按键");
            Width = 400;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
            FontSize = 13;
            InputMethod.SetIsInputMethodEnabled(this, false);

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = Lang.T("按下要映射的按键"), Style = (Style)FindResource("H2") });
            panel.Children.Add(new TextBlock { Text = Lang.T("可以组合 Ctrl / Shift / Alt / Win"), Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 6, 0, 0) });
            shown = new TextBlock
            {
                Text = Lang.T("等待按键…"), FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Faint"),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 26)
            };
            panel.Children.Add(shown);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = Lang.T("取消"), Style = (Style)FindResource("Ghost"), Focusable = false };
            ok = new Button { Content = Lang.T("确定"), Style = (Style)FindResource("Primary"), Focusable = false, IsEnabled = false, Margin = new Thickness(8, 0, 0, 0) };
            cancel.Click += (s, e) => DialogResult = false;
            ok.Click += (s, e) => DialogResult = true;
            actions.Children.Add(cancel);
            actions.Children.Add(ok);
            panel.Children.Add(actions);

            var card = new Border
            {
                Background = (Brush)FindResource("Surface"), BorderBrush = (Brush)FindResource("Line"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14), Padding = new Thickness(24, 22, 24, 20), Child = panel
            };
            card.MouseLeftButtonDown += (s, e) => DragMove();
            Content = card;
            PreviewKeyDown += OnKey;
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
            int vk = KeyInterop.VirtualKeyFromKey(key);
            int held = 0;
            if (Keyboard.IsKeyDown(Key.LeftCtrl)) held |= 0x01;
            if (Keyboard.IsKeyDown(Key.LeftShift)) held |= 0x02;
            if (Keyboard.IsKeyDown(Key.LeftAlt)) held |= 0x04;
            if (Keyboard.IsKeyDown(Key.LWin)) held |= 0x08;
            if (Keyboard.IsKeyDown(Key.RightCtrl)) held |= 0x10;
            if (Keyboard.IsKeyDown(Key.RightShift)) held |= 0x20;
            if (Keyboard.IsKeyDown(Key.RightAlt)) held |= 0x40;
            if (Keyboard.IsKeyDown(Key.RWin)) held |= 0x80;

            int bit = Keys.ModifierBit(vk);
            int u = bit != 0 ? 0 : Keys.Usage(vk);
            if (bit == 0 && u == 0)
            {
                shown.Text = Lang.T("这个键暂不支持");
                shown.Foreground = (Brush)FindResource("Danger");
                ok.IsEnabled = false;
                return;
            }
            mods = held | bit;
            usage = u;
            shown.Text = Keys.Label(mods, usage);
            shown.Foreground = (Brush)FindResource("Accent");
            ok.IsEnabled = true;
        }
    }
}

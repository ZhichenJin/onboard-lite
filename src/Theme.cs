using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace OnboardLite
{
    // Two palettes swapped at runtime; every brush in the UI is a DynamicResource on these keys.
    static class Theme
    {
        static readonly Dictionary<string, string> Dark = new Dictionary<string, string>
        {
            { "Bg", "#131416" }, { "Surface", "#1B1D21" }, { "Surface2", "#24272C" }, { "Hover", "#2A2E34" },
            { "Line", "#2C3036" }, { "Track", "#3A3F46" }, { "Knob", "#C4C9D0" },
            { "Text", "#E8EAED" }, { "Muted", "#969CA6" }, { "Faint", "#646B75" },
            { "Accent", "#5C8DFF" }, { "AccentHover", "#7AA2FF" }, { "AccentSoft", "#1E2A44" }, { "OnAccent", "#FFFFFF" },
            { "Amber", "#E9AE48" }, { "AmberSoft", "#332914" }, { "Good", "#43CF8C" }, { "GoodSoft", "#15301F" },
            { "Danger", "#EF6A72" }, { "DangerSoft", "#3A1D21" }, { "Popup", "#202328" }
        };

        // Kept a little below pure white so the switch from dark is easier on the eyes.
        static readonly Dictionary<string, string> Light = new Dictionary<string, string>
        {
            { "Bg", "#ECEEF1" }, { "Surface", "#F9FAFB" }, { "Surface2", "#EBEDF0" }, { "Hover", "#E2E5EA" },
            { "Line", "#DCDFE5" }, { "Track", "#CCD1D9" }, { "Knob", "#FFFFFF" },
            { "Text", "#1F2329" }, { "Muted", "#626A75" }, { "Faint", "#9EA5AF" },
            { "Accent", "#3366F0" }, { "AccentHover", "#2A57D6" }, { "AccentSoft", "#E2E9FC" }, { "OnAccent", "#FFFFFF" },
            { "Amber", "#B4760A" }, { "AmberSoft", "#FBEFD3" }, { "Good", "#17895A" }, { "GoodSoft", "#D9F0E4" },
            { "Danger", "#D23F4A" }, { "DangerSoft", "#F9E1E3" }, { "Popup", "#F9FAFB" }
        };

        public static bool IsDark { get; private set; }
        public static event Action Changed;

        // Saved choice first, otherwise follow the Windows app theme.
        public static void Init()
        {
            var saved = Settings.Get("theme");
            Apply(saved != null ? saved == "dark" : SystemPrefersDark(), false);
        }

        public static void Toggle()
        {
            Apply(!IsDark, true);
            Settings.Set("theme", IsDark ? "dark" : "light");
        }

        static bool SystemPrefersDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var v = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }

        // WPF freezes brushes held in a resource dictionary, so a switch replaces them;
        // the window cross-fades a snapshot of the old look on top (MainWindow.ToggleTheme).
        static void Apply(bool dark, bool save)
        {
            IsDark = dark;
            var res = Application.Current.Resources;
            foreach (var kv in dark ? Dark : Light)
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(kv.Value));
                brush.Freeze();
                res[kv.Key] = brush;
            }
            var changed = Changed;
            if (changed != null) changed();
        }
    }
}

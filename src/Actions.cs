using System;
using System.Collections.Generic;
using System.Linq;

namespace OnboardLite
{
    sealed class ActionItem
    {
        public string Label;
        public byte[] Code;
        public string Group;
    }

    // 4-byte button bindings (Feature 0x8100 BUTTON_BEHAVIOR):
    //   80 01 mask(BE)  mouse button      80 02 mods key   keyboard
    //   80 03 usage(BE) consumer control  90 op 00 00      function      00 ..  macro
    static class Actions
    {
        public static readonly ActionItem[] Catalog =
        {
            Mouse("左键", 0x01), Mouse("右键", 0x02), Mouse("中键", 0x04), Mouse("后退", 0x08), Mouse("前进", 0x10),
            Fn("DPI 循环切换", 5), Fn("DPI 加", 3), Fn("DPI 减", 4), Fn("DPI Shift（按住切换）", 7), Fn("恢复默认 DPI", 6),
            Fn("切换配置", 10), Fn("滚轮左倾", 1), Fn("滚轮右倾", 2),
            Consumer("播放 / 暂停", 0xCD), Consumer("下一曲", 0xB5), Consumer("上一曲", 0xB6),
            Consumer("静音", 0xE2), Consumer("音量加", 0xE9), Consumer("音量减", 0xEA),
            Fn("禁用", 0),
        };

        public static readonly byte[] LeftClick = { 0x80, 0x01, 0x00, 0x01 };

        static readonly string[] Functions =
        {
            "禁用", "滚轮左倾", "滚轮右倾", "DPI 加", "DPI 减", "DPI 循环切换", "恢复默认 DPI", "DPI Shift（按住切换）",
            "下一个配置", "上一个配置", "切换配置", "G-Shift", "电量指示", "指定配置", "性能模式", "主机按键", "向下滚动", "向上滚动"
        };

        static ActionItem Mouse(string label, int mask)
        {
            return new ActionItem { Label = label, Group = "鼠标", Code = new byte[] { 0x80, 0x01, (byte)(mask >> 8), (byte)mask } };
        }

        static ActionItem Fn(string label, int op)
        {
            return new ActionItem { Label = label, Group = op == 0 ? "其他" : "功能", Code = new byte[] { 0x90, (byte)op, 0, 0 } };
        }

        static ActionItem Consumer(string label, int usage)
        {
            return new ActionItem { Label = label, Group = "媒体", Code = new byte[] { 0x80, 0x03, (byte)(usage >> 8), (byte)usage } };
        }

        public static byte[] KeyCode(int modifiers, int usage)
        {
            return new byte[] { 0x80, 0x02, (byte)modifiers, (byte)usage };
        }

        public static ActionItem Find(byte[] code)
        {
            return Catalog.FirstOrDefault(a => a.Code.SequenceEqual(code));
        }

        public static string Describe(byte[] c)
        {
            if (c.All(b => b == 0xFF)) return Lang.T("未分配");
            var known = Find(c);
            if (known != null) return Lang.T(known.Label);
            if (c[0] == 0x80 && c[1] == 0x01) return Lang.T("鼠标按键 0x") + ((c[2] << 8) | c[3]).ToString("X");
            if (c[0] == 0x80 && c[1] == 0x02) return Keys.Label(c[2], c[3]);
            if (c[0] == 0x80 && c[1] == 0x03) return Lang.T("媒体键 0x") + ((c[2] << 8) | c[3]).ToString("X4");
            if (c[0] == 0x90) return c[1] < Functions.Length ? Lang.T(Functions[c[1]]) : Lang.T("功能 ") + c[1];
            if (c[0] == 0x00) return Lang.T("宏（保留原设置）");
            return Lang.T("自定义 ") + BitConverter.ToString(c);
        }
    }

    // Windows virtual-key codes <-> HID keyboard usages.
    static class Keys
    {
        static readonly Dictionary<int, int> VkToUsage = new Dictionary<int, int>();
        static readonly Dictionary<int, string> UsageNames = new Dictionary<int, string>();

        static Keys()
        {
            for (int i = 0; i < 26; i++) Add(0x41 + i, 0x04 + i, ((char)('A' + i)).ToString());
            for (int i = 1; i <= 9; i++) Add(0x30 + i, 0x1D + i, i.ToString());
            Add(0x30, 0x27, "0");
            Add(0x0D, 0x28, "Enter"); Add(0x1B, 0x29, "Esc"); Add(0x08, 0x2A, "Backspace"); Add(0x09, 0x2B, "Tab");
            Add(0x20, 0x2C, "空格"); Add(0xBD, 0x2D, "-"); Add(0xBB, 0x2E, "="); Add(0xDB, 0x2F, "[");
            Add(0xDD, 0x30, "]"); Add(0xDC, 0x31, "\\"); Add(0xBA, 0x33, ";"); Add(0xDE, 0x34, "'");
            Add(0xC0, 0x35, "`"); Add(0xBC, 0x36, ","); Add(0xBE, 0x37, "."); Add(0xBF, 0x38, "/");
            Add(0x14, 0x39, "CapsLock");
            for (int i = 0; i < 12; i++) Add(0x70 + i, 0x3A + i, "F" + (i + 1));
            for (int i = 0; i < 12; i++) Add(0x7C + i, 0x68 + i, "F" + (i + 13));
            Add(0x2C, 0x46, "PrintScreen"); Add(0x91, 0x47, "ScrollLock"); Add(0x13, 0x48, "Pause");
            Add(0x2D, 0x49, "Insert"); Add(0x24, 0x4A, "Home"); Add(0x21, 0x4B, "PageUp"); Add(0x2E, 0x4C, "Delete");
            Add(0x23, 0x4D, "End"); Add(0x22, 0x4E, "PageDown"); Add(0x27, 0x4F, "→"); Add(0x25, 0x50, "←");
            Add(0x28, 0x51, "↓"); Add(0x26, 0x52, "↑"); Add(0x90, 0x53, "NumLock");
            Add(0x6F, 0x54, "小键盘 /"); Add(0x6A, 0x55, "小键盘 *"); Add(0x6D, 0x56, "小键盘 -"); Add(0x6B, 0x57, "小键盘 +");
            for (int i = 1; i <= 9; i++) Add(0x60 + i, 0x58 + i, "小键盘 " + i);
            Add(0x60, 0x62, "小键盘 0"); Add(0x6E, 0x63, "小键盘 ."); Add(0x5D, 0x65, "菜单键");
        }

        static void Add(int vk, int usage, string name)
        {
            VkToUsage[vk] = usage;
            UsageNames[usage] = name;
        }

        // Modifier bit for a modifier virtual key, or 0.
        public static int ModifierBit(int vk)
        {
            switch (vk)
            {
                case 0xA2: return 0x01; case 0xA0: return 0x02; case 0xA4: return 0x04; case 0x5B: return 0x08;
                case 0xA3: return 0x10; case 0xA1: return 0x20; case 0xA5: return 0x40; case 0x5C: return 0x80;
                default: return 0;
            }
        }

        public static int Usage(int vk)
        {
            int u;
            return VkToUsage.TryGetValue(vk, out u) ? u : 0;
        }

        public static string Label(int mods, int usage)
        {
            var parts = new List<string>();
            if ((mods & 0x11) != 0) parts.Add("Ctrl");
            if ((mods & 0x22) != 0) parts.Add("Shift");
            if ((mods & 0x44) != 0) parts.Add("Alt");
            if ((mods & 0x88) != 0) parts.Add("Win");
            if (usage != 0)
            {
                string name;
                parts.Add(UsageNames.TryGetValue(usage, out name) ? KeyName(name) : Lang.T("键 0x") + usage.ToString("X2"));
            }
            return parts.Count == 0 ? Lang.T("键盘（空）") : string.Join(" + ", parts);
        }

        // Numpad names are stored as "小键盘 X"; the prefix is translated separately.
        static string KeyName(string name)
        {
            return name.StartsWith("小键盘 ") ? Lang.T("小键盘") + name.Substring(3) : Lang.T(name);
        }
    }
}

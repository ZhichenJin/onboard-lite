using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;

namespace OnboardLite
{
    // UI language. Chinese text is the key; English comes from the table below (missing entries fall back to Chinese).
    // Sentences built by concatenation are translated piece by piece, so pieces keep their spacing.
    static class Lang
    {
        public static bool En { get; private set; }
        public static event Action Changed;

        public static string T(string zh)
        {
            if (!En || zh == null) return zh;
            string en;
            return Table.TryGetValue(zh, out en) ? en : zh;
        }

        // Keys used by DynamicResource in the XAML files.
        static readonly Dictionary<string, string> XamlKeys = new Dictionary<string, string>
        {
            { "L_MyDevices", "我的设备" },
            { "L_HomeSub", "选择一个设备进行设置，所有修改都保存在设备自己的板载内存里。" },
            { "L_Rescan", "重新扫描" },
            { "L_Perf", "性能" },
            { "L_Buttons", "按键" },
            { "L_Light", "灯光" },
            { "L_Profiles", "配置管理" },
            { "L_Info", "设备信息" },
            { "L_Revert", "撤销更改" },
            { "L_Save", "保存到设备" },
            { "L_Min", "最小化" },
            { "L_Max", "最大化" },
            { "L_Close", "关闭" },
        };

        // Saved choice first, otherwise follow the Windows display language.
        public static void Init()
        {
            var saved = Settings.Get("language");
            bool en = saved != null ? saved == "en" : !CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            Apply(en, false);
        }

        public static void Toggle() { Apply(!En, true); }

        static void Apply(bool en, bool save)
        {
            En = en;
            var res = Application.Current.Resources;
            foreach (var kv in XamlKeys) res[kv.Key] = T(kv.Value);
            if (save) Settings.Set("language", en ? "en" : "zh");
            var changed = Changed;
            if (changed != null) changed();
        }

        public static IEnumerable<string> Keys { get { return Table.Keys; } }

        static readonly Dictionary<string, string> Table = new Dictionary<string, string>
        {
            // shell & home
            { "我的设备", "My devices" },
            { "选择一个设备进行设置，所有修改都保存在设备自己的板载内存里。", "Pick a device to configure. Every change is stored in the device's own onboard memory." },
            { "重新扫描", "Rescan" },
            { "撤销更改", "Discard changes" }, { "保存到设备", "Save to device" },
            { "最小化", "Minimize" }, { "最大化", "Maximize" }, { "关闭", "Close" },
            { "切换到亮色", "Switch to light theme" }, { "切换到暗色", "Switch to dark theme" },
            { "启动失败：", "Failed to start: " },
            { "正在扫描设备…", "Scanning for devices…" },
            { "扫描失败：", "Scan failed: " },
            { "没有找到可设置的设备", "No configurable devices found" },
            { "已找到 ", "Found " }, { " 个设备", " device(s)" },
            { "罗技设备", "Logitech device" },
            { "“", "“" }, { "”", "”" }, { "”已断开", "” disconnected" },
            { "正在写入设备，请稍候…", "Writing to the device, please wait…" },
            { "有未保存的更改，确定退出吗？", "You have unsaved changes. Quit anyway?" },
            { "不支持", "Unsupported" },
            { "接收器上的设备", "Device on receiver" },
            { "正在休眠，动一下鼠标即可唤醒", "Asleep. Move the mouse to wake it" },
            { "休眠中", "Asleep" },
            { "正在查找设备…", "Looking for devices…" },
            { "没有找到设备", "No devices found" },
            { "用数据线或 LIGHTSPEED 接收器连接后会自动出现", "Connect by cable or LIGHTSPEED receiver and it will show up here" },
            { "另有 ", "" }, { " 个接收器上的设备在休眠，动一下即可唤醒。", " more device(s) on a receiver are asleep. Move them to wake up." },
            { "暂不支持", "Not supported" },
            { "未保存", "Unsaved" },
            { "主机模式", "Host mode" },
            { "在线", "Online" },
            { "配置格式 ", "Profile format " }, { "，本工具支持格式 1–5", "; formats 1–5 are supported" },
            { "无线 · LIGHTSPEED 接收器", "Wireless · LIGHTSPEED receiver" },
            { "有线 · USB", "Wired · USB" },
            { "由主机软件控制", "Controlled by host software" },
            { "充电 ", "Charging " },

            // device page chrome
            { "无线 · 接收器", "Wireless · receiver" },
            { " · 休眠中", " · asleep" },
            { "充电中 ", "Charging " }, { "已充满 ", "Full " },
            { "主机模式：设置由 G HUB 等软件实时控制", "Host mode: settings are controlled live by G HUB or similar software" },
            { "板载模式：设置保存在设备里，换电脑也生效", "Onboard mode: settings live on the device and work on any PC" },
            { "设备处于主机模式，由 G HUB 等软件实时控制，板载配置暂不生效。", "The device is in host mode and controlled live by G HUB or similar software. Onboard profiles are not active." },
            { "切换到板载模式", "Switch to onboard mode" },
            { "设备在休眠或信号中断。动一下鼠标唤醒后才能保存。", "The device is asleep or out of range. Move the mouse to wake it before saving." },
            { "配置", "Profile" }, { "配置 ", "Profile " },
            { " · 未启用", " · disabled" }, { " · 正在使用", " · in use" }, { " · 有未保存的更改", " · unsaved changes" },
            { "正在编辑：", "Editing: " },
            { "按键", "Buttons" }, { "按键 ", "Button " },
            { "为每个按键指定功能 · ", "Assign a function to each button · " },
            { "配置管理", "Profiles" },
            { "设备最多保存 ", "The device stores up to " },
            { " 套配置，“切换配置”按键会在已启用的配置之间循环", " profiles. The “Cycle profiles” button steps through the enabled ones" },
            { "设备信息", "Device info" },
            { "连接、固件与板载内存", "Connection, firmware and onboard memory" },
            { "性能", "Performance" },
            { "DPI、回报率与移动 · ", "DPI, report rate and movement · " },

            // performance
            { "DPI 档位", "DPI stages" },
            { "范围 ", "Range " }, { "，步进 ", ", step " }, { "，最多 ", ", up to " },
            { " 档。可以拖动滑杆或直接输入数值", " stages. Drag a slider or type a value" },
            { "回报率", "Report rate" },
            { "每秒向电脑报告位置的次数，越高延迟越低、耗电越多", "How many times per second the mouse reports to the PC. Higher means lower latency and more power use" },
            { "角度捕捉", "Angle snapping" },
            { "把略微倾斜的移动修正成水平或垂直直线，FPS 游戏一般建议关闭", "Straightens slightly slanted movement into horizontal or vertical lines. Usually best off for FPS games" },
            { "设备当前：第 ", "On device: stage " }, { " 档 · ", " · " },
            { "这个配置没有 DPI 档位", "This profile has no DPI stages" },
            { "默认档位", "Default stage" },
            { "按住 DPI Shift 键时使用", "Used while DPI Shift is held" },
            { "+ 添加档位", "+ Add stage" },
            { "设为默认档位（开机或切换到此配置时使用）", "Make this the default stage (used at power-on and when switching to this profile)" },
            { "按住 DPI Shift 键时切换到这一档", "Switch to this stage while DPI Shift is held" },
            { "删除这一档", "Remove this stage" },

            // lighting
            { "灯光", "Lighting" },
            { "灯光效果与颜色 · ", "Effect and color · " },
            { "当前为设备默认灯效，选择一种效果后保存即可写入", "The device is using its default lighting. Pick an effect and save to change it" },
            { "当前是本工具不支持编辑的灯效（例如 G HUB 设置的特殊效果），选择一种效果会替换它", "This profile uses an effect this tool can't edit (such as a special G HUB effect). Picking an effect replaces it" },
            { "整套配置使用同一种灯效，作用于设备的所有灯区", "One effect per profile, applied to every lighting zone" },
            { "灯光效果", "Effect" },
            { "颜色", "Color" },
            { "自定义颜色", "Custom color" },
            { "速度", "Speed" },
            { " 秒 / 周期", " s per cycle" },
            { "亮度", "Brightness" },
            { "速度与亮度", "Speed and brightness" },
            { "设备进入省电状态时，会自动换成一半亮度的同款灯效。", "In power-saving mode the device uses the same effect at half brightness. " },
            { "灯光和其他设置一样，点“保存到设备”后生效。", "Like other settings, lighting is applied when you click “Save to device”." },
            { "设备默认", "Default" },
            { "其他灯效", "Other effect" },

            // buttons
            { "每个配置至少要保留一个“左键”。键盘按键支持组合键，例如 Ctrl + C。", "Every profile must keep at least one “Left click”. Keyboard keys can be combinations such as Ctrl + C." },
            { "每个配置至少要有一个按键保留为“左键”", "Every profile needs at least one button set to “Left click”" },
            { "键盘按键…", "Keyboard key…" },

            // profiles
            { "启用", "Enabled" },
            { "使用", "Use" },
            { "让设备立即切换到这个配置", "Switch the device to this profile now" },
            { "恢复出厂", "Factory reset" },
            { "载入设备内置的出厂设置（保存后生效）", "Load the device's built-in factory settings (applied when you save)" },
            { "配置名称（最多 24 个字符）", "Profile name (up to 24 characters)" },
            { "正在使用", "In use" },
            { "空", "Empty" },
            { "至少要保留一个启用的配置。改名、启用和恢复出厂都要点“保存到设备”后才会写入。", "At least one profile must stay enabled. Renames, enable switches and factory resets are written when you click “Save to device”." },
            { "至少要保留一个启用的配置", "At least one profile must stay enabled" },

            // device info
            { "名称", "Name" }, { "连接方式", "Connection" }, { "固件版本", "Firmware" }, { "序列号", "Serial number" },
            { "电量", "Battery" }, { "工作模式", "Mode" }, { "板载内存", "Onboard memory" }, { "按键数量", "Buttons" },
            { "DPI 范围", "DPI range" }, { "备份", "Backups" },
            { "无线（LIGHTSPEED 接收器）", "Wireless (LIGHTSPEED receiver)" }, { "有线（USB）", "Wired (USB)" },
            { "，充电中", ", charging" }, { "，已充满", ", full" },
            { "主机模式（由软件实时控制）", "Host mode (controlled live by software)" },
            { "板载模式（使用设备里保存的配置）", "Onboard mode (uses the profiles stored on the device)" },
            { "格式 ", "Format " }, { " 个配置槽 · 每扇区 ", " profile slots · " }, { " 字节", "-byte sectors" },
            { "打开文件夹", "Open folder" },
            { "每次运行后第一次保存前，会自动备份设备里原来的配置", "Before the first save of each run, the device's original profiles are backed up automatically" },
            { "打不开备份文件夹：", "Cannot open the backup folder: " },

            // saving & toasts
            { "启用状态", "enabled state" },
            { "正在写入设备…", "Writing to the device…" },
            { "有未保存的更改：", "Unsaved changes: " },
            { "、", ", " },
            { "已保存到设备", "Saved to device" },
            { "这个配置还没保存为启用状态，保存后才能切换", "This profile isn't saved as enabled yet. Save first, then switch" },
            { "已切换到“", "Switched to “" },
            { "切换配置失败：", "Could not switch profile: " },
            { "已撤销所有未保存的更改", "All unsaved changes discarded" },
            { "把“", "Reset “" },
            { "”恢复为设备内置的出厂设置？\nDPI、回报率、按键和名称都会重置，点“保存到设备”后才会写入。", "” to the device's built-in factory settings?\nDPI, report rate, buttons and name will be reset. Nothing is written until you click “Save to device”." },
            { "已载入出厂设置，保存后生效", "Factory settings loaded. Save to apply" },
            { "读取出厂设置失败：", "Could not read factory settings: " },
            { "已切换到板载模式", "Switched to onboard mode" },
            { "切换失败：", "Switch failed: " },
            { "保存失败：", "Save failed: " },
            { "已保存。原配置已备份，可在“设备信息”里打开备份文件夹", "Saved. The original profiles were backed up; open the folder from “Device info”" },
            { "配置目录无效，已重建（保存后写入）", "The profile directory was invalid and has been rebuilt (written when you save)" },

            // device & protocol errors
            { " 没有板载内存功能", " has no onboard memory" },
            { "板载内存扇区大小异常：", "Unexpected onboard sector size: " },
            { "拒绝写入只读区域", "Refusing to write to read-only memory" },
            { "扇区长度不对", "Wrong sector length" },
            { "写入后校验不一致（扇区 ", "Verification after writing failed (sector " }, { "）", ")" },
            { "无法打开设备（错误 ", "Cannot open the device (error " },
            { "设备拒绝：参数无效", "The device rejected the request: invalid argument" },
            { "设备拒绝：超出范围", "The device rejected the request: out of range" },
            { "设备硬件错误", "Device hardware error" },
            { "设备不支持该功能", "The device does not support this feature" },
            { "设备不支持该命令", "The device does not support this command" },
            { "设备忙，请稍后再试", "The device is busy, try again later" },
            { "设备不支持该操作", "The device does not support this operation" },
            { "设备返回错误 ", "The device returned error " },
            { "设备没有响应（可能已休眠或断开）", "The device is not responding (asleep or disconnected?)" },

            // key capture
            { "键盘按键", "Keyboard key" },
            { "按下要映射的按键", "Press the key to assign" },
            { "可以组合 Ctrl / Shift / Alt / Win", "Ctrl / Shift / Alt / Win combinations work too" },
            { "等待按键…", "Waiting for a key…" },
            { "取消", "Cancel" }, { "确定", "OK" },
            { "这个键暂不支持", "This key isn't supported" },

            // actions (Actions.cs translates at display time)
            { "鼠标", "Mouse" }, { "功能", "Functions" }, { "媒体", "Media" }, { "其他", "Other" },
            { "左键", "Left click" }, { "右键", "Right click" }, { "中键", "Middle click" }, { "后退", "Back" }, { "前进", "Forward" },
            { "DPI 循环切换", "DPI cycle" }, { "DPI 加", "DPI up" }, { "DPI 减", "DPI down" },
            { "DPI Shift（按住切换）", "DPI Shift (hold)" }, { "恢复默认 DPI", "Default DPI" },
            { "切换配置", "Cycle profiles" }, { "滚轮左倾", "Scroll left" }, { "滚轮右倾", "Scroll right" },
            { "播放 / 暂停", "Play / Pause" }, { "下一曲", "Next track" }, { "上一曲", "Previous track" },
            { "静音", "Mute" }, { "音量加", "Volume up" }, { "音量减", "Volume down" }, { "禁用", "Disabled" },
            { "下一个配置", "Next profile" }, { "上一个配置", "Previous profile" }, { "电量指示", "Battery indicator" },
            { "指定配置", "Specific profile" }, { "性能模式", "Performance mode" }, { "主机按键", "Host button" },
            { "向下滚动", "Scroll down" }, { "向上滚动", "Scroll up" },
            { "未分配", "Unassigned" }, { "鼠标按键 0x", "Mouse button 0x" }, { "媒体键 0x", "Media key 0x" },
            { "功能 ", "Function " }, { "宏（保留原设置）", "Macro (kept as is)" }, { "自定义 ", "Custom " },
            { "空格", "Space" }, { "小键盘", "Num" }, { "菜单键", "Menu" }, { "键盘（空）", "Keyboard (none)" }, { "键 0x", "Key 0x" },
        };
    }

    // %AppData%\OnboardLite\settings.ini, simple key=value lines.
    static class Settings
    {
        static string FilePath
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OnboardLite", "settings.ini"); }
        }

        static Dictionary<string, string> Read()
        {
            var d = new Dictionary<string, string>();
            try
            {
                if (System.IO.File.Exists(FilePath))
                    foreach (var line in System.IO.File.ReadAllLines(FilePath))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0) d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                    }
            }
            catch { }
            return d;
        }

        public static string Get(string key)
        {
            string v;
            return Read().TryGetValue(key, out v) ? v : null;
        }

        public static void Set(string key, string value)
        {
            var d = Read();
            d[key] = value;
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath));
                var lines = new List<string>();
                foreach (var kv in d) lines.Add(kv.Key + "=" + kv.Value);
                System.IO.File.WriteAllLines(FilePath, lines);
            }
            catch { }
        }
    }
}

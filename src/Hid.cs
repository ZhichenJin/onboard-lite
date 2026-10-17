using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace OnboardLite
{
    sealed class HidInfo
    {
        public string Path;
        public ushort Vid, Pid, UsagePage, Usage;
        public int InputLength, OutputLength;
        public string Product;
    }

    // Plain Win32 HID access: SetupAPI enumeration, one overlapped handle for reading, one for writing.
    static class HidApi
    {
        const int DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10;
        const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

        [StructLayout(LayoutKind.Sequential)]
        struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES attrs);
        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, out HIDP_CAPS caps);
        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetProductString(SafeFileHandle h, byte[] buf, int len);

        [DllImport("setupapi.dll", SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, int index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr info);
        [DllImport("setupapi.dll")]
        static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        public static List<HidInfo> Enumerate(ushort vid)
        {
            var list = new List<HidInfo>();
            Guid guid;
            HidD_GetHidGuid(out guid);
            IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == new IntPtr(-1)) return list;
            try
            {
                var data = new SP_DEVICE_INTERFACE_DATA();
                data.cbSize = Marshal.SizeOf(data);
                for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data); i++)
                {
                    int size;
                    SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out size, IntPtr.Zero);
                    if (size <= 0) continue;
                    IntPtr detail = Marshal.AllocHGlobal(size);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out size, IntPtr.Zero)) continue;
                        var info = Query(Marshal.PtrToStringUni(IntPtr.Add(detail, 4)), vid);
                        if (info != null) list.Add(info);
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return list;
        }

        static HidInfo Query(string path, ushort vid)
        {
            using (var h = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
            {
                if (h.IsInvalid) return null;
                var attrs = new HIDD_ATTRIBUTES();
                attrs.Size = Marshal.SizeOf(attrs);
                if (!HidD_GetAttributes(h, ref attrs) || attrs.VendorID != vid) return null;
                IntPtr pre;
                if (!HidD_GetPreparsedData(h, out pre)) return null;
                HIDP_CAPS caps;
                try { HidP_GetCaps(pre, out caps); }
                finally { HidD_FreePreparsedData(pre); }
                var name = new byte[256];
                string product = "";
                if (HidD_GetProductString(h, name, name.Length))
                {
                    product = Encoding.Unicode.GetString(name);
                    int nul = product.IndexOf('\0');
                    if (nul >= 0) product = product.Substring(0, nul);
                }
                return new HidInfo
                {
                    Path = path, Vid = attrs.VendorID, Pid = attrs.ProductID,
                    UsagePage = caps.UsagePage, Usage = caps.Usage,
                    InputLength = caps.InputReportByteLength, OutputLength = caps.OutputReportByteLength,
                    Product = product
                };
            }
        }

        public static SafeFileHandle Open(string path, bool overlapped)
        {
            return CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero,
                OPEN_EXISTING, overlapped ? FILE_FLAG_OVERLAPPED : 0, IntPtr.Zero);
        }
    }

    sealed class HidStream : IDisposable
    {
        const int WriteTimeoutMs = 1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CancelIoEx(SafeFileHandle handle, IntPtr overlapped);

        readonly FileStream input, output;
        readonly SafeFileHandle writeHandle;
        volatile bool closed;
        public readonly HidInfo Info;
        public event Action<byte[]> Report;
        public event Action Lost;

        public HidStream(HidInfo info)
        {
            Info = info;
            var read = HidApi.Open(info.Path, true);
            if (read.IsInvalid) throw new IOException(Lang.T("无法打开设备（错误 ") + Marshal.GetLastWin32Error() + Lang.T("）"));
            var write = HidApi.Open(info.Path, true);
            if (write.IsInvalid)
            {
                read.Dispose();
                throw new IOException(Lang.T("无法打开设备（错误 ") + Marshal.GetLastWin32Error() + Lang.T("）"));
            }
            input = new FileStream(read, FileAccess.Read, 1, true);
            output = new FileStream(write, FileAccess.Write, 1, true);
            writeHandle = write;
            var t = new Thread(ReadLoop);
            t.IsBackground = true;
            t.Name = "hid-read";
            t.Start();
        }

        void ReadLoop()
        {
            var buf = new byte[Math.Max(Info.InputLength, 1)];
            while (!closed)
            {
                int n;
                try { n = input.Read(buf, 0, buf.Length); }
                catch { break; }
                if (n <= 0) break;
                var copy = new byte[n];
                Buffer.BlockCopy(buf, 0, copy, 0, n);
                var handler = Report;
                if (handler != null) handler(copy);
            }
            if (!closed)
            {
                var lost = Lost;
                if (lost != null) lost();
            }
        }

        // Overlapped write with a timeout: a device that is being unplugged or is still starting up
        // can leave a plain WriteFile blocked forever, which would freeze discovery and polling.
        public void Write(byte[] report)
        {
            var buf = new byte[Info.OutputLength];
            Buffer.BlockCopy(report, 0, buf, 0, Math.Min(report.Length, buf.Length));
            var pending = output.BeginWrite(buf, 0, buf.Length, null, null);
            if (!pending.AsyncWaitHandle.WaitOne(WriteTimeoutMs))
            {
                try { CancelIoEx(writeHandle, IntPtr.Zero); } catch { }
                try { output.EndWrite(pending); } catch { }
                throw new TimeoutException(Lang.T("设备没有响应（可能已休眠或断开）"));
            }
            output.EndWrite(pending);
        }

        public void Dispose()
        {
            closed = true;
            try { input.Dispose(); } catch { }
            try { output.Dispose(); } catch { }
        }
    }
}

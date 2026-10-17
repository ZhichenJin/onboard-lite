using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OnboardLite
{
    sealed class Candidate
    {
        public HidInfo Info;
        public byte Index;
    }

    sealed class BatteryState
    {
        public int Percent = -1;
        public bool Charging, Full;
    }

    static class Crc
    {
        // CRC-CCITT (poly 0x1021, init 0xFFFF), stored big-endian in the last two bytes of every sector.
        public static ushort Ccitt(byte[] data, int length)
        {
            int crc = 0xFFFF;
            for (int i = 0; i < length; i++)
            {
                crc ^= data[i] << 8;
                for (int b = 0; b < 8; b++) crc = (crc & 0x8000) != 0 ? ((crc << 1) ^ 0x1021) & 0xFFFF : (crc << 1) & 0xFFFF;
            }
            return (ushort)crc;
        }

        public static bool Valid(byte[] sector)
        {
            int n = sector.Length - 2;
            return Ccitt(sector, n) == ((sector[n] << 8) | sector[n + 1]);
        }

        public static void Seal(byte[] sector)
        {
            int n = sector.Length - 2;
            ushort crc = Ccitt(sector, n);
            sector[n] = (byte)(crc >> 8);
            sector[n + 1] = (byte)crc;
        }

        // Compares sector contents, ignoring the CRC.
        public static bool SameBody(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length - 2; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }

    // Sector 0x0000: 4-byte entries {sector hi, sector lo, enabled, 0xFF}, terminated by 0xFFFF.
    static class ProfileDirectory
    {
        public static List<KeyValuePair<int, bool>> Entries(byte[] dir)
        {
            var list = new List<KeyValuePair<int, bool>>();
            for (int i = 0; i + 3 < dir.Length - 2; i += 4)
            {
                int sector = (dir[i] << 8) | dir[i + 1];
                if (sector == 0xFFFF) break;
                list.Add(new KeyValuePair<int, bool>(sector, dir[i + 2] == 1));
            }
            return list;
        }

        public static bool IsEnabled(byte[] dir, int sector)
        {
            foreach (var e in Entries(dir)) if (e.Key == sector) return e.Value;
            return false;
        }

        public static void SetEnabled(byte[] dir, int sector, bool enabled)
        {
            int i = 0;
            for (; i + 3 < dir.Length - 2; i += 4)
            {
                int s = (dir[i] << 8) | dir[i + 1];
                if (s == sector) { dir[i + 2] = (byte)(enabled ? 1 : 0); return; }
                if (s == 0xFFFF) break;
            }
            if (i + 3 >= dir.Length - 2) return;
            dir[i] = (byte)(sector >> 8);
            dir[i + 1] = (byte)sector;
            dir[i + 2] = (byte)(enabled ? 1 : 0);
            dir[i + 3] = 0xFF;
        }
    }

    // One Logitech device reachable over HID++ 2.0 that has the onboard profiles feature (0x8100).
    sealed class MouseDevice : IDisposable
    {
        public const ushort LogitechVid = 0x046D;
        const int RomBase = 0x100;

        public readonly Hidpp Io;
        public readonly bool Wireless;
        public string Name = "", UnitId = "", Firmware = "";
        public int Format, ProfileCount, OobCount, ButtonCount, SectorSize;
        public int DpiMin = 100, DpiMax = 25600, DpiStep = 50;
        public int[] DpiList;
        public int[] RatesMs = { 8, 4, 2, 1 };
        public int LightFeature;   // 0 = no lighting, else 0x8070 or 0x8071
        byte f8100, fBattery;
        int batteryKind;
        bool backedUp;

        // silent counts HID++ interfaces where nothing answered: a receiver whose mouse is asleep or off.
        public static List<Candidate> Discover(out int silent)
        {
            var list = new List<Candidate>();
            silent = 0;
            foreach (var info in HidApi.Enumerate(LogitechVid))
            {
                if (info.UsagePage != 0xFF00 || info.Usage != 0x0002 || info.OutputLength < 20) continue;
                try
                {
                    using (var io = new Hidpp(info, Hidpp.Direct))
                    {
                        var found = io.Probe(new byte[] { Hidpp.Direct, 1, 2, 3, 4, 5, 6 }, 700);
                        if (found.Contains(Hidpp.Direct)) found = new List<byte> { Hidpp.Direct };
                        if (found.Count == 0) silent++;
                        foreach (var idx in found) list.Add(new Candidate { Info = info, Index = idx });
                    }
                }
                catch { }
            }
            return list;
        }

        public MouseDevice(Candidate c)
        {
            Io = new Hidpp(c.Info, c.Index);
            Wireless = c.Index != Hidpp.Direct;
            try { Load(); }
            catch { Io.Dispose(); throw; }
        }

        public bool Editable { get { return Format >= 1 && Format <= 5; } }

        void Load()
        {
            f8100 = Io.Feature(0x8100);
            Name = ReadName();
            if (string.IsNullOrEmpty(Name)) Name = Io.Info.Product;
            if (f8100 == 0) throw new NotSupportedException(Name + Lang.T(" 没有板载内存功能"));

            var d = Io.Call(f8100, 0);
            Format = d[1];
            ProfileCount = d[3];
            OobCount = d[4];
            ButtonCount = d[5];
            SectorSize = (d[7] << 8) | d[8];
            if (SectorSize < 32 || SectorSize > 4096) throw new InvalidDataException(Lang.T("板载内存扇区大小异常：") + SectorSize);

            var f3 = Io.Feature(0x0003);
            if (f3 != 0)
            {
                var r = Io.Call(f3, 0);
                UnitId = BitConverter.ToString(r, 1, 4).Replace("-", "");
                for (int e = 0; e < r[0] && e < 8; e++)
                {
                    var fw = Io.Call(f3, 1, (byte)e);
                    if (fw[0] != 0) continue;   // entity type 0 = main application firmware
                    Firmware = Encoding.ASCII.GetString(fw, 1, 3).Trim('\0', ' ') + " " + fw[4].ToString("X") + "." + fw[5].ToString("X2")
                        + " · build " + fw[6].ToString("X2") + fw[7].ToString("X2");
                    break;
                }
            }

            var f2201 = Io.Feature(0x2201);
            if (f2201 != 0)
            {
                var r = Io.Call(f2201, 1, 0);
                var values = new List<int>();
                int step = 0;
                for (int i = 1; i + 1 < 16; i += 2)
                {
                    int v = (r[i] << 8) | r[i + 1];
                    if (v == 0) break;
                    if (v >= 0xE000) step = v - 0xE000;
                    else values.Add(v);
                }
                if (values.Count >= 2 && step > 0)
                {
                    DpiMin = values[0]; DpiMax = values[values.Count - 1]; DpiStep = step;
                }
                else if (values.Count > 0)
                {
                    DpiList = values.ToArray(); DpiMin = values[0]; DpiMax = values[values.Count - 1]; DpiStep = 1;
                }
            }

            // Lighting, decided like OnboardMemoryManager: 0x8071 (RGB effects) wins over 0x8070 (color LED effects);
            // a feature flagged hidden (type bit 0x40) or 0x8070 reporting no zones means no lighting.
            var rgb = Io.Call(0, 0, 0x80, 0x71);
            if (rgb[0] != 0 && (rgb[1] & 0x40) == 0) LightFeature = 0x8071;
            else
            {
                var led = Io.Call(0, 0, 0x80, 0x70);
                if (led[0] != 0 && (led[1] & 0x40) == 0)
                {
                    try { if (Io.Call(led[0], 0)[0] > 0) LightFeature = 0x8070; }
                    catch (HidppError) { }
                }
            }

            var f8060 = Io.Feature(0x8060);
            if (f8060 != 0)
            {
                int mask = Io.Call(f8060, 0)[0];
                var rates = new List<int>();
                for (int b = 7; b >= 0; b--) if ((mask & (1 << b)) != 0) rates.Add(b + 1);
                if (rates.Count > 0) RatesMs = rates.ToArray();
            }
        }

        string ReadName()
        {
            var f = Io.Feature(0x0005);
            if (f == 0) return null;
            int len = Io.Call(f, 0)[0];
            var bytes = new List<byte>();
            while (bytes.Count < len)
            {
                var r = Io.Call(f, 1, (byte)bytes.Count);
                int before = bytes.Count;
                for (int i = 0; i < 16 && bytes.Count < len && r[i] != 0; i++) bytes.Add(r[i]);
                if (bytes.Count == before) break;
            }
            return Encoding.UTF8.GetString(bytes.ToArray()).Trim();
        }

        public int Snap(int dpi)
        {
            if (DpiList != null)
            {
                int best = DpiList[0];
                foreach (var v in DpiList) if (Math.Abs(v - dpi) < Math.Abs(best - dpi)) best = v;
                return best;
            }
            dpi = Math.Max(DpiMin, Math.Min(DpiMax, dpi));
            return DpiMin + (int)Math.Round((dpi - DpiMin) / (double)DpiStep) * DpiStep;
        }

        public BatteryState ReadBattery()
        {
            if (batteryKind == 0)
            {
                fBattery = Io.Feature(0x1004);
                if (fBattery != 0) batteryKind = 1;
                else
                {
                    fBattery = Io.Feature(0x1000);
                    batteryKind = fBattery != 0 ? 2 : -1;
                }
            }
            var s = new BatteryState();
            if (batteryKind == 1)
            {
                var r = Io.Call(fBattery, 1);
                s.Percent = r[0] > 0 ? r[0] : LevelPercent(r[1]);
                s.Charging = r[2] == 1 || r[2] == 2;
                s.Full = r[2] == 3;
            }
            else if (batteryKind == 2)
            {
                var r = Io.Call(fBattery, 0);
                s.Percent = r[0];
                s.Charging = r[2] == 1 || r[2] == 2 || r[2] == 4;
                s.Full = r[2] == 3;
            }
            return s;
        }

        static int LevelPercent(int level)
        {
            if ((level & 8) != 0) return 90;
            if ((level & 4) != 0) return 50;
            if ((level & 2) != 0) return 20;
            if ((level & 1) != 0) return 5;
            return -1;
        }

        // 0x8100 functions: 1 set mode, 2 get mode, 3 set profile, 4 get profile, 5 read,
        // 6 start write, 7 write 16 bytes, 8 end write, 0xB get DPI index, 0xC set DPI index.
        public int GetMode() { return Io.Call(f8100, 2)[0]; }
        public void SetMode(int mode) { Io.Call(f8100, 1, (byte)mode); }

        public int GetActiveSector()
        {
            var r = Io.Call(f8100, 4);
            return (r[0] << 8) | r[1];
        }

        public void SetActiveSector(int sector) { Io.Call(f8100, 3, (byte)(sector >> 8), (byte)sector); }
        public int GetDpiIndex() { return Io.Call(f8100, 0xB)[0]; }
        public void SetDpiIndex(int index) { Io.Call(f8100, 0xC, (byte)index); }

        public byte[] ReadSector(int sector)
        {
            var buf = new byte[SectorSize];
            for (int a = 0; a < SectorSize; a += 16)
            {
                int at = Math.Min(a, SectorSize - 16);
                var r = Io.Call(f8100, 5, (byte)(sector >> 8), (byte)sector, (byte)(at >> 8), (byte)at);
                Buffer.BlockCopy(r, 0, buf, at, 16);
            }
            return buf;
        }

        public byte[] ReadFactoryProfile(int sector)
        {
            int n = OobCount < 1 ? 1 : (OobCount < sector ? 1 : sector);
            return ReadSector(RomBase + n);
        }

        // Seals the CRC into data, writes it and reads it back to verify.
        public void WriteSector(int sector, byte[] data)
        {
            if (sector >= RomBase) throw new InvalidOperationException(Lang.T("拒绝写入只读区域"));
            if (data.Length != SectorSize) throw new ArgumentException(Lang.T("扇区长度不对"));
            Crc.Seal(data);
            Io.Call(f8100, 6, (byte)(sector >> 8), (byte)sector, 0, 0, (byte)(SectorSize >> 8), (byte)SectorSize);
            for (int a = 0; a < SectorSize; a += 16)
            {
                var chunk = new byte[16];
                for (int i = 0; i < 16; i++) chunk[i] = a + i < SectorSize ? data[a + i] : (byte)0xFF;
                Io.Call(f8100, 7, chunk);
            }
            Io.Call(f8100, 8);
            var back = ReadSector(sector);
            if (!back.SequenceEqual(data)) throw new IOException(Lang.T("写入后校验不一致（扇区 ") + sector + Lang.T("）"));
        }

        // Saves the sectors as they were on the device before this session's first write.
        public string BackupOnce(IDictionary<int, byte[]> sectors)
        {
            if (backedUp) return null;
            string safe = new string(Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OnboardLite", "backups", safe + "-" + UnitId, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(dir);
            foreach (var kv in sectors) File.WriteAllBytes(Path.Combine(dir, "sector_" + kv.Key.ToString("X4") + ".bin"), kv.Value);
            backedUp = true;
            return dir;
        }

        public void Dispose() { Io.Dispose(); }
    }
}

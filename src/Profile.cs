using System;
using System.Collections.Generic;
using System.Text;

namespace OnboardLite
{
    // Profile sector layout for formats 1-5, as built by OnboardMemoryManager's logi_nethidppio.dll:
    //   0x00 report rate (ms)   0x01 default DPI index   0x02 DPI-shift index   0x03 DPI table, 5 x u16 LE
    //   0x11 angle snapping     0x20 buttons, 16 x 4     0x60 G-shift layer      0xA0 name, 24 x UTF-16LE
    // Everything else is kept byte-for-byte.
    sealed class Profile
    {
        public const int MaxDpi = 5, MaxButtons = 16, NameChars = 24;
        const int RateAt = 0x00, DefaultAt = 0x01, ShiftAt = 0x02, DpiAt = 0x03, AngleAt = 0x11, ButtonsAt = 0x20, NameAt = 0xA0;
        const string DefaultName = "PROFILE_NAME_DEFAULT";

        public int Sector;
        public byte[] Raw;    // working copy
        public byte[] Base;   // what "no changes" means: device contents, or factory data for a blank sector
        public bool Blank;    // sector held no valid profile

        public bool Dirty { get { return !Crc.SameBody(Raw, Base); } }

        public int RateMs { get { return Raw[RateAt]; } set { Raw[RateAt] = (byte)value; } }
        public int DefaultIndex { get { return Raw[DefaultAt]; } set { Raw[DefaultAt] = (byte)value; } }
        public int ShiftIndex { get { return Raw[ShiftAt]; } set { Raw[ShiftAt] = (byte)value; } }

        public List<int> Dpis { get { return ReadDpis(Raw); } }

        public static List<int> ReadDpis(byte[] raw)
        {
            var list = new List<int>();
            for (int i = 0; i < MaxDpi; i++)
            {
                int v = raw[DpiAt + 2 * i] | (raw[DpiAt + 2 * i + 1] << 8);
                if (v == 0 || v == 0xFFFF) break;
                list.Add(v);
            }
            return list;
        }

        public void SetDpis(IList<int> values)
        {
            for (int i = 0; i < MaxDpi; i++)
            {
                int v = i < values.Count ? values[i] : 0;
                Raw[DpiAt + 2 * i] = (byte)v;
                Raw[DpiAt + 2 * i + 1] = (byte)(v >> 8);
            }
            if (DefaultIndex >= values.Count) DefaultIndex = 0;
            if (ShiftIndex >= values.Count) ShiftIndex = 0;
        }

        public bool AngleSnapping
        {
            get { return Raw[AngleAt] != 0 && Raw[AngleAt] != 0xFF; }
            set { Raw[AngleAt] = (byte)(value ? 1 : 0); }
        }

        // Lighting slots (OnboardMemoryManager writes one effect to every slot the format has):
        //   0xD0 / 0xDB  zone 0 / zone 1 (Feature 0x8070 logo/side, or 0x8071 cluster 0/1 active)
        //   0xE6 / 0xF1  0x8071 cluster 0/1 power-save copy,  0xFC  0x8071 lighting flags (0 = per-cluster effect)
        const int LightAt = 0xD0, LightAt2 = 0xDB, PassiveAt = 0xE6, PassiveAt2 = 0xF1, LightFlagsAt = 0xFC;

        public LightEffect GetLighting() { return LightEffect.Read(Raw, LightAt); }

        public void SetLighting(LightEffect e, int feature)
        {
            var active = e.Bytes(false);
            Buffer.BlockCopy(active, 0, Raw, LightAt, LightEffect.Size);
            Buffer.BlockCopy(active, 0, Raw, LightAt2, LightEffect.Size);
            if (feature == 0x8071)
            {
                var passive = e.Bytes(true);
                Buffer.BlockCopy(passive, 0, Raw, PassiveAt, LightEffect.Size);
                Buffer.BlockCopy(passive, 0, Raw, PassiveAt2, LightEffect.Size);
                Raw[LightFlagsAt] = 0;
            }
        }

        public byte[] GetButton(int i)
        {
            var b = new byte[4];
            Buffer.BlockCopy(Raw, ButtonsAt + 4 * i, b, 0, 4);
            return b;
        }

        public void SetButton(int i, byte[] code) { Buffer.BlockCopy(code, 0, Raw, ButtonsAt + 4 * i, 4); }

        public string Name
        {
            get
            {
                var sb = new StringBuilder();
                for (int i = 0; i < NameChars; i++)
                {
                    int c = Raw[NameAt + 2 * i] | (Raw[NameAt + 2 * i + 1] << 8);
                    if (c == 0 || c == 0xFFFF) break;
                    sb.Append((char)c);
                }
                var s = sb.ToString();
                return s == DefaultName ? "" : s;
            }
            set
            {
                var s = string.IsNullOrEmpty(value) ? DefaultName : value;
                for (int i = 0; i < NameChars; i++)
                {
                    int c = i < s.Length ? s[i] : 0;
                    Raw[NameAt + 2 * i] = (byte)c;
                    Raw[NameAt + 2 * i + 1] = (byte)(c >> 8);
                }
            }
        }
    }
}

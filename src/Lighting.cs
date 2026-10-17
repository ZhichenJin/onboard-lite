using System;

namespace OnboardLite
{
    // One lighting effect as stored in an onboard profile: 11 bytes = effect id + 10 parameter bytes.
    // Layout from logi_nethidppio.dll (Feature8070/8071 EffectData*), identical for both features:
    //   1  fixed      p[0..2] RGB, p[3] animation (2 = none)
    //   3  cycling    p[5..6] period ms (BE), p[7] intensity
    //   10 breathing  p[0..2] RGB, p[3..4] period ms (BE), p[5] waveform, p[6] intensity
    //   0  off;  0xFF = slot never written (device default)
    sealed class LightEffect
    {
        public const int Off = 0, Fixed = 1, Cycling = 3, Breathing = 10, Unset = 0xFF;
        public const int Size = 11;

        public int Id = Unset;
        public byte R = 0x5C, G = 0x8D, B = 0xFF;
        public int Period = 5000;      // ms
        public int Intensity = 100;    // 1-100

        public bool HasColor { get { return Id == Fixed || Id == Breathing; } }
        public bool HasSpeed { get { return Id == Cycling || Id == Breathing; } }
        public bool Known { get { return Id == Off || Id == Fixed || Id == Cycling || Id == Breathing; } }

        public LightEffect Clone() { return (LightEffect)MemberwiseClone(); }

        public static LightEffect Read(byte[] raw, int at)
        {
            var e = new LightEffect { Id = raw[at] };
            int p = at + 1;
            switch (e.Id)
            {
                case Fixed:
                    e.R = raw[p]; e.G = raw[p + 1]; e.B = raw[p + 2];
                    break;
                case Cycling:
                    e.Period = (raw[p + 5] << 8) | raw[p + 6];
                    e.Intensity = raw[p + 7];
                    break;
                case Breathing:
                    e.R = raw[p]; e.G = raw[p + 1]; e.B = raw[p + 2];
                    e.Period = (raw[p + 3] << 8) | raw[p + 4];
                    e.Intensity = raw[p + 6];
                    break;
            }
            if (e.Period <= 0 || e.Period == 0xFFFF) e.Period = 5000;
            if (e.Intensity <= 0 || e.Intensity > 100) e.Intensity = 100;
            return e;
        }

        // passive = the dimmer copy used while the device is in power-save (OnboardMemoryManager halves it).
        public byte[] Bytes(bool passive)
        {
            var b = new byte[Size];
            b[0] = (byte)Id;
            int intensity = Math.Max(1, Math.Min(100, passive ? Intensity / 2 : Intensity));
            switch (Id)
            {
                case Fixed:
                    int div = passive ? 2 : 1;
                    b[1] = (byte)(R / div); b[2] = (byte)(G / div); b[3] = (byte)(B / div);
                    b[4] = 2;
                    break;
                case Cycling:
                    b[6] = (byte)(Period >> 8); b[7] = (byte)Period;
                    b[8] = (byte)intensity;
                    break;
                case Breathing:
                    b[1] = R; b[2] = G; b[3] = B;
                    b[4] = (byte)(Period >> 8); b[5] = (byte)Period;
                    b[7] = (byte)intensity;
                    break;
            }
            return b;
        }
    }
}

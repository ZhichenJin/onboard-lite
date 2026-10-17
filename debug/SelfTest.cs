using System;
using System.IO;
using System.Linq;

namespace OnboardLite
{
    // Offline checks against sector dumps read from the real mouse.
    static class SelfTest
    {
        static int fails;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) fails++; }

        static void Main(string[] args)
        {
            string dir = args[0];
            var p1 = File.ReadAllBytes(Path.Combine(dir, "prof_0001.bin"));
            var p2 = File.ReadAllBytes(Path.Combine(dir, "prof_0002.bin"));
            var rom = File.ReadAllBytes(Path.Combine(dir, "rom_0101.bin"));
            var d0 = File.ReadAllBytes(Path.Combine(dir, "dir.bin"));

            Check(Crc.Valid(p1) && Crc.Valid(p2) && Crc.Valid(d0), "CRC of device sectors");
            Check(!Crc.Valid(rom), "ROM sector has no CRC");

            var a = new Profile { Sector = 1, Base = p1, Raw = (byte[])p1.Clone() };
            Check(a.RateMs == 1, "profile 1 report rate 1 ms");
            Check(a.Dpis.SequenceEqual(new[] { 2000 }), "profile 1 DPI table = [2000]: " + string.Join(",", a.Dpis));
            Check(a.DefaultIndex == 0 && a.ShiftIndex == 0, "profile 1 default/shift index");
            Check(a.Name == "", "PROFILE_NAME_DEFAULT shows as empty");
            Check(!a.AngleSnapping, "angle snapping off");
            string[] expect = { "左键", "右键", "中键", "后退", "前进" };
            for (int i = 0; i < 5; i++) Check(Actions.Describe(a.GetButton(i)) == expect[i], "button " + i + " = " + Actions.Describe(a.GetButton(i)));
            Check(!a.Dirty, "fresh profile not dirty");

            var b = new Profile { Sector = 2, Base = p2, Raw = (byte[])p2.Clone() };
            Check(b.Dpis.SequenceEqual(new[] { 400, 800, 1600, 3200, 6400 }), "profile 2 DPI table: " + string.Join(",", b.Dpis));
            Check(b.DefaultIndex == 1, "profile 2 default index 1 (800)");

            // edits round-trip and only touch their own bytes
            a.SetDpis(new[] { 400, 800, 1600 });
            a.DefaultIndex = 1; a.ShiftIndex = 0; a.RateMs = 2; a.Name = "CS2"; a.AngleSnapping = true;
            a.SetButton(4, Actions.KeyCode(0x01, 0x06));
            Check(a.Dirty, "edited profile is dirty");
            var changed = Enumerable.Range(0, p1.Length - 2).Where(i => a.Raw[i] != p1[i]).ToArray();
            Check(changed.All(i => i <= 0x0C || i == 0x11 || (i >= 0x30 && i < 0x34) || (i >= 0xA0 && i < 0xD0)), "edits stay inside known fields: " + string.Join(",", changed.Select(i => i.ToString("X2"))));
            var re = new Profile { Raw = a.Raw, Base = a.Raw };
            Check(re.Dpis.SequenceEqual(new[] { 400, 800, 1600 }) && re.DefaultIndex == 1 && re.RateMs == 2 && re.Name == "CS2" && re.AngleSnapping, "round-trip of edited fields");
            Check(Actions.Describe(re.GetButton(4)) == "Ctrl + C", "keyboard binding label: " + Actions.Describe(re.GetButton(4)));
            var sealedCopy = (byte[])a.Raw.Clone(); Crc.Seal(sealedCopy);
            Check(Crc.Valid(sealedCopy), "sealed CRC validates");

            // factory data matches the stored untouched profile 3 apart from the CRC
            var p3 = File.ReadAllBytes(Path.Combine(dir, "prof_0003.bin"));
            var romSealed = (byte[])rom.Clone(); Crc.Seal(romSealed);
            Check(romSealed.SequenceEqual(p3), "ROM + CRC == stored profile 3");

            // directory
            var entries = ProfileDirectory.Entries(d0);
            Check(entries.Count == 5 && entries[0].Key == 1 && entries[0].Value && entries[1].Value && !entries[2].Value, "directory entries");
            var d1 = (byte[])d0.Clone();
            ProfileDirectory.SetEnabled(d1, 3, true);
            Check(ProfileDirectory.IsEnabled(d1, 3) && d1.Where((x, i) => x != d0[i]).Count() == 1, "enabling profile 3 changes one byte");

            // lighting: slots never written read as "device default", and each effect round-trips
            var l = new Profile { Sector = 1, Base = p1, Raw = (byte[])p1.Clone() };
            Check(l.GetLighting().Id == LightEffect.Unset, "unwritten lighting slot = device default");
            var fixedFx = new LightEffect { Id = LightEffect.Fixed, R = 0x12, G = 0x34, B = 0x56 };
            l.SetLighting(fixedFx, 0x8071);
            var back = l.GetLighting();
            Check(back.Id == LightEffect.Fixed && back.R == 0x12 && back.G == 0x34 && back.B == 0x56, "fixed color round-trip");
            Check(l.Raw[0xD0 + 4] == 2, "fixed: animation byte = no effect");
            Check(l.Raw.Skip(0xDB).Take(11).SequenceEqual(l.Raw.Skip(0xD0).Take(11)), "zone 1 gets the same effect");
            Check(l.Raw[0xE6] == 1 && l.Raw[0xE7] == 0x09 && l.Raw[0xE8] == 0x1A && l.Raw[0xE9] == 0x2B, "8071 power-save copy at half color");
            Check(l.Raw[0xFC] == 0, "8071 lighting flags = 0");
            var breath = new LightEffect { Id = LightEffect.Breathing, R = 255, G = 0, B = 128, Period = 3500, Intensity = 80 };
            l.SetLighting(breath, 0x8071);
            back = l.GetLighting();
            Check(back.Id == LightEffect.Breathing && back.R == 255 && back.B == 128 && back.Period == 3500 && back.Intensity == 80, "breathing round-trip");
            Check(l.Raw[0xD0 + 4] == 0x0D && l.Raw[0xD0 + 5] == 0xAC && l.Raw[0xD0 + 7] == 80, "breathing: period big-endian at p[3..4], intensity at p[6]");
            Check(l.Raw[0xE6 + 7] == 40, "breathing power-save intensity halved");
            var cycle = new LightEffect { Id = LightEffect.Cycling, Period = 5000, Intensity = 100 };
            l.SetLighting(cycle, 0x8070);
            back = l.GetLighting();
            Check(back.Id == LightEffect.Cycling && back.Period == 5000 && back.Intensity == 100, "cycling round-trip");
            Check(l.Raw[0xD0 + 6] == 0x13 && l.Raw[0xD0 + 7] == 0x88 && l.Raw[0xD0 + 8] == 100, "cycling: period at p[5..6], intensity at p[7]");
            Check(l.Raw[0xE6] == 10, "8070 device leaves the 8071 power-save slots alone");
            var changedBytes = Enumerable.Range(0, p1.Length - 2).Where(i => l.Raw[i] != p1[i]).ToArray();
            Check(changedBytes.All(i => i >= 0xD0 && i <= 0xFC), "lighting edits stay in 0xD0-0xFC");

            Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
        }
    }
}

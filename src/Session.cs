using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OnboardLite
{
    sealed class PollResult
    {
        public int Mode = -1, Active = -1, DpiIndex = -1;
        public BatteryState Battery;
        public bool Ok;
    }

    sealed class SaveResult
    {
        public string BackupDir;
        public Exception Error;
    }

    // Everything the UI edits for one device. Device I/O methods run off the UI thread.
    sealed class Session : IDisposable
    {
        public readonly MouseDevice Dev;
        public readonly string Key;
        public List<Profile> Profiles = new List<Profile>();
        public Dictionary<int, byte[]> Originals = new Dictionary<int, byte[]>();
        public byte[] Dir, DirBase, DirClean;
        public int Active = -1, DpiIndex = -1, Mode, Selected;
        public BatteryState Battery;
        public bool Loaded, Offline, Busy;
        public string Note;

        public Session(MouseDevice dev, string key)
        {
            Dev = dev;
            Key = key;
        }

        public string Path { get { return Dev.Io.Info.Path; } }
        public Profile Current { get { return Selected >= 0 && Selected < Profiles.Count ? Profiles[Selected] : null; } }
        public Profile ActiveProfile { get { return Profiles.FirstOrDefault(p => p.Sector == Active); } }
        public bool Enabled(Profile p) { return Dir != null && ProfileDirectory.IsEnabled(Dir, p.Sector); }
        public bool HasChanges { get { return Loaded && (Profiles.Any(p => p.Dirty) || !Crc.SameBody(Dir, DirBase)); } }

        // A profile can be switched to on the device only if the device already has it enabled.
        public bool CanActivate(Profile p)
        {
            return Mode != 2 && CanActivateIgnoringMode(p);
        }

        public bool CanActivateIgnoringMode(Profile p)
        {
            return !p.Blank && Crc.Valid(DirBase) && ProfileDirectory.IsEnabled(DirBase, p.Sector);
        }

        public string Title(Profile p)
        {
            var n = p.Name;
            return n.Length > 0 ? n : Lang.T("配置 ") + (Profiles.IndexOf(p) + 1);
        }

        public void Load()
        {
            var d = Dev;
            Mode = d.GetMode();
            DirBase = d.ReadSector(0);
            Originals[0] = DirBase;
            Dir = (byte[])DirBase.Clone();
            if (!Crc.Valid(DirBase))
            {
                for (int i = 0; i < Dir.Length; i++) Dir[i] = 0xFF;
                Note = Lang.T("配置目录无效，已重建（保存后写入）");
            }

            var sectors = ProfileDirectory.Entries(Dir).Select(e => e.Key).Where(x => x > 0 && x < 0x100).Take(d.ProfileCount).ToList();
            for (int x = 1; sectors.Count < d.ProfileCount && x < 0x100; x++) if (!sectors.Contains(x)) sectors.Add(x);
            if (sectors.Count > 0 && !sectors.Any(x => ProfileDirectory.IsEnabled(Dir, x))) ProfileDirectory.SetEnabled(Dir, sectors[0], true);
            DirClean = (byte[])Dir.Clone();

            var list = new List<Profile>();
            foreach (var sector in sectors)
            {
                var raw = d.ReadSector(sector);
                Originals[sector] = raw;
                var p = new Profile { Sector = sector, Base = raw };
                if (!Crc.Valid(raw))
                {
                    p.Base = d.ReadFactoryProfile(sector);
                    p.Blank = true;
                }
                p.Raw = (byte[])p.Base.Clone();
                list.Add(p);
            }
            Profiles = list;
            Active = d.GetActiveSector();
            DpiIndex = d.GetDpiIndex();
            try { Battery = d.ReadBattery(); } catch { }
            Selected = Math.Max(0, Profiles.FindIndex(p => p.Sector == Active));
            Loaded = true;
        }

        public PollResult Poll()
        {
            var r = new PollResult();
            try
            {
                r.Mode = Dev.GetMode();
                r.Active = Dev.GetActiveSector();
                r.DpiIndex = Dev.GetDpiIndex();
                try { r.Battery = Dev.ReadBattery(); } catch (HidppError) { }
                r.Ok = true;
            }
            catch { }
            return r;
        }

        public void Apply(PollResult r)
        {
            Offline = !r.Ok;
            if (!r.Ok) return;
            Mode = r.Mode;
            Active = r.Active;
            DpiIndex = r.DpiIndex;
            if (r.Battery != null) Battery = r.Battery;
        }

        public void Revert()
        {
            foreach (var p in Profiles) p.Raw = (byte[])p.Base.Clone();
            Dir = (byte[])DirClean.Clone();
        }

        // Writes changed sectors, then the directory, then reloads the profile to keep in use.
        // Updates Base for whatever reached the device, even when a later step fails.
        public SaveResult Save()
        {
            var result = new SaveResult();
            var jobs = Profiles.Where(p => p.Dirty || (p.Blank && Enabled(p)))
                .Select(p => new KeyValuePair<Profile, byte[]>(p, (byte[])p.Raw.Clone())).ToList();
            var dirData = (byte[])Dir.Clone();
            bool writeDir = !Crc.SameBody(Dir, DirBase) || !Crc.Valid(DirBase);
            var cur = Current;
            var keep = cur != null && Enabled(cur) ? cur : Profiles.FirstOrDefault(Enabled);
            try
            {
                result.BackupDir = Dev.BackupOnce(Originals);
                foreach (var job in jobs)
                {
                    Dev.WriteSector(job.Key.Sector, job.Value);
                    job.Key.Base = job.Value;
                    job.Key.Blank = false;
                }
                if (writeDir)
                {
                    Dev.WriteSector(0, dirData);
                    DirBase = dirData;
                    DirClean = (byte[])dirData.Clone();
                }
                if (Mode != 2 && keep != null) Dev.SetActiveSector(keep.Sector);
                Active = Dev.GetActiveSector();
                DpiIndex = Dev.GetDpiIndex();
            }
            catch (Exception ex) { result.Error = ex; }
            return result;
        }

        public void Dispose() { Dev.Dispose(); }
    }

    sealed class DiscoveryResult
    {
        public List<Session> Sessions = new List<Session>();
        public Dictionary<string, string> Failed = new Dictionary<string, string>();
        public List<HidInfo> Silent = new List<HidInfo>();
        public HashSet<string> Paths = new HashSet<string>();
    }

    static class Discovery
    {
        // Probes every Logitech HID++ interface; opens and loads devices whose key is not in skip.
        public static DiscoveryResult Run(ICollection<string> skip)
        {
            var r = new DiscoveryResult();
            foreach (var info in HidApi.Enumerate(MouseDevice.LogitechVid))
            {
                if (info.UsagePage != 0xFF00 || info.Usage != 0x0002 || info.OutputLength < 20) continue;
                r.Paths.Add(info.Path);
                List<byte> found;
                try
                {
                    using (var io = new Hidpp(info, Hidpp.Direct))
                        found = io.Probe(new byte[] { Hidpp.Direct, 1, 2, 3, 4, 5, 6 }, 700);
                }
                catch (Exception ex) { Log.W("probe failed " + info.Pid.ToString("X4") + ": " + ex.Message); continue; }
                Log.W("probe " + info.Pid.ToString("X4") + " -> [" + string.Join(",", found) + "]");
                if (found.Contains(Hidpp.Direct)) found = new List<byte> { Hidpp.Direct };
                if (found.Count == 0) r.Silent.Add(info);

                foreach (var idx in found)
                {
                    string key = info.Path + "#" + idx;
                    if (skip.Contains(key)) continue;
                    MouseDevice dev = null;
                    try
                    {
                        dev = new MouseDevice(new Candidate { Info = info, Index = idx });
                        var s = new Session(dev, key);
                        if (dev.Editable) s.Load();
                        r.Sessions.Add(s);
                    }
                    catch (Exception ex)
                    {
                        if (dev != null) dev.Dispose();
                        Log.W("open failed " + key + ": " + ex);
                        // Only a device that really lacks onboard profiles is remembered and skipped;
                        // anything else (e.g. a mouse still starting up after being plugged in) is retried next scan.
                        if (ex is NotSupportedException || ex is InvalidDataException) r.Failed[key] = ex.Message;
                    }
                }
            }
            return r;
        }
    }
}

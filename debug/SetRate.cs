using System;
using System.Linq;

namespace OnboardLite
{
    // Debug tool: sets the report rate of one profile through Session.Save, the same path the UI uses.
    // usage: setrate <profile number> <Hz>
    static class SetRate
    {
        static int Main(string[] args)
        {
            int profile = int.Parse(args[0]), hz = int.Parse(args[1]);
            var r = Discovery.Run(new string[0]);
            var s = r.Sessions.FirstOrDefault(x => x.Loaded);
            if (s == null) { Console.WriteLine("no loaded device"); return 1; }
            using (s)
            {
                var p = s.Profiles[profile - 1];
                Console.WriteLine("{0}: profile {1} rate {2} Hz -> {3} Hz", s.Dev.Name, profile, 1000 / p.RateMs, hz);
                p.RateMs = 1000 / hz;
                s.Selected = profile - 1;
                var res = s.Save();
                Console.WriteLine(res.Error == null ? "saved; backup: " + (res.BackupDir ?? "(none)") : "FAILED: " + res.Error.Message);
                Console.WriteLine("device now: rate byte {0}, active sector {1}", s.Dev.ReadSector(p.Sector)[0], s.Dev.GetActiveSector());
                return res.Error == null ? 0 : 1;
            }
        }
    }
}

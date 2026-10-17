using System;
using System.Diagnostics;
using System.Linq;

namespace OnboardLite
{
    // Console harness: runs the app's discovery twice and reports timing and results.
    static class DebugMain
    {
        static void Main()
        {
            for (int round = 0; round < 2; round++)
            {
                var sw = Stopwatch.StartNew();
                var r = Discovery.Run(new string[0]);
                Console.WriteLine("round {0}: {1} ms, sessions [{2}], failed [{3}], silent {4}", round, sw.ElapsedMilliseconds,
                    string.Join(", ", r.Sessions.Select(s => s.Dev.Name + " loaded=" + s.Loaded + " profiles=" + s.Profiles.Count)),
                    string.Join(", ", r.Failed.Select(kv => kv.Value)), r.Silent.Count);
                foreach (var s in r.Sessions) s.Dispose();
            }
        }
    }
}

using System;
using System.IO;

namespace OnboardLite
{
    // Diagnostic log, written only when the OBL_LOG environment variable names a file.
    static class Log
    {
        static readonly string path = Environment.GetEnvironmentVariable("OBL_LOG");
        static readonly object gate = new object();

        public static void W(string text)
        {
            if (string.IsNullOrEmpty(path)) return;
            lock (gate)
            {
                try { File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff") + " " + text + Environment.NewLine); }
                catch { }
            }
        }
    }
}

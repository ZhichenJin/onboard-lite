using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace OnboardLite
{
    sealed class HidppError : Exception
    {
        public readonly int Code;

        public HidppError(int code) : base(Describe(code)) { Code = code; }

        static string Describe(int code)
        {
            switch (code)
            {
                case 2: return Lang.T("设备拒绝：参数无效");
                case 3: return Lang.T("设备拒绝：超出范围");
                case 4: return Lang.T("设备硬件错误");
                case 6: return Lang.T("设备不支持该功能");
                case 7: return Lang.T("设备不支持该命令");
                case 8: return Lang.T("设备忙，请稍后再试");
                case 9: return Lang.T("设备不支持该操作");
                default: return Lang.T("设备返回错误 ") + code;
            }
        }
    }

    // HID++ 2.0 over the long report collection (report id 0x11, 20 bytes).
    // Requests are serialized; replies are matched by device index, feature index, function and software id.
    sealed class Hidpp : IDisposable
    {
        public const byte Direct = 0xFF;
        const byte LongReport = 0x11;
        const byte SwId = 0x0B;

        readonly HidStream stream;
        readonly BlockingCollection<byte[]> replies = new BlockingCollection<byte[]>();
        readonly object io = new object();
        readonly Dictionary<ushort, byte> features = new Dictionary<ushort, byte>();
        volatile bool probing;
        public byte Index;
        public event Action Lost;

        public Hidpp(HidInfo info, byte index)
        {
            Index = index;
            stream = new HidStream(info);
            stream.Report += OnReport;
            stream.Lost += () => { var lost = Lost; if (lost != null) lost(); };
        }

        public HidInfo Info { get { return stream.Info; } }

        void OnReport(byte[] r)
        {
            if (r.Length < 7 || r[0] != LongReport) return;
            if (!probing && r[1] != Index) return;
            bool reply = (r[3] & 0x0F) == SwId || (r[2] == 0xFF && (r[4] & 0x0F) == SwId);
            if (reply) replies.Add(r);
        }

        static byte[] Message(byte index, byte feature, int function, byte[] args)
        {
            var msg = new byte[20];
            msg[0] = LongReport;
            msg[1] = index;
            msg[2] = feature;
            msg[3] = (byte)((function << 4) | SwId);
            if (args != null) Buffer.BlockCopy(args, 0, msg, 4, Math.Min(16, args.Length));
            return msg;
        }

        void Drain()
        {
            byte[] stale;
            while (replies.TryTake(out stale)) { }
        }

        public byte[] Call(byte feature, int function, params byte[] args)
        {
            return CallWithTimeout(feature, function, 1500, args);
        }

        public byte[] CallWithTimeout(byte feature, int function, int timeoutMs, byte[] args)
        {
            lock (io)
            {
                Drain();
                var msg = Message(Index, feature, function, args);
                stream.Write(msg);
                int deadline = Environment.TickCount + timeoutMs;
                while (true)
                {
                    int left = deadline - Environment.TickCount;
                    byte[] r;
                    if (left <= 0 || !replies.TryTake(out r, left)) throw new TimeoutException(Lang.T("设备没有响应（可能已休眠或断开）"));
                    if (r[1] != Index) continue;
                    if (r[2] == 0xFF && r[3] == feature && r[4] == msg[3]) throw new HidppError(r[5]);
                    if (r[2] == feature && r[3] == msg[3])
                    {
                        var p = new byte[16];
                        Buffer.BlockCopy(r, 4, p, 0, Math.Min(16, r.Length - 4));
                        return p;
                    }
                }
            }
        }

        // Pings several device indices at once and returns those that answer as HID++ 2.0+ devices.
        // A dozing wireless mouse often misses the first ping while its link wakes up, so an empty
        // first round is retried once.
        public List<byte> Probe(byte[] indices, int timeoutMs)
        {
            var found = new List<byte>();
            lock (io)
            {
                probing = true;
                try
                {
                    Drain();
                    for (int round = 0; round < 2 && found.Count == 0; round++)
                    {
                        foreach (var i in indices) stream.Write(Message(i, 0, 1, new byte[] { 0, 0, 0x5A }));
                        int deadline = Environment.TickCount + timeoutMs;
                        while (true)
                        {
                            int left = deadline - Environment.TickCount;
                            byte[] r;
                            if (left <= 0 || !replies.TryTake(out r, left)) break;
                            if (r[2] == 0 && r[3] == ((1 << 4) | SwId) && r[4] >= 2 && !found.Contains(r[1])) found.Add(r[1]);
                        }
                    }
                }
                finally { probing = false; }
            }
            return found;
        }

        // Feature index for a feature id, 0 when the device lacks it.
        public byte Feature(ushort id)
        {
            byte idx;
            lock (features) { if (features.TryGetValue(id, out idx)) return idx; }
            idx = Call(0, 0, (byte)(id >> 8), (byte)id)[0];
            lock (features) features[id] = idx;
            return idx;
        }

        public void Dispose() { stream.Dispose(); }
    }
}

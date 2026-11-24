using System.Runtime.InteropServices;

namespace mk.dev.runner.core.Native
{
    public static class TcpListeners
    {
        private const int AF_INET = 2;
        private const int AF_INET6 = 23;
        private const int TCP_TABLE_OWNER_PID_LISTENER = 3;
        private const uint ERROR_INSUFFICIENT_BUFFER = 122;

        public static List<(int Pid, int Port)> All()
        {
            var list = new List<(int, int)>();
            Read(AF_INET, 24, 8, 20, list);
            Read(AF_INET6, 56, 20, 52, list);
            return list;
        }

        private static void Read(int af, int rowSize, int portOffset, int pidOffset, List<(int, int)> into)
        {
            var size = 0;
            Win32.GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, TCP_TABLE_OWNER_PID_LISTENER, 0);
            if (size <= 0)
                return;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var buf = Marshal.AllocHGlobal(size);
                try
                {
                    var rc = Win32.GetExtendedTcpTable(buf, ref size, false, af, TCP_TABLE_OWNER_PID_LISTENER, 0);
                    if (rc == ERROR_INSUFFICIENT_BUFFER)
                        continue;
                    if (rc != 0)
                        return;
                    var n = Marshal.ReadInt32(buf);
                    for (int i = 0; i < n; i++)
                    {
                        var row = 4 + i * rowSize;
                        var raw = Marshal.ReadInt32(buf, row + portOffset);
                        var port = ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF);
                        var pid = Marshal.ReadInt32(buf, row + pidOffset);
                        into.Add((pid, port));
                    }
                    return;
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }

        public static IEnumerable<int> PidsOnPort(int port)
        {
            return All().Where(x => x.Port == port && x.Pid > 4).Select(x => x.Pid).Distinct();
        }
    }
}

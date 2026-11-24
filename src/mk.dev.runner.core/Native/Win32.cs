using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace mk.dev.runner.core.Native
{
    internal static class Win32
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateJobObjectW(IntPtr attrs, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetInformationJobObject(SafeHandle job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint len);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool QueryInformationJobObject(SafeHandle job, int infoClass, IntPtr info, uint len, out uint returned);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AssignProcessToJobObject(SafeHandle job, SafeHandle process);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool TerminateJobObject(SafeHandle job, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool K32GetProcessMemoryInfo(SafeProcessHandle process, out PROCESS_MEMORY_COUNTERS_EX counters, uint cb);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder name, ref int size);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int af, int tableClass, uint reserved);

        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        public const uint PROCESS_VM_READ = 0x0010;
        public const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_MEMORY_COUNTERS_EX
        {
            public uint cb;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
            public UIntPtr PrivateUsage;
        }
    }

    public sealed class JobObject : IDisposable
    {
        private readonly SafeFileHandle _h;

        public JobObject()
        {
            _h = Win32.CreateJobObjectW(IntPtr.Zero, null);
            if (_h.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var info = new Win32.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = Win32.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            Win32.SetInformationJobObject(_h, 9, ref info, (uint)Marshal.SizeOf<Win32.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>());
        }

        public bool TryAssign(Process p)
        {
            try { return Win32.AssignProcessToJobObject(_h, p.SafeHandle); }
            catch { return false; }
        }

        public void Terminate()
        {
            if (!_h.IsClosed)
                Win32.TerminateJobObject(_h, 1);
        }

        public int[] ProcessIds()
        {
            if (_h.IsClosed)
                return [];
            const int max = 1024;
            int size = 8 + IntPtr.Size * max;
            var buf = Marshal.AllocHGlobal(size);
            try
            {
                if (!Win32.QueryInformationJobObject(_h, 3, buf, (uint)size, out _) && Marshal.GetLastWin32Error() != 234)
                    return [];
                int n = Math.Min(Marshal.ReadInt32(buf, 4), max);
                var ids = new int[n];
                for (int i = 0; i < n; i++) ids[i] = (int)Marshal.ReadIntPtr(buf, 8 + i * IntPtr.Size);
                return ids;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        public TimeSpan TotalCpu()
        {
            if (_h.IsClosed)
                return TimeSpan.Zero;
            const int size = 48;
            var buf = Marshal.AllocHGlobal(size);
            try
            {
                if (!Win32.QueryInformationJobObject(_h, 1, buf, size, out _))
                    return TimeSpan.Zero;
                return TimeSpan.FromTicks(Marshal.ReadInt64(buf, 0) + Marshal.ReadInt64(buf, 8));
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        public void Dispose()
        {
            _h.Dispose();
        }
    }

    public readonly record struct ProcessSample(int Pid, string Name, long WorkingSet, long PrivateBytes);

    public static class ProcessInfo
    {
        public static ProcessSample? Sample(int pid)
        {
            using var h = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION | Win32.PROCESS_VM_READ, false, pid);
            if (h.IsInvalid)
                return null;
            long ws = 0, priv = 0;
            if (Win32.K32GetProcessMemoryInfo(h, out var c, (uint)Marshal.SizeOf<Win32.PROCESS_MEMORY_COUNTERS_EX>()))
            {
                ws = (long)c.WorkingSetSize;
                priv = (long)c.PrivateUsage;
            }
            var sb = new StringBuilder(1024);
            int len = sb.Capacity;
            var name = Win32.QueryFullProcessImageNameW(h, 0, sb, ref len) ? Path.GetFileName(sb.ToString(0, len)) : $"pid {pid}";
            return new ProcessSample(pid, name, ws, priv);
        }
    }

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
            int size = 0;
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
                    int n = Marshal.ReadInt32(buf);
                    for (int i = 0; i < n; i++)
                    {
                        int row = 4 + i * rowSize;
                        int raw = Marshal.ReadInt32(buf, row + portOffset);
                        int port = ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF);
                        int pid = Marshal.ReadInt32(buf, row + pidOffset);
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

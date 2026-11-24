using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace mk.dev.runner.core.Native
{
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
            var len = sb.Capacity;
            var name = Win32.QueryFullProcessImageNameW(h, 0, sb, ref len) ? Path.GetFileName(sb.ToString(0, len)) : $"pid {pid}";
            return new ProcessSample(pid, name, ws, priv);
        }
    }
}

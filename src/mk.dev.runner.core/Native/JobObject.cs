using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace mk.dev.runner.core.Native
{
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
            KillOnClose = Win32.SetInformationJobObject(_h, 9, ref info, (uint)Marshal.SizeOf<Win32.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>());
        }

        public bool KillOnClose { get; }

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
            var size = 8 + IntPtr.Size * max;
            var buf = Marshal.AllocHGlobal(size);
            try
            {
                if (!Win32.QueryInformationJobObject(_h, 3, buf, (uint)size, out _) && Marshal.GetLastWin32Error() != 234)
                    return [];
                var n = Math.Min(Marshal.ReadInt32(buf, 4), max);
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
}

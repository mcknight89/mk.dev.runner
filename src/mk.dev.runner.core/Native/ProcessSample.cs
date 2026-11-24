using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace mk.dev.runner.core.Native
{
    public readonly record struct ProcessSample(int Pid, string Name, long WorkingSet, long PrivateBytes);
}

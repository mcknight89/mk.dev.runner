using mk.helpers;
using System.Diagnostics;
using System.Text.RegularExpressions;
using mk.dev.runner.core.Config;
using mk.dev.runner.core.Logs;
using mk.dev.runner.core.Native;

namespace mk.dev.runner.core
{
    public sealed record ServiceMetrics(long WorkingSet, long PrivateBytes, double CpuPercent,
                                        IReadOnlyList<ProcessSample> Processes, IReadOnlyList<int> Ports)
    {
        public static readonly ServiceMetrics Empty = new(0, 0, 0, [], []);
        public int ProcessCount => Processes.Count;
    }
}

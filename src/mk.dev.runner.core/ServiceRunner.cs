using System.Diagnostics;
using System.Text.RegularExpressions;
using mk.dev.runner.core.Config;
using mk.dev.runner.core.Logs;
using mk.dev.runner.core.Native;

namespace mk.dev.runner.core
{
    public enum ServiceState { Disabled, Stopped, Starting, Running, Building, Restarting, Retrying, Crashed, Failed }

    public enum BuildState { Idle, Building, Ok, Failed }

    public sealed record ServiceMetrics(long WorkingSet, long PrivateBytes, double CpuPercent,
                                        IReadOnlyList<ProcessSample> Processes, IReadOnlyList<int> Ports)
    {
        public static readonly ServiceMetrics Empty = new(0, 0, 0, [], []);
        public int ProcessCount => Processes.Count;
    }

    public sealed record RunnerNotice(string Project, string? Service, string Message, bool IsError);

    internal sealed class ServiceProcess
    {
        public ServiceProcess(Process process, JobObject job, bool inJob)
        {
            P = process;
            Job = job;
            InJob = inJob;
        }

        public Process P { get; }
        public JobObject Job { get; }
        public bool InJob { get; }
        public string? KillReason { get; set; }
        public DateTime StartedAt { get; } = DateTime.Now;
        public TimeSpan LastCpu { get; set; }
        public DateTime LastSampleAt { get; set; } = DateTime.Now;

        public void Kill(string reason, bool wait)
        {
            KillReason ??= reason;
            try
            {
                if (InJob)
                    Job.Terminate();
                else
                    P.Kill(entireProcessTree: true);
            }
            catch { }
            if (wait) try { P.WaitForExit(5000); } catch { }
        }
    }

    public sealed class ServiceRunner
    {
        private const int HistoryLength = 90;

        private readonly object _historyGate = new();
        private readonly Queue<long> _memHistory = new();
        private readonly Queue<double> _cpuHistory = new();

        internal ServiceRunner(int index, ServiceConfig config, int maxLines)
        {
            Index = index;
            Config = config;
            Logs = new LogBuffer(maxLines);
        }

        public int Index { get; }
        public ServiceConfig Config { get; internal set; }
        public string Key => Config.Key;
        public string Name => Config.DisplayName;

        public ServiceState State { get; internal set; } = ServiceState.Stopped;
        public LogBuffer Logs { get; }
        public ServiceMetrics Metrics { get; internal set; } = ServiceMetrics.Empty;

        public int? Pid { get { try { return Proc?.P.Id; } catch { return null; } } }
        public DateTime? StartedAt => Proc?.StartedAt;
        public bool HasProcess => Proc != null;

        internal ServiceProcess? Proc { get; set; }
        internal bool Stopped { get; set; }
        internal int Fails { get; set; }
        internal Timer? RetryTimer { get; set; }
        internal Timer? ReadyTimer { get; set; }
        internal Dictionary<string, string>? StartedEnv { get; set; }
        internal Regex? Ready { get; set; }
        internal Regex? Fail { get; set; }

        internal void CancelTimers()
        {
            RetryTimer?.Dispose(); RetryTimer = null;
            ReadyTimer?.Dispose(); ReadyTimer = null;
        }

        internal void PushHistory(long mem, double cpu)
        {
            lock (_historyGate)
            {
                _memHistory.Enqueue(mem);
                _cpuHistory.Enqueue(cpu);
                while (_memHistory.Count > HistoryLength) _memHistory.Dequeue();
                while (_cpuHistory.Count > HistoryLength) _cpuHistory.Dequeue();
            }
        }

        internal void ClearHistory()
        {
            lock (_historyGate) { _memHistory.Clear(); _cpuHistory.Clear(); }
        }

        public long[] MemoryHistory() { lock (_historyGate) return [.. _memHistory]; }

        public double[] CpuHistory() { lock (_historyGate) return [.. _cpuHistory]; }

        public bool IsBusy => State is ServiceState.Starting or ServiceState.Building or ServiceState.Restarting or ServiceState.Retrying;
    }

    public static class Format
    {
        public static string Bytes(long b)
        {
            return b switch
            {
                <= 0 => "—",
                < 1024L * 1024 => $"{b / 1024.0:0} KB",
                < 1024L * 1024 * 1024 => $"{b / (1024.0 * 1024):0} MB",
                _ => $"{b / (1024.0 * 1024 * 1024):0.0} GB",
            };
        }

        public static string BytesShort(long b)
        {
            return b switch
            {
                <= 0 => "",
                < 1024L * 1024 => $"{b / 1024.0:0}K",
                < 1024L * 1024 * 1024 => $"{b / (1024.0 * 1024):0}M",
                _ => $"{b / (1024.0 * 1024 * 1024):0.0}G",
            };
        }

        public static string Duration(TimeSpan t)
        {
            return t.TotalSeconds < 60 ? $"{t.Seconds}s"
            : t.TotalMinutes < 60 ? $"{(int)t.TotalMinutes}m {t.Seconds:00}s"
            : t.TotalHours < 24 ? $"{(int)t.TotalHours}h {t.Minutes:00}m"
            : $"{(int)t.TotalDays}d {t.Hours}h";
        }
    }
}

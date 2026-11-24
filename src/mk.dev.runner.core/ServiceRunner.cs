using mk.helpers;
using System.Diagnostics;
using System.Text.RegularExpressions;
using mk.dev.runner.core.Config;
using mk.dev.runner.core.Logs;
using mk.dev.runner.core.Native;

namespace mk.dev.runner.core
{
    public sealed class ServiceRunner
    {
        private const int HistoryLength = 90;

        private readonly object _historyGate = new();
        private readonly Queue<long> _memHistory = new();

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

        public bool NoReadyLine { get; internal set; }

        public string? LastProblem { get; internal set; }

        internal void CancelTimers()
        {
            RetryTimer?.Dispose(); RetryTimer = null;
            ReadyTimer?.Dispose(); ReadyTimer = null;
        }

        public int HistoryVersion { get; private set; }

        internal void PushHistory(long mem)
        {
            lock (_historyGate)
            {
                _memHistory.Enqueue(mem);
                while (_memHistory.Count > HistoryLength)
                    _memHistory.Dequeue();
                HistoryVersion++;
            }
        }

        internal void ClearHistory()
        {
            lock (_historyGate)
            {
                _memHistory.Clear();
                HistoryVersion++;
            }
        }

        public long[] MemoryHistory()
        {
            lock (_historyGate)
            {
                return [.. _memHistory];
            }
        }

        public bool IsBusy => State is ServiceState.Starting or ServiceState.Building or ServiceState.Restarting or ServiceState.Retrying;
    }
}

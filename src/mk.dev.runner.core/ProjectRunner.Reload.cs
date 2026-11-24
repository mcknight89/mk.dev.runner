using System.Text.Json;
using mk.dev.runner.core.Config;
using mk.dev.runner.core.Runtime;

namespace mk.dev.runner.core
{
    public sealed partial class ProjectRunner
    {
        private void ReloadWhileRunning()
        {
            var before = _services.ToDictionary(s => s.Key, s => ServiceSnapshot(s.Config), StringComparer.OrdinalIgnoreCase);
            var sessionBefore = SessionSnapshot();
            var active = _services.Where(s => s.HasProcess || s.IsBusy).Select(s => s.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

            Project.Reload();
            BuildLog.MaxLines = Project.Config.Globals.LogBufferLines;

            if (SessionSnapshot() != sessionBefore || _building || _pendingAffected.Count > 0)
            {
                RestartSession(active);
                return;
            }

            var fresh = Project.Config.Services.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);
            var restart = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in _services)
            {
                var kept = fresh.TryGetValue(s.Key, out var c);
                if (kept && ServiceSnapshot(c!) == before[s.Key])
                    continue;
                var running = active.Contains(s.Key);
                if (running && kept)
                {
                    restart.Add(s.Key);
                    s.Logs.Add("\x1b[33m[runner] settings changed — restarting…\x1b[0m");
                }
                s.Stopped = true;
                s.CancelTimers();
                if (s.Proc is { } sp)
                {
                    KillLogged(s, sp, kept ? StopReason.Restart : StopReason.Stop, wait: true);
                    s.Proc = null;
                }
                s.Metrics = ServiceMetrics.Empty;
                s.State = ServiceState.Stopped;
                if (!kept)
                    s.Logs.CloseFile();
            }

            var known = _services.Select(s => s.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            LoadServices();
            var stamp = DateTime.Now.ToString("g");
            foreach (var s in _services.Where(s => !known.Contains(s.Key)))
            {
                s.Logs.OpenFile(LogFileOf(s), $"{s.Name} ({Project.Name}): session {stamp}");
                s.Stopped = true;
            }
            if (IsManaged)
                EnsureDepGraph(force: true);

            StartAgain(_services.Where(s => restart.Contains(s.Key)));
            RestartIfEnvChanged();
        }

        private void RestartSession(HashSet<string> active)
        {
            BuildLog.Add("\x1b[33m[runner] project settings changed — restarting the project…\x1b[0m");
            EndSession();
            BeginSession();
            StartAgain(_services.Where(s => active.Contains(s.Key)));
        }

        private void StartAgain(IEnumerable<ServiceRunner> services)
        {
            var dotnet = new HashSet<int>();
            foreach (var s in services.Where(IsEnabled))
            {
                s.Stopped = false;
                s.Fails = 0;
                if (IsManaged && s.Config.IsDotnet)
                    dotnet.Add(s.Index);
                else
                    StartCore(s);
            }
            if (dotnet.Count > 0)
                QueueBuild(dotnet, false);
        }

        private void RestartIfEnvChanged()
        {
            foreach (var s in _services)
            {
                if (!s.HasProcess || s.StartedEnv == null || SameMap(UserEnv(s), s.StartedEnv))
                    continue;
                s.Logs.Add("\x1b[33m[runner] environment changed — restarting…\x1b[0m");
                RestartCore(s);
            }
        }

        private string ServiceSnapshot(ServiceConfig c)
        {
            return $"{JsonSerializer.Serialize(c)}|{Project.PortOf(c)}|{Project.IsEnabled(c)}";
        }

        private string SessionSnapshot()
        {
            var g = Project.Config.Globals;
            return $"{g.Mode}|{g.Solution}|{g.AutoRebuild}|{Project.RootDir}";
        }
    }
}

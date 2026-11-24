using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using mk.dev.runner.core.Config;
using mk.dev.runner.core.Logs;
using mk.dev.runner.core.Native;
using mk.dev.runner.core.Runtime;

namespace mk.dev.runner.core
{
    public sealed partial class ProjectRunner
    {
        private void SampleMetrics()
        {
            if (!SessionActive)
                return;
            List<(int Pid, int Port)> listeners;
            try { listeners = TcpListeners.All(); } catch { listeners = []; }
            var now = DateTime.Now;
            int cores = Environment.ProcessorCount;

            foreach (var s in _services)
            {
                if (s.Proc is not { } sp)
                {
                    if (s.Metrics != ServiceMetrics.Empty)
                        s.Metrics = ServiceMetrics.Empty;
                    continue;
                }
                int[] pids;
                if (sp.InJob)
                    pids = sp.Job.ProcessIds();
                else { try { pids = [sp.P.Id]; } catch { pids = []; } }

                var samples = new List<ProcessSample>(pids.Length);
                foreach (var pid in pids)
                    // conhost is the hidden console window each service gets, not part of the app
                    if (ProcessInfo.Sample(pid) is { } ps && !ps.Name.Equals("conhost.exe", StringComparison.OrdinalIgnoreCase))
                        samples.Add(ps);

                double cpuPct = 0;
                if (sp.InJob)
                {
                    var cpu = sp.Job.TotalCpu();
                    var wall = (now - sp.LastSampleAt).TotalSeconds;
                    if (wall > 0.2)
                        cpuPct = Math.Clamp((cpu - sp.LastCpu).TotalSeconds / (wall * cores) * 100, 0, 100);
                    sp.LastCpu = cpu;
                    sp.LastSampleAt = now;
                }

                var pidSet = pids.ToHashSet();
                var ports = listeners.Where(l => pidSet.Contains(l.Pid)).Select(l => l.Port).Distinct().Order().ToList();
                var ws = samples.Sum(x => x.WorkingSet);
                s.Metrics = new ServiceMetrics(ws, samples.Sum(x => x.PrivateBytes), cpuPct,
                                               samples.OrderByDescending(x => x.WorkingSet).ToList(), ports);
                s.PushHistory(ws);
            }
            MetricsUpdated?.Invoke();
        }
    }
}

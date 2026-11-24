using System.Text.RegularExpressions;
using mk.dev.runner.core.Config;
using mk.dev.runner.core.Runtime;

namespace mk.dev.runner.core
{
    public sealed partial class ProjectRunner
    {
        private static readonly TimeSpan StartingProbeDelay = TimeSpan.FromSeconds(1);
        private const int FailsBeforeNotResponding = 3;

        private void BeginHealth(ServiceRunner s, ServiceProcess sp, int? port)
        {
            var c = s.Config;
            var h = c.HealthOrDefault;
            var kind = h.Kind;
            s.Health = h;
            s.HealthFails = 0;
            s.PortAnswered = false;
            s.NotResponding = false;

            s.HealthUrl = Project.HealthUrlOf(c);
            if (kind == HealthCheck.Port && port is not > 0)
            {
                s.Logs.Add($"\x1b[33m[runner] the port health check needs a port, so it's off for {s.Name}\x1b[0m");
                kind = HealthCheck.None;
            }
            if (kind == HealthCheck.Http && s.HealthUrl == null)
            {
                s.Logs.Add($"\x1b[33m[runner] the URL health check needs a port or an address, so it's off for {s.Name}\x1b[0m");
                kind = HealthCheck.None;
            }
            s.HealthKind = kind;
            s.Ready = kind switch
            {
                HealthCheck.Log => LogPattern(s, h.Pattern) ?? UsualReadyLine(c),
                HealthCheck.Auto => UsualReadyLine(c),
                _ => null,
            };

            var probes = kind is HealthCheck.Port or HealthCheck.Http || (kind == HealthCheck.Auto && (port is > 0 || !c.IsDotnet));
            if (kind == HealthCheck.None || (kind == HealthCheck.Auto && !probes && s.Ready == null))
            {
                MarkRunning(s);
                return;
            }
            if (probes)
                ScheduleProbe(s, sp, StartingProbeDelay);

            var timeout = TimeSpan.FromSeconds(Math.Max(5, Project.Config.Globals.StartTimeoutSec));
            s.ReadyTimer = _q.After(timeout, () => OnStartTimeout(s, sp, port, timeout));
        }

        private Regex? LogPattern(ServiceRunner s, string? pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return null;
            try
            {
                return new Regex(pattern, RegexOptions.IgnoreCase);
            }
            catch (ArgumentException e)
            {
                s.Logs.Add($"\x1b[91m[runner] the health check's log pattern isn't valid ({e.Message}), using the usual one\x1b[0m");
                return null;
            }
        }

        private static Regex? UsualReadyLine(ServiceConfig c)
        {
            return c.IsNpm ? new Regex(@"ready in|- local:|started server|compiled|localhost:\d+", RegexOptions.IgnoreCase)
                : c.IsDotnet ? new Regex(@"now listening on|application started", RegexOptions.IgnoreCase)
                : null;
        }

        private static Regex? FailLine(ServiceConfig c)
        {
            return c.IsDotnet ? new Regex(@"build failed|exited with error code", RegexOptions.IgnoreCase) : null;
        }

        private static void MarkRunning(ServiceRunner s)
        {
            s.State = ServiceState.Running;
            s.HealthFails = 0;
            if (s.NotResponding)
            {
                s.NotResponding = false;
                s.LastProblem = null;
            }
            s.ReadyTimer?.Dispose();
            s.ReadyTimer = null;
        }

        private static bool IsStarting(ServiceRunner s)
        {
            return s.State is ServiceState.Starting or ServiceState.Restarting or ServiceState.Failed or ServiceState.Crashed;
        }

        private void ScheduleProbe(ServiceRunner s, ServiceProcess sp, TimeSpan delay)
        {
            s.HealthTimer?.Dispose();
            s.HealthTimer = _q.After(delay, () => Probe(s, sp));
        }

        private void Probe(ServiceRunner s, ServiceProcess sp)
        {
            if (s.Proc != sp)
                return;
            var kind = s.HealthKind;
            var port = PortOf(s);
            var url = s.HealthUrl;
            var listening = s.Metrics.Ports;
            var anyPortCounts = !s.Config.IsDotnet;
            Task.Run(async () =>
            {
                var result = await HealthProbe.Check(kind, port, url, listening, anyPortCounts);
                _q.Post(() => OnProbe(s, sp, result));
            });
        }

        private void OnProbe(ServiceRunner s, ServiceProcess sp, ProbeResult r)
        {
            if (s.Proc != sp)
                return;
            if (r.PortAnswered)
                s.PortAnswered = true;
            var every = TimeSpan.FromSeconds(Math.Max(2, s.Health.EverySec));

            if (IsStarting(s))
            {
                if (r.Ok)
                {
                    MarkRunning(s);
                    ScheduleProbe(s, sp, every);
                }
                else
                    ScheduleProbe(s, sp, s.State is ServiceState.Starting or ServiceState.Restarting ? StartingProbeDelay : every);
                return;
            }

            var auto = s.HealthKind == HealthCheck.Auto;
            var judged = !auto || s.PortAnswered || s.NotResponding;
            if (s.State == ServiceState.Running && judged)
            {
                var ok = auto ? r.PortAnswered : r.Ok;
                if (ok)
                {
                    s.HealthFails = 0;
                    if (s.NotResponding)
                    {
                        s.NotResponding = false;
                        s.LastProblem = null;
                        s.Logs.Add($"\x1b[32m[runner] {s.Name} is answering again\x1b[0m");
                    }
                }
                else if (++s.HealthFails >= FailsBeforeNotResponding && !s.NotResponding)
                    FlagNotResponding(s, r.Why ?? "it stopped answering");
            }
            ScheduleProbe(s, sp, every);
        }

        private void OnStartTimeout(ServiceRunner s, ServiceProcess sp, int? port, TimeSpan timeout)
        {
            if (s.Proc != sp || s.State is not (ServiceState.Starting or ServiceState.Restarting))
                return;
            MarkRunning(s);
            var why = s.HealthKind switch
            {
                HealthCheck.Port => $"nothing is answering on :{port}",
                HealthCheck.Http => $"{s.HealthUrl} didn't answer",
                HealthCheck.Log => "the log line it waits for never appeared",
                _ => port is > 0 ? $"nothing is answering on :{port}" : null,
            };
            if (why != null)
                FlagNotResponding(s, $"{why} after {timeout.TotalSeconds:0} s");
        }

        private void FlagNotResponding(ServiceRunner s, string why)
        {
            s.NotResponding = true;
            s.LastProblem = why;
            s.Logs.Add($"\x1b[33m[runner] {s.Name} is running, but {why}. Check its log, or its health check in Edit.\x1b[0m");
            Notice?.Invoke(new RunnerNotice(Project.Name, s.Name, $"not responding: {why}"));
        }
    }
}

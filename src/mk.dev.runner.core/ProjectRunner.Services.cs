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
        private void StartCore(ServiceRunner s)
        {
            if (!IsEnabled(s))
                return;
            s.Stopped = false;
            s.CancelTimers();
            s.State = ServiceState.Starting;
            s.NoReadyLine = false;
            s.LastProblem = null;
            s.Logs.Add($"\x1b[2m[runner] starting {s.Name}…\x1b[0m");

            var port = PortOf(s);
            var holder = PortHolder(port);
            if (holder != null)
            {
                s.State = ServiceState.Crashed;
                s.Logs.Add($"\x1b[91m[runner] port {port} is in use by {holder}. Stop it, then start {s.Name} again.\x1b[0m");
                s.LastProblem = $"port {port} is in use by {holder}";
                Notice?.Invoke(new RunnerNotice(Project.Name, s.Name, s.LastProblem));
                return;
            }

            var dir = Project.ServiceDir(s.Config);
            if (!Directory.Exists(dir))
            {
                s.State = ServiceState.Crashed;
                s.Logs.Add($"\x1b[91m[runner] folder not found: {dir}\x1b[0m");
                s.LastProblem = "folder not found";
                Notice?.Invoke(new RunnerNotice(Project.Name, s.Name, s.LastProblem));
                return;
            }

            string line;
            try
            {
                line = CommandLine(s, port);
            }
            catch (Exception e)
            {
                s.State = ServiceState.Crashed;
                s.Logs.Add($"\x1b[91m[runner] {e.Message}\x1b[0m");
                return;
            }

            var psi = CmdProcess.Info(line, dir);
            psi.Environment["FORCE_COLOR"] = "1";
            psi.Environment["DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION"] = "1";
            if (s.Config.IsDotnet)
            {
                foreach (var (k, v) in DotnetNoServers)
                    psi.Environment[k] = v;
                if (port is > 0 && !IsManaged)
                    psi.Environment["ASPNETCORE_URLS"] = $"http://localhost:{port}";
                psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            }
            else if (port is > 0)
                psi.Environment["PORT"] = port.ToString();

            var userEnv = UserEnv(s);
            foreach (var (k, v) in userEnv)
                psi.Environment[k] = v;

            var (ready, fail, badPattern) = Patterns(s.Config);
            if (badPattern != null)
                s.Logs.Add($"\x1b[91m[runner] the ready pattern isn't valid ({badPattern}), using the usual one\x1b[0m");
            s.Ready = ready;
            s.Fail = fail;

            ServiceProcess sp;
            try
            {
                sp = CmdProcess.Start(psi);
            }
            catch (Exception e)
            {
                s.State = ServiceState.Crashed;
                s.Logs.Add($"\x1b[91m[runner] failed to start: {e.Message}\x1b[0m");
                return;
            }
            var p = sp.P;
            if (!sp.InJob || !sp.Job.KillOnClose)
                s.Logs.Add($"\x1b[33m[runner] couldn't fully attach {s.Name} to Dev Runner, so if Dev Runner crashes it may keep running\x1b[0m");
            s.Proc = sp;
            s.StartedEnv = userEnv;

            p.OutputDataReceived += (_, e) => OnLine(s, sp, e.Data);
            p.ErrorDataReceived += (_, e) => OnLine(s, sp, e.Data);
            p.Exited += (_, _) => OnProcessExited(s, sp);
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            if (p.HasExited)
                OnProcessExited(s, sp);

            var timeout = TimeSpan.FromSeconds(Math.Max(5, Project.Config.Globals.StartTimeoutSec));
            s.ReadyTimer = _q.After(timeout, () =>
            {
                if (s.Proc == sp && s.State is ServiceState.Starting or ServiceState.Restarting)
                {
                    s.State = ServiceState.Running;
                    s.NoReadyLine = true;
                    s.Logs.Add($"\x1b[33m[runner] still running after {timeout.TotalSeconds:0} s but never printed its ready line. Check the log, or set readyPattern.\x1b[0m");
                }
            });
        }

        private void OnLine(ServiceRunner s, ServiceProcess sp, string? line)
        {
            if (line == null)
                return;
            s.Logs.Add(line);
            var plain = Ansi.Strip(line);
            if (s.Fail != null && s.Fail.IsMatch(plain))
            {
                var crashed = plain.Contains("exited with error code", StringComparison.OrdinalIgnoreCase);
                var state = crashed ? ServiceState.Crashed : ServiceState.Failed;
                _q.Post(() =>
                {
                    if (s.Proc != sp || s.State == state)
                        return;
                    s.State = state;
                    var message = crashed ? "crashed — waiting for a file change" : "build failed — waiting for a file change";
                    s.LastProblem = message;
                    Notice?.Invoke(new RunnerNotice(Project.Name, s.Name, s.LastProblem));
                });
            }
            else if (s.State is ServiceState.Starting or ServiceState.Restarting or ServiceState.Failed or ServiceState.Crashed
                     && (s.Ready == null || s.Ready.IsMatch(plain)))
            {
                _q.Post(() =>
                {
                    if (s.Proc != sp || s.State is not (ServiceState.Starting or ServiceState.Restarting or ServiceState.Failed or ServiceState.Crashed))
                        return;
                    s.State = ServiceState.Running;
                    s.NoReadyLine = false;
                    s.ReadyTimer?.Dispose();
                    s.ReadyTimer = null;
                });
            }
        }

        private void OnProcessExited(ServiceRunner s, ServiceProcess sp)
        {
            Task.Run(() =>
            {
                var code = -1;
                try
                {
                    if (sp.InJob)
                        sp.Job.Terminate();
                    sp.P.WaitForExit(2000);
                    code = sp.P.ExitCode;
                }
                catch { }
                _q.Post(() => HandleExit(s, sp, code));
            });
        }

        private void HandleExit(ServiceRunner s, ServiceProcess sp, int code)
        {
            if (sp.ExitHandled)
                return;
            sp.ExitHandled = true;
            try
            {
                sp.Job.Dispose();
            }
            catch { }
            try
            {
                sp.P.Dispose();
            }
            catch { }

            var current = s.Proc == sp;
            if (current)
            {
                s.Proc = null;
                s.ReadyTimer?.Dispose();
                s.ReadyTimer = null;
                s.Metrics = ServiceMetrics.Empty;
            }

            if (sp.KillReason == StopReason.Restart)
                return;

            if (sp.KillReason == StopReason.Stop || s.Stopped)
            {
                if (s.Proc == null && s.State is not (ServiceState.Building or ServiceState.Retrying or ServiceState.Starting))
                    s.State = IsEnabled(s) ? ServiceState.Stopped : ServiceState.Disabled;
                if (s.Proc == null && SessionActive)
                    s.Logs.Add("\x1b[2m[runner] stopped.\x1b[0m");
                return;
            }
            if (!current)
                return;

            var g = Project.Config.Globals;
            if (g.AutoRetry)
            {
                var ran = DateTime.Now - sp.StartedAt;
                s.Fails = ran.TotalMilliseconds < 8000 ? s.Fails + 1 : 0;
                var max = Math.Max(1, g.MaxRetries);
                if (s.Fails > max)
                {
                    s.State = ServiceState.Crashed;
                    s.Logs.Add($"\x1b[91m[runner] crashed {s.Fails}× — stopped. Fix it, then restart.\x1b[0m");
                    s.LastProblem = $"crashed {s.Fails} times — stopped";
                    Notice?.Invoke(new RunnerNotice(Project.Name, s.Name, s.LastProblem));
                }
                else
                {
                    s.State = ServiceState.Retrying;
                    var baseDelay = Math.Max(0.5, g.RetryDelaySec);
                    var delay = s.Fails == 0 ? baseDelay : Math.Min(baseDelay * Math.Pow(2, s.Fails - 1), 30);
                    s.Logs.Add($"\x1b[90m[runner] exited ({code}) — retry {Math.Max(1, s.Fails)}/{max} in {delay:0.#}s…\x1b[0m");
                    s.RetryTimer = _q.After(TimeSpan.FromSeconds(delay), () =>
                    {
                        if (!s.Stopped && s.State == ServiceState.Retrying && SessionActive)
                            StartCore(s);
                    });
                }
            }
            else
            {
                s.State = ServiceState.Crashed;
                s.Logs.Add($"\x1b[91m[runner] exited ({code}) — stopped. Read the error above, then restart.\x1b[0m");
                s.LastProblem = $"exited with code {code}";
                Notice?.Invoke(new RunnerNotice(Project.Name, s.Name, s.LastProblem));
            }
        }

        private void StopCore(ServiceRunner s)
        {
            s.Stopped = true;
            s.CancelTimers();
            s.State = IsEnabled(s) ? ServiceState.Stopped : ServiceState.Disabled;
            if (s.Proc is { } sp)
            {
                KillLogged(s, sp, StopReason.Stop, wait: false);
                s.Proc = null;
            }
            s.Metrics = ServiceMetrics.Empty;
        }

        private void RestartCore(ServiceRunner s)
        {
            s.Fails = 0;
            s.Stopped = false;
            if (IsManaged && s.Config.IsDotnet)
            {
                s.Logs.Add("\x1b[2m[runner] rebuild + restart…\x1b[0m");
                QueueBuild([s.Index], false);
                return;
            }
            s.Logs.Add("\x1b[2m[runner] restarting…\x1b[0m");
            s.CancelTimers();
            if (s.Proc is { } sp)
            {
                KillLogged(s, sp, StopReason.Restart, wait: true);
                s.Proc = null;
            }
            StartCore(s);
        }

        private void ToggleCore(ServiceRunner s)
        {
            var enable = !IsEnabled(s);
            try
            {
                Project.SetEnabled(s.Config, enable);
            }
            catch (InvalidOperationException e)
            {
                s.Logs.Add($"\x1b[91m[runner] {e.Message}\x1b[0m");
                return;
            }
            if (enable)
            {
                s.Fails = 0;
                if (!SessionActive)
                {
                    s.State = ServiceState.Stopped;
                    return;
                }
                s.Stopped = false;
                if (IsManaged && s.Config.IsDotnet)
                {
                    QueueBuild([s.Index], false);
                }
                else
                    StartCore(s);
            }
            else
            {
                StopCore(s);
                s.State = ServiceState.Disabled;
                s.Logs.Add("\x1b[90m[runner] disabled.\x1b[0m");
            }
        }

        private Dictionary<string, string> UserEnv(ServiceRunner s)
        {
            var fresh = Project.Service(s.Key);
            if (fresh != null)
                s.Config = fresh;
            return Project.EffectiveEnv(s.Config);
        }

        private static bool SameMap(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            return a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);
        }

        private string CommandLine(ServiceRunner s, int? port)
        {
            var c = s.Config;
            var g = Project.Config.Globals;
            var args = new List<string>();
            string exe;
            if (c.IsNpm)
            {
                exe = "npm";
                args.AddRange(["run", string.IsNullOrWhiteSpace(c.Script) ? "dev" : c.Script!]);
                if (c.Args is { Count: > 0 })
                {
                    args.Add("--");
                    args.AddRange(c.Args);
                }
            }
            else if (c.IsDotnet)
            {
                exe = "dotnet";
                var mode = g.Mode;
                args.AddRange(mode switch
                {
                    DotnetMode.Managed => ["run", "--no-build", "--no-restore"],
                    DotnetMode.Run => ["run"],
                    _ => ["watch", "--no-hot-reload"],
                });
                var extra = new List<string>();
                if (mode == DotnetMode.Managed && port is > 0)
                    extra.AddRange(["--urls", $"http://localhost:{port}"]);
                if (c.Args is { Count: > 0 })
                    extra.AddRange(c.Args);
                if (extra.Count > 0)
                {
                    args.Add("--");
                    args.AddRange(extra);
                }
            }
            else
            {
                exe = string.IsNullOrWhiteSpace(c.Cmd) ? throw new InvalidOperationException($"service '{c.Key}' has no \"cmd\".") : c.Cmd!;
                if (c.Args != null)
                    args.AddRange(c.Args);
            }
            return CommandArgs.Join(new[] { exe }.Concat(args));
        }

        private static (Regex? ready, Regex? fail, string? badPattern) Patterns(ServiceConfig c)
        {
            Regex? ready = null;
            string? badPattern = null;
            if (!string.IsNullOrWhiteSpace(c.ReadyPattern))
            {
                try
                {
                    ready = new Regex(c.ReadyPattern!, RegexOptions.IgnoreCase);
                }
                catch (ArgumentException e)
                {
                    badPattern = e.Message;
                }
            }
            ready ??= c.IsNpm ? new Regex(@"ready in|- local:|started server|compiled|localhost:\d+", RegexOptions.IgnoreCase)
                    : c.IsDotnet ? new Regex(@"now listening on|application started", RegexOptions.IgnoreCase)
                    : null;
            var fail = c.IsDotnet ? new Regex(@"build failed|exited with error code", RegexOptions.IgnoreCase) : null;
            return (ready, fail, badPattern);
        }
    }
}

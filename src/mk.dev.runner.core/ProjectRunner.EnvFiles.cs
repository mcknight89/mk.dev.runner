namespace mk.dev.runner.core
{
    public sealed partial class ProjectRunner
    {
        private void StartEnvWatchers()
        {
            var files = Project.Config.Globals.RestartOnChange ?? [".env"];
            foreach (var f in files.Where(f => !string.IsNullOrWhiteSpace(f)))
            {
                var full = Path.GetFullPath(Path.Combine(Project.RootDir, f));
                var dir = Path.GetDirectoryName(full);
                if (dir == null || !Directory.Exists(dir))
                    continue;
                try
                {
                    var w = new FileSystemWatcher(dir, Path.GetFileName(full))
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    };
                    FileSystemEventHandler h = (_, _) => _q.Post(OnEnvFileChanged);
                    w.Changed += h;
                    w.Created += h;
                    w.Renamed += (_, _) => _q.Post(OnEnvFileChanged);
                    w.EnableRaisingEvents = true;
                    _envWatchers.Add(w);
                }
                catch (Exception e)
                {
                    BuildLog.Add($"\x1b[91m[runner] couldn't watch {f} for changes: {e.Message}\x1b[0m");
                }
            }
        }

        private void StopEnvWatchers()
        {
            _envDebounce?.Dispose();
            _envDebounce = null;
            _envChangedAt = null;
            foreach (var w in _envWatchers)
                w.Dispose();
            _envWatchers.Clear();
        }

        private void OnEnvFileChanged()
        {
            _envChangedAt ??= DateTime.Now;
            _envDebounce?.Dispose();
            _envDebounce = _q.After(TimeSpan.FromMilliseconds(300), () =>
            {
                var changedAt = _envChangedAt ?? DateTime.Now;
                _envChangedAt = null;
                if (!SessionActive)
                    return;
                var managed = new HashSet<int>();
                foreach (var s in _services.Where(s => s.Config.IsDotnet && IsEnabled(s) && !s.Stopped))
                {
                    if (s.IsBusy || s.StartedAt > changedAt.AddSeconds(-1))
                        continue;
                    s.Logs.Add("\x1b[33m[runner] .env changed — restarting…\x1b[0m");
                    if (IsManaged)
                        managed.Add(s.Index);
                    else
                        RestartCore(s);
                }
                if (managed.Count > 0)
                    QueueBuild(managed, false);
            });
        }
    }
}

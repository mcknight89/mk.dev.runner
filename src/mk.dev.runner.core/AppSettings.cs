using System.Text.Json;
using mk.dev.runner.core.Config;

namespace mk.dev.runner.core
{
    public sealed class AppSettings
    {
        public List<ProjectEntry> Projects { get; set; } = [];

        public bool StartWithWindows { get; set; }
        public bool StartMinimized { get; set; } = true;
        public bool CloseToTray { get; set; } = true;
        public bool Notifications { get; set; } = true;

        public bool TrayHintShown { get; set; }

        public WindowLayout? Window { get; set; }

        public string? UpdateChannel { get; set; }

        public string? LastVersion { get; set; }

        public bool CheckForUpdates { get; set; } = true;

        public static string Dir => AppPaths.SettingsDir;
        public static string FilePath => Path.Combine(Dir, "settings.json");

        [System.Text.Json.Serialization.JsonIgnore]
        public string? LoadProblem { get; private set; }

        private bool _unreadable;

        public static AppSettings Load(bool moveBroken = true)
        {
            if (!File.Exists(FilePath))
                return new();
            string text;
            try
            {
                text = ReadWithRetry();
            }
            catch (Exception e) { return new() { LoadProblem = $"Couldn't read {FilePath}: {e.Message}", _unreadable = true }; }
            try
            {
                var s = JsonSerializer.Deserialize<AppSettings>(text, Json.Options) ?? new();
                s.Projects.RemoveAll(p => p.ConfigFile.Length == 0);
                return s;
            }
            catch (Exception e)
            {
                if (!moveBroken)
                    return new()
                    {
                        LoadProblem = $"settings.json couldn't be read ({e.Message})",
                        _unreadable = true
                    };
                var aside = Path.Combine(Dir, $"settings.broken-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                try
                {
                    File.Move(FilePath, aside);
                }
                catch { aside = FilePath + " (left in place)"; }
                return new()
                {
                    LoadProblem = $"settings.json couldn't be read ({e.Message}). It was kept as {aside}; the project list starts empty."
                };
            }
        }

        private static string ReadWithRetry()
        {
            for (var i = 0; ; i++)
            {
                try
                {
                    return File.ReadAllText(FilePath);
                }
                catch (IOException) when (i < 4) { Thread.Sleep(200); }
            }
        }

        public void Save()
        {
            if (_unreadable)
                throw new InvalidOperationException("settings.json couldn't be read when Dev Runner started, so it wasn't overwritten. Restart Dev Runner to load it.");
            Json.WriteText(FilePath, JsonSerializer.Serialize(this, Json.Options));
        }

        public List<LoadedProject> LoadProjects()
        {
            return Projects.Select(LoadOne).ToList();
        }

        public static LoadedProject LoadOne(ProjectEntry e)
        {
            try
            {
                return new LoadedProject(e, Project.Load(e.ConfigFile, e.Name), null);
            }
            catch (Exception ex) { return new LoadedProject(e, null, ex.Message); }
        }

        public ProjectEntry? Find(string nameOrPath)
        {
            var full = SafeFull(nameOrPath);
            foreach (var e in Projects)
            {
                if (full != null && (Same(e.ConfigFile, full) || e.Folder != null && Same(e.Folder, full)))
                    return e;
                if (string.Equals(e.DisplayName, nameOrPath, StringComparison.OrdinalIgnoreCase))
                    return e;
            }
            return null;
        }

        public Project Add(string path, string? name = null)
        {
            var entry = EntryFor(path);
            if (name != null)
                entry.Name = name;
            var project = Project.Load(entry.ConfigFile, entry.Name);
            if (Projects.FirstOrDefault(e => Same(e.ConfigFile, project.ConfigPath)) is { } already)
                return Project.Load(already.ConfigFile, already.Name);
            entry.Name ??= project.Name;
            if (Projects.FirstOrDefault(e => string.Equals(e.DisplayName, entry.Name, StringComparison.OrdinalIgnoreCase)) is { } clash)
                throw new InvalidOperationException($"A project called '{entry.Name}' is already added ({clash.ConfigFile}). Add it under another name.");
            Projects.Add(entry);
            return Project.Load(entry.ConfigFile, entry.Name);
        }

        public static ProjectEntry EntryFor(string path)
        {
            var full = Path.GetFullPath(path);
            if (Directory.Exists(full))
            {
                var found = Project.FindConfigs(full);
                if (found.Count == 0)
                    throw new ConfigNotFoundException(full, []);
                if (found.Count > 1)
                    throw new ConfigNotFoundException(full, found);
                return EntryFor(found[0]);
            }
            if (!File.Exists(full))
                throw new FileNotFoundException($"Not found: {full}", full);
            return Project.IsLegacyFile(full)
                ? new ProjectEntry { Config = full }
                : new ProjectEntry { Folder = Path.GetDirectoryName(full), Config = Path.GetFileName(full) == Project.FolderConfigName ? null : Path.GetFileName(full) };
        }

        public (List<string> added, List<string> errors) ImportFolder(string dir)
        {
            var added = new List<string>();
            var errors = new List<string>();
            var dirs = new List<string> { dir };
            try
            {
                dirs.AddRange(Directory.GetDirectories(dir)
                    .Where(d => !Path.GetFileName(d).StartsWith('.') && Path.GetFileName(d) is not ("node_modules" or "bin" or "obj")));
            }
            catch (Exception e)
            {
                errors.Add($"{dir}: couldn't list its folders ({e.Message})");
            }
            var files = dirs.SelectMany(d =>
            {
                var found = Project.FindConfigs(d);
                if (found.Count > 1 && d != dir)
                {
                    errors.Add($"{d}: more than one config ({string.Join(", ", found.Select(Path.GetFileName))}), add the one you want");
                    return [];
                }
                return found;
            });
            foreach (var f in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var before = Projects.Count;
                    var p = Add(f);
                    if (Projects.Count > before)
                        added.Add(p.Name);
                }
                catch (Exception ex) { errors.Add($"{f}: {ex.Message}"); }
            }
            return (added, errors);
        }

        private static bool Same(string a, string b)
        {
            return string.Equals(SafeFull(a)?.TrimEnd('\\'), SafeFull(b)?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        private static string? SafeFull(string p)
        {
            try
            {
                return Path.GetFullPath(p);
            }
            catch { return null; }
        }
    }
}

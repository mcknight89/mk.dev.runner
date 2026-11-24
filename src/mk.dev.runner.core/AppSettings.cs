using System.Text.Json;
using mk.dev.runner.core.Config;

namespace mk.dev.runner.core;

/// <summary>
/// What the runner itself saves (AppPaths.SettingsDir\settings.json): the list of projects, each a
/// display name and a folder (whose .dev.json says what to run), and the desktop app's options.
/// Everything about a project's stack lives in its own folder.
/// </summary>
public sealed class AppSettings
{
    public List<ProjectEntry> Projects { get; set; } = [];

    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool Notifications { get; set; } = true;

    /// <summary>The "still running in the tray" tip has been shown once (closing with nothing running).</summary>
    public bool TrayHintShown { get; set; }

    /// <summary>Updates: "stable" (normal releases) or "dev" (pre-releases too). Null until you choose: then a Dev build follows Dev releases.</summary>
    public string? UpdateChannel { get; set; }

    /// <summary>Check GitHub for a newer version in the background (it's only shown; nothing installs by itself).</summary>
    public bool CheckForUpdates { get; set; } = true;

    public static string Dir => AppPaths.SettingsDir;
    public static string FilePath => Path.Combine(Dir, "settings.json");

    /// <summary>
    /// Set when settings.json couldn't be read: it was moved aside (named here) rather than lost,
    /// and the app starts with an empty list. Say so to the user.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore] public string? LoadProblem { get; private set; }

    public static AppSettings Load()
    {
        if (!File.Exists(FilePath)) return new();
        string text;
        try { text = File.ReadAllText(FilePath); }
        catch (Exception e) { return new() { LoadProblem = $"Couldn't read {FilePath}: {e.Message}" }; }
        try
        {
            var s = JsonSerializer.Deserialize<AppSettings>(text, Json.Options) ?? new();
            s.Projects.RemoveAll(p => p.ConfigFile.Length == 0);
            return s;
        }
        catch (Exception e)
        {
            // Never save an empty list over a file we couldn't read: keep it for the user to fix.
            var aside = Path.Combine(Dir, $"settings.broken-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            try { File.Move(FilePath, aside); }
            catch { aside = FilePath + " (left in place)"; }
            return new() { LoadProblem = $"settings.json couldn't be read ({e.Message}). It was kept as {aside}; the project list starts empty." };
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json.Options));
        File.Move(tmp, FilePath, overwrite: true);
    }

    /// <summary>Loads every registered project. Broken ones come back with an error instead of a project.</summary>
    public List<LoadedProject> LoadProjects() => Projects.Select(LoadOne).ToList();

    public static LoadedProject LoadOne(ProjectEntry e)
    {
        try { return new LoadedProject(e, Project.Load(e.ConfigFile, e.Name), null); }
        catch (Exception ex) { return new LoadedProject(e, null, ex.Message); }
    }

    /// <summary>A project by its display name, its folder, or its config file.</summary>
    public ProjectEntry? Find(string nameOrPath)
    {
        var full = SafeFull(nameOrPath);
        foreach (var e in Projects)
        {
            if (full != null && (Same(e.ConfigFile, full) || e.Folder != null && Same(e.Folder, full))) return e;
            if (string.Equals(e.DisplayName, nameOrPath, StringComparison.OrdinalIgnoreCase)) return e;
        }
        return null;
    }

    /// <summary>
    /// Adds a project: a folder (with a .dev.json in it) or a config file (.dev.json, or an
    /// old dev.&lt;name&gt;.config.json / dev.mjs.config.json). Returns the loaded project.
    /// </summary>
    public Project Add(string path, string? name = null)
    {
        var entry = EntryFor(path);
        if (name != null) entry.Name = name;
        var project = Project.Load(entry.ConfigFile, entry.Name);
        if (Projects.FirstOrDefault(e => Same(e.ConfigFile, project.ConfigPath)) is { } already)
            return Project.Load(already.ConfigFile, already.Name);
        entry.Name ??= project.Name;
        if (Projects.FirstOrDefault(e => string.Equals(e.DisplayName, entry.Name, StringComparison.OrdinalIgnoreCase)) is { } clash)
            throw new InvalidOperationException($"A project called '{entry.Name}' is already added ({clash.ConfigFile}). Add it under another name.");
        Projects.Add(entry);
        return Project.Load(entry.ConfigFile, entry.Name);
    }

    /// <summary>The entry for a folder or config file (not added yet).</summary>
    public static ProjectEntry EntryFor(string path)
    {
        var full = Path.GetFullPath(path);
        if (Directory.Exists(full))
        {
            var found = Project.FindConfigs(full);
            if (found.Count == 0) throw new ConfigNotFoundException(full, []);
            if (found.Count > 1) throw new ConfigNotFoundException(full, found);
            return EntryFor(found[0]);
        }
        if (!File.Exists(full)) throw new FileNotFoundException($"Not found: {full}", full);
        return Project.IsLegacyFile(full)
            ? new ProjectEntry { Config = full }                       // the old layout: a config file anywhere
            : new ProjectEntry { Folder = Path.GetDirectoryName(full), Config = Path.GetFileName(full) == Project.FolderConfigName ? null : Path.GetFileName(full) };
    }

    /// <summary>
    /// Adds every project in a folder and its immediate subfolders: .dev.json files, and old
    /// dev.&lt;name&gt;.config.json / dev.mjs.config.json files. Returns the names added and any errors.
    /// </summary>
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
        catch { }
        var files = dirs.SelectMany(d =>
        {
            var found = Project.FindConfigs(d);
            // a folder with more than one config is left for you to pick from (devrunner add <file>)
            if (found.Count > 1 && d != dir) { errors.Add($"{d}: more than one config ({string.Join(", ", found.Select(Path.GetFileName))}), add the one you want"); return []; }
            return found;
        });
        foreach (var f in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var before = Projects.Count;
                var p = Add(f);
                if (Projects.Count > before) added.Add(p.Name);
            }
            catch (Exception ex) { errors.Add($"{f}: {ex.Message}"); }
        }
        return (added, errors);
    }

    static bool Same(string a, string b) => string.Equals(SafeFull(a)?.TrimEnd('\\'), SafeFull(b)?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    static string? SafeFull(string p)
    {
        try { return Path.GetFullPath(p); } catch { return null; }
    }
}

/// <summary>
/// One project in the list: its display name and its folder, whose .dev.json (or the file named in
/// Config) says what to run. An old entry has only Config, a full path to a dev.&lt;name&gt;.config.json or
/// dev.mjs.config.json; "Move config into folder" turns it into a folder entry.
/// </summary>
public sealed class ProjectEntry
{
    /// <summary>What the app calls it. Defaults to the folder's name.</summary>
    public string? Name { get; set; }

    /// <summary>The project's folder.</summary>
    public string? Folder { get; set; }

    /// <summary>The config file: a name in the folder (default .dev.json), or for an old entry a full path.</summary>
    public string? Config { get; set; }

    /// <summary>Desktop app: start this project's services when the app starts.</summary>
    public bool AutoStart { get; set; }

    /// <summary>The config file's full path.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ConfigFile => Folder != null ? Path.Combine(Folder, string.IsNullOrWhiteSpace(Config) ? Project.FolderConfigName : Config!) : Config ?? "";

    /// <summary>True for an old entry (a config file outside the project's folder layout).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsLegacy => Folder == null;

    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name! : Folder != null ? Path.GetFileName(Folder.TrimEnd('\\', '/')) : Project.NameFromFile(Config ?? "");
}

public sealed record LoadedProject(ProjectEntry Entry, Project? Project, string? Error);

/// <summary>A folder with no config the runner recognises (Candidates empty), or more than one (pick one).</summary>
public sealed class ConfigNotFoundException(string folder, List<string> candidates) : Exception(
    candidates.Count == 0
        ? $"No config file in {folder}. Create one ({Project.FolderConfigName}), or pick the file if it has another name."
        : $"More than one config in {folder}: {string.Join(", ", candidates.Select(Path.GetFileName))}. Pick the one to use.")
{
    public string Folder { get; } = folder;
    public List<string> Candidates { get; } = candidates;
}

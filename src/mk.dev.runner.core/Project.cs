using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using mk.dev.runner.core.Config;

namespace mk.dev.runner.core
{
    public enum EnvScope
    {
        Shared,
        Local,
    }

    public sealed partial class Project
    {
        public const string FolderConfigName = ".dev.json";

        public const string LocalDirName = ".devrunner";

        public string ConfigPath { get; }
        public string LocalPath { get; }
        public string ConfigDir { get; }
        public string Name { get; private set; } = "";
        public string RootDir { get; private set; } = "";

        public bool IsLegacy { get; }

        public string LogsDir => Path.Combine(AppPaths.LogsRoot, $"{AppPaths.Safe(Slug)}-{AppPaths.Key(ConfigPath)}");

        private string Slug => IsLegacy ? NameFromFile(ConfigPath) : Path.GetFileName(ConfigDir);

        public string LockKey => AppPaths.Key(ConfigPath);

        private string? _displayName;

        public bool IsPerFolderLayout { get; }

        public const string PerFolderConfigName = "dev.mjs.config.json";

        private static bool IsPerFolderFile(string path)
        {
            return Path.GetFileName(path).Equals(PerFolderConfigName, StringComparison.OrdinalIgnoreCase);
        }

        public ProjectConfig Config { get; private set; } = new();

        private JsonObject _cfg = new();
        private JsonObject _local = new();

        [GeneratedRegex(@"^dev\.(.+)\.config\.json$", RegexOptions.IgnoreCase)]
        private static partial Regex DevConfigName();

        private Project(string configPath, string? displayName)
        {
            ConfigPath = Path.GetFullPath(configPath);
            ConfigDir = Path.GetDirectoryName(ConfigPath)!;
            _displayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName;
            var file = Path.GetFileName(ConfigPath);
            IsPerFolderLayout = IsPerFolderFile(ConfigPath);
            IsLegacy = IsLegacyFile(ConfigPath);
            var m = DevConfigName().Match(file);
            LocalPath = !IsLegacy ? Path.Combine(ConfigDir, LocalDirName, "local.json")
                : IsPerFolderLayout ? Path.Combine(ConfigDir, "dev.mjs.env")
                : Path.Combine(ConfigDir, $"dev.{m.Groups[1].Value}.env");
        }

        public static Project Load(string configPath, string? displayName = null)
        {
            var p = new Project(configPath, displayName);
            p.Reload();
            return p;
        }

        public void SetDisplayName(string name)
        {
            _displayName = name;
            Name = name;
        }

        public static string? FolderConfigIn(string folder)
        {
            return FindConfigs(folder) is [var only] ? only : null;
        }

        public static List<string> FindConfigs(string folder)
        {
            try
            {
                return Directory.GetFiles(folder, "*.json")
                    .Where(IsConfigFile)
                    .OrderBy(f => Path.GetFileName(f).Equals(FolderConfigName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch { return []; }
        }

        public static bool IsLegacyFile(string path)
        {
            return IsPerFolderFile(path) || DevConfigName().IsMatch(Path.GetFileName(path));
        }

        public static string NameFromFile(string configPath)
        {
            if (IsPerFolderFile(configPath))
                return Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(configPath))!);
            var file = Path.GetFileName(configPath);
            var m = DevConfigName().Match(file);
            return m.Success ? m.Groups[1].Value : Path.GetFileNameWithoutExtension(file);
        }

        public static bool IsConfigFile(string path)
        {
            return IsLegacyFile(path) || Path.GetFileName(path).Equals(FolderConfigName, StringComparison.OrdinalIgnoreCase);
        }

        public void Reload()
        {
            _cfg = Json.ReadObject(ConfigPath, missingIsEmpty: false);
            try { _local = Json.ReadObject(LocalPath, missingIsEmpty: true); }
            catch { _local = new JsonObject(); }

            var merged = (JsonObject)_cfg.DeepClone();
            if (_local["globals"] is JsonObject lg)
                merged["globals"] = merged["globals"] is JsonObject cg ? Json.DeepMerge(cg, lg) : lg.DeepClone();

            Config = merged.Deserialize<ProjectConfig>(Json.Options) ?? new ProjectConfig();
            Config.Globals ??= new GlobalsConfig();
            Config.Services ??= [];
            foreach (var s in Config.Services)
            {
                if (string.IsNullOrWhiteSpace(s.Key))
                    s.Key = s.Name ?? $"svc{Config.Services.IndexOf(s) + 1}";
                s.Dir = string.IsNullOrWhiteSpace(s.Dir) ? "." : s.Dir;
            }

            Name = _displayName ?? (!string.IsNullOrWhiteSpace(Config.Name) ? Config.Name! : IsLegacy ? NameFromFile(ConfigPath) : Path.GetFileName(ConfigDir));
            RootDir = string.IsNullOrWhiteSpace(Config.Root) ? ConfigDir : Path.GetFullPath(Path.Combine(ConfigDir, Config.Root!));
        }

        public ServiceConfig? Service(string key)
        {
            return Config.Services.FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        }

        public string ServiceDir(ServiceConfig svc)
        {
            return Path.GetFullPath(Path.Combine(RootDir, svc.Dir));
        }

        public string? SolutionPath =>
            string.IsNullOrWhiteSpace(Config.Globals.Solution) ? null : Path.GetFullPath(Path.Combine(RootDir, Config.Globals.Solution!));

        public bool IsManaged => Config.Globals.DotnetMode.Equals("managed", StringComparison.OrdinalIgnoreCase);


        private JsonObject? LocalService(string key)
        {
            return (_local["services"] as JsonObject)?[key] as JsonObject;
        }

        public bool IsEnabled(ServiceConfig svc)
        {
            if (LocalService(svc.Key)?["enabled"] is JsonValue v && v.TryGetValue<bool>(out var b))
                return b;
            return svc.Enabled ?? true;
        }

        public int? PortOf(ServiceConfig svc)
        {
            if (LocalService(svc.Key)?["port"] is JsonValue v && v.TryGetValue<int>(out var p))
                return p;
            return svc.DefaultPort;
        }

        public string? UrlOf(ServiceConfig svc)
        {
            var port = PortOf(svc);
            if (!string.IsNullOrWhiteSpace(svc.Url))
                return svc.Url!.Replace("{port}", port?.ToString() ?? "");
            return port is > 0 ? $"http://localhost:{port}" : null;
        }

        public void SetEnabled(ServiceConfig svc, bool enabled)
        {
            var o = Json.Obj(Json.Obj(_local, "services"), svc.Key);
            if (enabled == (svc.Enabled ?? true))
                o.Remove("enabled");
            else o["enabled"] = enabled;
            SaveLocal();
        }

        public void SetPort(ServiceConfig svc, int? port)
        {
            var o = Json.Obj(Json.Obj(_local, "services"), svc.Key);
            if (port == null || port == svc.DefaultPort)
                o.Remove("port");
            else o["port"] = port;
            SaveLocal();
        }


        public Dictionary<string, string> GetEnv(string? serviceKey, EnvScope scope)
        {
            if (scope == EnvScope.Shared)
            {
                if (serviceKey == null)
                    return Json.ToStringMap(_cfg["env"]);
                return Json.ToStringMap(ConfigServiceNode(serviceKey)?["env"]);
            }
            if (serviceKey == null)
                return Json.ToStringMap(_local["env"]);
            return Json.ToStringMap(LocalService(serviceKey)?["env"]);
        }

        public void SetEnv(string? serviceKey, EnvScope scope, IEnumerable<KeyValuePair<string, string>> values)
        {
            var map = Json.FromStringMap(values);
            if (scope == EnvScope.Shared)
            {
                var target = serviceKey == null ? _cfg : ConfigServiceNode(serviceKey)
                             ?? throw new KeyNotFoundException($"No service '{serviceKey}' in {Path.GetFileName(ConfigPath)}.");
                if (map.Count == 0)
                    target.Remove("env");
                else target["env"] = map;
                Json.WriteObject(ConfigPath, _cfg);
                Reload();
                return;
            }
            var t = serviceKey == null ? _local : Json.Obj(Json.Obj(_local, "services"), serviceKey);
            if (map.Count == 0)
                t.Remove("env");
            else t["env"] = map;
            SaveLocal();
        }

        public Dictionary<string, string> EffectiveEnv(ServiceConfig svc)
        {
            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            void Add(Dictionary<string, string> m) { foreach (var (k, v) in m) env[k] = v; }
            Add(GetEnv(null, EnvScope.Shared));
            Add(GetEnv(null, EnvScope.Local));
            Add(GetEnv(svc.Key, EnvScope.Shared));
            Add(GetEnv(svc.Key, EnvScope.Local));
            return env;
        }

        private JsonObject? ConfigServiceNode(string key)
        {
            return (_cfg["services"] as JsonArray)?.OfType<JsonObject>()
                .FirstOrDefault(o => (o["key"]?.GetValue<string>() ?? "").Equals(key, StringComparison.OrdinalIgnoreCase));
        }

        private void SaveLocal()
        {
            if (_local["services"] is JsonObject svcs)
            {
                foreach (var k in svcs.Where(kv => kv.Value is JsonObject o && o.Count == 0).Select(kv => kv.Key).ToList())
                    svcs.Remove(k);
                if (svcs.Count == 0)
                    _local.Remove("services");
            }
            foreach (var k in new[] { "globals", "env" })
                if (_local[k] is JsonObject o && o.Count == 0)
                    _local.Remove(k);

            Json.WriteObject(LocalPath, _local);
            if (!IsLegacy)
                KeepOutOfGit(Path.GetDirectoryName(LocalPath)!);
            Reload();
        }

        private static void KeepOutOfGit(string dir)
        {
            try
            {
                var gi = Path.Combine(dir, ".gitignore");
                if (!File.Exists(gi))
                    File.WriteAllText(gi, "# Dev Runner: this machine's choices and secrets. Never committed.\n*\n");
            }
            catch { }
        }

        public static string CreateNew(string dir)
        {
            var path = Path.Combine(Path.GetFullPath(dir), FolderConfigName);
            if (File.Exists(path))
                throw new IOException($"{path} already exists.");
            var cfg = new JsonObject
            {
                ["globals"] = new JsonObject { ["dotnetMode"] = "watch" },
                ["services"] = new JsonArray(),
            };
            Json.WriteObject(path, cfg);
            return path;
        }
    }
}

using System.Text;
using System.Text.Json.Nodes;
using mk.dev.runner.core.Config;

namespace mk.dev.runner.core
{
    public sealed class ProjectDefinition
    {
        public string DotnetMode { get; set; } = "watch";
        public string? Solution { get; set; }
        public bool AutoRetry { get; set; }
        public int MaxRetries { get; set; } = 5;
        public bool AutoRebuild { get; set; } = true;
        public List<ServiceDefinition> Services { get; set; } = [];
    }

    public sealed class ServiceDefinition
    {
        public string? OriginalKey { get; set; }
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string Dir { get; set; } = ".";
        public string Type { get; set; } = "dotnet";
        public string? Color { get; set; }
        public int? Port { get; set; }
        public bool Enabled { get; set; } = true;
        public string? ReadyPattern { get; set; }
        public string? Cmd { get; set; }
        public string? Args { get; set; }
        public string? Script { get; set; }
        public string? Url { get; set; }
    }

    public sealed partial class Project
    {
        public static readonly string[] ServiceTypes = ["dotnet", "npm", "command"];
        public static readonly string[] DotnetModes = ["watch", "run", "managed"];
        public static readonly string[] Colors = ["cyan", "green", "magenta", "yellow", "blue", "red", "white", "orange", "teal", "purple", "pink", "lime"];

        public ProjectDefinition ReadDefinition()
        {
            lock (_gate)
            {
                var raw = Json.ReadObject(ConfigPath, missingIsEmpty: false);
                var g = raw["globals"] as JsonObject ?? new JsonObject();
                var def = new ProjectDefinition
                {
                    DotnetMode = (Json.String(g["dotnetMode"]) ?? "watch").Trim().ToLowerInvariant(),
                    Solution = Json.String(g["solution"]),
                    AutoRetry = Json.Bool(g["autoRetry"]) ?? false,
                    MaxRetries = Json.Int(g["maxRetries"]) ?? 5,
                    AutoRebuild = Json.Bool(g["autoRebuild"]) ?? true,
                };
                foreach (var s in (raw["services"] as JsonArray)?.OfType<JsonObject>() ?? [])
                {
                    var key = Json.String(s["key"]) ?? "";
                    def.Services.Add(new ServiceDefinition
                    {
                        OriginalKey = key,
                        Key = key,
                        Name = Json.String(s["name"]) ?? key,
                        Dir = Json.String(s["dir"]) ?? ".",
                        Type = (Json.String(s["type"]) ?? "command").ToLowerInvariant(),
                        Color = Json.String(s["color"]),
                        Port = Json.Int(s["defaultPort"]),
                        Enabled = Json.Bool(s["enabled"]) ?? true,
                        ReadyPattern = Json.String(s["readyPattern"]),
                        Cmd = Json.String(s["cmd"]),
                        Args = (s["args"] as JsonArray) is { } a ? JoinArgs(a.Select(Json.String).OfType<string>()) : null,
                        Script = Json.String(s["script"]),
                        Url = Json.String(s["url"]),
                    });
                }
                return def;
            }
        }

        public static List<string> Validate(ProjectDefinition def)
        {
            var problems = new List<string>();
            if (def.DotnetMode == "managed" && string.IsNullOrWhiteSpace(def.Solution))
                problems.Add("Managed builds need the solution file.");
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in def.Services)
            {
                var label = string.IsNullOrWhiteSpace(s.Name) ? s.Key : s.Name;
                if (string.IsNullOrWhiteSpace(s.Key))
                    problems.Add($"A service has no key.");
                else if (!keys.Add(s.Key.Trim()))
                    problems.Add($"Two services use the key '{s.Key}'.");
                if (string.IsNullOrWhiteSpace(s.Dir))
                    problems.Add($"{label}: no folder.");
                if (s.Type == "command" && string.IsNullOrWhiteSpace(s.Cmd))
                    problems.Add($"{label}: no command to run.");
                if (s.Port is < 1 or > 65535)
                    problems.Add($"{label}: the port must be 1–65535.");
            }
            return problems;
        }

        public void SaveDefinition(ProjectDefinition def)
        {
            lock (_gate)
            {
                var problems = Validate(def);
                if (problems.Count > 0)
                    throw new InvalidOperationException(string.Join("\n", problems));

                var raw = Json.ReadObject(ConfigPath, missingIsEmpty: false);
                var g = Json.Obj(raw, "globals");
                g["dotnetMode"] = def.DotnetMode;
                Set(g, "solution", def.Solution);
                g["autoRetry"] = def.AutoRetry;
                if (def.AutoRetry || g["maxRetries"] != null)
                    g["maxRetries"] = def.MaxRetries;
                if (!def.AutoRebuild || g["autoRebuild"] != null)
                    g["autoRebuild"] = def.AutoRebuild;

                var old = (raw["services"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
                var services = new JsonArray();
                foreach (var s in def.Services)
                {
                    var node = s.OriginalKey == null ? null
                        : old.FirstOrDefault(o => string.Equals(Json.String(o["key"]), s.OriginalKey, StringComparison.OrdinalIgnoreCase));
                    var o = node != null ? (JsonObject)node.DeepClone() : new JsonObject();
                    o["key"] = s.Key.Trim();
                    o["name"] = string.IsNullOrWhiteSpace(s.Name) ? s.Key.Trim() : s.Name.Trim();
                    o["dir"] = s.Dir.Trim();
                    o["type"] = s.Type;
                    Set(o, "color", s.Color);
                    if (s.Port is { } port)
                        o["defaultPort"] = port;
                    else o.Remove("defaultPort");
                    o["enabled"] = s.Enabled;
                    Set(o, "readyPattern", s.ReadyPattern);
                    Set(o, "url", s.Url);
                    Set(o, "cmd", s.Type == "command" ? s.Cmd : null);
                    Set(o, "script", s.Type == "npm" ? s.Script : null);
                    var args = SplitArgs(s.Args);
                    if (args.Count > 0)
                        o["args"] = new JsonArray(args.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray());
                    else
                        o.Remove("args");
                    services.Add(o);

                    if (s.OriginalKey != null && !s.OriginalKey.Equals(s.Key.Trim(), StringComparison.OrdinalIgnoreCase))
                        RenameLocalService(s.OriginalKey, s.Key.Trim());
                }
                raw["services"] = services;
                Json.WriteObject(ConfigPath, raw);
                Reload();
            }
        }

        private void RenameLocalService(string from, string to)
        {
            if (_local["services"] is not JsonObject svcs || svcs[from] is not JsonObject mine)
                return;
            svcs.Remove(from);
            svcs[to] = mine;
            SaveLocal();
        }

        private static void Set(JsonObject o, string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                o.Remove(key);
            else o[key] = value.Trim();
        }




        public static List<string> SplitArgs(string? line)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(line))
                return list;
            var cur = new StringBuilder();
            var quoted = false;
            var any = false;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '\\' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cur.Append('"');
                    i++;
                    continue;
                }
                if (c == '"')
                {
                    quoted = !quoted;
                    any = true;
                    continue;
                }
                if (char.IsWhiteSpace(c) && !quoted)
                {
                    if (cur.Length > 0 || any)
                    {
                        list.Add(cur.ToString());
                        cur.Clear();
                        any = false;
                    }
                    continue;
                }
                cur.Append(c);
            }
            if (cur.Length > 0 || any)
                list.Add(cur.ToString());
            return list;
        }

        public static string JoinArgs(IEnumerable<string> args)
        {
            return string.Join(' ', args.Select(QuoteArg));
        }

        private static string QuoteArg(string a)
        {
            var escaped = a.Replace("\"", "\\\"");
            return a.Length == 0 || a.Any(char.IsWhiteSpace) || a.Contains('"') ? $"\"{escaped}\"" : escaped;
        }
    }
}
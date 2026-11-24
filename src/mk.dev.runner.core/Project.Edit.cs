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
            var raw = Json.ReadObject(ConfigPath, missingIsEmpty: false);
            var g = raw["globals"] as JsonObject ?? new JsonObject();
            var def = new ProjectDefinition
            {
                DotnetMode = Str(g["dotnetMode"]) ?? "watch",
                Solution = Str(g["solution"]),
                AutoRetry = Bool(g["autoRetry"]) ?? false,
                MaxRetries = Int(g["maxRetries"]) ?? 5,
                AutoRebuild = Bool(g["autoRebuild"]) ?? true,
            };
            foreach (var s in (raw["services"] as JsonArray)?.OfType<JsonObject>() ?? [])
            {
                var key = Str(s["key"]) ?? "";
                def.Services.Add(new ServiceDefinition
                {
                    OriginalKey = key,
                    Key = key,
                    Name = Str(s["name"]) ?? key,
                    Dir = Str(s["dir"]) ?? ".",
                    Type = (Str(s["type"]) ?? "command").ToLowerInvariant(),
                    Color = Str(s["color"]),
                    Port = Int(s["defaultPort"]),
                    Enabled = Bool(s["enabled"]) ?? true,
                    ReadyPattern = Str(s["readyPattern"]),
                    Cmd = Str(s["cmd"]),
                    Args = (s["args"] as JsonArray) is { } a ? JoinArgs(a.Select(Str).OfType<string>()) : null,
                    Script = Str(s["script"]),
                    Url = Str(s["url"]),
                });
            }
            return def;
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
                    : old.FirstOrDefault(o => string.Equals(Str(o["key"]), s.OriginalKey, StringComparison.OrdinalIgnoreCase));
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

        private static string? Str(JsonNode? n)
        {
            return n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        }

        private static bool? Bool(JsonNode? n)
        {
            return n is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;
        }

        private static int? Int(JsonNode? n)
        {
            return n is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;
        }

        public static List<string> SplitArgs(string? line)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(line))
                return list;
            var cur = new StringBuilder();
            bool quoted = false, any = false;
            foreach (var c in line)
            {
                if (c == '"') { quoted = !quoted; any = true; continue; }
                if (char.IsWhiteSpace(c) && !quoted)
                {
                    if (cur.Length > 0 || any) { list.Add(cur.ToString()); cur.Clear(); any = false; }
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
            return string.Join(' ', args.Select(a => a.Length == 0 || a.Any(char.IsWhiteSpace) ? $"\"{a}\"" : a));
        }
    }
}

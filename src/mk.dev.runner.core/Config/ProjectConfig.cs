using System.Text.Json;
using System.Text.Json.Serialization;

namespace mk.dev.runner.core.Config
{
    public sealed class ProjectConfig
    {
        public string? Name { get; set; }

        public string? Root { get; set; }

        public GlobalsConfig Globals { get; set; } = new();
        public List<ServiceConfig> Services { get; set; } = [];

        public Dictionary<string, string>? Env { get; set; }
    }

    public sealed class GlobalsConfig
    {
        public string DotnetMode { get; set; } = "watch";

        public string? Solution { get; set; }

        public bool AutoRetry { get; set; }
        public int MaxRetries { get; set; } = 5;
        public double RetryDelaySec { get; set; } = 2;
        public int LogBufferLines { get; set; } = 2000;

        public int StartTimeoutSec { get; set; } = 90;
        public bool AutoRebuild { get; set; } = true;
        public double RebuildDebounceSec { get; set; } = 2;

        public List<string>? RestartOnChange { get; set; }

    }

    public sealed class ServiceConfig
    {
        public string Key { get; set; } = "";
        public string? Name { get; set; }
        public string Dir { get; set; } = ".";

        public string Type { get; set; } = "command";

        public string? Color { get; set; }
        public int? DefaultPort { get; set; }
        public bool? Enabled { get; set; }
        public string? ReadyPattern { get; set; }

        public string? Cmd { get; set; }
        public List<string>? Args { get; set; }

        public string? Script { get; set; }

        public Dictionary<string, string>? Env { get; set; }

        public string? Url { get; set; }

        [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Key : Name!;

        [JsonIgnore] public bool IsDotnet => Type.Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        [JsonIgnore] public bool IsNpm => Type.Equals("npm", StringComparison.OrdinalIgnoreCase);
    }
}

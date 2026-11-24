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

        [JsonIgnore] public DotnetMode Mode => Enum.TryParse<DotnetMode>(DotnetMode, ignoreCase: true, out var mode) ? mode : Config.DotnetMode.Watch;

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

        public HealthConfig? Health { get; set; }

        [JsonIgnore]
        public HealthConfig HealthOrDefault =>
            Health ?? (string.IsNullOrWhiteSpace(ReadyPattern) ? new HealthConfig() : new HealthConfig { Type = "log", Pattern = ReadyPattern });

        [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Key : Name!;

        [JsonIgnore] public ServiceType Kind => Enum.TryParse<ServiceType>(Type, ignoreCase: true, out var kind) ? kind : ServiceType.Command;
        [JsonIgnore] public bool IsDotnet => Kind == ServiceType.Dotnet;
        [JsonIgnore] public bool IsNpm => Kind == ServiceType.Npm;
    }
}

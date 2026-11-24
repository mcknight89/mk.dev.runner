using System.Text.Json.Serialization;

namespace mk.dev.runner.core.Config
{
    public enum HealthCheck
    {
        Auto, Port, Http, Log, None
    }

    public sealed class HealthConfig
    {
        public string? Type { get; set; }
        public string? Path { get; set; }
        public string? Pattern { get; set; }
        public double EverySec { get; set; } = 10;

        [JsonIgnore] public HealthCheck Kind => Enum.TryParse<HealthCheck>(Type, ignoreCase: true, out var kind) ? kind : HealthCheck.Auto;
    }
}

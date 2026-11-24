using System.Text.Json;
using System.Text.Json.Serialization;

namespace mk.dev.runner.core.Config;

/// <summary>
/// A project definition: the same shape as dev.mjs's dev.&lt;name&gt;.config.json, plus optional extras
/// (name, root, env, per-service cmd/args/script/env/url).
/// </summary>
public sealed class ProjectConfig
{
    /// <summary>Display name. Defaults to the &lt;name&gt; part of dev.&lt;name&gt;.config.json.</summary>
    public string? Name { get; set; }

    /// <summary>Folder that service dirs are relative to, relative to the config file. Defaults to the config's folder.</summary>
    public string? Root { get; set; }

    public GlobalsConfig Globals { get; set; } = new();
    public List<ServiceConfig> Services { get; set; } = [];

    /// <summary>Environment variables passed to every service.</summary>
    public Dictionary<string, string>? Env { get; set; }
}

public sealed class GlobalsConfig
{
    /// <summary>watch (dotnet watch), run (dotnet run) or managed (build the solution, then dotnet run --no-build).</summary>
    public string DotnetMode { get; set; } = "watch";

    /// <summary>Solution to build in managed mode, relative to the root.</summary>
    public string? Solution { get; set; }

    public bool AutoRetry { get; set; }
    public int MaxRetries { get; set; } = 5;
    public double RetryDelaySec { get; set; } = 2;
    public int LogBufferLines { get; set; } = 2000;

    public int StartTimeoutSec { get; set; } = 90;
    public bool AutoRebuild { get; set; } = true;
    public double RebuildDebounceSec { get; set; } = 2;

    /// <summary>Files (relative to the root) that restart the dotnet services when they change. Defaults to [".env"].</summary>
    public List<string>? RestartOnChange { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class ServiceConfig
{
    public string Key { get; set; } = "";
    public string? Name { get; set; }
    public string Dir { get; set; } = ".";

    /// <summary>npm, dotnet or command.</summary>
    public string Type { get; set; } = "command";

    public string? Color { get; set; }
    public int? DefaultPort { get; set; }
    public bool? Enabled { get; set; }
    public string? ReadyPattern { get; set; }

    /// <summary>For type "command": the program to run (through cmd.exe, so .cmd/.bat work).</summary>
    public string? Cmd { get; set; }
    public List<string>? Args { get; set; }

    /// <summary>For type "npm": the package.json script to run. Defaults to "dev".</summary>
    public string? Script { get; set; }

    /// <summary>Environment variables for this service only.</summary>
    public Dictionary<string, string>? Env { get; set; }

    /// <summary>Address to open in the browser. {port} is replaced with the service's port. Defaults to http://localhost:{port}.</summary>
    public string? Url { get; set; }

    [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Key : Name!;

    [JsonIgnore] public bool IsDotnet => Type.Equals("dotnet", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public bool IsNpm => Type.Equals("npm", StringComparison.OrdinalIgnoreCase);
}

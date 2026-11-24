using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace mk.dev.runner.core;

/// <summary>
/// Where the runner keeps its own things, all outside the projects: the settings (the project list),
/// the logs and the run locks.
/// </summary>
public static class AppPaths
{
    /// <summary>The folder name under AppData. One place to change when the app is renamed.</summary>
    public const string FolderName = "mk.dev.runner";

    /// <summary>The project list and the app's options (%APPDATA%).</summary>
    public static string SettingsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName);

    /// <summary>Every project's logs (%TEMP%: temporary, overwritten each run, never in a repo).</summary>
    public static string LogsRoot => Path.Combine(Path.GetTempPath(), FolderName, "logs");

    /// <summary>One lock file per running project (%TEMP%).</summary>
    public static string LocksDir => Path.Combine(Path.GetTempPath(), FolderName);

    /// <summary>A short, stable key for a path (the same path, however it is written, gives the same key).</summary>
    public static string Key(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd('\\', '/').ToLowerInvariant();
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(full)))[..8].ToLowerInvariant();
    }

    /// <summary>A name made safe for a file or folder name: "DataToolkit API" → "datatoolkit-api".</summary>
    public static string Safe(string name)
    {
        var s = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return s.Length == 0 ? "x" : s;
    }
}

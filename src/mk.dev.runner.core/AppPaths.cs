using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace mk.dev.runner.core
{
    public static class AppPaths
    {
        public const string FolderName = "mk.dev.runner";

        public static string SettingsName { get; set; } = FolderName;

        public static string SettingsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), SettingsName);

        public static string LogsRoot => Path.Combine(Path.GetTempPath(), FolderName, "logs");

        public static string LocksDir => Path.Combine(Path.GetTempPath(), FolderName);

        public static string Key(string path)
        {
            var full = Path.GetFullPath(path).TrimEnd('\\', '/').ToLowerInvariant();
            return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(full)))[..8].ToLowerInvariant();
        }

        public static string Safe(string name)
        {
            var s = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            return s.Length == 0 ? "x" : s;
        }
    }
}

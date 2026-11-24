using System.Security.Cryptography;
using System.Text;
using mk.helpers;

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
            var slug = name.ToSlug();
            return slug.Length == 0 ? "x" : slug;
        }
    }
}

using mk.dev.runner.desktop.Updates;

namespace mk.dev.runner.desktop
{
    public static class BugReport
    {
        public static string Url(string version)
        {
            var about = $"{version} on Windows {Environment.OSVersion.Version}";
            return $"{Updater.RepoUrl}/issues/new?template=bug_report.yml&version={Uri.EscapeDataString(about)}";
        }

        public static void Open(string version)
        {
            Shell.Open(Url(version));
        }
    }
}

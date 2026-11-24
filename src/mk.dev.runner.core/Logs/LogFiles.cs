using System.Globalization;

namespace mk.dev.runner.core.Logs
{
    public static class LogFiles
    {
        public static string Format(LogLine line)
        {
            return line.Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + Ansi.Strip(line.Text);
        }
    }
}

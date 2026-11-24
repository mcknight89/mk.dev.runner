using System.Globalization;

namespace mk.dev.runner.core.Logs;

/// <summary>
/// Log files are temporary: one per service (and _build) under %TEMP%\mk.dev.runner\logs\&lt;project&gt;\,
/// overwritten on every run. Each line is "HH:mm:ss.fff text" with the colour codes stripped.
/// </summary>
public static class LogFiles
{
    public static string Format(LogLine line) =>
        line.Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + Ansi.Strip(line.Text);
}

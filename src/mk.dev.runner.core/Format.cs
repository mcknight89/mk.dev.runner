using mk.helpers;

namespace mk.dev.runner.core
{
    public static class Format
    {
        public static string Bytes(long b)
        {
            return b <= 0 ? "—" : b.BytesToString();
        }

        public static string Duration(TimeSpan t)
        {
            return t.TotalSeconds < 60 ? $"{t.Seconds}s"
            : t.TotalMinutes < 60 ? $"{(int)t.TotalMinutes}m {t.Seconds:00}s"
            : t.TotalHours < 24 ? $"{(int)t.TotalHours}h {t.Minutes:00}m"
            : $"{(int)t.TotalDays}d {t.Hours}h";
        }
    }
}

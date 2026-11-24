using System.Windows.Media;

namespace mk.dev.runner.desktop
{
    public static class Palette
    {
        public const string Good = "#4ade80";
        public const string Bad = "#f87171";
        public const string Info = "#38bdf8";
        public const string Building = "#c084fc";
        public const string Warn = "#facc15";
        public const string Retry = "#fb923c";
        public const string Faint = "#71717a";
        public const string Off = "#52525b";

        public static readonly Dictionary<string, string> ServiceColors = new(StringComparer.OrdinalIgnoreCase)
        {
            ["cyan"] = "#38bdf8",
            ["green"] = "#4ade80",
            ["magenta"] = "#e879f9",
            ["yellow"] = "#facc15",
            ["blue"] = "#60a5fa",
            ["red"] = "#f87171",
            ["white"] = "#e2e8f0",
            ["orange"] = "#fb923c",
            ["teal"] = "#2dd4bf",
            ["purple"] = "#a78bfa",
            ["pink"] = "#f472b6",
            ["lime"] = "#a3e635",
        };

        private static readonly Dictionary<string, SolidColorBrush> _brushes = new(StringComparer.OrdinalIgnoreCase);

        public static string ServiceHex(string? colorName)
        {
            return colorName != null && ServiceColors.TryGetValue(colorName, out var hex) ? hex : "#a1a1aa";
        }

        public static SolidColorBrush Brush(string hex)
        {
            if (_brushes.TryGetValue(hex, out var brush))
                return brush;

            brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            _brushes[hex] = brush;
            return brush;
        }

        public static SolidColorBrush Brush(int rgb)
        {
            return Brush($"#{rgb:x6}");
        }
    }
}

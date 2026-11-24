using System.Text;
using System.Text.RegularExpressions;

namespace mk.dev.runner.core.Logs
{
    public static partial class Ansi
    {
        [GeneratedRegex(@"\x1b\[[0-9;]*m")]
        private static partial Regex Sgr();

        public static string Strip(string s)
        {
            return s.IndexOf('\x1b') < 0 ? s : Sgr().Replace(s, "");
        }

        public static string Sanitize(string s)
        {
            var clean = true;
            foreach (var ch in s)
                if (ch < 0x20 && ch != '\x1b' || ch == '\x7f') { clean = false; break; }
            if (clean && s.IndexOf('\x1b') < 0)
                return s;

            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\x1b' && i + 1 < s.Length)
                {
                    char n = s[i + 1];
                    if (n == '[')
                    {
                        var j = i + 2;
                        while (j < s.Length && s[j] is >= '0' and <= '?') j++;
                        while (j < s.Length && s[j] is >= ' ' and <= '/') j++;
                        if (j < s.Length)
                        {
                            if (s[j] == 'm')
                                sb.Append(s, i, j - i + 1);
                            i = j;
                        }
                        else
                            i = s.Length;
                        continue;
                    }
                    if (n == ']')
                    {
                        var j = i + 2;
                        while (j < s.Length && s[j] != '\x07' && !(s[j] == '\x1b' && j + 1 < s.Length && s[j + 1] == '\\')) j++;
                        i = j < s.Length && s[j] == '\x1b' ? j + 1 : j;
                        continue;
                    }
                    i++;
                    continue;
                }
                if (c == '\t') { sb.Append("    "); continue; }
                if (c < 0x20 || c == '\x7f')
                    continue;
                sb.Append(c);
            }
            return sb.ToString();
        }

        public readonly record struct Segment(string Text, int? Fg, int? Bg, bool Bold, bool Dim);

        private static readonly int[] Basic =
        [
            0x1e1e1e, 0xf87171, 0x4ade80, 0xfacc15, 0x60a5fa, 0xe879f9, 0x2dd4bf, 0xd4d4d4,
            0x737373, 0xfca5a5, 0x86efac, 0xfde047, 0x93c5fd, 0xf0abfc, 0x5eead4, 0xffffff,
        ];

        public static List<Segment> Parse(string s)
        {
            var list = new List<Segment>();
            int? fg = null, bg = null; bool bold = false, dim = false;
            var start = 0;
            var m = Sgr().Match(s);
            while (true)
            {
                var end = m.Success ? m.Index : s.Length;
                if (end > start)
                    list.Add(new Segment(s[start..end], fg, bg, bold, dim));
                if (!m.Success)
                    break;
                var codes = m.Value[2..^1].Split(';');
                if (codes.Length == 1 && codes[0] == "")
                    codes = ["0"];
                for (int k = 0; k < codes.Length; k++)
                {
                    if (!int.TryParse(codes[k], out var c))
                        continue;
                    switch (c)
                    {
                        case 0: fg = bg = null; bold = dim = false; break;
                        case 1: bold = true; break;
                        case 2: dim = true; break;
                        case 22: bold = dim = false; break;
                        case 39: fg = null; break;
                        case 49: bg = null; break;
                        case >= 30 and <= 37: fg = Basic[c - 30 + (bold ? 8 : 0)]; break;
                        case >= 90 and <= 97: fg = Basic[c - 90 + 8]; break;
                        case >= 40 and <= 47: bg = Basic[c - 40]; break;
                        case >= 100 and <= 107: bg = Basic[c - 100 + 8]; break;
                        case 38 or 48:
                            int? col = null;
                            if (k + 1 < codes.Length && codes[k + 1] == "2" && k + 4 < codes.Length)
                            {
                                col = (P(codes[k + 2]) << 16) | (P(codes[k + 3]) << 8) | P(codes[k + 4]);
                                k += 4;
                            }
                            else if (k + 1 < codes.Length && codes[k + 1] == "5" && k + 2 < codes.Length)
                            {
                                col = Xterm256(P(codes[k + 2]));
                                k += 2;
                            }
                            if (c == 38)
                                fg = col;
                            else bg = col;
                            break;
                    }
                }
                start = m.Index + m.Length;
                m = m.NextMatch();
            }
            return list;

            static int P(string x)
            {
                return int.TryParse(x, out var v) ? Math.Clamp(v, 0, 255) : 0;
            }
        }

        private static int Xterm256(int n)
        {
            if (n < 16)
                return Basic[n];
            if (n >= 232) { int g = 8 + (n - 232) * 10; return (g << 16) | (g << 8) | g; }
            n -= 16;
            int r = n / 36, gg = n / 6 % 6, b = n % 6;
            static int L(int v)
            {
                return v == 0 ? 0 : 55 + v * 40;
            }

            return (L(r) << 16) | (L(gg) << 8) | L(b);
        }
    }
}

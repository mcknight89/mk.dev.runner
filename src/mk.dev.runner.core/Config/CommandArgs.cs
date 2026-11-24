using System.Text;

namespace mk.dev.runner.core.Config
{
    public static class CommandArgs
    {
        public static List<string> Split(string? line)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(line))
                return list;
            var cur = new StringBuilder();
            var quoted = false;
            var any = false;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '\\' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cur.Append('"');
                    i++;
                    continue;
                }
                if (c == '"')
                {
                    quoted = !quoted;
                    any = true;
                    continue;
                }
                if (char.IsWhiteSpace(c) && !quoted)
                {
                    if (cur.Length > 0 || any)
                    {
                        list.Add(cur.ToString());
                        cur.Clear();
                        any = false;
                    }
                    continue;
                }
                cur.Append(c);
            }
            if (cur.Length > 0 || any)
                list.Add(cur.ToString());
            return list;
        }

        public static string Join(IEnumerable<string> args)
        {
            return string.Join(' ', args.Select(Quote));
        }

        public static string Quote(string a)
        {
            if (a.Length == 0)
                return "\"\"";
            if (a.IndexOfAny([' ', '\t', '"', '&', '|', '<', '>', '^', '(', ')']) < 0)
                return a;
            return "\"" + a.Replace("\"", "\\\"") + "\"";
        }
    }
}

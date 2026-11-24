using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace mk.dev.runner.core.Config
{
    internal static class Json
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };
        private static readonly JsonDocumentOptions DocOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        public static JsonObject ReadObject(string path, bool missingIsEmpty)
        {
            if (!File.Exists(path))
            {
                if (missingIsEmpty)
                    return new JsonObject(NodeOptions);
                throw new FileNotFoundException($"File not found: {path}", path);
            }
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
                return new JsonObject(NodeOptions);
            return JsonNode.Parse(text, NodeOptions, DocOptions) as JsonObject
                   ?? throw new InvalidDataException($"{Path.GetFileName(path)} is not a JSON object.");
        }

        public static void WriteObject(string path, JsonObject obj)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, obj.ToJsonString(Options) + Environment.NewLine);
            File.Move(tmp, path, overwrite: true);
        }

        public static JsonObject Obj(JsonObject parent, string key)
        {
            if (parent[key] is JsonObject o)
                return o;
            var created = new JsonObject(NodeOptions);
            parent[key] = created;
            return created;
        }

        public static JsonObject DeepMerge(JsonObject baseObj, JsonObject over)
        {
            var result = (JsonObject)baseObj.DeepClone();
            foreach (var (k, v) in over)
            {
                if (v is JsonObject vo && result[k] is JsonObject ro)
                    result[k] = DeepMerge(ro, vo);
                else
                    result[k] = v?.DeepClone();
            }
            return result;
        }

        public static Dictionary<string, string> ToStringMap(JsonNode? node)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (node is not JsonObject o)
                return map;
            foreach (var (k, v) in o)
                map[k] = v switch
                {
                    null => "",
                    JsonValue jv when jv.TryGetValue<string>(out var s) => s,
                    _ => v.ToJsonString(),
                };
            return map;
        }

        public static string? String(JsonNode? n)
        {
            return n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        }

        public static bool? Bool(JsonNode? n)
        {
            return n is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;
        }

        public static int? Int(JsonNode? n)
        {
            return n is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;
        }

        public static JsonObject FromStringMap(IEnumerable<KeyValuePair<string, string>> map)
        {
            var o = new JsonObject(NodeOptions);
            foreach (var (k, v) in map) o[k] = v;
            return o;
        }
    }
}

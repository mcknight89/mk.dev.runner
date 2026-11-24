using System.Text.Json;
using mk.dev.runner.core.Config;

namespace mk.dev.runner.core
{
    public sealed class ProjectEntry
    {
        public string? Name { get; set; }

        public string? Folder { get; set; }

        public string? Config { get; set; }

        public bool AutoStart { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public string ConfigFile => Folder != null ? Path.Combine(Folder, string.IsNullOrWhiteSpace(Config) ? Project.FolderConfigName : Config!) : Config ?? "";

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsLegacy => Folder == null;

        [System.Text.Json.Serialization.JsonIgnore]
        public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name! : Folder != null ? Path.GetFileName(Folder.TrimEnd('\\', '/')) : Project.NameFromFile(Config ?? "");
    }
}

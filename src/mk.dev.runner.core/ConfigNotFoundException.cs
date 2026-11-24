namespace mk.dev.runner.core
{
    public sealed class ConfigNotFoundException : Exception
    {
        public ConfigNotFoundException(string folder, List<string> candidates) : base(candidates.Count == 0
            ? $"No config file in {folder}. Create one ({Project.FolderConfigName}), or pick the file if it has another name."
            : $"More than one config in {folder}: {string.Join(", ", candidates.Select(Path.GetFileName))}. Pick the one to use.")
        {
            Folder = folder;
            Candidates = candidates;
        }

        public string Folder { get; }
        public List<string> Candidates { get; }
    }
}

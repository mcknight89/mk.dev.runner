namespace mk.dev.runner.tests
{
    public sealed class TempProject : IDisposable
    {
        public TempProject(string config)
        {
            Dir = Path.Combine(Path.GetTempPath(), "devrunner-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Dir);
            File.WriteAllText(ConfigPath, config);
        }

        public string Dir { get; }
        public string ConfigPath => Path.Combine(Dir, ".dev.json");
        public string LocalPath => Path.Combine(Dir, ".devrunner", "local.json");

        public void Write(string relative, string text)
        {
            var path = Path.Combine(Dir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        public void Dispose()
        {
            try { Directory.Delete(Dir, true); }
            catch (IOException) { }
        }
    }
}

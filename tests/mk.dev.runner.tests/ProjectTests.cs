using mk.dev.runner.core;
using Xunit;

namespace mk.dev.runner.tests
{
    public class ProjectTests
    {
        private const string Config = """
            {
              "globals": { "dotnetMode": "Managed" },
              "env": { "A": "shared-project", "B": "shared-project" },
              "services": [
                { "key": "api", "type": "Dotnet", "dir": "api", "env": { "B": "shared-service", "C": "shared-service" } },
                { "key": "web", "type": "npm", "dir": "web" }
              ]
            }
            """;

        [Fact]
        public void ModeAndTypeAreReadInAnyCase()
        {
            using var t = new TempProject(Config);

            var p = Project.Load(t.ConfigPath);

            Assert.True(p.IsManaged);
            Assert.True(p.Service("api")!.IsDotnet);
        }

        [Fact]
        public void ServiceValuesWinOverProjectValuesAndLocalWinsOverShared()
        {
            using var t = new TempProject(Config);
            var p = Project.Load(t.ConfigPath);
            p.SetEnv(null, EnvScope.Local, new Dictionary<string, string> { ["A"] = "local-project" });
            p.SetEnv("api", EnvScope.Local, new Dictionary<string, string> { ["C"] = "local-service" });

            var env = p.EffectiveEnv(p.Service("api")!);

            Assert.Equal("local-project", env["A"]);
            Assert.Equal("shared-service", env["B"]);
            Assert.Equal("local-service", env["C"]);
        }

        [Fact]
        public void BrokenLocalFileIsReportedAndNeverOverwritten()
        {
            using var t = new TempProject(Config);
            t.Write(".devrunner/local.json", "{ broken");

            var p = Project.Load(t.ConfigPath);

            Assert.NotNull(p.LocalProblem);
            Assert.Throws<InvalidOperationException>(() => p.SetEnabled(p.Service("api")!, false));
            Assert.Equal("{ broken", File.ReadAllText(t.LocalPath));
        }

        [Fact]
        public void EnvFilesComeFromTheRootAndServiceFoldersOnly()
        {
            using var t = new TempProject(Config);
            t.Write(".env", "X=1");
            t.Write("web/.env.local", "Y=1");
            t.Write("web/deep/.env", "Z=1");
            t.Write("other/.env", "Z=1");

            var files = Project.Load(t.ConfigPath).EnvFiles().Select(f => Path.GetRelativePath(t.Dir, f)).ToList();

            Assert.Equal([".env", Path.Combine("web", ".env.local")], files);
        }

        [Fact]
        public void CommentsInTheConfigAreNoticed()
        {
            using var t = new TempProject(Config.Replace("\"globals\"", "// the stack\n  \"globals\""));
            using var plain = new TempProject(Config);

            Assert.True(Project.Load(t.ConfigPath).ConfigHasComments);
            Assert.False(Project.Load(plain.ConfigPath).ConfigHasComments);
        }
    }
}

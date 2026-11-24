using mk.dev.runner.core;
using mk.dev.runner.core.Config;
using Xunit;

namespace mk.dev.runner.tests
{
    public class HealthTests
    {
        private const string Config = """
            {
              "services": [
                { "key": "plain", "type": "dotnet" },
                { "key": "old", "type": "command", "cmd": "node", "readyPattern": "started" },
                { "key": "probe", "type": "dotnet", "defaultPort": 5000, "health": { "type": "HTTP", "path": "/health", "everySec": 30 } }
              ]
            }
            """;

        [Fact]
        public void NoHealthCheckMeansAuto()
        {
            using var t = new TempProject(Config);

            var h = Project.Load(t.ConfigPath).Service("plain")!.HealthOrDefault;

            Assert.Equal(HealthCheck.Auto, h.Kind);
        }

        [Fact]
        public void OldReadyPatternBecomesALogCheck()
        {
            using var t = new TempProject(Config);

            var h = Project.Load(t.ConfigPath).Service("old")!.HealthOrDefault;

            Assert.Equal(HealthCheck.Log, h.Kind);
            Assert.Equal("started", h.Pattern);
        }

        [Fact]
        public void HealthCheckIsReadInAnyCase()
        {
            using var t = new TempProject(Config);

            var h = Project.Load(t.ConfigPath).Service("probe")!.HealthOrDefault;

            Assert.Equal(HealthCheck.Http, h.Kind);
            Assert.Equal("/health", h.Path);
            Assert.Equal(30, h.EverySec);
        }

        [Theory]
        [InlineData(null, null, "http://localhost:5000")]
        [InlineData(null, "/health", "http://localhost:5000/health")]
        [InlineData("https://localhost:{port}/", "health", "https://localhost:5000/health")]
        [InlineData("https://app.test", "/health", "https://app.test/health")]
        [InlineData("https://app.test", "http://localhost:{port}/ping", "http://localhost:5000/ping")]
        public void UrlCheckBuildsOnTheServiceAddress(string? address, string? path, string expected)
        {
            var svc = new ServiceConfig { Key = "x", DefaultPort = 5000, Url = address, Health = new HealthConfig { Type = "http", Path = path } };
            using var t = new TempProject(Config);

            Assert.Equal(expected, Project.Load(t.ConfigPath).HealthUrlOf(svc));
        }

        [Fact]
        public void EditorKeepsTheHealthCheckAndDropsReadyPattern()
        {
            using var t = new TempProject(Config);
            var p = Project.Load(t.ConfigPath);
            var def = p.ReadDefinition();
            Assert.Equal("log", def.Services[1].Health);
            Assert.Equal("started", def.Services[1].HealthValue);

            p.SaveDefinition(def);
            var saved = File.ReadAllText(t.ConfigPath);

            Assert.DoesNotContain("readyPattern", saved);
            Assert.Contains("\"pattern\": \"started\"", saved);
            Assert.Contains("\"everySec\": 30", saved);
        }
    }
}

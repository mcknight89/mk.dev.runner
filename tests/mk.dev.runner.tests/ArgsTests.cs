using mk.dev.runner.core;
using Xunit;

namespace mk.dev.runner.tests
{
    public class ArgsTests
    {
        public static TheoryData<string[]> ArgSets => new()
        {
            new[] { "run", "--urls", "http://localhost:5000" },
            new[] { "say", "a \"quoted\" word", "" },
            new[] { "x\"y", "two words" },
        };

        [Theory]
        [MemberData(nameof(ArgSets))]
        public void JoinThenSplitGivesTheSameArgs(string[] args)
        {
            var line = Project.JoinArgs(args);

            Assert.Equal(args, Project.SplitArgs(line));
        }

        [Fact]
        public void SplitKeepsQuotedSpaces()
        {
            Assert.Equal(["run", "my app", "--fast"], Project.SplitArgs("run \"my app\" --fast"));
        }
    }
}

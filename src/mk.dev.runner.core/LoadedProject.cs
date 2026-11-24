using System.Text.Json;
using mk.dev.runner.core.Config;

namespace mk.dev.runner.core
{
    public sealed record LoadedProject(ProjectEntry Entry, Project? Project, string? Error);
}

namespace mk.dev.runner.core
{
    public sealed record LoadedProject(ProjectEntry Entry, Project? Project, string? Error);
}

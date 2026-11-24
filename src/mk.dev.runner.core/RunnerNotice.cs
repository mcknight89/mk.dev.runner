namespace mk.dev.runner.core
{
    public sealed record RunnerNotice(string Project, string? Service, string Message);
}

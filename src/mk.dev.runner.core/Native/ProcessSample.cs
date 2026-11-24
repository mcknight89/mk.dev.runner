namespace mk.dev.runner.core.Native
{
    public readonly record struct ProcessSample(int Pid, string Name, long WorkingSet, long PrivateBytes);
}

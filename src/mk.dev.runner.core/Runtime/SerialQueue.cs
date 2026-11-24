using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace mk.dev.runner.core.Runtime;

/// <summary>
/// Runs actions one at a time on a dedicated thread, so a project's runner logic never races itself
/// (the same model as dev.mjs's single Node thread).
/// </summary>
internal sealed class SerialQueue : IDisposable
{
    readonly BlockingCollection<Action> _q = new();
    readonly Thread _thread;

    public event Action<Exception>? Error;

    public SerialQueue(string name)
    {
        _thread = new Thread(Run) { IsBackground = true, Name = name };
        _thread.Start();
    }

    bool OnThread => Thread.CurrentThread == _thread;

    void Run()
    {
        foreach (var a in _q.GetConsumingEnumerable())
        {
            try { a(); }
            catch (Exception e) { Error?.Invoke(e); }
        }
    }

    public void Post(Action a)
    {
        try { if (!_q.IsAddingCompleted) _q.Add(a); }
        catch (InvalidOperationException) { }
    }

    /// <summary>Runs and waits; exceptions are rethrown to the caller.</summary>
    public void Invoke(Action a)
    {
        if (OnThread || _q.IsAddingCompleted) { a(); return; }
        ExceptionDispatchInfo? err = null;
        using var done = new ManualResetEventSlim();
        Post(() =>
        {
            try { a(); }
            catch (Exception e) { err = ExceptionDispatchInfo.Capture(e); }
            finally { done.Set(); }
        });
        done.Wait();
        err?.Throw();
    }

    public Timer After(TimeSpan delay, Action a) =>
        new(_ => Post(a), null, delay < TimeSpan.Zero ? TimeSpan.Zero : delay, Timeout.InfiniteTimeSpan);

    public void Dispose()
    {
        _q.CompleteAdding();
        if (!OnThread) _thread.Join(5000);
    }
}

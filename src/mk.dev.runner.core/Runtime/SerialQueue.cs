using System.Collections.Concurrent;

namespace mk.dev.runner.core.Runtime
{
    internal sealed class SerialQueue : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new();
        private readonly Thread _thread;

        public event Action<Exception>? Error;

        public SerialQueue(string name)
        {
            _thread = new Thread(Run) { IsBackground = true, Name = name };
            _thread.Start();
        }

        private bool OnThread => Thread.CurrentThread == _thread;

        private void Run()
        {
            foreach (var action in _queue.GetConsumingEnumerable())
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Error?.Invoke(e);
                }
            }
        }

        public void Post(Action action)
        {
            if (_queue.IsAddingCompleted)
                return;
            try
            {
                _queue.Add(action);
            }
            catch (InvalidOperationException)
            {
            }
        }

        public void Invoke(Action action)
        {
            if (OnThread || _queue.IsAddingCompleted)
            {
                action();
                return;
            }
            var done = new TaskCompletionSource();
            Post(() =>
            {
                try
                {
                    action();
                    done.SetResult();
                }
                catch (Exception e)
                {
                    done.SetException(e);
                }
            });
            done.Task.GetAwaiter().GetResult();
        }

        public Timer After(TimeSpan delay, Action action)
        {
            return new Timer(_ => Post(action), null, delay < TimeSpan.Zero ? TimeSpan.Zero : delay, Timeout.InfiniteTimeSpan);
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            if (!OnThread)
                _thread.Join(5000);
        }
    }
}

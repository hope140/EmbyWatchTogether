using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>One bounded advisory-message drain per engine, independent of polling.</summary>
    internal sealed class SyncNotificationDispatcher
    {
        private readonly object _gate = new object();
        private readonly Queue<(Action<CancellationToken> send, double queuedAt)> _queue =
            new Queue<(Action<CancellationToken>, double)>();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly CancellationToken _token;
        private readonly Func<double> _elapsedSeconds;
        private bool _draining;
        private bool _stopped;
        private bool _cancelCompleted;
        private bool _disposed;
        private TaskCompletionSource<bool> _idle = CompletedIdle();

        internal SyncNotificationDispatcher(Func<double> elapsedSeconds = null)
        {
            _token = _stop.Token;
            _elapsedSeconds = elapsedSeconds ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        }

        internal Task WhenIdle { get { lock (_gate) { return _idle.Task; } } }

        internal bool TryEnqueueFresh(Action<CancellationToken, Func<bool>> send)
        {
            double queuedAt = _elapsedSeconds();
            return TryEnqueue(token => send(token, () => _elapsedSeconds() - queuedAt <= 10));
        }

        internal bool TryEnqueue(Action<CancellationToken> send)
        {
            lock (_gate)
            {
                if (_stopped || _queue.Count >= 64)
                {
                    return false;
                }

                _queue.Enqueue((send, _elapsedSeconds()));
                if (!_draining)
                {
                    _draining = true;
                    _idle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    Task.Run((Action)Drain);
                }

                return true;
            }
        }

        internal void Stop()
        {
            lock (_gate)
            {
                if (_stopped) return;
                _stopped = true;
                _queue.Clear();
            }

            // Cancellation callbacks belong to the transport. Even an uncooperative
            // callback must not hold the caller of Stop/Dispose or our queue lock.
            Task.Run(() =>
            {
                try { _stop.Cancel(); }
                catch { /* Advisory cancellation is best effort. */ }
                finally
                {
                    lock (_gate)
                    {
                        _cancelCompleted = true;
                        DisposeStoppedSource();
                    }
                }
            });
        }

        private void Drain()
        {
            while (true)
            {
                Action<CancellationToken> send;
                lock (_gate)
                {
                    if (_stopped || _queue.Count == 0)
                    {
                        _draining = false;
                        _idle.TrySetResult(true);
                        DisposeStoppedSource();
                        return;
                    }

                    var next = _queue.Dequeue();
                    if (_elapsedSeconds() - next.queuedAt > 10) continue;
                    send = next.send;
                }

                try { send(_token); }
                catch { /* One failed message must not strand the drain. */ }
            }
        }

        private void DisposeStoppedSource()
        {
            if (_stopped && _cancelCompleted && !_draining && !_disposed)
            {
                _disposed = true;
                _stop.Dispose();
            }
        }

        private static TaskCompletionSource<bool> CompletedIdle()
        {
            var idle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            idle.SetResult(true);
            return idle;
        }
    }
}

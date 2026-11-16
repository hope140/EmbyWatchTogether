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
        private readonly Queue<(Action<CancellationToken> send, double queuedAt, double readyAt)> _queue =
            new Queue<(Action<CancellationToken>, double, double)>();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly CancellationToken _token;
        private readonly Func<double> _elapsedSeconds;
        private Timer _wake;
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

        internal bool TryEnqueueFresh(Action<CancellationToken, Func<bool>> send, double delaySeconds = 0)
        {
            double queuedAt = _elapsedSeconds();
            return Enqueue(token => send(token, () => _elapsedSeconds() - queuedAt <= 10),
                queuedAt, delaySeconds);
        }

        internal bool TryEnqueue(Action<CancellationToken> send) => Enqueue(send, _elapsedSeconds(), 0);

        private bool Enqueue(Action<CancellationToken> send, double queuedAt, double delaySeconds)
        {
            // Only advisory text can be delayed, never playback or a room gate.
            if (double.IsNaN(delaySeconds) || delaySeconds < 0 || delaySeconds > .5)
                throw new ArgumentOutOfRangeException(nameof(delaySeconds));
            lock (_gate)
            {
                if (_stopped || _queue.Count >= 64) return false;
                _queue.Enqueue((send, queuedAt, queuedAt + delaySeconds));
                if (_idle.Task.IsCompleted)
                    _idle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                StartDrain();
                return true;
            }
        }

        // Called only under _gate. There is at most one worker and one wake timer.
        private void StartDrain()
        {
            if (_draining || _stopped) return;
            _wake?.Change(Timeout.Infinite, Timeout.Infinite);
            _draining = true;
            Task.Run((Action)Drain);
        }

        private void Wake(object unused)
        {
            lock (_gate)
            {
                if (!_stopped && _queue.Count > 0) StartDrain();
            }
        }

        internal void Stop()
        {
            lock (_gate)
            {
                if (_stopped) return;
                _stopped = true;
                _queue.Clear();
                _wake?.Dispose();
                _wake = null;
                if (!_draining) _idle.TrySetResult(true);
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
                Action<CancellationToken> send = null;
                lock (_gate)
                {
                    double now = _elapsedSeconds();
                    double nextReady = double.PositiveInfinity;
                    // Rotate deferred entries rather than blocking ready messages behind them.
                    int count = _queue.Count;
                    for (int i = 0; !_stopped && i < count; i++)
                    {
                        var next = _queue.Dequeue();
                        if (now - next.queuedAt > 10) continue;
                        if (next.readyAt <= now)
                        {
                            send = next.send;
                            break;
                        }
                        nextReady = Math.Min(nextReady, next.readyAt);
                        _queue.Enqueue(next);
                    }
                    if (send == null)
                    {
                        _draining = false;
                        if (_stopped || _queue.Count == 0) _idle.TrySetResult(true);
                        else
                        {
                            if (_wake == null) _wake = new Timer(Wake, null, Timeout.Infinite, Timeout.Infinite);
                            _wake.Change(Math.Max(1, (int)Math.Ceiling((nextReady - now) * 1000)), Timeout.Infinite);
                        }
                        DisposeStoppedSource();
                        return;
                    }
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

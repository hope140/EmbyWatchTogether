using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.Plugins.WatchTogether
{
    internal sealed class BoundedSessionDispatch
    {
        private const int MaximumFlights = 64;
        private readonly object _lock = new object();
        private readonly Dictionary<string, Flight> _flights =
            new Dictionary<string, Flight>(StringComparer.OrdinalIgnoreCase);
        private readonly TimeSpan _timeout;

        private sealed class Flight
        {
            internal Task Transport { get; set; }
        }

        internal BoundedSessionDispatch(TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }
            _timeout = timeout;
        }

        internal void Execute(string sessionId, Func<CancellationToken, Task> dispatch, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var elapsed = Stopwatch.StartNew();
            var flight = new Flight();
            lock (_lock)
            {
                // Completion publishes the Task result before its cleanup
                // continuation necessarily runs. Finished flights cannot block
                // the next command or consume the unfinished-flight capacity.
                var completedSessions = new List<string>();
                foreach (var pair in _flights)
                {
                    if (pair.Value.Transport?.IsCompleted == true)
                    {
                        completedSessions.Add(pair.Key);
                    }
                }
                foreach (var completedSession in completedSessions)
                {
                    _flights.Remove(completedSession);
                }
                if (_flights.ContainsKey(sessionId) || _flights.Count >= MaximumFlights)
                {
                    throw new OperationCanceledException("Session dispatch is still in flight.", cancellationToken);
                }
                _flights.Add(sessionId, flight);
            }

            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(_timeout);
            var token = linked.Token;
            // Even an Emby transport that blocks before returning its Task must
            // not occupy the room's polling thread. Expired queued work is inert.
            var transport = Task.Run(async () =>
            {
                token.ThrowIfCancellationRequested();
                if (elapsed.Elapsed >= _timeout)
                {
                    throw new OperationCanceledException(token);
                }
                await dispatch(token).ConfigureAwait(false);
            });
            lock (_lock)
            {
                flight.Transport = transport;
            }
            transport.ContinueWith(completed =>
            {
                // A timed-out transport retains its slot until it truly ends.
                // Observe late faults without publishing a late result to callers.
                var ignored = completed.Exception;
                lock (_lock)
                {
                    if (_flights.TryGetValue(sessionId, out var current) && ReferenceEquals(current, flight))
                    {
                        _flights.Remove(sessionId);
                    }
                }
                linked.Dispose();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            try
            {
                var remaining = _timeout - elapsed.Elapsed;
                int waitMilliseconds = (int)Math.Min(int.MaxValue, Math.Ceiling(Math.Max(0, remaining.TotalMilliseconds)));
                if (remaining <= TimeSpan.Zero || !transport.Wait(waitMilliseconds, token) || elapsed.Elapsed >= _timeout)
                {
                    // Cancel callbacks belong to the external transport and may
                    // themselves block. Never execute them on the polling thread.
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try { linked.Cancel(); }
                        catch (ObjectDisposedException) { }
                        catch (AggregateException) { }
                    });
                    throw new OperationCanceledException(token);
                }
            }
            catch (AggregateException)
            {
                // Task.Wait wraps faults; preserve the original exception object
                // consumed by the issuer's existing stable error/logging path.
                transport.GetAwaiter().GetResult();
                throw;
            }
            transport.GetAwaiter().GetResult();
        }
    }
}

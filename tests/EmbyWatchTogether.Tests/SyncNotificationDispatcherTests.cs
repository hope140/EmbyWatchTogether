using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class SyncNotificationDispatcherTests
    {
        [Fact]
        public async Task Queue_CapsWaitingAt64_UsesOneWorker_AndRecoversAfterException()
        {
            using var queue = new QueueProbe();
            using var release = new ManualResetEventSlim();
            var entered = Signal();
            int calls = 0, active = 0, maxActive = 0;
            Assert.True(queue.Enqueue(_ =>
            {
                Interlocked.Increment(ref active);
                entered.TrySetResult(true);
                release.Wait();
                Interlocked.Decrement(ref active);
            }));
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                for (int i = 0; i < 64; i++)
                    Assert.True(queue.Enqueue(_ =>
                    {
                        maxActive = Math.Max(maxActive, Interlocked.Increment(ref active));
                        Interlocked.Increment(ref calls);
                        Interlocked.Decrement(ref active);
                        throw new InvalidOperationException("advisory failure");
                    }));
                Assert.False(queue.Enqueue(_ => Interlocked.Add(ref calls, 1000)));
                release.Set();
                await queue.Idle.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(64, calls);
                Assert.Equal(1, maxActive);
                Assert.True(queue.Enqueue(_ => Interlocked.Increment(ref calls)));
                await queue.Idle.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(65, calls);
            }
            finally { release.Set(); }
        }

        [Theory]
        [InlineData(10, 1)]
        [InlineData(10.001, 0)]
        public async Task Queue_ExpiryUsesMonotonicQueueAge(double elapsed, int expected)
        {
            double monotonic = 0;
            using var queue = new QueueProbe(() => Volatile.Read(ref monotonic));
            using var release = new ManualResetEventSlim();
            var entered = Signal();
            int calls = 0;
            queue.Enqueue(_ => { entered.TrySetResult(true); release.Wait(); });
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                queue.Enqueue(_ => Interlocked.Increment(ref calls));
                Volatile.Write(ref monotonic, elapsed);
                release.Set();
                await queue.Idle.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(expected, calls);
            }
            finally { release.Set(); }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Stop_ClearsQueue_RejectsNewMessages_AndDoesNotWaitForTransportOrCallback(bool blockCallback)
        {
            using var queue = new QueueProbe();
            using var release = new ManualResetEventSlim();
            var entered = Signal();
            var cancelled = Signal();
            int queuedCalls = 0;
            queue.Enqueue(token =>
            {
                using var registration = token.Register(() =>
                {
                    cancelled.TrySetResult(true);
                    if (blockCallback) release.Wait();
                });
                entered.TrySetResult(true);
                release.Wait(); // models a legacy transport ignoring cancellation
            });
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                queue.Enqueue(_ => Interlocked.Increment(ref queuedCalls));
                await Task.Run(queue.Stop).WaitAsync(TimeSpan.FromSeconds(5));
                await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False(queue.Idle.IsCompleted);
                Assert.False(queue.Enqueue(_ => Interlocked.Increment(ref queuedCalls)));
                queue.Stop();
                release.Set();
                await queue.Idle.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(0, queuedCalls);
            }
            finally { release.Set(); }
        }

        [Fact]
        public async Task DeferredEntries_DoNotBlockReadyWork_AndUseExactMonotonicBoundary()
        {
            double monotonic = 0;
            using var queue = new QueueProbe(() => Volatile.Read(ref monotonic));
            int calls = 0;
            Assert.True(queue.EnqueueFresh((_, fresh) => { Assert.True(fresh()); Interlocked.Increment(ref calls); }, .5));
            await queue.DrainReady();
            Assert.Equal(0, calls);
            Assert.False(queue.Idle.IsCompleted);
            Volatile.Write(ref monotonic, .499);
            await queue.DrainReady();
            Assert.Equal(0, calls);
            Volatile.Write(ref monotonic, .5);
            await queue.DrainReady();
            await queue.Idle.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, calls);
        }

        [Fact]
        public async Task DeferredEntries_ShareCapacity_AndTimerWakesWithoutNewEnqueue()
        {
            using var queue = new QueueProbe();
            using var release = new ManualResetEventSlim();
            var entered = Signal();
            int calls = 0;
            queue.Enqueue(_ => { entered.TrySetResult(true); release.Wait(); });
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                for (int i = 0; i < 64; i++)
                    Assert.True(queue.EnqueueFresh((_, fresh) => { if (fresh()) Interlocked.Increment(ref calls); }, .5));
                Assert.False(queue.EnqueueFresh((_, fresh) => Interlocked.Add(ref calls, 1000), .5));
                release.Set();
                await queue.Idle.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(64, calls);
            }
            finally { release.Set(); }
        }

        [Theory]
        [InlineData(10, 1)]
        [InlineData(10.001, 0)]
        public async Task DeferredEntries_ExpiryIncludesTheDelay(double elapsed, int expected)
        {
            double monotonic = 0;
            using var queue = new QueueProbe(() => Volatile.Read(ref monotonic));
            int calls = 0;
            queue.EnqueueFresh((_, fresh) => { if (fresh()) Interlocked.Increment(ref calls); }, .5);
            await queue.DrainReady();
            Volatile.Write(ref monotonic, elapsed);
            await queue.DrainReady();
            await queue.Idle.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(expected, calls);
        }

        [Fact]
        public async Task Stop_ClearsDeferredEntriesAndCompletesIdleWithoutWaitingForTimer()
        {
            double monotonic = 0;
            using var queue = new QueueProbe(() => Volatile.Read(ref monotonic));
            int calls = 0;
            queue.EnqueueFresh((_, fresh) => Interlocked.Increment(ref calls), .5);
            await queue.DrainReady();
            queue.Stop();
            await queue.Idle.WaitAsync(TimeSpan.FromSeconds(5));
            Volatile.Write(ref monotonic, 1);
            Assert.False(queue.EnqueueFresh((_, fresh) => Interlocked.Increment(ref calls), .5));
            Assert.Equal(0, calls);
        }

        [Fact]
        public async Task DeferredEntries_ReuseOneTimer_AndStopDisposesIt()
        {
            double monotonic = 0;
            using var queue = new QueueProbe(() => Volatile.Read(ref monotonic));
            queue.EnqueueFresh((_, fresh) => { }, .5);
            var first = queue.WaitForDeferredTimer();
            Volatile.Write(ref monotonic, .2);
            await queue.DrainReady();
            var second = queue.WaitForDeferredTimer();
            Assert.Same(first, second);
            queue.Stop();
            await queue.Idle.WaitAsync(TimeSpan.FromSeconds(5));
            // The current Timer API reports a disposed timer by returning false.
            Assert.False(first.Change(1, Timeout.Infinite));
        }

        private static TaskCompletionSource<bool> Signal() =>
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        internal sealed class QueueProbe : IDisposable
        {
            private readonly object _queue;
            private readonly Type _type;
            internal QueueProbe(Func<double> monotonic = null)
            {
                _type = typeof(SyncEngine).Assembly.GetType("Emby.Plugins.WatchTogether.SyncNotificationDispatcher");
                _queue = Activator.CreateInstance(_type, BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new object[] { monotonic }, null);
            }
            internal bool Enqueue(Action<CancellationToken> send) =>
                (bool)_type.GetMethod("TryEnqueue", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(_queue, new object[] { send });
            internal bool EnqueueFresh(Action<CancellationToken, Func<bool>> send, double delay) =>
                (bool)_type.GetMethod("TryEnqueueFresh", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(_queue, new object[] { send, delay });
            internal Task DrainReady()
            {
                var signal = Signal();
                Assert.True(Enqueue(_ => signal.TrySetResult(true)));
                return signal.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            internal Timer WaitForDeferredTimer()
            {
                var gate = _type.GetField("_gate", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_queue);
                Timer timer = null;
                Assert.True(SpinWait.SpinUntil(() =>
                {
                    lock (gate)
                    {
                        timer = (Timer)_type.GetField("_wake", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_queue);
                        return timer != null && !(bool)_type.GetField("_draining", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_queue);
                    }
                }, TimeSpan.FromSeconds(5)), "Deferred worker did not yield to its timer");
                return timer;
            }
            internal Task Idle => (Task)_type.GetProperty("WhenIdle", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(_queue);
            internal void Stop() => _type.GetMethod("Stop", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_queue, null);
            public void Dispose() => Stop();
        }
    }
}

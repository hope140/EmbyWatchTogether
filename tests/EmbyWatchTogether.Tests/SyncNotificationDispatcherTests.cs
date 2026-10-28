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
            internal Task Idle => (Task)_type.GetProperty("WhenIdle", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(_queue);
            internal void Stop() => _type.GetMethod("Stop", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_queue, null);
            public void Dispose() => Stop();
        }
    }
}

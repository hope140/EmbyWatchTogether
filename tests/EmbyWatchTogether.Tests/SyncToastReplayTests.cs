using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public partial class SyncEnginePerformanceTests
    {
        private static SessionSnapshot WithActivity(SessionSnapshot s, DateTimeOffset at) =>
            new SessionSnapshot(s.SessionId, s.UserId, s.ItemId, s.MediaSourceId, s.PositionTicks,
                s.RunTimeTicks, s.IsPaused, s.PlaybackRate, s.Stopped, s.SupportsRemoteControl, s.Capabilities, at);

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(1)]
        public void StaleReplay_OnlyStrictlyOlderActivityIsIgnored(int offset)
        {
            using var h = new Harness(); h.EnterWatching();
            var at = h.Runtime.Previous["u1"].LastActivityDateUtc.AddSeconds(offset);
            h.Provider.Snapshots = new[] {
                WithActivity(h.Snapshot("u1", true, 40), at),
                WithActivity(h.Snapshot("u2", false, 40), at) };
            h.Poll();
            Assert.Equal(offset < 0 ? 0 : 1, h.Commands.Count);
            if (offset >= 0) Assert.Equal(("u2", RemoteCommands.Pause), h.Commands.Single());
            else
            {
                Assert.False(h.Runtime.Previous["u1"].IsPaused);
                h.Set(false, false, 80, 40); h.Poll();
                Assert.Equal(RoomState.Barrier, h.Runtime.State);
                Assert.Equal(2, h.Commands.Count);
            }
        }

        [Theory]
        [InlineData("session")]
        [InlineData("item")]
        [InlineData("capability")]
        public void StalePeer_DoesNotMaskIdentityOrEligibilityChange(string change)
        {
            using var h = new Harness(); h.EnterWatching();
            var oldPeer = WithActivity(h.Snapshot("u2", false, 40), h.Runtime.Previous["u2"].LastActivityDateUtc.AddSeconds(-1));
            var first = h.Snapshot("u1", false, 40,
                session: change == "session" ? "replacement" : "s1", item: change == "item" ? "new-item" : "item");
            if (change == "capability") first = new SessionSnapshot(first.SessionId, first.UserId,
                first.ItemId, first.MediaSourceId, first.PositionTicks, first.RunTimeTicks, false, 1, false,
                false, new SessionCapabilityReport(false, Array.Empty<string>()), h.Now);
            h.Provider.Snapshots = new[] { first, oldPeer }; h.Poll();
            Assert.NotEqual(RoomState.Watching, h.Runtime.State);
        }

        [Fact]
        public async Task CompletedSeek_OlderPausedSnapshotCannotGeneratePauseAndUnpause()
        {
            using var h = new Harness(); h.EnterWatching();
            h.Set(false, false, 80, 41); h.Poll();
            h.Set(true, true, 80, 42); h.Poll();
            h.Set(true, true, 80, 80); var old = h.Provider.Snapshots; h.Poll();
            h.Set(false, false, 80, 80); h.Poll();
            Assert.Equal(RoomState.Watching, h.Runtime.State);
            var count = h.Commands.Count;
            h.Provider.Snapshots = old; h.Poll();
            h.Set(false, false, 81, 81); h.Poll(); await h.Idle;
            Assert.Equal(count, h.Commands.Count);
        }

        [Fact]
        public async Task QueuedRetry_IsDiscardedAfterSeekStageAdvances()
        {
            using var h = new Harness(); h.EnterWatching(); await h.Idle; h.EnableNotifications();
            h.Enqueue("u1"); await h.Messages.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                h.Set(false, false, 80, 41); h.Poll();
                h.Set(true, true, 80, 42); h.Poll();
                for (int n = 0; n < 12; n++) { h.Set(true, true, 80, 42); h.Poll(); }
                Assert.True((bool)typeof(RoomRuntime).GetProperty("RetryNotificationSent",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(h.Runtime));
                h.Set(true, true, 80, 80); h.Poll();
                Assert.Equal(BarrierStage.Restore, h.Runtime.Barrier.Stage);
            }
            finally { h.Messages.Release.Set(); }
            await h.Idle.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(h.Messages.Calls);
        }
        [Fact]
        public async Task QueuedCompletion_IsDiscardedWhenAnotherSeekStarts()
        {
            using var h = new Harness(); h.EnterWatching(); await h.Idle; h.EnableNotifications();
            h.Enqueue("u1"); await h.Messages.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                h.Set(false, false, 80, 41); h.Poll();
                h.Set(true, true, 80, 42); h.Poll();
                h.Set(true, true, 80, 80); h.Poll();
                h.Set(false, false, 80, 80); h.Poll();
                Assert.Equal(RoomState.Watching, h.Runtime.State);
                h.Set(false, false, 20, 80); h.Poll();
                Assert.Equal(RoomState.Barrier, h.Runtime.State);
            }
            finally { h.Messages.Release.Set(); }
            await h.Idle.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(h.Messages.Calls); // Only the synthetic already-in-flight blocker.
        }
    }
}

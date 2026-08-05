using System.Collections.Generic;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class SessionSnapshotTests
    {
        [Fact]
        public void Snapshot_ClampsNegativeTicksToZero()
        {
            var snapshot = new SessionSnapshot(
                sessionId: "s1",
                userId: "u1",
                itemId: "i1",
                mediaSourceId: "m1",
                positionTicks: -100,
                runTimeTicks: -50,
                isPaused: false,
                playbackRate: 1.0,
                stopped: false,
                supportsRemoteControl: true,
                capabilities: new SessionCapabilityReport(true, new[] { "Pause", "Unpause", "Seek" }));

            Assert.Equal(0, snapshot.PositionTicks);
            Assert.Equal(0, snapshot.RunTimeTicks);
        }

        [Fact]
        public void Snapshot_NonPositivePlaybackRate_FallsBackToOne()
        {
            var snapshot = CreateSnapshot(playbackRate: 0);

            Assert.Equal(1.0, snapshot.PlaybackRate);
        }

        [Fact]
        public void Snapshot_PositionSeconds_ConvertsTicks()
        {
            var snapshot = CreateSnapshot(positionTicks: 25_000_000);

            Assert.Equal(2.5, snapshot.PositionSeconds);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        public void Snapshot_Online_RequiresSessionIdAndNotStopped(bool hasSessionId, bool stopped)
        {
            var snapshot = CreateSnapshot(sessionId: hasSessionId ? "s1" : "", stopped: stopped);

            Assert.Equal(hasSessionId && !stopped, snapshot.Online);
        }

        [Fact]
        public void Snapshot_MergesCapabilityCommands()
        {
            var capabilities = new SessionCapabilityReport(
                true,
                new HashSet<string>(new[] { "Pause", "Unpause", "Seek" }, System.StringComparer.OrdinalIgnoreCase));
            var snapshot = new SessionSnapshot(
                "s1", "u1", "i1", "m1", 0, 100, false, 1.0, false, true, capabilities);

            Assert.True(snapshot.Capabilities.CanControlPlayback);
            Assert.True(snapshot.Capabilities.CanSeek);
        }

        private static SessionSnapshot CreateSnapshot(
            string sessionId = "s1",
            bool stopped = false,
            long positionTicks = 0,
            double playbackRate = 1.0)
        {
            return new SessionSnapshot(
                sessionId,
                "u1",
                "i1",
                "m1",
                positionTicks,
                runTimeTicks: 100,
                isPaused: false,
                playbackRate,
                stopped,
                supportsRemoteControl: true,
                capabilities: new SessionCapabilityReport(true, new[] { "Pause", "Unpause", "Seek" }));
        }
    }
}

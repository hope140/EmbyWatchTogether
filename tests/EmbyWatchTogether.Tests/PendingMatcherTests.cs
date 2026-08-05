using System;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class PendingMatcherTests
    {
        [Fact]
        public void Pause_MatchesPausedSnapshot()
        {
            var pending = Pending(RemoteCommands.Pause);

            Assert.True(PendingMatcher.Matches(pending, TestSnapshots.Online("u1").WithPaused(true)));
        }

        [Fact]
        public void Pause_DoesNotMatchPlayingSnapshot()
        {
            var pending = Pending(RemoteCommands.Pause);

            Assert.False(PendingMatcher.Matches(pending, TestSnapshots.Online("u1")));
        }

        [Fact]
        public void Unpause_MatchesPlayingSnapshot()
        {
            var pending = Pending(RemoteCommands.Unpause);

            Assert.True(PendingMatcher.Matches(pending, TestSnapshots.Online("u1")));
        }

        [Fact]
        public void Seek_WithinTwoSeconds_Matches()
        {
            var pending = Pending(RemoteCommands.Seek, positionTicks: 50 * SessionSnapshot.TicksPerSecond);
            var snapshot = TestSnapshots.Online("u1")
                .WithPosition(50 * SessionSnapshot.TicksPerSecond + SessionSnapshot.TicksPerSecond);

            Assert.True(PendingMatcher.Matches(pending, snapshot));
        }

        [Fact]
        public void Seek_BeyondTwoSeconds_DoesNotMatch()
        {
            var pending = Pending(RemoteCommands.Seek, positionTicks: 50 * SessionSnapshot.TicksPerSecond);
            var snapshot = TestSnapshots.Online("u1")
                .WithPosition(50 * SessionSnapshot.TicksPerSecond + 5 * SessionSnapshot.TicksPerSecond);

            Assert.False(PendingMatcher.Matches(pending, snapshot));
        }

        [Fact]
        public void UnknownCommand_DoesNotMatch()
        {
            Assert.False(PendingMatcher.Matches(Pending("DisplayMessage"), TestSnapshots.Online("u1")));
        }

        [Fact]
        public void NullArguments_DoNotMatch()
        {
            Assert.False(PendingMatcher.Matches(null, TestSnapshots.Online("u1")));
            Assert.False(PendingMatcher.Matches(Pending(RemoteCommands.Pause), null));
        }

        private static PendingCommand Pending(string command, long? positionTicks = null)
        {
            return new PendingCommand
            {
                UserId = "u1",
                Command = command,
                PositionTicks = positionTicks,
                IssuedAtUtc = DateTimeOffset.UtcNow,
            };
        }
    }

    internal static class SnapshotExtensions
    {
        public static SessionSnapshot WithPaused(this SessionSnapshot s, bool paused)
        {
            return new SessionSnapshot(
                s.SessionId, s.UserId, s.ItemId, s.MediaSourceId,
                s.PositionTicks, s.RunTimeTicks, paused, s.PlaybackRate,
                s.Stopped, s.SupportsRemoteControl, s.Capabilities);
        }

        public static SessionSnapshot WithPosition(this SessionSnapshot s, long positionTicks)
        {
            return new SessionSnapshot(
                s.SessionId, s.UserId, s.ItemId, s.MediaSourceId,
                positionTicks, s.RunTimeTicks, s.IsPaused, s.PlaybackRate,
                s.Stopped, s.SupportsRemoteControl, s.Capabilities);
        }
    }
}

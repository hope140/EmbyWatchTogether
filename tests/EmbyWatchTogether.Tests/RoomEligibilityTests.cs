using System;
using System.Collections.Generic;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class RoomEligibilityTests
    {
        [Fact]
        public void IsPairEligible_SameItemOnlinePair_ReturnsTrue()
        {
            var snapshots = TwoOnline("i1", "i1");

            Assert.True(RoomEligibility.IsPairEligible(snapshots));
        }

        [Fact]
        public void IsPairEligible_ItemIdsAreCaseInsensitive()
        {
            var snapshots = TwoOnline("ITEM-1", "item-1");

            Assert.True(RoomEligibility.IsPairEligible(snapshots));
        }

        [Fact]
        public void IsPairEligible_DifferentItems_ReturnsFalse()
        {
            var snapshots = TwoOnline("i1", "i2");

            Assert.False(RoomEligibility.IsPairEligible(snapshots));
        }

        [Fact]
        public void IsPairEligible_StoppedParticipant_ReturnsFalse()
        {
            var snapshots = new Dictionary<string, SessionSnapshot>
            {
                ["u1"] = TestSnapshots.Online("u1", "i1"),
                ["u2"] = TestSnapshots.Offline("u2"),
            };

            Assert.False(RoomEligibility.IsPairEligible(snapshots));
        }

        [Fact]
        public void IsPairEligible_RuntimeDifferenceAboveThreeSeconds_ReturnsFalse()
        {
            var snapshots = new Dictionary<string, SessionSnapshot>
            {
                ["u1"] = TestSnapshots.Online("u1", "i1"),
                ["u2"] = new SessionSnapshot(
                    "s2", "u2", "i1", "m1", 0,
                    runTimeTicks: 100 * SessionSnapshot.TicksPerSecond + 4 * SessionSnapshot.TicksPerSecond,
                    isPaused: false, playbackRate: 1.0, stopped: false, true,
                    new SessionCapabilityReport(true, new[] { "Pause", "Unpause", "Seek" })),
            };

            Assert.False(RoomEligibility.IsPairEligible(snapshots));
        }

        [Fact]
        public void IsPairEligible_NonOnePlaybackRate_ReturnsFalse()
        {
            var snapshots = new Dictionary<string, SessionSnapshot>
            {
                ["u1"] = TestSnapshots.Online("u1", "i1"),
                ["u2"] = new SessionSnapshot(
                    "s2", "u2", "i1", "m1", 0,
                    100 * SessionSnapshot.TicksPerSecond,
                    false, playbackRate: 2.0, stopped: false, true,
                    new SessionCapabilityReport(true, new[] { "Pause", "Unpause", "Seek" })),
            };

            Assert.False(RoomEligibility.IsPairEligible(snapshots));
        }

        [Fact]
        public void IsPairEligible_WrongMemberCount_ReturnsFalse()
        {
            Assert.False(RoomEligibility.IsPairEligible(new Dictionary<string, SessionSnapshot>
            {
                ["u1"] = TestSnapshots.Online("u1", "i1"),
            }));
            Assert.False(RoomEligibility.IsPairEligible(new Dictionary<string, SessionSnapshot>()));
        }

        [Fact]
        public void IsPairEligible_NullDictionary_ReturnsFalse()
        {
            Assert.False(RoomEligibility.IsPairEligible(null));
        }

        private static Dictionary<string, SessionSnapshot> TwoOnline(string itemA, string itemB)
        {
            return new Dictionary<string, SessionSnapshot>
            {
                ["u1"] = TestSnapshots.Online("u1", itemA),
                ["u2"] = TestSnapshots.Online("u2", itemB),
            };
        }
    }
}

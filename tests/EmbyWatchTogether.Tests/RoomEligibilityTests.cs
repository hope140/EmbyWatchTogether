using System;
using System.Collections.Generic;
using System.Reflection;
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
        public void IsPairEligible_RemoteControlFlagsMustBothBeTrueWithoutCommandDeclarations()
        {
            var snapshots = new Dictionary<string, SessionSnapshot>
            {
                ["u1"] = Snapshot("u1", supportsRemoteControl: true),
                ["u2"] = Snapshot("u2", supportsRemoteControl: true),
            };

            Assert.True(RoomEligibility.IsPairEligible(snapshots));
        }

        [Fact]
        public void IsPairEligible_RemoteControlFlagFalseForEitherParticipantReturnsFalse()
        {
            var snapshots = new Dictionary<string, SessionSnapshot>
            {
                ["u1"] = Snapshot("u1", supportsRemoteControl: true),
                ["u2"] = Snapshot("u2", supportsRemoteControl: false),
            };

            Assert.False(RoomEligibility.IsPairEligible(snapshots));
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

        [Fact]
        public void Evaluate_CoversEveryReachableReason_AndMatchesCompatibilityBoolean()
        {
            var cases = new[]
            {
                Case("SnapshotCount", new Dictionary<string, SessionSnapshot>
                {
                    ["u1"] = TestSnapshots.Online("u1", "i1"),
                    ["u2"] = TestSnapshots.Online("u2", "i1"),
                    ["u3"] = TestSnapshots.Online("u3", "i1"),
                }),
                Case("MissingSnapshot", new Dictionary<string, SessionSnapshot>
                {
                    ["u1"] = TestSnapshots.Online("u1", "i1"),
                }),
                Case("NullSnapshot", new Dictionary<string, SessionSnapshot>
                {
                    ["u1"] = TestSnapshots.Online("u1", "i1"),
                    ["u2"] = null,
                }),
                Case("OfflineOrStopped", new Dictionary<string, SessionSnapshot>
                {
                    ["u1"] = TestSnapshots.Online("u1", "i1"),
                    ["u2"] = TestSnapshots.Offline("u2"),
                }),
                Case("RemoteControlUnsupportedOrMismatch", new Dictionary<string, SessionSnapshot>
                {
                    ["u1"] = Snapshot("u1", supportsRemoteControl: true),
                    ["u2"] = Snapshot("u2", supportsRemoteControl: false),
                }),
                Case("EmptyOrDifferentItem", TwoOnline("i1", "i2")),
                Case("InvalidOrDifferentRuntime", new Dictionary<string, SessionSnapshot>
                {
                    ["u1"] = TestSnapshots.Online("u1", "i1"),
                    ["u2"] = new SessionSnapshot(
                        "s2", "u2", "i1", "m1", 0,
                        runTimeTicks: 0,
                        isPaused: false, playbackRate: 1.0, stopped: false, true,
                        new SessionCapabilityReport(true, new[] { "Pause" })),
                }),
                Case("PlaybackRateNotOne", new Dictionary<string, SessionSnapshot>
                {
                    ["u1"] = TestSnapshots.Online("u1", "i1"),
                    ["u2"] = new SessionSnapshot(
                        "s2", "u2", "i1", "m1", 0,
                        runTimeTicks: 100 * SessionSnapshot.TicksPerSecond,
                        isPaused: false, playbackRate: 2.0, stopped: false, true,
                        new SessionCapabilityReport(true, new[] { "Pause" })),
                }),
                Case("None", TwoOnline("i1", "i1")),
            };

            foreach (var testCase in cases)
            {
                bool compatible = RoomEligibility.IsPairEligible(testCase.Snapshots);
                var evaluation = Evaluate(testCase.Snapshots);

                Assert.Equal(testCase.ExpectedReason == "None", compatible);
                Assert.Equal(compatible, evaluation.IsEligible);
                Assert.Equal(testCase.ExpectedReason, evaluation.FailureReason);
            }

            bool nullCompatible = RoomEligibility.IsPairEligible(null);
            var nullEvaluation = Evaluate(null);
            Assert.False(nullCompatible);
            Assert.False(nullEvaluation.IsEligible);
            Assert.Equal("SnapshotCount", nullEvaluation.FailureReason);
        }

        private static Dictionary<string, SessionSnapshot> TwoOnline(string itemA, string itemB)
        {
            return new Dictionary<string, SessionSnapshot>
            {
                ["u1"] = TestSnapshots.Online("u1", itemA),
                ["u2"] = TestSnapshots.Online("u2", itemB),
            };
        }

        private static SessionSnapshot Snapshot(string userId, bool supportsRemoteControl)
        {
            return new SessionSnapshot(
                "session-" + userId,
                userId,
                "i1",
                "m1",
                0,
                100 * SessionSnapshot.TicksPerSecond,
                false,
                1.0,
                stopped: false,
                supportsRemoteControl,
                new SessionCapabilityReport(true, Array.Empty<string>()));
        }

        private static (string ExpectedReason, Dictionary<string, SessionSnapshot> Snapshots) Case(
            string expectedReason,
            Dictionary<string, SessionSnapshot> snapshots)
        {
            return (expectedReason, snapshots);
        }

        private static (bool IsEligible, string FailureReason) Evaluate(
            IReadOnlyDictionary<string, SessionSnapshot> snapshots)
        {
            var method = typeof(RoomEligibility).GetMethod(
                "Evaluate",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var result = method.Invoke(null, new object[] { snapshots });
            Assert.NotNull(result);
            return (
                (bool)result.GetType().GetProperty("IsEligible").GetValue(result, null),
                result.GetType().GetProperty("FailureReason").GetValue(result, null).ToString());
        }
    }
}

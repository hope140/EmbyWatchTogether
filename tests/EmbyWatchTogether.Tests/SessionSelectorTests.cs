using System;
using System.Collections.Generic;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class SessionSelectorTests
    {
        [Fact]
        public void Select_PicksSingleCapableSessionPerUser()
        {
            var candidates = new List<SessionSnapshot>
            {
                Snapshot("s1", "u1", "i1", activity: 1000),
                Snapshot("s2", "u2", "i1", activity: 900),
            };

            var result = SessionSelector.Select(candidates, new[] { "u1", "u2" });

            Assert.Equal(2, result.Count);
            Assert.Equal("s1", result["u1"].SessionId);
            Assert.Equal("s2", result["u2"].SessionId);
        }

        [Fact]
        public void Select_IgnoresOfflineRecords()
        {
            var offline = new SessionSnapshot(
                "", "u1", "", "", 0, 0, false, 1.0, stopped: true, false,
                new SessionCapabilityReport(false, Array.Empty<string>()), default);
            var candidates = new List<SessionSnapshot>
            {
                offline,
                Snapshot("s2", "u2", "i1", activity: 900),
            };

            var result = SessionSelector.Select(candidates, new[] { "u1", "u2" });

            Assert.Single(result);
            Assert.False(result.ContainsKey("u1"));
        }

        [Fact]
        public void Select_PrefersCapableOverUnknownWhenEquallyFresh()
        {
            var unknown = Snapshot("s1", "u1", "i1", capable: false, commands: Array.Empty<string>(), activity: 1000);
            var capable = Snapshot("s2", "u1", "i1", capable: true, activity: 1000);

            var result = SessionSelector.Select(
                new List<SessionSnapshot> { unknown, capable, Snapshot("s3", "u2", "i1", activity: 900) },
                new[] { "u1", "u2" });

            // The explicitly capable record outranks the unknown one.
            Assert.Equal("s2", result["u1"].SessionId);
        }

        [Fact]
        public void Select_AmbiguousTie_SkipsUser()
        {
            var candidates = new List<SessionSnapshot>
            {
                Snapshot("s1", "u1", "i1", activity: 1000),
                Snapshot("s2", "u1", "i1", activity: 1000),
            };

            var result = SessionSelector.Select(candidates, new[] { "u1", "u2" });

            Assert.False(result.ContainsKey("u1"));
        }

        [Fact]
        public void Select_DeduplicatesSameSessionIdKeepingFreshest()
        {
            var candidates = new List<SessionSnapshot>
            {
                Snapshot("s1", "u1", "i1", activity: 500),
                Snapshot("s1", "u1", "i1", activity: 2000),
            };

            var result = SessionSelector.Select(
                candidates,
                new[] { "u1", Snapshot("s2", "u2", "i1", activity: 900).UserId });

            // Same session id -> one target; the freshest copy is retained.
            Assert.True(result.ContainsKey("u1"));
            Assert.Equal(2000, result["u1"].LastActivityDateUtc.Ticks - new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).Ticks);
        }

        [Fact]
        public void Select_PrefersCommonItem()
        {
            var candidates = new List<SessionSnapshot>
            {
                Snapshot("s1a", "u1", "itemA", activity: 1000),
                Snapshot("s1b", "u1", "itemB", activity: 2000),
                Snapshot("s2", "u2", "itemA", activity: 900),
            };

            var result = SessionSelector.Select(candidates, new[] { "u1", "u2" });

            Assert.Equal("itemA", result["u1"].ItemId);
            Assert.Equal("itemA", result["u2"].ItemId);
        }

        private static SessionSnapshot Snapshot(
            string sessionId,
            string userId,
            string itemId,
            bool capable = true,
            string[] commands = null,
            long activity = 0)
        {
            var capabilities = new SessionCapabilityReport(capable, commands ?? new[] { "Pause", "Unpause", "Seek" });
            return new SessionSnapshot(
                sessionId, userId, itemId, "m1",
                0, 100 * SessionSnapshot.TicksPerSecond, false, 1.0, stopped: false,
                capable, capabilities,
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(activity));
        }
    }
}

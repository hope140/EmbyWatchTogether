using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class SessionSelectorTests
    {
        private static readonly DateTimeOffset TestNow =
            new DateTimeOffset(2026, 1, 1, 0, 0, 30, TimeSpan.Zero);

        [Fact]
        public void Select_PicksSingleCapableSessionPerUser()
        {
            var candidates = new List<SessionSnapshot>
            {
                Snapshot("s1", "u1", "i1", activity: 1000),
                Snapshot("s2", "u2", "i1", activity: 900),
            };

            var result = Select(candidates, new[] { "u1", "u2" });

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

            var result = Select(candidates, new[] { "u1", "u2" });

            Assert.Single(result);
            Assert.False(result.ContainsKey("u1"));
        }

        [Fact]
        public void Select_PrefersCapableOverUnknownWhenEquallyFresh()
        {
            var unknown = Snapshot("s1", "u1", "i1", capable: false, commands: Array.Empty<string>(), activity: 1000);
            var capable = Snapshot("s2", "u1", "i1", capable: true, activity: 1000);

            var result = Select(
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

            var result = Select(
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

            var result = Select(candidates, new[] { "u1", "u2" });

            Assert.Equal("itemA", result["u1"].ItemId);
            Assert.Equal("itemA", result["u2"].ItemId);
        }

        [Fact]
        public void Select_MultipleCommonItems_UsesGlobalMaximinScore()
        {
            var candidates = new List<SessionSnapshot>
            {
                Snapshot("s1a", "u1", "itemA", activity: 2000),
                Snapshot("s1b", "u1", "itemB", activity: 1000),
                Snapshot("s2a", "u2", "itemA", activity: 1000),
                Snapshot("s2b", "u2", "itemB", activity: 1900),
            };

            var result = Select(candidates, new[] { "u1", "u2" });

            // Both items have the same weaker endpoint; itemA wins on the
            // stronger endpoint and both users must select itemA.
            Assert.Equal("itemA", result["u1"].ItemId);
            Assert.Equal("itemA", result["u2"].ItemId);
        }

        [Fact]
        public void Select_MultipleCommonItems_IsIndependentOfInputAndUserOrder()
        {
            var candidates = new List<SessionSnapshot>
            {
                Snapshot("s1a", "u1", "itemA", activity: 2000),
                Snapshot("s1b", "u1", "itemB", activity: 1000),
                Snapshot("s2a", "u2", "itemA", activity: 1000),
                Snapshot("s2b", "u2", "itemB", activity: 1900),
            };

            var forward = Select(candidates, new[] { "u1", "u2" });
            candidates.Reverse();
            var reversed = Select(candidates, new[] { "u2", "u1" });

            Assert.Equal("itemA", forward["u1"].ItemId);
            Assert.Equal("itemA", forward["u2"].ItemId);
            Assert.Equal("itemA", reversed["u1"].ItemId);
            Assert.Equal("itemA", reversed["u2"].ItemId);
        }

        [Fact]
        public void Select_MultipleCommonItems_EqualGlobalScoreFailsClosed()
        {
            var candidates = new List<SessionSnapshot>
            {
                Snapshot("s1a", "u1", "itemA", activity: 1000),
                Snapshot("s1b", "u1", "itemB", activity: 1000),
                Snapshot("s2a", "u2", "itemA", activity: 1000),
                Snapshot("s2b", "u2", "itemB", activity: 1000),
            };

            var result = Select(candidates, new[] { "u1", "u2" });

            Assert.Empty(result);
        }

        [Fact]
        public void Select_MultipleCommonItems_CapabilityContributesToGlobalScore()
        {
            var candidates = new List<SessionSnapshot>
            {
                Snapshot("s1a", "u1", "itemA", capable: true, activity: 1000),
                Snapshot("s1b", "u1", "itemB", capable: false, commands: Array.Empty<string>(), activity: 1000),
                Snapshot("s2a", "u2", "itemA", capable: true, activity: 1000),
                Snapshot("s2b", "u2", "itemB", capable: true, activity: 1000),
            };

            var result = Select(candidates, new[] { "u1", "u2" });

            // ItemA is capable for both participants, while itemB has an
            // unknown-capability endpoint; capability therefore selects itemA.
            Assert.Equal("itemA", result["u1"].ItemId);
            Assert.Equal("itemA", result["u2"].ItemId);
        }

        [Fact]
        public void Select_MultipleCommonItems_ExcludesExpiredCandidateBeforeScoring()
        {
            var candidates = new List<SessionSnapshot>
            {
                SnapshotAt("s1a-stale", "u1", "itemA", TestNow.AddSeconds(-61)),
                SnapshotAt("s1b", "u1", "itemB", TestNow.AddSeconds(-1)),
                SnapshotAt("s2a", "u2", "itemA", TestNow.AddSeconds(-1)),
                SnapshotAt("s2b", "u2", "itemB", TestNow.AddSeconds(-1)),
            };

            var result = Select(candidates, new[] { "u1", "u2" });

            Assert.Equal("itemB", result["u1"].ItemId);
            Assert.Equal("itemB", result["u2"].ItemId);
        }

        [Fact]
        public void Select_MultipleCommonItems_ExcludesLaggingCandidateBeforeScoring()
        {
            var candidates = new List<SessionSnapshot>
            {
                SnapshotAt("s1a-lagging", "u1", "itemA", TestNow.AddSeconds(-17)),
                SnapshotAt("s1b", "u1", "itemB", TestNow.AddSeconds(-1)),
                SnapshotAt("s2a", "u2", "itemA", TestNow.AddSeconds(-1)),
                SnapshotAt("s2b", "u2", "itemB", TestNow.AddSeconds(-1)),
            };

            var result = Select(candidates, new[] { "u1", "u2" });

            Assert.Equal("itemB", result["u1"].ItemId);
            Assert.Equal("itemB", result["u2"].ItemId);
        }

        [Fact]
        public void SelectWithDiagnostics_UniqueWinnerUsesSameFilteringPipeline()
        {
            var candidates = new List<SessionSnapshot>
            {
                Snapshot("s1a", "u1", "itemA", activity: 2000),
                Snapshot("s1b", "u1", "itemB", activity: 1000),
                Snapshot("s2a", "u2", "itemA", activity: 1000),
                Snapshot("s2b", "u2", "itemB", activity: 1900),
            };

            var diagnostics = InvokeSelectWithDiagnostics(candidates, new[] { "u1", "u2" });
            var selected = (IReadOnlyDictionary<string, SessionSnapshot>)
                diagnostics.GetType().GetProperty("Selected").GetValue(diagnostics);

            Assert.Equal(2, selected.Count);
            Assert.All(selected.Values, snapshot => Assert.Equal("itemA", snapshot.ItemId));
            Assert.Equal(
                new[] { "selected", "selected" },
                GetDispositions(diagnostics, "itemA"));
            Assert.Equal(
                new[] { "common-item-filtered", "common-item-filtered" },
                GetDispositions(diagnostics, "itemB"));
        }

        [Fact]
        public void SelectWithDiagnostics_EqualGlobalScoreMarksCommonCandidatesAmbiguous()
        {
            var candidates = new List<SessionSnapshot>
            {
                Snapshot("s1a", "u1", "itemA", activity: 1000),
                Snapshot("s1b", "u1", "itemB", activity: 1000),
                Snapshot("s2a", "u2", "itemA", activity: 1000),
                Snapshot("s2b", "u2", "itemB", activity: 1000),
            };

            var diagnostics = InvokeSelectWithDiagnostics(candidates, new[] { "u1", "u2" });
            var selected = (IReadOnlyDictionary<string, SessionSnapshot>)
                diagnostics.GetType().GetProperty("Selected").GetValue(diagnostics);

            Assert.Empty(selected);
            Assert.All(GetDispositions(diagnostics, "itemA"), disposition => Assert.Equal("ambiguous", disposition));
            Assert.All(GetDispositions(diagnostics, "itemB"), disposition => Assert.Equal("ambiguous", disposition));
        }

        [Fact]
        public void SelectWithPreviousDiagnostics_EqualTieReusesUniquePreviousIdentity()
        {
            var previous = Snapshot("s1", "u1", "itemA", activity: 1000);
            var candidates = new List<SessionSnapshot>
            {
                Snapshot("s2", "u1", "itemA", activity: 1000),
                previous,
            };

            var diagnostics = InvokeSelectWithPreviousDiagnostics(
                candidates,
                new[] { "u1" },
                new Dictionary<string, SessionSnapshot> { ["u1"] = previous });
            var selected = (IReadOnlyDictionary<string, SessionSnapshot>)
                diagnostics.GetType().GetProperty("Selected").GetValue(diagnostics);

            Assert.Equal("s1", selected["u1"].SessionId);
            Assert.Contains("selected", GetDispositions(diagnostics, "itemA"));
            Assert.Contains("previous-selection-filtered", GetDispositions(diagnostics, "itemA"));

            var reversedCandidates = new List<SessionSnapshot>(candidates);
            reversedCandidates.Reverse();
            diagnostics = InvokeSelectWithPreviousDiagnostics(
                reversedCandidates,
                new[] { "u1" },
                new Dictionary<string, SessionSnapshot> { ["u1"] = previous });
            selected = (IReadOnlyDictionary<string, SessionSnapshot>)
                diagnostics.GetType().GetProperty("Selected").GetValue(diagnostics);

            Assert.Equal("s1", selected["u1"].SessionId);
        }

        [Fact]
        public void SelectWithPreviousDiagnostics_DoesNotReuseExpiredOrDifferentItemIdentity()
        {
            var previous = Snapshot("s1", "u1", "itemA", activity: 1000);
            var expired = SnapshotAt("s1", "u1", "itemA", TestNow.AddSeconds(-61));
            var candidates = new List<SessionSnapshot>
            {
                expired,
                Snapshot("s2", "u1", "itemA", activity: 1000),
                Snapshot("s3", "u1", "itemA", activity: 1000),
            };

            var diagnostics = InvokeSelectWithPreviousDiagnostics(
                candidates,
                new[] { "u1" },
                new Dictionary<string, SessionSnapshot> { ["u1"] = previous });
            var selected = (IReadOnlyDictionary<string, SessionSnapshot>)
                diagnostics.GetType().GetProperty("Selected").GetValue(diagnostics);

            Assert.Empty(selected);
            Assert.Equal("expired", GetDisposition(diagnostics, "s1"));
            Assert.Equal("ambiguous", GetDisposition(diagnostics, "s2"));
            Assert.Equal("ambiguous", GetDisposition(diagnostics, "s3"));

            diagnostics = InvokeSelectWithPreviousDiagnostics(
                new[]
                {
                    Snapshot("s2", "u1", "itemB", activity: 1000),
                    Snapshot("s3", "u1", "itemB", activity: 1000),
                },
                new[] { "u1" },
                new Dictionary<string, SessionSnapshot> { ["u1"] = previous });
            selected = (IReadOnlyDictionary<string, SessionSnapshot>)
                diagnostics.GetType().GetProperty("Selected").GetValue(diagnostics);

            Assert.Empty(selected);
            Assert.All(GetDispositions(diagnostics, "itemB"), disposition => Assert.Equal("ambiguous", disposition));

            diagnostics = InvokeSelectWithPreviousDiagnostics(
                new[]
                {
                    Snapshot("s2", "u1", "itemA", activity: 1000),
                    Snapshot("s3", "u1", "itemA", activity: 1000),
                },
                new[] { "u1" },
                new Dictionary<string, SessionSnapshot> { ["u1"] = previous });
            selected = (IReadOnlyDictionary<string, SessionSnapshot>)
                diagnostics.GetType().GetProperty("Selected").GetValue(diagnostics);

            Assert.Empty(selected);
            Assert.All(GetDispositions(diagnostics, "itemA"), disposition => Assert.Equal("ambiguous", disposition));
        }

        [Fact]
        public void Select_RemovesAbsolutelyExpiredCommonCandidateBeforePreferringCommonItem()
        {
            var candidates = new List<SessionSnapshot>
            {
                SnapshotAt("s1-stale", "u1", "itemA", TestNow.AddSeconds(-61)),
                SnapshotAt("s1-fresh", "u1", "itemB", TestNow.AddSeconds(-1)),
                SnapshotAt("s2", "u2", "itemA", TestNow.AddSeconds(-1)),
            };

            var result = Select(candidates, new[] { "u1", "u2" });

            Assert.Equal("itemB", result["u1"].ItemId);
            Assert.Equal("itemA", result["u2"].ItemId);
        }

        [Fact]
        public void Select_RemovesCandidateLaggingBehindUsersLatestSessionBeforePreferringCommonItem()
        {
            var candidates = new List<SessionSnapshot>
            {
                SnapshotAt("s1-stale", "u1", "itemA", TestNow.AddSeconds(-17)),
                SnapshotAt("s1-fresh", "u1", "itemB", TestNow.AddSeconds(-1)),
                SnapshotAt("s2", "u2", "itemA", TestNow.AddSeconds(-1)),
            };

            var result = Select(candidates, new[] { "u1", "u2" });

            Assert.Equal("itemB", result["u1"].ItemId);
            Assert.Equal("itemA", result["u2"].ItemId);
        }

        private static Dictionary<string, SessionSnapshot> Select(
            IEnumerable<SessionSnapshot> candidates,
            IReadOnlyList<string> userIds)
        {
            return SessionSelector.Select(
                candidates,
                userIds,
                TestNow,
                TimeSpan.FromSeconds(SessionSelector.StaleSessionTimeoutSeconds));
        }

        private static object InvokeSelectWithDiagnostics(
            IEnumerable<SessionSnapshot> candidates,
            IReadOnlyList<string> userIds)
        {
            var method = typeof(SessionSelector).GetMethod(
                "SelectWithDiagnostics",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return method.Invoke(
                null,
                new object[]
                {
                    candidates,
                    userIds,
                    TestNow,
                    SessionSelector.StaleSessionTimeoutSeconds,
                });
        }

        private static object InvokeSelectWithPreviousDiagnostics(
            IEnumerable<SessionSnapshot> candidates,
            IReadOnlyList<string> userIds,
            IReadOnlyDictionary<string, SessionSnapshot> previous)
        {
            var method = typeof(SessionSelector).GetMethod(
                "SelectWithPreviousDiagnostics",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return method.Invoke(
                null,
                new object[]
                {
                    candidates,
                    userIds,
                    TestNow,
                    SessionSelector.StaleSessionTimeoutSeconds,
                    previous,
                });
        }

        private static List<string> GetDispositions(object diagnostics, string itemId)
        {
            var candidates = (IEnumerable)diagnostics.GetType().GetProperty("Candidates").GetValue(diagnostics);
            var dispositions = new List<string>();
            foreach (var candidate in candidates)
            {
                var snapshot = (SessionSnapshot)candidate.GetType().GetProperty("Snapshot").GetValue(candidate);
                if (string.Equals(snapshot.ItemId, itemId, StringComparison.OrdinalIgnoreCase))
                {
                    dispositions.Add((string)candidate.GetType().GetProperty("Disposition").GetValue(candidate));
                }
            }

            return dispositions;
        }

        private static string GetDisposition(object diagnostics, string sessionId)
        {
            var candidates = (IEnumerable)diagnostics.GetType().GetProperty("Candidates").GetValue(diagnostics);
            foreach (var candidate in candidates)
            {
                var snapshot = (SessionSnapshot)candidate.GetType().GetProperty("Snapshot").GetValue(candidate);
                if (string.Equals(snapshot.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
                {
                    return (string)candidate.GetType().GetProperty("Disposition").GetValue(candidate);
                }
            }

            return null;
        }

        private static SessionSnapshot Snapshot(
            string sessionId,
            string userId,
            string itemId,
            bool capable = true,
            string[] commands = null,
            long activity = 0)
        {
            return SnapshotAt(
                sessionId,
                userId,
                itemId,
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(activity),
                capable,
                commands);
        }

        private static SessionSnapshot SnapshotAt(
            string sessionId,
            string userId,
            string itemId,
            DateTimeOffset activity,
            bool capable = true,
            string[] commands = null)
        {
            var capabilities = new SessionCapabilityReport(capable, commands ?? new[] { "Pause", "Unpause", "Seek" });
            return new SessionSnapshot(
                sessionId, userId, itemId, "m1",
                0, 100 * SessionSnapshot.TicksPerSecond, false, 1.0, stopped: false,
                capable, capabilities,
                activity);
        }
    }
}

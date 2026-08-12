using System;
using System.Collections.Generic;
using System.Linq;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Selects one current session snapshot per room participant. Ported from the
    /// Python _select_sessions: prefers active, remotely-controllable and fresh
    /// records, isolates stale unknown records, prefers the single common item,
    /// and skips ambiguous ties.
    /// </summary>
    public static class SessionSelector
    {
        public const double StaleActivityGapSeconds = 5 * 60;
        public const double StaleSessionTimeoutSeconds = 60;
        public const double PerUserActivityGapSeconds = 15;

        public static Dictionary<string, SessionSnapshot> Select(
            IEnumerable<SessionSnapshot> candidates,
            IReadOnlyList<string> userIds,
            DateTimeOffset? now = null,
            double staleTimeoutSeconds = StaleSessionTimeoutSeconds)
        {
            return SelectCore(
                candidates,
                userIds,
                now,
                TimeSpan.FromSeconds(staleTimeoutSeconds),
                null);
        }

        public static Dictionary<string, SessionSnapshot> Select(
            IEnumerable<SessionSnapshot> candidates,
            IReadOnlyList<string> userIds,
            DateTimeOffset? now,
            TimeSpan staleTimeout)
        {
            return SelectCore(
                candidates,
                userIds,
                now,
                staleTimeout,
                null);
        }

        internal static SessionSelectionDiagnostics SelectWithDiagnostics(
            IEnumerable<SessionSnapshot> candidates,
            IReadOnlyList<string> userIds,
            DateTimeOffset? now = null,
            double staleTimeoutSeconds = StaleSessionTimeoutSeconds)
        {
            var diagnostics = new SessionSelectionDiagnostics();
            var selected = SelectCore(
                candidates,
                userIds,
                now,
                TimeSpan.FromSeconds(staleTimeoutSeconds),
                diagnostics);
            diagnostics.Selected = selected;
            return diagnostics;
        }

        private static Dictionary<string, SessionSnapshot> SelectCore(
            IEnumerable<SessionSnapshot> candidates,
            IReadOnlyList<string> userIds,
            DateTimeOffset? now,
            TimeSpan staleTimeout,
            SessionSelectionDiagnostics diagnostics)
        {
            var byUser = new Dictionary<string, List<SelectionCandidate>>(StringComparer.OrdinalIgnoreCase);
            var allCandidates = diagnostics == null
                ? null
                : new List<SelectionCandidate>();
            foreach (var userId in userIds ?? Array.Empty<string>())
            {
                byUser[userId] = new List<SelectionCandidate>();
            }

            foreach (var snapshot in candidates ?? Enumerable.Empty<SessionSnapshot>())
            {
                if (snapshot == null || snapshot.Online == false || !byUser.ContainsKey(snapshot.UserId))
                {
                    continue;
                }

                var candidate = new SelectionCandidate(snapshot);
                byUser[snapshot.UserId].Add(candidate);
                allCandidates?.Add(candidate);
            }

            if (diagnostics != null)
            {
                diagnostics.HasMultipleCandidates = byUser.Values.Any(values =>
                    values.Select(value => value.Snapshot.SessionId)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count() > 1);
            }

            if (now.HasValue)
            {
                RemoveExpired(byUser, now.Value, staleTimeout);
            }

            RemoveSessionsLaggingBehindUserLatest(byUser);
            PreferCommonItem(byUser);

            var selected = new Dictionary<string, SessionSnapshot>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in byUser)
            {
                var values = DeduplicateBySessionId(pair.Value);
                if (values.Count == 0)
                {
                    continue;
                }

                var maxKey = values.Select(v => SelectionKey(v.Snapshot)).Max();
                var latest = values.Where(v => SelectionKey(v.Snapshot) == maxKey).ToList();
                if (latest.Count != 1)
                {
                    // Ambiguous: multiple equally-fresh records; never guess.
                    foreach (var value in latest)
                    {
                        value.Disposition = "ambiguous";
                    }
                    continue;
                }

                latest[0].Disposition = "selected";
                selected[pair.Key] = latest[0].Snapshot;
            }

            if (diagnostics != null)
            {
                diagnostics.Candidates = allCandidates
                    .Select(candidate => new SessionSelectionCandidateDiagnostic
                    {
                        UserId = candidate.Snapshot.UserId,
                        Snapshot = candidate.Snapshot,
                        Disposition = candidate.Disposition ?? "lower-ranked",
                    })
                    .ToList();
            }

            return selected;
        }

        private static void RemoveExpired(
            Dictionary<string, List<SelectionCandidate>> byUser,
            DateTimeOffset now,
            TimeSpan staleTimeout)
        {
            DateTimeOffset cutoff = now - staleTimeout;
            foreach (var key in byUser.Keys.ToList())
            {
                byUser[key] = byUser[key]
                    .Where(s =>
                    {
                        if (s.Snapshot.LastActivityDateUtc == default || s.Snapshot.LastActivityDateUtc >= cutoff)
                        {
                            return true;
                        }

                        s.Disposition = "expired";
                        return false;
                    })
                    .ToList();
            }
        }

        private static void RemoveSessionsLaggingBehindUserLatest(
            Dictionary<string, List<SelectionCandidate>> byUser)
        {
            foreach (var key in byUser.Keys.ToList())
            {
                var values = byUser[key];
                if (values.Count == 0)
                {
                    continue;
                }

                DateTimeOffset latest = values.Max(s => s.Snapshot.LastActivityDateUtc);
                byUser[key] = values
                    .Where(s => s.Snapshot.LastActivityDateUtc == default ||
                                latest == default ||
                                (latest - s.Snapshot.LastActivityDateUtc).TotalSeconds <= PerUserActivityGapSeconds ||
                                MarkDisposition(s, "lagging"))
                    .ToList();
            }
        }

        private static void PreferCommonItem(Dictionary<string, List<SelectionCandidate>> byUser)
        {
            var itemSets = byUser
                .Where(p => p.Value.Count > 0)
                .Select(p => new HashSet<string>(
                    p.Value.Select(s => s.Snapshot.ItemId).Where(id => !string.IsNullOrEmpty(id)),
                    StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (itemSets.Count != byUser.Count || itemSets.Count == 0)
            {
                return;
            }

            HashSet<string> common = new HashSet<string>(itemSets[0], StringComparer.OrdinalIgnoreCase);
            foreach (var set in itemSets.Skip(1))
            {
                common.IntersectWith(set);
            }

            if (common.Count != 1)
            {
                return;
            }

            string commonItem = common.First();
            foreach (var key in byUser.Keys.ToList())
            {
                byUser[key] = byUser[key]
                    .Where(s =>
                    {
                        if (string.Equals(s.Snapshot.ItemId, commonItem, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                        s.Disposition = "common-item-filtered";
                        return false;
                    })
                    .ToList();
            }
        }

        private static List<SelectionCandidate> DeduplicateBySessionId(List<SelectionCandidate> values)
        {
            var bySession = new Dictionary<string, SelectionCandidate>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in values)
            {
                if (!bySession.TryGetValue(value.Snapshot.SessionId, out var previous))
                {
                    bySession[value.Snapshot.SessionId] = value;
                }
                else if (SelectionKey(value.Snapshot).CompareTo(SelectionKey(previous.Snapshot)) > 0)
                {
                    previous.Disposition = "lower-ranked";
                    bySession[value.Snapshot.SessionId] = value;
                }
                else
                {
                    value.Disposition = "lower-ranked";
                }
            }

            return bySession.Values.ToList();
        }

        private static bool MarkDisposition(SelectionCandidate candidate, string disposition)
        {
            candidate.Disposition = disposition;
            return false;
        }

        private static (int active, int capabilityRank, long activityTicks) SelectionKey(SessionSnapshot snapshot)
        {
            int active = snapshot.Online ? 1 : 0;
            int capabilityRank = snapshot.Capabilities != null && snapshot.Capabilities.SupportsRemoteControl
                ? 2
                : (snapshot.Capabilities != null && snapshot.Capabilities.SupportedCommands.Count > 0 ? 1 : 0);
            long activity = snapshot.LastActivityDateUtc.Ticks;
            return (active, capabilityRank, activity);
        }

        private sealed class SelectionCandidate
        {
            public SelectionCandidate(SessionSnapshot snapshot)
            {
                Snapshot = snapshot;
            }

            public SessionSnapshot Snapshot { get; }

            public string Disposition { get; set; }
        }
    }
}

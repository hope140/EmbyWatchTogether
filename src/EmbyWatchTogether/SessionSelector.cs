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

        public static Dictionary<string, SessionSnapshot> Select(
            IEnumerable<SessionSnapshot> candidates,
            IReadOnlyList<string> userIds)
        {
            var byUser = new Dictionary<string, List<SessionSnapshot>>(StringComparer.OrdinalIgnoreCase);
            foreach (var userId in userIds ?? Array.Empty<string>())
            {
                byUser[userId] = new List<SessionSnapshot>();
            }

            foreach (var snapshot in candidates ?? Enumerable.Empty<SessionSnapshot>())
            {
                if (snapshot == null || snapshot.Online == false || !byUser.ContainsKey(snapshot.UserId))
                {
                    continue;
                }

                byUser[snapshot.UserId].Add(snapshot);
            }

            IsolateStaleUnknown(byUser);
            PreferCommonItem(byUser);

            var selected = new Dictionary<string, SessionSnapshot>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in byUser)
            {
                var values = DeduplicateBySessionId(pair.Value);
                if (values.Count == 0)
                {
                    continue;
                }

                var maxKey = values.Select(SelectionKey).Max();
                var latest = values.Where(v => SelectionKey(v) == maxKey).ToList();
                if (latest.Count != 1)
                {
                    // Ambiguous: multiple equally-fresh records; never guess.
                    continue;
                }

                selected[pair.Key] = latest[0];
            }

            return selected;
        }

        private static void IsolateStaleUnknown(Dictionary<string, List<SessionSnapshot>> byUser)
        {
            var capableActivity = byUser.Values
                .SelectMany(v => v)
                .Where(s => s.Capabilities != null && s.Capabilities.SupportsRemoteControl)
                .Select(s => s.LastActivityDateUtc)
                .Where(t => t != default)
                .ToList();

            if (capableActivity.Count == 0)
            {
                return;
            }

            var freshest = capableActivity.Max();
            foreach (var key in byUser.Keys.ToList())
            {
                var retained = byUser[key]
                    .Where(s =>
                    {
                        bool capabilityKnown = s.Capabilities != null && s.Capabilities.SupportsRemoteControl;
                        if (capabilityKnown || s.LastActivityDateUtc == default)
                        {
                            return true;
                        }

                        return (freshest - s.LastActivityDateUtc).TotalSeconds <= StaleActivityGapSeconds;
                    })
                    .ToList();
                byUser[key] = retained;
            }
        }

        private static void PreferCommonItem(Dictionary<string, List<SessionSnapshot>> byUser)
        {
            var itemSets = byUser
                .Where(p => p.Value.Count > 0)
                .Select(p => new HashSet<string>(
                    p.Value.Select(s => s.ItemId).Where(id => !string.IsNullOrEmpty(id)),
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
                    .Where(s => string.Equals(s.ItemId, commonItem, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        private static List<SessionSnapshot> DeduplicateBySessionId(List<SessionSnapshot> values)
        {
            var bySession = new Dictionary<string, SessionSnapshot>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in values)
            {
                if (!bySession.TryGetValue(value.SessionId, out var previous) ||
                    SelectionKey(value).CompareTo(SelectionKey(previous)) > 0)
                {
                    bySession[value.SessionId] = value;
                }
            }

            return bySession.Values.ToList();
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
    }
}

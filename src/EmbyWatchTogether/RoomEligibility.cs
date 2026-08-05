using System;
using System.Collections.Generic;
using System.Linq;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Pure pair-eligibility decision ported from the Python _pair_is_eligible.
    /// </summary>
    public static class RoomEligibility
    {
        public static bool IsPairEligible(IReadOnlyDictionary<string, SessionSnapshot> snapshots)
        {
            if (snapshots == null || snapshots.Count != 2)
            {
                return false;
            }

            var values = snapshots.Values.Where(v => v != null).ToList();
            if (values.Count != 2)
            {
                return false;
            }

            if (values.Any(v => !v.Online || v.Stopped))
            {
                return false;
            }

            if (string.IsNullOrEmpty(values[0].ItemId) ||
                !string.Equals(values[0].ItemId, values[1].ItemId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (values.Any(v => v.RunTimeTicks <= 0))
            {
                return false;
            }

            if (Math.Abs(values[0].RunTimeTicks - values[1].RunTimeTicks) > SyncConstants.MaxRuntimeDifferenceTicks)
            {
                return false;
            }

            return values.All(v =>
                Math.Abs(v.PlaybackRate - 1.0) <= SyncConstants.PlaybackRateTolerance);
        }
    }
}

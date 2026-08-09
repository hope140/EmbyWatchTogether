using System;
using System.Collections.Generic;
using System.Linq;

namespace Emby.Plugins.WatchTogether
{
    internal enum RoomEligibilityFailureReason
    {
        None,
        SnapshotCount,
        MissingSnapshot,
        NullSnapshot,
        OfflineOrStopped,
        RemoteControlUnsupportedOrMismatch,
        EmptyOrDifferentItem,
        InvalidOrDifferentRuntime,
        PlaybackRateNotOne,
    }

    internal sealed class RoomEligibilityEvaluation
    {
        public bool IsEligible { get; set; }

        public RoomEligibilityFailureReason FailureReason { get; set; }
    }

    /// <summary>
    /// Pure pair-eligibility decision ported from the Python _pair_is_eligible.
    /// </summary>
    public static class RoomEligibility
    {
        public static bool IsPairEligible(IReadOnlyDictionary<string, SessionSnapshot> snapshots)
        {
            return Evaluate(snapshots).IsEligible;
        }

        internal static RoomEligibilityEvaluation Evaluate(
            IReadOnlyDictionary<string, SessionSnapshot> snapshots)
        {
            if (snapshots == null || snapshots.Count > 2)
            {
                return Failure(RoomEligibilityFailureReason.SnapshotCount);
            }

            if (snapshots.Count < 2)
            {
                return Failure(RoomEligibilityFailureReason.MissingSnapshot);
            }

            var values = snapshots.Values.Where(v => v != null).ToList();
            if (values.Count != snapshots.Count)
            {
                return Failure(RoomEligibilityFailureReason.NullSnapshot);
            }

            if (values.Count != 2)
            {
                return Failure(RoomEligibilityFailureReason.MissingSnapshot);
            }

            if (values.Any(v => !v.Online || v.Stopped))
            {
                return Failure(RoomEligibilityFailureReason.OfflineOrStopped);
            }

            if (values[0].SupportsRemoteControl != values[1].SupportsRemoteControl ||
                !values[0].SupportsRemoteControl)
            {
                return Failure(RoomEligibilityFailureReason.RemoteControlUnsupportedOrMismatch);
            }

            if (string.IsNullOrEmpty(values[0].ItemId) ||
                !string.Equals(values[0].ItemId, values[1].ItemId, StringComparison.OrdinalIgnoreCase))
            {
                return Failure(RoomEligibilityFailureReason.EmptyOrDifferentItem);
            }

            if (values.Any(v => v.RunTimeTicks <= 0))
            {
                return Failure(RoomEligibilityFailureReason.InvalidOrDifferentRuntime);
            }

            if (Math.Abs(values[0].RunTimeTicks - values[1].RunTimeTicks) > SyncConstants.MaxRuntimeDifferenceTicks)
            {
                return Failure(RoomEligibilityFailureReason.InvalidOrDifferentRuntime);
            }

            if (!values.All(v =>
                Math.Abs(v.PlaybackRate - 1.0) <= SyncConstants.PlaybackRateTolerance))
            {
                return Failure(RoomEligibilityFailureReason.PlaybackRateNotOne);
            }

            return new RoomEligibilityEvaluation
            {
                IsEligible = true,
                FailureReason = RoomEligibilityFailureReason.None,
            };
        }

        private static RoomEligibilityEvaluation Failure(RoomEligibilityFailureReason reason)
        {
            return new RoomEligibilityEvaluation
            {
                IsEligible = false,
                FailureReason = reason,
            };
        }
    }
}

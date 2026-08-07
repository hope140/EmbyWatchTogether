using System;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Room lifecycle states ported from the Python reference coordinator:
    /// waiting (idle/error recovery), barrier (pause-&gt;seek-&gt;restore),
    /// watching (active sync), unavailable (server mismatch).
    /// </summary>
    public enum RoomState
    {
        Waiting = 0,
        Barrier = 1,
        Watching = 2,
        Unavailable = 3,
    }

    public enum BarrierStage
    {
        Pause = 0,
        Seek = 1,
        Restore = 2,
        FinalAlign = 3,
    }

    public sealed class BarrierState
    {
        public BarrierStage Stage { get; set; }

        public DateTimeOffset StartedAtUtc { get; set; }

        public long PrimaryPositionTicks { get; set; }

        public bool PrimaryPaused { get; set; }

        public string ItemId { get; set; }

        public bool PauseSent { get; set; }

        public bool SeekSent { get; set; }

        public bool RestoreSent { get; set; }

        public bool FinalAlignSent { get; set; }

        public long FinalAlignPositionTicks { get; set; }
    }

    public sealed class PendingCommand
    {
        public string UserId { get; set; }

        public string Command { get; set; }

        public long? PositionTicks { get; set; }

        public DateTimeOffset IssuedAtUtc { get; set; }

        public int Retries { get; set; }
    }

    public sealed class SuppressedCommand
    {
        public string Command { get; set; }

        public long? PositionTicks { get; set; }

        public DateTimeOffset UntilUtc { get; set; }
    }

    /// <summary>
    /// Tracks "wait for the slow side" after a manual seek was propagated.
    /// Whichever side is still loading at the seek target is the one we wait
    /// for; the leading side is paused until the stuck side actually starts
    /// playing again.
    /// </summary>
    public sealed class RealignState
    {
        public string SeekerUserId { get; set; }

        public long AnchorPositionTicks { get; set; }

        public string PausedUserId { get; set; }

        public DateTimeOffset? PauseSentAtUtc { get; set; }

        public bool TimeoutAdvisorySent { get; set; }

        public DateTimeOffset StartedAtUtc { get; set; }
    }
}

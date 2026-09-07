using System;
using System.Collections.Generic;
using System.Linq;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Mutable per-room runtime state held in memory (persisted room metadata is
    /// the Room entity; runtime is rebuilt on restart).
    /// </summary>
    public sealed class RoomRuntime
    {
        public RoomState State { get; set; } = RoomState.Waiting;

        /// <summary>
        /// Set when the session snapshot provider has been unavailable long
        /// enough that the room can no longer safely use its in-memory sync
        /// state. This is deliberately separate from <see cref="Error"/> so
        /// command failures remain visible to callers.
        /// </summary>
        public bool SnapshotUnavailable { get; private set; }

        internal bool SnapshotRecoveryPending { get; private set; }

        internal string SnapshotErrorBeforeProtection { get; private set; }

        public string Error { get; set; }

        internal DateTimeOffset? ParticipantResyncRequestedAtUtc { get; set; }

        internal RoomEligibilityFailureReason? LastEligibilityFailureReason { get; set; }

        internal string LastMultipleSessionDiagnosticSignature { get; set; }

        public Dictionary<string, PendingCommand> Pending { get; } = new Dictionary<string, PendingCommand>();

        public Dictionary<string, WaitingPauseRetryState> WaitingPauseRetries { get; } =
            new Dictionary<string, WaitingPauseRetryState>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, SuppressedCommand> Suppressed { get; } = new Dictionary<string, SuppressedCommand>();

        /// <summary>
        /// Rolling exponential moving average (seconds) of the time between a
        /// remote command being issued and its acknowledgement appearing in a
        /// SessionInfo snapshot, per user. Used to raise the manual-seek
        /// detection threshold for clients whose snapshots lag. Retained across
        /// resets because it describes the client, not the room state.
        /// </summary>
        public Dictionary<string, double> AckLatencySeconds { get; } =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Pending pause-alignment targets per user (see <see cref="PauseAlignState"/>).
        /// </summary>
        public Dictionary<string, PauseAlignState> PauseAlign { get; } =
            new Dictionary<string, PauseAlignState>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Last time a Seek command was issued to each user. Used to ignore the
        /// small position rewind some players report shortly after a remote seek
        /// lands (clock re-basing) without ignoring real user seeks.
        /// </summary>
        public Dictionary<string, DateTimeOffset> LastSeekAtUtc { get; } =
            new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, SessionSnapshot> Previous { get; } = new Dictionary<string, SessionSnapshot>();

        public DateTimeOffset? PreviousAtUtc { get; set; }

        // Transient in-memory grace period for a Watching room whose raw
        // remote-control flag briefly drops while effective capability evidence
        // remains present. This does not assert that a live WebSocket is usable.
        // SyncEngine binds this state to session/item identities.
        internal DateTimeOffset? RemoteControlRecoveryStartedAtUtc { get; private set; }

        internal string RemoteControlRecoverySignature { get; private set; }

        private readonly HashSet<string> _remoteControlRecoveryAffectedUserIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal IReadOnlyList<string> RemoteControlRecoveryAffectedUserIds
        {
            get { return _remoteControlRecoveryAffectedUserIds.ToList(); }
        }

        public DateTimeOffset? MissingSessionSinceUtc { get; set; }

        public int DriftRounds { get; set; }

        public string SyncItemId { get; set; }

        public BarrierState Barrier { get; set; }

        public DateTimeOffset? BarrierRetryAtUtc { get; set; }

        private readonly object _diagnosticsLock = new object();
        private readonly Dictionary<string, SessionSnapshot> _diagnosticSnapshots =
            new Dictionary<string, SessionSnapshot>(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<SyncDiagnosticEventRecord> _diagnosticEvents =
            new LinkedList<SyncDiagnosticEventRecord>();
        private DateTimeOffset? _diagnosticSnapshotsAtUtc;
        private SyncDiagnosticActionRecord _diagnosticLastAction;

        internal void RecordDiagnosticSnapshots(
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            DateTimeOffset observedAtUtc)
        {
            lock (_diagnosticsLock)
            {
                _diagnosticSnapshots.Clear();
                if (snapshots != null)
                {
                    foreach (var pair in snapshots)
                    {
                        if (!string.IsNullOrEmpty(pair.Key) && pair.Value != null)
                        {
                            _diagnosticSnapshots[pair.Key] = CopySnapshot(pair.Value);
                        }
                    }
                }
                _diagnosticSnapshotsAtUtc = observedAtUtc;
            }
        }

        internal void RecordDiagnosticEvent(
            string type,
            string userId,
            string command,
            string result,
            long? positionTicks,
            double? latencySeconds,
            DateTimeOffset atUtc)
        {
            if (string.IsNullOrWhiteSpace(type)) return;
            var record = new SyncDiagnosticEventRecord
            {
                Type = type.Trim(), UserId = userId, Command = SyncDiagnostics.NormalizeCommand(command),
                Result = SyncDiagnostics.NormalizeResult(result), PositionTicks = positionTicks,
                LatencySeconds = latencySeconds, AtUtc = atUtc,
            };
            lock (_diagnosticsLock)
            {
                var last = _diagnosticEvents.Last?.Value;
                if (last != null && string.Equals(EventSignature(last), EventSignature(record), StringComparison.Ordinal))
                {
                    return;
                }
                _diagnosticEvents.AddLast(record);
                while (_diagnosticEvents.Count > SyncDiagnostics.MaxEvents)
                {
                    _diagnosticEvents.RemoveFirst();
                }
                _diagnosticLastAction = new SyncDiagnosticActionRecord
                {
                    Type = record.Type, UserId = record.UserId, Command = record.Command,
                    Result = record.Result, PositionTicks = record.PositionTicks,
                    LatencySeconds = record.LatencySeconds, AtUtc = record.AtUtc,
                };
            }
        }

        internal void RecordDiagnosticAction(
            string type,
            string userId,
            string command,
            string result,
            long? positionTicks,
            double? latencySeconds,
            DateTimeOffset atUtc)
        {
            RecordDiagnosticEvent(type, userId, command, result, positionTicks, latencySeconds, atUtc);
        }

        internal SyncDiagnosticsRuntimeSnapshot CaptureDiagnostics()
        {
            lock (_diagnosticsLock)
            {
                return new SyncDiagnosticsRuntimeSnapshot
                {
                    Snapshots = _diagnosticSnapshots.ToDictionary(
                        pair => pair.Key,
                        pair => CopySnapshot(pair.Value),
                        StringComparer.OrdinalIgnoreCase),
                    SnapshotsAtUtc = _diagnosticSnapshotsAtUtc,
                    Events = _diagnosticEvents.Select(CopyEvent).ToList(),
                    LastAction = CopyAction(_diagnosticLastAction),
                };
            }
        }

        private static string EventSignature(SyncDiagnosticEventRecord record)
        {
            return string.Join("|", record.Type, record.UserId, record.Command, record.Result,
                record.PositionTicks?.ToString() ?? "", record.LatencySeconds?.ToString("0.###") ?? "");
        }

        private static SessionSnapshot CopySnapshot(SessionSnapshot source)
        {
            if (source == null) return null;
            return new SessionSnapshot(source.SessionId, source.UserId, source.ItemId, source.MediaSourceId,
                source.PositionTicks, source.RunTimeTicks, source.IsPaused, source.PlaybackRate,
                source.Stopped, source.SupportsRemoteControl,
                new SessionCapabilityReport(source.Capabilities?.SupportsRemoteControl == true,
                    source.Capabilities?.SupportedCommands ?? Array.Empty<string>()),
                source.LastActivityDateUtc);
        }

        private static SyncDiagnosticEventRecord CopyEvent(SyncDiagnosticEventRecord source)
        {
            return new SyncDiagnosticEventRecord
            {
                Type = source.Type, UserId = source.UserId, Command = source.Command, Result = source.Result,
                PositionTicks = source.PositionTicks, LatencySeconds = source.LatencySeconds, AtUtc = source.AtUtc,
            };
        }

        private static SyncDiagnosticActionRecord CopyAction(SyncDiagnosticActionRecord source)
        {
            return source == null ? null : new SyncDiagnosticActionRecord
            {
                Type = source.Type, UserId = source.UserId, Command = source.Command, Result = source.Result,
                PositionTicks = source.PositionTicks, LatencySeconds = source.LatencySeconds, AtUtc = source.AtUtc,
            };
        }

        internal void EnterSnapshotUnavailableProtection()
        {
            if (!SnapshotUnavailable)
            {
                SnapshotErrorBeforeProtection = Error;
            }
            SnapshotUnavailable = true;
            SnapshotRecoveryPending = false;
            State = RoomState.Waiting;
            Barrier = null;
            Pending.Clear();
            WaitingPauseRetries.Clear();
            Suppressed.Clear();
            PauseAlign.Clear();
            LastSeekAtUtc.Clear();
            Previous.Clear();
            PreviousAtUtc = null;
            MissingSessionSinceUtc = null;
            DriftRounds = 0;
            SyncItemId = null;
            BarrierRetryAtUtc = null;
            ClearRemoteControlRecovery();
        }

        internal void ExitSnapshotUnavailableProtection()
        {
            SnapshotUnavailable = false;
            SnapshotRecoveryPending = true;
        }

        internal bool ConsumeSnapshotRecoveryError()
        {
            if (!SnapshotRecoveryPending)
            {
                return false;
            }

            SnapshotRecoveryPending = false;
            if (!string.IsNullOrEmpty(SnapshotErrorBeforeProtection) &&
                string.Equals(Error, SnapshotErrorBeforeProtection, StringComparison.Ordinal))
            {
                Error = null;
            }

            SnapshotErrorBeforeProtection = null;
            return true;
        }

        public void ResetToWaiting()
        {
            bool preserveStopIdentity = MissingSessionSinceUtc.HasValue;
            State = RoomState.Waiting;
            Error = null;
            Barrier = null;
            Pending.Clear();
            WaitingPauseRetries.Clear();
            Suppressed.Clear();
            PauseAlign.Clear();
            LastSeekAtUtc.Clear();
            if (!preserveStopIdentity)
            {
                Previous.Clear();
                PreviousAtUtc = null;
            }
            DriftRounds = 0;
            if (!preserveStopIdentity)
            {
                SyncItemId = null;
            }
            BarrierRetryAtUtc = null;
            ClearRemoteControlRecovery();
        }

        internal void StartRemoteControlRecovery(
            DateTimeOffset startedAtUtc,
            string signature,
            IEnumerable<string> affectedUserIds)
        {
            RemoteControlRecoveryStartedAtUtc = startedAtUtc;
            RemoteControlRecoverySignature = signature;
            _remoteControlRecoveryAffectedUserIds.Clear();
            if (affectedUserIds != null)
            {
                foreach (var userId in affectedUserIds.Where(id => !string.IsNullOrWhiteSpace(id)))
                {
                    _remoteControlRecoveryAffectedUserIds.Add(userId);
                }
            }
        }

        internal void ClearRemoteControlRecovery()
        {
            RemoteControlRecoveryStartedAtUtc = null;
            RemoteControlRecoverySignature = null;
            _remoteControlRecoveryAffectedUserIds.Clear();
        }
    }
}

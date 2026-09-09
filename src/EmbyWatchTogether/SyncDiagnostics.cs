using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// A bounded, read-only view of one room's synchronization state. The DTOs in
    /// this file are deliberately separate from Room and RoomRuntime so that a
    /// diagnostics response cannot accidentally serialize a token, user id or
    /// exception object.
    /// </summary>
    public sealed class RoomDiagnostics
    {
        public string SchemaVersion { get; set; }
        public string PluginVersion { get; set; }
        public string RoomHash { get; set; }
        public string ServerHash { get; set; }
        public string RoomState { get; set; }
        public string StatusReason { get; set; }
        public string SnapshotHealth { get; set; }
        public string SyncItemHash { get; set; }
        public IReadOnlyList<RoomDiagnosticParticipant> Participants { get; set; }
        public IReadOnlyList<RoomDiagnosticSession> Sessions { get; set; }
        public IReadOnlyList<RoomDiagnosticSession> Snapshots { get; set; }
        public IReadOnlyList<RoomDiagnosticPending> Pending { get; set; }
        public RoomDiagnosticBarrier Barrier { get; set; }
        public RoomDiagnosticRecovery RecoveryWindow { get; set; }
        public RoomDiagnosticAction LastAction { get; set; }
        public string LastError { get; set; }
        public IReadOnlyList<RoomDiagnosticEvent> Events { get; set; }
        public DateTimeOffset GeneratedAtUtc { get; set; }
    }

    public sealed class RoomDiagnosticParticipant
    {
        public string Alias { get; set; }
        public bool IsPrimary { get; set; }
        public bool Joined { get; set; }
    }

    public sealed class RoomDiagnosticSession
    {
        public string Alias { get; set; }
        public string SessionHash { get; set; }
        public string ItemHash { get; set; }
        public double PositionSeconds { get; set; }
        public long PositionTicks { get; set; }
        public bool Paused { get; set; }
        public bool Online { get; set; }
        public double PlaybackRate { get; set; }
        public double? RuntimeSeconds { get; set; }
        public double? LastActivityAgeSeconds { get; set; }
        public double? AckLatencySeconds { get; set; }
        public bool ReportedSupportsRemoteControl { get; set; }
        public bool EffectiveSupportsRemoteControl { get; set; }
        public bool CanPause { get; set; }
        public bool CanUnpause { get; set; }
        public bool CanSeek { get; set; }
        public bool CanDisplayMessage { get; set; }
        public IReadOnlyList<string> SupportedCommandNames { get; set; }
    }

    public sealed class RoomDiagnosticPending
    {
        public string Alias { get; set; }
        public string Command { get; set; }
        public long? PositionTicks { get; set; }
        public double AgeSeconds { get; set; }
        public int Retries { get; set; }
        public string SessionHash { get; set; }
        public string ItemHash { get; set; }
    }

    public sealed class RoomDiagnosticBarrier
    {
        public string Stage { get; set; }
        public string AnchorAlias { get; set; }
        public long TargetPositionTicks { get; set; }
        public double TargetPositionSeconds { get; set; }
        public string ItemHash { get; set; }
        public double AgeSeconds { get; set; }
        public bool PauseSent { get; set; }
        public bool SeekSent { get; set; }
        public bool RestoreSent { get; set; }
        public bool SeekRetryPending { get; set; }
    }

    public sealed class RoomDiagnosticRecovery
    {
        public bool Active { get; set; }
        public double? AgeSeconds { get; set; }
        public IReadOnlyList<string> AffectedAliases { get; set; }
    }

    public sealed class RoomDiagnosticAction
    {
        public string Type { get; set; }
        public string Command { get; set; }
        public string Alias { get; set; }
        public string Result { get; set; }
        public long? PositionTicks { get; set; }
        public double? LatencySeconds { get; set; }
        public DateTimeOffset AtUtc { get; set; }
    }

    public sealed class RoomDiagnosticEvent
    {
        public string Type { get; set; }
        public string Command { get; set; }
        public string Alias { get; set; }
        public string Result { get; set; }
        public long? PositionTicks { get; set; }
        public double? LatencySeconds { get; set; }
        public DateTimeOffset AtUtc { get; set; }
    }

    internal sealed class SyncDiagnosticEventRecord
    {
        public string Type { get; set; }
        public string UserId { get; set; }
        public string Command { get; set; }
        public string Result { get; set; }
        public long? PositionTicks { get; set; }
        public double? LatencySeconds { get; set; }
        public DateTimeOffset AtUtc { get; set; }
    }

    internal sealed class SyncDiagnosticActionRecord
    {
        public string Type { get; set; }
        public string UserId { get; set; }
        public string Command { get; set; }
        public string Result { get; set; }
        public long? PositionTicks { get; set; }
        public double? LatencySeconds { get; set; }
        public DateTimeOffset AtUtc { get; set; }
    }

    internal sealed class SyncDiagnosticsRuntimeSnapshot
    {
        public Dictionary<string, SessionSnapshot> Snapshots { get; set; }
        public DateTimeOffset? SnapshotsAtUtc { get; set; }
        public List<SyncDiagnosticEventRecord> Events { get; set; }
        public SyncDiagnosticActionRecord LastAction { get; set; }
    }

    /// <summary>
    /// Hashing, aliasing and allow-list conversion for the diagnostics endpoint.
    /// This class has no Emby or command side effects.
    /// </summary>
    public static class SyncDiagnostics
    {
        public const string SchemaVersion = "1";
        public const int MaxEvents = 500;
        private const double SnapshotFreshnessSeconds = 10;
        private const int HashLength = 12;

        private static readonly HashSet<string> AllowedCommands = new HashSet<string>(
            new[] { RemoteCommands.Pause, RemoteCommands.Unpause, RemoteCommands.PlayPause,
                RemoteCommands.Seek, RemoteCommands.Stop, RemoteCommands.DisplayMessage },
            StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> AllowedEventTypes = new HashSet<string>(
            new[] { "barrier_started", "barrier_stage_changed", "entered_watching", "command_issued",
                "command_acknowledged", "command_failed", "retry_scheduled", "stop_confirmed",
                "snapshot_protection_entered", "snapshot_protection_recovered", "eligibility_changed",
                "manual_action", "resync" }, StringComparer.OrdinalIgnoreCase);

        public static string Hash(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) builder.Append(b.ToString("x2"));
                return builder.ToString(0, HashLength);
            }
        }

        public static string AliasFor(Room room, string userId)
        {
            if (room == null || string.IsNullOrWhiteSpace(userId)) return "unknown";
            if (string.Equals(userId, room.PrimaryUserId, StringComparison.OrdinalIgnoreCase)) return "userA";
            return room.ParticipantUserIds != null && room.ParticipantUserIds.Any(u =>
                string.Equals(u, userId, StringComparison.OrdinalIgnoreCase)) ? "userB" : "unknown";
        }

        internal static string NormalizeCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            foreach (var allowed in AllowedCommands)
            {
                if (string.Equals(allowed, command, StringComparison.OrdinalIgnoreCase)) return allowed;
            }
            return null;
        }

        internal static string NormalizeEventType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return "observed";
            foreach (var allowed in AllowedEventTypes)
            {
                if (string.Equals(allowed, type.Trim(), StringComparison.OrdinalIgnoreCase)) return allowed;
            }
            return "observed";
        }

        internal static string NormalizeResult(string result)
        {
            if (string.IsNullOrWhiteSpace(result)) return null;
            switch (result.Trim().ToLowerInvariant())
            {
                case "ok": case "success": case "acknowledged": return "success";
                case "failed": case "failure": case "error": return "failed";
                case "retry": case "retry_scheduled": return "retry_scheduled";
                case "pending": return "pending";
                case "stopped": return "stopped";
                case "entered": return "entered";
                case "recovered": return "recovered";
                case "changed": return "changed";
                default: return "observed";
            }
        }

        public static RoomDiagnostics Build(
            Room room,
            RoomRuntime runtime,
            string currentServerId,
            string pluginVersion,
            string statusReason,
            DateTimeOffset now)
        {
            if (room == null || runtime == null) return null;
            var state = runtime.CaptureDiagnostics();
            var aliases = (room.ParticipantUserIds ?? Array.Empty<string>())
                .Take(2).Select(userId => new RoomDiagnosticParticipant
                {
                    Alias = AliasFor(room, userId),
                    IsPrimary = string.Equals(userId, room.PrimaryUserId, StringComparison.OrdinalIgnoreCase),
                    Joined = room.IsJoined(userId),
                }).ToList();

            var selected = (state.Snapshots ?? new Dictionary<string, SessionSnapshot>())
                .Where(p => p.Value != null && room.HasParticipant(p.Key))
                .Select(p => ToSession(room, runtime, p.Key, p.Value, now)).ToList();
            var selectedByUser = new Dictionary<string, RoomDiagnosticSession>(StringComparer.OrdinalIgnoreCase);
            foreach (var session in selected)
            {
                selectedByUser[session.Alias] = session;
            }
            var allSessions = (room.ParticipantUserIds ?? Array.Empty<string>())
                .Take(2).Select(userId =>
                {
                    var alias = AliasFor(room, userId);
                    return selectedByUser.TryGetValue(alias, out var session)
                        ? session
                        : MissingSession(room, runtime, userId);
                }).ToList();
            var pending = runtime.Pending.ToList().Take(2).Select(p => ToPending(room, p.Key, p.Value, now)).ToList();
            var barrier = ToBarrier(room, runtime.Barrier, now);
            var recovery = new RoomDiagnosticRecovery
            {
                Active = runtime.RemoteControlRecoveryStartedAtUtc.HasValue,
                AgeSeconds = runtime.RemoteControlRecoveryStartedAtUtc.HasValue
                    ? (double?)Math.Max(0, (now - runtime.RemoteControlRecoveryStartedAtUtc.Value).TotalSeconds) : null,
                AffectedAliases = runtime.RemoteControlRecoveryStartedAtUtc.HasValue
                    ? runtime.RemoteControlRecoveryAffectedUserIds
                        .Select(userId => AliasFor(room, userId))
                        .Where(alias => alias != "unknown")
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                    : Array.Empty<string>(),
            };

            return new RoomDiagnostics
            {
                SchemaVersion = SchemaVersion,
                PluginVersion = pluginVersion,
                RoomHash = Hash(room.Id),
                ServerHash = Hash(currentServerId),
                RoomState = runtime.State.ToString(),
                StatusReason = statusReason,
                SnapshotHealth = runtime.SnapshotUnavailable ? "unavailable" :
                    !state.SnapshotsAtUtc.HasValue ? "unknown" :
                    (now - state.SnapshotsAtUtc.Value).TotalSeconds > SnapshotFreshnessSeconds ? "stale" : "fresh",
                SyncItemHash = Hash(runtime.SyncItemId),
                Participants = aliases,
                Sessions = allSessions,
                Snapshots = allSessions,
                Pending = pending,
                Barrier = barrier,
                RecoveryWindow = recovery,
                LastAction = ToAction(room, state.LastAction),
                LastError = PublicError(runtime.Error),
                Events = (state.Events ?? new List<SyncDiagnosticEventRecord>())
                    .Select(e => ToEvent(room, e)).ToList(),
                GeneratedAtUtc = now,
            };
        }

        private static RoomDiagnosticSession ToSession(Room room, RoomRuntime runtime, string userId, SessionSnapshot s, DateTimeOffset now)
        {
            var caps = s.Capabilities;
            return new RoomDiagnosticSession
            {
                Alias = AliasFor(room, userId),
                SessionHash = Hash(s.SessionId), ItemHash = Hash(s.ItemId),
                PositionTicks = s.PositionTicks, PositionSeconds = s.PositionSeconds,
                Paused = s.IsPaused, Online = s.Online, PlaybackRate = s.PlaybackRate,
                RuntimeSeconds = s.RunTimeTicks > 0 ? (double?)s.RunTimeTicks / SessionSnapshot.TicksPerSecond : null,
                LastActivityAgeSeconds = s.LastActivityDateUtc == default(DateTimeOffset)
                    ? null : (double?)Math.Max(0, (now - s.LastActivityDateUtc).TotalSeconds),
                AckLatencySeconds = runtime.AckLatencySeconds.TryGetValue(userId, out var latency)
                    ? (double?)Math.Max(0, latency) : null,
                ReportedSupportsRemoteControl = s.SupportsRemoteControl,
                EffectiveSupportsRemoteControl = caps?.SupportsRemoteControl == true,
                CanPause = caps?.CanPause == true, CanUnpause = caps?.CanUnpause == true,
                CanSeek = caps?.CanSeek == true, CanDisplayMessage = caps?.CanDisplayMessage == true,
                SupportedCommandNames = (caps?.SupportedCommands ?? Array.Empty<string>())
                    .Select(NormalizeCommand).Where(c => c != null).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList(),
            };
        }

        private static RoomDiagnosticSession MissingSession(Room room, RoomRuntime runtime, string userId)
        {
            return new RoomDiagnosticSession
            {
                Alias = AliasFor(room, userId), Online = false,
                SupportedCommandNames = Array.Empty<string>(),
                AckLatencySeconds = runtime.AckLatencySeconds.TryGetValue(userId, out var latency)
                    ? (double?)Math.Max(0, latency) : null,
            };
        }

        private static RoomDiagnosticPending ToPending(Room room, string userId, PendingCommand p, DateTimeOffset now)
        {
            if (p == null) return null;
            return new RoomDiagnosticPending
            {
                Alias = AliasFor(room, userId), Command = NormalizeCommand(p.Command) ?? "unknown",
                PositionTicks = p.PositionTicks, AgeSeconds = Math.Max(0, (now - p.IssuedAtUtc).TotalSeconds),
                Retries = Math.Max(0, p.Retries), SessionHash = Hash(p.SessionId), ItemHash = Hash(p.ItemId),
            };
        }

        private static RoomDiagnosticBarrier ToBarrier(Room room, BarrierState b, DateTimeOffset now)
        {
            if (b == null) return null;
            return new RoomDiagnosticBarrier
            {
                Stage = b.Stage.ToString(), AnchorAlias = AliasFor(room, b.AnchorUserId),
                TargetPositionTicks = b.PrimaryPositionTicks,
                TargetPositionSeconds = b.PrimaryPositionTicks / (double)SessionSnapshot.TicksPerSecond,
                ItemHash = Hash(b.ItemId), AgeSeconds = Math.Max(0, (now - b.StartedAtUtc).TotalSeconds),
                PauseSent = b.PauseSent, SeekSent = b.SeekSent, RestoreSent = b.RestoreSent,
                SeekRetryPending = b.SeekRetryAtUtc.HasValue,
            };
        }

        private static RoomDiagnosticAction ToAction(Room room, SyncDiagnosticActionRecord a)
        {
            if (a == null) return null;
            return new RoomDiagnosticAction
            {
                Type = NormalizeEventType(a.Type), Command = NormalizeCommand(a.Command), Alias = AliasFor(room, a.UserId),
                Result = NormalizeResult(a.Result), PositionTicks = a.PositionTicks,
                LatencySeconds = a.LatencySeconds, AtUtc = a.AtUtc,
            };
        }

        private static RoomDiagnosticEvent ToEvent(Room room, SyncDiagnosticEventRecord e)
        {
            return new RoomDiagnosticEvent
            {
                Type = NormalizeEventType(e.Type), Command = NormalizeCommand(e.Command), Alias = AliasFor(room, e.UserId),
                Result = NormalizeResult(e.Result), PositionTicks = e.PositionTicks,
                LatencySeconds = e.LatencySeconds, AtUtc = e.AtUtc,
            };
        }

        private static string PublicError(string error)
        {
            if (string.IsNullOrEmpty(error)) return null;
            switch (error)
            {
                case "room server is unavailable": return "server_unavailable";
                case "manual action conflicts with active synchronization": return "action_conflict";
                case "两位参与者打开了不同视频，暂不发送同步指令": return "different_video";
                case "播放已停止，等待双方重新打开同一视频": return "playback_stopped";
                case "barrier seek retry budget exhausted": return "barrier_retry_exhausted";
                case "waiting pause retry limit reached": return "waiting_pause_retry_limit";
                default: return "command_failed";
            }
        }
    }
}

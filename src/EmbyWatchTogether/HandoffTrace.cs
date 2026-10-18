using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MediaBrowser.Model.Logging;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Bounded, fail-open diagnostics for media handoff attempts. This class
    /// deliberately has no effect on the handoff state machine or command
    /// result when logging is unavailable or a logger implementation throws.
    /// </summary>
    internal static class HandoffTrace
    {
        private const string Prefix = "WT_HANDOFF";

        private static readonly HashSet<string> KnownErrors = new HashSet<string>(
            new[]
            {
                "invalid_argument",
                "session_offline",
                "remote_control_unsupported",
                "command_timeout",
                "command_failed",
                "media handoff play item issuer unavailable",
                "media handoff play item failed",
                "media handoff timed out",
                "media handoff identity invalid",
                "media handoff participant changed item",
                "play item acknowledgement timed out",
            },
            StringComparer.OrdinalIgnoreCase);

        public static string OperationHash(string roomId, MediaHandoffState handoff)
        {
            if (handoff == null)
            {
                return null;
            }

            return HashComposite(roomId, handoff.StartedAtUtc, handoff.TargetItemId);
        }

        public static string RequestHash(
            string roomId,
            string targetSessionId,
            string targetItemId,
            DateTimeOffset now)
        {
            if (string.IsNullOrWhiteSpace(roomId) ||
                string.IsNullOrWhiteSpace(targetSessionId) ||
                string.IsNullOrWhiteSpace(targetItemId))
            {
                return null;
            }

            return HashComposite(roomId, targetSessionId, targetItemId, now);
        }

        public static void Started(
            ILogger logger,
            Room room,
            string roomId,
            MediaHandoffState handoff,
            DateTimeOffset now)
        {
            if (logger == null) return;
            try
            {
                Write(logger, "handoff_started",
                    Field("operationHash", OperationHash(roomId, handoff)),
                    Field("generation", handoff?.Generation ?? 0),
                    Field("targetItemHash", HashValue(handoff?.TargetItemId)),
                    Field("primary", Alias(room, handoff?.PrimaryUserId)),
                    Field("participant", Alias(room, handoff?.ParticipantUserId)),
                    Field("primarySessionHash", HashValue(handoff?.PrimarySessionId)),
                    Field("participantSessionHash", HashValue(handoff?.ParticipantSessionId)),
                    Field("atUtc", Utc(now)));
            }
            catch { }
        }

        public static void TargetSuperseded(
            ILogger logger,
            string roomId,
            MediaHandoffState handoff,
            string supersededTargetItemId,
            DateTimeOffset now)
        {
            if (logger == null) return;
            try
            {
                Write(logger, "target_superseded",
                    Field("operationHash", OperationHash(roomId, handoff)),
                    Field("generation", handoff?.Generation ?? 0),
                    Field("targetItemHash", HashValue(supersededTargetItemId)),
                    Field("result", "changed"),
                    Field("atUtc", Utc(now)));
            }
            catch { }
        }

        public static void SessionRebind(
            ILogger logger,
            Room room,
            string roomId,
            MediaHandoffState handoff,
            string role,
            string userId,
            string oldSessionId,
            string newSessionId,
            DateTimeOffset now)
        {
            if (logger == null) return;
            try
            {
                Write(logger, "session_rebind",
                    Field("operationHash", OperationHash(roomId, handoff)),
                    Field("generation", handoff?.Generation ?? 0),
                    Field("role", NormalizeRole(role)),
                    Field("alias", Alias(room, userId)),
                    Field("oldSessionHash", HashValue(oldSessionId)),
                    Field("newSessionHash", HashValue(newSessionId)),
                    Field("atUtc", Utc(now)));
            }
            catch { }
        }

        public static void Attempt(
            ILogger logger,
            string roomId,
            MediaHandoffState handoff,
            SessionSnapshot targetSnapshot,
            DateTimeOffset now)
        {
            if (logger == null) return;
            try
            {
                Write(logger, "attempt",
                    Field("operationHash", OperationHash(roomId, handoff)),
                    Field("generation", handoff?.Generation ?? 0),
                    Field("attempt", handoff?.RetryCount ?? 0),
                    Field("targetItemHash", HashValue(handoff?.TargetItemId)),
                    Field("targetSessionHash", HashValue(targetSnapshot?.SessionId)),
                    Field("requestHash", RequestHash(roomId, targetSnapshot?.SessionId, handoff?.TargetItemId, now)),
                    Field("atUtc", Utc(now)));
            }
            catch { }
        }

        public static void IssueResult(
            ILogger logger,
            string roomId,
            MediaHandoffState handoff,
            SessionSnapshot targetSnapshot,
            bool accepted,
            string error,
            DateTimeOffset now)
        {
            if (logger == null) return;
            try
            {
                Write(logger, "issue_result",
                    Field("operationHash", OperationHash(roomId, handoff)),
                    Field("generation", handoff?.Generation ?? 0),
                    Field("attempt", handoff?.RetryCount ?? 0),
                    Field("accepted", accepted),
                    Field("result", accepted ? "accepted" : "rejected"),
                    Field("error", accepted ? "none" : NormalizeError(error)),
                    Field("targetSessionHash", HashValue(targetSnapshot?.SessionId)),
                    Field("requestHash", RequestHash(roomId, targetSnapshot?.SessionId, handoff?.TargetItemId, now)),
                    Field("atUtc", Utc(now)));
            }
            catch { }
        }

        public static void ConfirmationTimeout(
            ILogger logger,
            string roomId,
            MediaHandoffState handoff,
            DateTimeOffset now)
        {
            if (logger == null) return;
            try
            {
                var requestAt = handoff?.PlayItemIssuedAtUtc ?? now;
                string[] fields =
                {
                    Field("operationHash", OperationHash(roomId, handoff)),
                    Field("generation", handoff?.Generation ?? 0),
                    Field("attempt", handoff?.RetryCount ?? 0),
                    Field("result", "confirmation_timeout"),
                    Field("targetItemHash", HashValue(handoff?.TargetItemId)),
                    Field("participantSessionHash", HashValue(handoff?.ParticipantSessionId)),
                    Field("requestHash", RequestHash(roomId, handoff?.ParticipantSessionId, handoff?.TargetItemId, requestAt)),
                    Field("atUtc", Utc(now)),
                };
                Write(logger, "confirmation_timeout", fields);
            }
            catch { }
        }

        public static void TargetConfirmed(
            ILogger logger,
            Room room,
            string roomId,
            MediaHandoffState handoff,
            SessionSnapshot participantSnapshot,
            DateTimeOffset now)
        {
            if (logger == null) return;
            try
            {
                var ageSeconds = handoff == null
                    ? 0d
                    : Math.Max(0d, (now - handoff.StartedAtUtc).TotalSeconds);
                Write(logger, "target_confirmed",
                    Field("operationHash", OperationHash(roomId, handoff)),
                    Field("generation", handoff?.Generation ?? 0),
                    Field("targetItemHash", HashValue(handoff?.TargetItemId)),
                    Field("participant", Alias(room, handoff?.ParticipantUserId)),
                    Field("participantSessionHash", HashValue(participantSnapshot?.SessionId)),
                    Field("requestHash", handoff?.PlayItemIssuedAtUtc.HasValue == true
                        ? RequestHash(roomId, handoff.ParticipantSessionId, handoff.TargetItemId,
                            handoff.PlayItemIssuedAtUtc.Value)
                        : null),
                    Field("positionTicks", participantSnapshot?.PositionTicks ?? 0L),
                    Field("paused", participantSnapshot?.IsPaused ?? false),
                    Field("ageSeconds", ageSeconds),
                    Field("atUtc", Utc(now)));
            }
            catch { }
        }

        public static void Failed(
            ILogger logger,
            string roomId,
            MediaHandoffState handoff,
            string error,
            DateTimeOffset now)
        {
            if (logger == null) return;
            try
            {
                Write(logger, "failed",
                    Field("operationHash", OperationHash(roomId, handoff)),
                    Field("generation", handoff?.Generation ?? 0),
                    Field("attempt", handoff?.RetryCount ?? 0),
                    Field("result", "failed"),
                    Field("error", NormalizeError(error)),
                    Field("targetItemHash", HashValue(handoff?.TargetItemId)),
                    Field("atUtc", Utc(now)));
            }
            catch { }
        }

        public static void Dispatch(
            ILogger logger,
            string roomId,
            string targetSessionId,
            string targetItemId,
            DateTimeOffset now)
        {
            if (logger == null) return;
            try
            {
                Write(logger, "dispatch",
                    Field("targetSessionHash", HashValue(targetSessionId)),
                    Field("targetItemHash", HashValue(targetItemId)),
                    Field("requestHash", RequestHash(roomId, targetSessionId, targetItemId, now)),
                    Field("atUtc", Utc(now)),
                    Field("meaning", "send_play_command_call"));
            }
            catch { }
        }

        public static void DispatchResult(
            ILogger logger,
            string roomId,
            string targetSessionId,
            string targetItemId,
            DateTimeOffset now,
            string result,
            long elapsedMilliseconds)
        {
            if (logger == null) return;
            try
            {
                Write(logger, "dispatch_result",
                    Field("targetSessionHash", HashValue(targetSessionId)),
                    Field("targetItemHash", HashValue(targetItemId)),
                    Field("requestHash", RequestHash(roomId, targetSessionId, targetItemId, now)),
                    Field("result", NormalizeDispatchResult(result)),
                    Field("durationMs", Math.Max(0L, elapsedMilliseconds)),
                    Field("atUtc", Utc(now)),
                    Field("meaning", "send_play_command_return"));
            }
            catch { }
        }

        private static void Write(ILogger logger, string eventName, params string[] fields)
        {
            if (logger == null) return;
            try
            {
                var builder = new StringBuilder(Prefix.Length + 32);
                builder.Append(Prefix).Append(" event=").Append(eventName ?? "unknown");
                if (fields != null)
                {
                    foreach (var field in fields)
                    {
                        if (!string.IsNullOrEmpty(field))
                        {
                            builder.Append(' ').Append(field);
                        }
                    }
                }

                logger.Info(builder.ToString());
            }
            catch
            {
                // Diagnostics are strictly fail-open.
            }
        }

        private static string HashComposite(string roomId, DateTimeOffset atUtc, string itemId)
        {
            return HashComposite(roomId, Utc(atUtc), itemId, null);
        }

        private static string HashComposite(
            string roomId,
            string sessionId,
            string itemId,
            DateTimeOffset atUtc)
        {
            return HashComposite(roomId, sessionId, itemId, Utc(atUtc));
        }

        private static string HashComposite(string first, string second, string third, string fourth)
        {
            try
            {
                var builder = new StringBuilder();
                AppendHashPart(builder, first);
                AppendHashPart(builder, second);
                AppendHashPart(builder, third);
                AppendHashPart(builder, fourth);
                return SyncDiagnostics.Hash(builder.ToString());
            }
            catch
            {
                return null;
            }
        }

        private static void AppendHashPart(StringBuilder builder, string value)
        {
            var part = value ?? string.Empty;
            builder.Append(part.Length.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(part)
                .Append('|');
        }

        private static string HashValue(string value)
        {
            try
            {
                return SyncDiagnostics.Hash(value) ?? "none";
            }
            catch
            {
                return "none";
            }
        }

        private static string Alias(Room room, string userId)
        {
            try
            {
                return SyncDiagnostics.AliasFor(room, userId);
            }
            catch
            {
                return "unknown";
            }
        }

        private static string NormalizeRole(string role)
        {
            return string.Equals(role, "primary", StringComparison.OrdinalIgnoreCase) ? "primary" :
                string.Equals(role, "participant", StringComparison.OrdinalIgnoreCase) ? "participant" :
                "unknown";
        }

        private static string NormalizeError(string error)
        {
            if (string.IsNullOrWhiteSpace(error)) return "unknown";
            if (KnownErrors.Contains(error.Trim()))
            {
                switch (error.Trim().ToLowerInvariant())
                {
                    case "media handoff timed out": return "handoff_timeout";
                    case "media handoff play item failed": return "play_item_failed";
                    case "media handoff play item issuer unavailable": return "issuer_unavailable";
                    case "media handoff identity invalid": return "identity_invalid";
                    case "media handoff participant changed item": return "participant_changed";
                    case "play item acknowledgement timed out": return "confirmation_timeout";
                    default: return error.Trim().ToLowerInvariant();
                }
            }

            return "unknown";
        }

        private static string NormalizeDispatchResult(string result)
        {
            switch (result)
            {
                case "returned": return "returned";
                case "timeout": return "timeout";
                case "failed": return "failed";
                default: return "unknown";
            }
        }

        private static string Utc(DateTimeOffset value)
        {
            try
            {
                return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            }
            catch
            {
                return "unknown";
            }
        }

        private static string Field(string name, string value)
        {
            try
            {
                return name + "=" + (string.IsNullOrEmpty(value) ? "none" : value);
            }
            catch
            {
                return null;
            }
        }

        private static string Field(string name, long value)
        {
            return Field(name, value.ToString(CultureInfo.InvariantCulture));
        }

        private static string Field(string name, int value)
        {
            return Field(name, value.ToString(CultureInfo.InvariantCulture));
        }

        private static string Field(string name, double value)
        {
            return Field(name, value.ToString("0.###", CultureInfo.InvariantCulture));
        }

        private static string Field(string name, bool value)
        {
            return Field(name, value ? "true" : "false");
        }
    }
}

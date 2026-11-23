using System;
using System.Threading;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Issues a single remote-control command to one session. Implemented by the
    /// sync engine (S4) over SessionBridge; kept as an interface so the room
    /// state machine stays pure and testable.
    /// </summary>
    public interface ICommandIssuer
    {
        /// <summary>
        /// Returns true when the command was accepted; otherwise sets error.
        /// Command names are "Pause", "Unpause", "Seek".
        /// </summary>
        bool TryIssue(
            string roomId,
            string controllingUserId,
            string userId,
            SessionSnapshot snapshot,
            string command,
            long? positionTicks,
            DateTimeOffset now,
            out string error);
    }

    /// <summary>
    /// Internal cancellation-aware adapter used by the live engine. The
    /// public ICommandIssuer contract remains unchanged so existing test and
    /// extension implementations continue to compile.
    /// </summary>
    internal interface ICancellableCommandIssuer
    {
        bool TryIssue(
            string roomId,
            string controllingUserId,
            string userId,
            SessionSnapshot snapshot,
            string command,
            long? positionTicks,
            DateTimeOffset now,
            CancellationToken cancellationToken,
            SessionSnapshot[] commandSources,
            out string error,
            PlaybackCommandTiming timing);
    }

    /// <summary>
    /// Internal live-engine path retaining the selected source playback markers.
    /// </summary>
    internal interface ISourceBoundPlayItemIssuer
    {
        bool TryIssuePlayItem(string roomId, string controllingUserId, string userId,
            SessionSnapshot snapshot, string itemId, DateTimeOffset now,
            CancellationToken cancellationToken, SessionSnapshot[] commandSources, out string error,
            PlaybackCommandTiming timing);
    }

    /// <summary>
    /// Records when an admitted transport begins. Read the timestamp only after
    /// the issuer has reported success, which observes transport completion.
    /// </summary>
    internal sealed class PlaybackCommandTiming
    {
        private readonly Func<DateTimeOffset> _clock;

        internal PlaybackCommandTiming(Func<DateTimeOffset> clock) => _clock = clock;

        internal DateTimeOffset? StartedAtUtc { get; private set; }

        internal void MarkStarted() => StartedAtUtc = _clock();
    }

    /// <summary>
    /// Additive cancellation-aware contract for issuing a PlayItem request.
    /// Existing ICommandIssuer implementations are not required to implement it.
    /// </summary>
    public interface IPlayItemIssuer
    {
        bool TryIssuePlayItem(
            string roomId,
            string controllingUserId,
            string userId,
            SessionSnapshot snapshot,
            string itemId,
            DateTimeOffset now,
            CancellationToken cancellationToken,
            out string error);
    }
}

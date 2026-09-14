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
            out string error);
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

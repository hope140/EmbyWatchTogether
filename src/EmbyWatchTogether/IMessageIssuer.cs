using System;
using System.Threading;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Issues a best-effort display message to one remote session.
    /// </summary>
    public interface IMessageIssuer
    {
        bool TryIssueMessage(
            string roomId,
            string controllingUserId,
            string userId,
            SessionSnapshot snapshot,
            string header,
            string text,
            int? timeoutMs,
            DateTimeOffset now,
            out string error);
    }

    /// <summary>
    /// Internal cancellation-aware adapter used by the live engine. The
    /// public IMessageIssuer contract remains unchanged so existing test and
    /// extension implementations continue to compile.
    /// </summary>
    internal interface ICancellableMessageIssuer
    {
        bool TryIssueMessage(
            string roomId,
            string controllingUserId,
            string userId,
            SessionSnapshot snapshot,
            string header,
            string text,
            int? timeoutMs,
            DateTimeOffset now,
            CancellationToken cancellationToken,
            out string error);
    }
}

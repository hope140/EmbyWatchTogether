using System;

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
}

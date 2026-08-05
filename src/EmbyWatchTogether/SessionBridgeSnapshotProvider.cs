using System.Collections.Generic;
using System.Linq;

namespace Emby.Plugins.WatchTogether
{
    public sealed class SessionBridgeSnapshotProvider : ISessionSnapshotProvider
    {
        private readonly SessionBridge _bridge;

        public SessionBridgeSnapshotProvider(SessionBridge bridge)
        {
            _bridge = bridge;
        }

        public IReadOnlyList<SessionSnapshot> GetSessionSnapshots()
        {
            return _bridge.GetSessions()
                .Select(s => SessionSnapshot.FromSessionInfo(s))
                .Where(s => s != null)
                .ToList();
        }
    }
}

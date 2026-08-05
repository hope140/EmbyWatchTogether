using System.Collections.Generic;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Supplies current session snapshots to the sync engine. Kept behind an
    /// interface so PollOnce is unit-testable without an Emby server.
    /// </summary>
    public interface ISessionSnapshotProvider
    {
        IReadOnlyList<SessionSnapshot> GetSessionSnapshots();
    }
}

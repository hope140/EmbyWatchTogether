using System.Collections.Generic;

namespace Emby.Plugins.WatchTogether
{
    internal sealed class SessionSelectionDiagnostics
    {
        public bool HasMultipleCandidates { get; set; }

        public IReadOnlyList<SessionSelectionCandidateDiagnostic> Candidates { get; set; } =
            new List<SessionSelectionCandidateDiagnostic>();

        public IReadOnlyDictionary<string, SessionSnapshot> Selected { get; set; } =
            new Dictionary<string, SessionSnapshot>();
    }

    internal sealed class SessionSelectionCandidateDiagnostic
    {
        public string UserId { get; set; }

        public SessionSnapshot Snapshot { get; set; }

        public string Disposition { get; set; }
    }
}

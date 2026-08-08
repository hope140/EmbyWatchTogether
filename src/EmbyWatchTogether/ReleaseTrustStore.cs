using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// The release public-key trust store. It intentionally starts empty until
    /// the operator bootstraps and reviews the production public key. An empty
    /// store makes update checks fail closed rather than accepting unsigned or
    /// untrusted release material.
    /// </summary>
    public static class ReleaseTrustStore
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyPublicKeys =
            new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(StringComparer.Ordinal));

        public static IReadOnlyDictionary<string, string> PublicKeys => EmptyPublicKeys;

        public static ReleaseSignatureVerifier CreateVerifier()
        {
            return new ReleaseSignatureVerifier(PublicKeys);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// The default release trust store. It remains empty until a production
    /// public key has been provided and approved by the operator.
    /// </summary>
    public static class ReleaseTrustStore
    {
        private static readonly IReadOnlyDictionary<string, string> DefaultPublicKeys =
            new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(StringComparer.Ordinal));

        /// <summary>
        /// Gets the immutable keyId to RSA XML public key mapping.
        /// An empty mapping deliberately makes the default verifier fail closed.
        /// </summary>
        public static IReadOnlyDictionary<string, string> TrustedPublicKeys
        {
            get { return DefaultPublicKeys; }
        }

        /// <summary>
        /// Creates a verifier using only the approved keys in this trust store.
        /// </summary>
        public static ReleaseSignatureVerifier CreateVerifier()
        {
            return new ReleaseSignatureVerifier(DefaultPublicKeys);
        }
    }
}

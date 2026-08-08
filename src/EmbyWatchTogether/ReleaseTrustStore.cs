using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// The release public-key trust store. It contains only reviewed production
    /// public keys and remains immutable at runtime. Unknown or invalid release
    /// material fails closed.
    /// </summary>
    public static class ReleaseTrustStore
    {
        private static readonly IReadOnlyDictionary<string, string> PublicKeyMap =
            new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["prod-2026-08"] = "<RSAKeyValue><Modulus>uzrQr04obKYghsRQLCA5fHweC9r37xhoUmRT1ltOWUiJn9Xec9AMLMTiDcVXmrvRrW3BRBuj4cRTBQp8U/o5z4p2Twe3Wj2RraF/9UHCUwoTnS74WcOy06OrVQ7oqYNm17/uzS7WsjahX7Y1bYm0l7GctKakj/2qMvCBeKEfizsnOhcKyySV3awl9yLzK6WI/5pNXf0QuJn+wVvG5wAiguLWXdpffY1zR8IOv6k3KVHYPMEoATX3AzazKzx+7aNc6b1sJyIUCDLLRRpn7kyx8fYmzXsnMyHjJTP1L9Bnh84stA80Bt6YcK033/lWNer+2ImyES0EC8d/v9u6nbNiR8Ja7ev8w7ylGM32EMZkDAgfLRfPCq5sVjd9VpBGLu3OxOwNlqI2my5UkU/2xR0hH8ch34W29jE83t2sb7iugX4CQxaJHp5xf5nuwpfq31pWs33e9rUGbmmSvQ4ciD0rN7lKGj372tEf9J0ZONMsqaUA/Ot7bdIWySWQbA6XnQql</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>"
                });

        public static IReadOnlyDictionary<string, string> PublicKeys => PublicKeyMap;

        public static ReleaseSignatureVerifier CreateVerifier()
        {
            return new ReleaseSignatureVerifier(PublicKeys);
        }
    }
}

using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    // These fixtures deliberately occupy worker threads while asserting short
    // dispatch deadlines. Other collections must not starve their transport
    // startup before the behavior under test can run on small CI runners.
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class TransportTimingCollection
    {
        public const string Name = "Transport timing";
    }
}

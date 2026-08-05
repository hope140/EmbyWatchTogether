using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class WatchTogetherEntryPointTests
    {
        [Fact]
        public void EntryPoint_RunAndDispose_DoNotThrowWithNullSessionManager()
        {
            using var entryPoint = new WatchTogetherEntryPoint(null, null, null);

            entryPoint.Run();
        }
    }
}

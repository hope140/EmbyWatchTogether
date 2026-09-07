using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class CiAcceptanceFailureTests
    {
        [Fact]
        public void IntentionalFailureForCiAcceptance()
        {
            Assert.True(false, "Intentional CI acceptance failure; remove after verifying TRX upload.");
        }
    }
}

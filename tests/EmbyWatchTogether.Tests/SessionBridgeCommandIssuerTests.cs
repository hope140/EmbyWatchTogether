using System.Collections.Generic;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class SessionBridgeCommandIssuerTests
    {
        [Fact]
        public void IsCommandSupported_RemoteControllableWithoutDeclaredPlaybackCommands_AllowsPause()
        {
            // Emby Theater style: SupportsRemoteControl without Pause/Unpause/Seek
            // in the declared command list (only OSD/navigation commands).
            var report = new SessionCapabilityReport(
                true,
                new[] { "MoveUp", "MoveDown", "Select", "Back", "DisplayMessage" });

            Assert.True(SessionBridgeCommandIssuer.IsCommandSupported(report, RemoteCommands.Pause));
            Assert.True(SessionBridgeCommandIssuer.IsCommandSupported(report, RemoteCommands.Unpause));
            Assert.True(SessionBridgeCommandIssuer.IsCommandSupported(report, RemoteCommands.Seek));
        }

        [Fact]
        public void IsCommandSupported_NonEmptyCommandListImpliesRemoteControl()
        {
            var report = new SessionCapabilityReport(false, new[] { "MoveUp", "Select" });

            Assert.True(SessionBridgeCommandIssuer.IsCommandSupported(report, RemoteCommands.Pause));
        }

        [Fact]
        public void IsCommandSupported_NotRemoteControllable_Rejects()
        {
            var report = new SessionCapabilityReport(false, new string[0]);

            Assert.False(SessionBridgeCommandIssuer.IsCommandSupported(report, RemoteCommands.Pause));
            Assert.False(SessionBridgeCommandIssuer.IsCommandSupported(report, RemoteCommands.Seek));
        }

        [Fact]
        public void IsCommandSupported_NullReport_Rejects()
        {
            Assert.False(SessionBridgeCommandIssuer.IsCommandSupported(null, RemoteCommands.Pause));
        }

        [Fact]
        public void IsCommandSupported_UnknownCommand_Rejects()
        {
            var report = new SessionCapabilityReport(true, new string[0]);

            Assert.False(SessionBridgeCommandIssuer.IsCommandSupported(report, "SomethingElse"));
        }
    }
}

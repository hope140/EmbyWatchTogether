using System;
using MediaBrowser.Model.Session;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class CommandFactoryTests
    {
        [Fact]
        public void Pause_SetsCommandAndController()
        {
            var request = PlaystateRequestFactory.Pause("admin-1");

            Assert.Equal(PlaystateCommand.Pause, request.Command);
            Assert.Equal("admin-1", request.ControllingUserId);
        }

        [Fact]
        public void Unpause_SetsCommand()
        {
            var request = PlaystateRequestFactory.Unpause("admin-1");

            Assert.Equal(PlaystateCommand.Unpause, request.Command);
        }

        [Fact]
        public void PlayPause_SetsCommand()
        {
            var request = PlaystateRequestFactory.PlayPause("admin-1");

            Assert.Equal(PlaystateCommand.PlayPause, request.Command);
        }

        [Fact]
        public void Stop_SetsCommand()
        {
            var request = PlaystateRequestFactory.Stop("admin-1");

            Assert.Equal(PlaystateCommand.Stop, request.Command);
        }

        [Fact]
        public void Seek_SetsPositionTicks()
        {
            var request = PlaystateRequestFactory.Seek("admin-1", 42_000_000);

            Assert.Equal(PlaystateCommand.Seek, request.Command);
            Assert.Equal(42_000_000, request.SeekPositionTicks);
        }

        [Fact]
        public void Seek_NegativeTicks_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PlaystateRequestFactory.Seek("admin-1", -1));
        }

        [Fact]
        public void DisplayMessage_SetsHeaderTextAndTimeout()
        {
            var command = MessageCommandFactory.DisplayMessage("Watch Together", "Room paused", 5000);

            Assert.Equal("Watch Together", command.Header);
            Assert.Equal("Room paused", command.Text);
            Assert.Equal(5000, command.TimeoutMs);
        }

        [Fact]
        public void DisplayMessage_NullTimeout_IsAllowed()
        {
            var command = MessageCommandFactory.DisplayMessage("h", "t");

            Assert.Null(command.TimeoutMs);
        }
    }
}

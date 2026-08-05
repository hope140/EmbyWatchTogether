using System.Collections.Generic;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class CapabilityProbeTests
    {
        [Fact]
        public void Probe_NoFlagAndNoCommands_IsNotRemotelyControllable()
        {
            var report = CapabilityProbe.Probe(false, null, null);

            Assert.False(report.SupportsRemoteControl);
            Assert.False(report.CanControlPlayback);
            Assert.False(report.CanDisplayMessage);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Probe_NonEmptyCommandList_EnablesRemoteControl(bool flag)
        {
            var report = CapabilityProbe.Probe(flag, new[] { "Pause", "Unpause", "Seek" }, null);

            Assert.True(report.SupportsRemoteControl);
            Assert.True(report.CanControlPlayback);
        }

        [Fact]
        public void Probe_FullCommandSet_SupportsPauseUnpauseSeekStopAndMessage()
        {
            var report = CapabilityProbe.Probe(
                true,
                new[] { "Pause", "Unpause", "PlayPause", "Seek", "Stop", "DisplayMessage" },
                null);

            Assert.True(report.CanPause);
            Assert.True(report.CanUnpause);
            Assert.True(report.CanPlayPause);
            Assert.True(report.CanSeek);
            Assert.True(report.CanStop);
            Assert.True(report.CanDisplayMessage);
            Assert.True(report.CanControlPlayback);
        }

        [Fact]
        public void Probe_MissingSeek_FailsPlaybackGate()
        {
            var report = CapabilityProbe.Probe(true, new[] { "Pause", "Unpause" }, null);

            Assert.True(report.CanPause);
            Assert.True(report.CanUnpause);
            Assert.False(report.CanSeek);
            Assert.False(report.CanControlPlayback);
        }

        [Fact]
        public void Probe_MissingPause_FailsPlaybackGate()
        {
            var report = CapabilityProbe.Probe(true, new[] { "Unpause", "Seek" }, null);

            Assert.False(report.CanControlPlayback);
        }

        [Fact]
        public void Probe_CommandNames_AreCaseInsensitive()
        {
            var report = CapabilityProbe.Probe(true, new[] { "pause", "UNPAUSE", "seek" }, null);

            Assert.True(report.CanPause);
            Assert.True(report.CanUnpause);
            Assert.True(report.CanSeek);
            Assert.True(report.CanControlPlayback);
        }

        [Fact]
        public void Probe_MergesSessionAndCapabilityCommandLists()
        {
            var report = CapabilityProbe.Probe(
                true,
                new[] { "Pause", "Unpause" },
                new[] { "Seek", "DisplayMessage" });

            Assert.True(report.CanPause);
            Assert.True(report.CanUnpause);
            Assert.True(report.CanSeek);
            Assert.True(report.CanDisplayMessage);
            Assert.True(report.CanControlPlayback);
        }

        [Fact]
        public void Probe_IgnoresWhitespaceEntries()
        {
            var report = CapabilityProbe.Probe(true, new[] { " ", "Pause" }, null);

            Assert.True(report.CanPause);
            Assert.Single(report.SupportedCommands);
        }

        [Fact]
        public void Probe_NullSession_ReportsNotControllable()
        {
            var report = CapabilityProbe.Probe(session: null);

            Assert.False(report.SupportsRemoteControl);
            Assert.False(report.CanControlPlayback);
        }
    }
}

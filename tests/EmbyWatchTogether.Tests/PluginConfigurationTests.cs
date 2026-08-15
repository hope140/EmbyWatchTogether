using System;
using System.Linq;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using Moq;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class PluginConfigurationTests
    {
        [Fact]
        public void NotifyOtherOnPlaybackStop_DefaultsToEnabledAndIsIndependent()
        {
            var configuration = new PluginConfiguration();

            Assert.True(configuration.NotifyOtherOnPlaybackStop);

            configuration.PauseOtherOnPlaybackStop = false;
            Assert.True(configuration.NotifyOtherOnPlaybackStop);

            configuration.NotifyOtherOnPlaybackStop = false;
            Assert.False(configuration.NotifyOtherOnPlaybackStop);
        }

        [Fact]
        public void NotifyOnSyncActions_DefaultsToEnabledAndIsIndependent()
        {
            var configuration = new PluginConfiguration();
            Assert.True(configuration.NotifyOnSyncActions);
            configuration.NotifyOtherOnPlaybackStop = false;
            Assert.True(configuration.NotifyOnSyncActions);
            configuration.NotifyOnSyncActions = false;
            Assert.False(configuration.NotifyOnSyncActions);
        }

        [Fact]
        public void SyncEngineOptions_From_PropagatesNotifyOnSyncActions_AndLegacyConstructorRemainsCompatible()
        {
            Assert.True(SyncEngineOptions.From(new PluginConfiguration { NotifyOnSyncActions = true }).NotifyOnSyncActions);
            Assert.True(new SyncEngineOptions(0.5, true, true, true).NotifyOnSyncActions);
            Assert.True(new SyncEngineOptions(0.5, true, true).NotifyOnSyncActions);
        }

        [Fact]
        public void PollIntervalSeconds_DefaultsToHalfSecond()
        {
            var configuration = new PluginConfiguration();

            Assert.Equal(0.5, configuration.PollIntervalSeconds);
        }

        [Fact]
        public void SyncEngineOptions_NormalizesInvalidAndOutOfRangeIntervals()
        {
            Assert.Equal(
                SyncEngineOptions.DefaultPollIntervalSeconds,
                new SyncEngineOptions(double.NaN, true, true).PollIntervalSeconds);
            Assert.Equal(
                SyncEngineOptions.DefaultPollIntervalSeconds,
                new SyncEngineOptions(double.PositiveInfinity, true, true).PollIntervalSeconds);
            Assert.Equal(
                SyncEngineOptions.DefaultPollIntervalSeconds,
                new SyncEngineOptions(0, true, true).PollIntervalSeconds);
            Assert.Equal(
                SyncEngineOptions.MinPollIntervalSeconds,
                new SyncEngineOptions(0.01, true, true).PollIntervalSeconds);
            Assert.Equal(
                SyncEngineOptions.MaxPollIntervalSeconds,
                new SyncEngineOptions(120, true, true).PollIntervalSeconds);
        }

        [Fact]
        public void Plugin_UpdateConfiguration_RaisesNormalizedImmutableOptionsAfterBaseUpdate()
        {
            var paths = new Mock<IApplicationPaths>();
            paths.SetupGet(p => p.PluginConfigurationsPath).Returns("C:\\watch-together-tests");
            var serializer = new Mock<IXmlSerializer>();
            var plugin = new Plugin(paths.Object, serializer.Object);
            plugin.SetAttributes(
                "C:\\watch-together-tests\\Emby.Plugins.WatchTogether.dll",
                "C:\\watch-together-tests\\data",
                new Version(1, 0, 0, 0));
            plugin.SetStartupInfo(_ => { });

            PluginConfigurationChangedEventArgs received = null;
            plugin.ConfigurationChanged += (sender, args) => received = args;
            var configuration = new PluginConfiguration
            {
                PollIntervalSeconds = double.NaN,
                PauseOtherOnPlaybackStop = false,
                NotifyOtherOnPlaybackStop = true,
            };

            try
            {
                plugin.UpdateConfiguration(configuration);

                Assert.NotNull(received);
                Assert.NotSame(configuration, received.Options);
                Assert.Equal(0.5, received.Options.PollIntervalSeconds);
                Assert.False(received.Options.PauseOtherOnPlaybackStop);
                Assert.True(received.Options.NotifyOtherOnPlaybackStop);
                Assert.Equal(configuration, plugin.Configuration);
                Assert.All(
                    typeof(SyncEngineOptions).GetProperties(),
                    property => Assert.Null(property.SetMethod));
            }
            finally
            {
                typeof(Plugin).GetProperty("Instance")?.GetSetMethod(true)?.Invoke(null, new object[] { null });
            }
        }

        [Fact]
        public void UpdateSettings_AreNotPartOfPluginConfiguration()
        {
            var properties = typeof(PluginConfiguration).GetProperties();

            Assert.DoesNotContain(properties, p => p.Name == "AutoUpdateEnabled");
            Assert.DoesNotContain(properties, p => p.Name == "UpdateCheckIntervalHours");
            Assert.DoesNotContain(properties, p => p.Name == "LastUpdateCheckAtUtc");
        }

        [Fact]
        public void PendingUpdateVersion_RemainsInternalStateWithNullDefault()
        {
            var configuration = new PluginConfiguration();

            Assert.Null(configuration.PendingUpdateVersion);
        }

        [Fact]
        public void UpdateChannel_DefaultsToStable_AndUnknownValuesFallBackToStable()
        {
            var configuration = new PluginConfiguration();

            Assert.Equal(PluginConfiguration.StableUpdateChannel, configuration.UpdateChannel);
            Assert.Equal(PluginConfiguration.StableUpdateChannel, PluginConfiguration.NormalizeUpdateChannel(null));
            Assert.Equal(PluginConfiguration.StableUpdateChannel, PluginConfiguration.NormalizeUpdateChannel("unknown"));
            Assert.Equal(PluginConfiguration.BetaUpdateChannel, PluginConfiguration.NormalizeUpdateChannel("beta"));
        }
    }
}

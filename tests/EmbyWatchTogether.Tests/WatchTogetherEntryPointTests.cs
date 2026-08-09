using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Session;
using Moq;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    [Collection("Plugin singleton")]
    public class WatchTogetherEntryPointTests
    {
        [Fact]
        public void EntryPoint_RunAndDispose_DoNotThrowWithNullSessionManager()
        {
            using var entryPoint = new WatchTogetherEntryPoint(null, null, null);

            entryPoint.Run();
        }

        [Fact]
        public void EntryPoint_RunIsIdempotent_AndDisposeIsIdempotent()
        {
            var root = Path.Combine(Path.GetTempPath(), "watch-together-entry-point-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var plugin = CreatePlugin(root);
            SetPluginInstance(plugin);

            try
            {
                var sessionManager = new Mock<ISessionManager>();
                sessionManager.SetupGet(x => x.Sessions).Returns(new List<SessionInfo>());
                var applicationHost = new Mock<IServerApplicationHost>();
                var serializer = new Mock<IJsonSerializer>();
                using (var entryPoint = new WatchTogetherEntryPoint(
                    sessionManager.Object,
                    applicationHost.Object,
                    serializer.Object))
                {
                    entryPoint.Run();
                    var bridge = plugin.Bridge;
                    var rooms = plugin.Rooms;

                    entryPoint.Run();

                    Assert.Same(bridge, plugin.Bridge);
                    Assert.Same(rooms, plugin.Rooms);

                    entryPoint.Dispose();
                    entryPoint.Dispose();
                    Assert.Null(plugin.Store);
                    Assert.Null(plugin.Rooms);
                    Assert.Null(plugin.Bridge);
                    Assert.Null(plugin.Issuer);
                }
            }
            finally
            {
                SetPluginInstance(null);
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void EntryPoint_StartupFailure_DoesNotPublishPartialRuntimeReferences()
        {
            var root = Path.Combine(Path.GetTempPath(), "watch-together-entry-point-failure-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var plugin = CreatePlugin(root);
            SetPluginInstance(plugin);

            try
            {
                var sessionManager = new Mock<ISessionManager>();
                sessionManager.SetupGet(x => x.Sessions).Returns(new List<SessionInfo>());
                var applicationHost = new Mock<IServerApplicationHost>();
                using (var entryPoint = new WatchTogetherEntryPoint(
                    sessionManager.Object,
                    applicationHost.Object,
                    jsonSerializer: null))
                {
                    entryPoint.Run();

                    Assert.Null(plugin.Store);
                    Assert.Null(plugin.Rooms);
                    Assert.Null(plugin.Bridge);
                    Assert.Null(plugin.Issuer);
                    Assert.Null(plugin.ApplicationHost);
                    Assert.Null(plugin.ServerId);
                }
            }
            finally
            {
                SetPluginInstance(null);
                Directory.Delete(root, true);
            }
        }

        private static Plugin CreatePlugin(string root)
        {
            var paths = new Mock<IApplicationPaths>();
            paths.SetupGet(x => x.PluginConfigurationsPath).Returns(root);
            var serializer = new Mock<IXmlSerializer>();
            var plugin = new Plugin(paths.Object, serializer.Object);
            plugin.SetAttributes(
                Path.Combine(root, "Emby.Plugins.WatchTogether.dll"),
                Path.Combine(root, "data"),
                new Version(1, 0, 0, 0));
            plugin.SetStartupInfo(_ => { });
            return plugin;
        }

        private static void SetPluginInstance(Plugin plugin)
        {
            typeof(Plugin)
                .GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .GetSetMethod(true)
                .Invoke(null, new object[] { plugin });
        }
    }
}

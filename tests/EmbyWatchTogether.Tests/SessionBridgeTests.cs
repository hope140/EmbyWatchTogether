using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Moq;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class SessionBridgeTests
    {
        [Fact]
        public void Ctor_NullSessionManager_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new SessionBridge(null));
        }

        [Fact]
        public async Task SendSeekAsync_IssuesSeekCommandWithTicks()
        {
            var manager = NewManager();
            using var bridge = new SessionBridge(manager.Object);

            await bridge.SendSeekAsync("admin-1", "session-1", 123_000_000, CancellationToken.None);

            manager.Verify(m => m.SendPlaystateCommand(
                It.IsAny<string>(),
                "session-1",
                It.Is<PlaystateRequest>(r => r.Command == PlaystateCommand.Seek && r.SeekPositionTicks == 123_000_000),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SendPauseAsync_IssuesPauseCommand()
        {
            var manager = NewManager();
            using var bridge = new SessionBridge(manager.Object);

            await bridge.SendPauseAsync("admin-1", "session-1", CancellationToken.None);

            manager.Verify(m => m.SendPlaystateCommand(
                It.IsAny<string>(),
                "session-1",
                It.Is<PlaystateRequest>(r => r.Command == PlaystateCommand.Pause),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SendDisplayMessageAsync_IssuesMessageCommand()
        {
            var manager = NewManager();
            using var bridge = new SessionBridge(manager.Object);

            await bridge.SendDisplayMessageAsync("admin-1", "session-1", "h", "t", 3000, CancellationToken.None);

            manager.Verify(m => m.SendMessageCommand(
                It.IsAny<string>(),
                "session-1",
                It.Is<MessageCommand>(c => c.Header == "h" && c.Text == "t" && c.TimeoutMs == 3000),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void FindSessionsForUsers_FiltersByUserId()
        {
            var manager = NewManager();
            var sessions = new List<SessionInfo>
            {
                NewSession("s1", "u1"),
                NewSession("s2", "u2"),
                NewSession("s3", "u1"),
            };
            manager.Setup(m => m.Sessions).Returns(sessions);
            using var bridge = new SessionBridge(manager.Object);

            var result = bridge.FindSessionsForUsers(new[] { "u1" });

            Assert.Equal(2, result.Count);
            Assert.All(result, s => Assert.Equal("u1", s.UserId));
        }

        [Fact]
        public void FindSession_ReturnsNullWhenMissing()
        {
            var manager = NewManager();
            manager.Setup(m => m.Sessions).Returns(new List<SessionInfo> { NewSession("s1", "u1") });
            using var bridge = new SessionBridge(manager.Object);

            Assert.Null(bridge.FindSession("nope"));
            Assert.NotNull(bridge.FindSession("s1"));
        }

        [Fact]
        public void Dispose_UnsubscribesWithoutThrowing()
        {
            var manager = NewManager();
            var bridge = new SessionBridge(manager.Object);

            bridge.Dispose();
            bridge.Dispose();
        }

        private static Mock<ISessionManager> NewManager()
        {
            var mock = new Mock<ISessionManager>();
            mock.Setup(m => m.Sessions).Returns(new List<SessionInfo>());
            mock.Setup(m => m.SendPlaystateCommand(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<PlaystateRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            mock.Setup(m => m.SendMessageCommand(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<MessageCommand>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            return mock;
        }

        private static SessionInfo NewSession(string id, string userId)
        {
            return new SessionInfo
            {
                Id = id,
                UserId = userId,
            };
        }
    }
}

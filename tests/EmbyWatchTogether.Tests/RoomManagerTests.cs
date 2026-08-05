using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class RoomManagerTests
    {
        [Fact]
        public void CreateRoom_Success_StoresTwoParticipantsAndPrimary()
        {
            var manager = new RoomManager();

            var room = manager.CreateRoom(
                "server-1", "http://emby", "night", "admin-1",
                new[] { "u1", "u2" }, "u1");

            Assert.Equal(2, room.ParticipantUserIds.Count);
            Assert.Equal("u1", room.PrimaryUserId);
            Assert.Equal("admin-1", room.AdminUserId);
            Assert.Equal("server-1", room.ServerId);
            Assert.NotNull(manager.GetRuntime(room.Id));
            Assert.Equal(RoomState.Waiting, manager.GetRuntime(room.Id).State);
        }

        public static TheoryData<string[]> InvalidParticipantSets => new TheoryData<string[]>
        {
            { Array.Empty<string>() },
            { new[] { "u1" } },
            { new[] { "u1", "u2", "u3" } },
        };

        [Theory]
        [MemberData(nameof(InvalidParticipantSets))]
        public void CreateRoom_RequiresExactlyTwoParticipants(string[] participants)
        {
            var manager = new RoomManager();

            Assert.Throws<ArgumentException>(() => manager.CreateRoom(
                "server-1", "http://emby", "n", "admin-1", participants, "u1"));
        }

        [Fact]
        public void CreateRoom_RejectsDuplicateParticipants()
        {
            var manager = new RoomManager();

            Assert.Throws<ArgumentException>(() => manager.CreateRoom(
                "server-1", "http://emby", "n", "admin-1", new[] { "u1", "u1" }, "u1"));
        }

        [Fact]
        public void CreateRoom_PrimaryMustBeParticipant()
        {
            var manager = new RoomManager();

            Assert.Throws<ArgumentException>(() => manager.CreateRoom(
                "server-1", "http://emby", "n", "admin-1", new[] { "u1", "u2" }, "u3"));
        }

        [Fact]
        public void CreateRoom_RejectsEmptyAdmin()
        {
            var manager = new RoomManager();

            Assert.Throws<ArgumentException>(() => manager.CreateRoom(
                "server-1", "http://emby", "n", "", new[] { "u1", "u2" }, "u1"));
        }

        [Fact]
        public void CreateRoom_RejectsMemberOverlapAcrossRooms()
        {
            var manager = new RoomManager();
            manager.CreateRoom("server-1", "http://emby", "a", "admin-1", new[] { "u1", "u2" }, "u1");

            Assert.Throws<InvalidOperationException>(() => manager.CreateRoom(
                "server-1", "http://emby", "b", "admin-2", new[] { "u2", "u3" }, "u2"));
        }

        [Fact]
        public void DeleteRoom_RemovesRoomAndRuntime()
        {
            var manager = new RoomManager();
            var room = manager.CreateRoom("server-1", "http://emby", "a", "admin-1", new[] { "u1", "u2" }, "u1");

            Assert.True(manager.DeleteRoom(room.Id));
            Assert.Null(manager.GetRoom(room.Id));
            Assert.False(manager.DeleteRoom(room.Id));
        }

        [Fact]
        public void Action_Resync_ResetsRuntimeToWaiting()
        {
            var manager = new RoomManager();
            var room = manager.CreateRoom("server-1", "http://emby", "a", "admin-1", new[] { "u1", "u2" }, "u1");
            var runtime = manager.GetRuntime(room.Id);
            runtime.State = RoomState.Watching;
            runtime.Error = "boom";
            runtime.Pending["u1"] = new PendingCommand { UserId = "u1", Command = "Pause", IssuedAtUtc = DateTimeOffset.UtcNow };

            var result = manager.Action(room.Id, "resync", new Dictionary<string, SessionSnapshot>(), null, DateTimeOffset.UtcNow);

            Assert.Equal(RoomState.Waiting, result.State);
            Assert.Null(runtime.Error);
            Assert.Empty(runtime.Pending);
            Assert.Null(runtime.Barrier);
        }

        [Fact]
        public void Action_Pause_IssuesToOnlineParticipantsOnly()
        {
            var manager = new RoomManager();
            var room = manager.CreateRoom("server-1", "http://emby", "a", "admin-1", new[] { "u1", "u2" }, "u1");
            var issuer = new FakeIssuer();
            var snapshots = new Dictionary<string, SessionSnapshot>
            {
                ["u1"] = TestSnapshots.Online("u1"),
                ["u2"] = TestSnapshots.Offline("u2"),
            };

            var result = manager.Action(room.Id, "pause", snapshots, issuer, DateTimeOffset.UtcNow);

            Assert.Equal(RemoteCommands.Pause, result.Command);
            Assert.Equal(new[] { "u1" }, result.Users);
            Assert.Single(issuer.Issued);
            Assert.Equal("u1", issuer.Issued[0].userId);
        }

        [Fact]
        public void Action_Resume_IssuesUnpause()
        {
            var manager = new RoomManager();
            var room = manager.CreateRoom("server-1", "http://emby", "a", "admin-1", new[] { "u1", "u2" }, "u1");
            var issuer = new FakeIssuer();
            var snapshots = new Dictionary<string, SessionSnapshot>
            {
                ["u1"] = TestSnapshots.Online("u1"),
                ["u2"] = TestSnapshots.Online("u2"),
            };

            var result = manager.Action(room.Id, "resume", snapshots, issuer, DateTimeOffset.UtcNow);

            Assert.Equal(RemoteCommands.Unpause, result.Command);
            Assert.Equal(2, result.Users.Count);
        }

        [Fact]
        public void Action_FailedIssue_RecordsError()
        {
            var manager = new RoomManager();
            var room = manager.CreateRoom("server-1", "http://emby", "a", "admin-1", new[] { "u1", "u2" }, "u1");
            var issuer = new FakeIssuer { AcceptAll = false };
            var snapshots = new Dictionary<string, SessionSnapshot>
            {
                ["u1"] = TestSnapshots.Online("u1"),
                ["u2"] = TestSnapshots.Online("u2"),
            };

            var result = manager.Action(room.Id, "pause", snapshots, issuer, DateTimeOffset.UtcNow);

            Assert.Contains("Pause command failed", result.Error);
            Assert.Empty(result.Users);
        }

        [Fact]
        public void Action_UnknownAction_Throws()
        {
            var manager = new RoomManager();
            var room = manager.CreateRoom("server-1", "http://emby", "a", "admin-1", new[] { "u1", "u2" }, "u1");

            Assert.Throws<ArgumentException>(() => manager.Action(
                room.Id, "nope", new Dictionary<string, SessionSnapshot>(), null, DateTimeOffset.UtcNow));
        }

        [Fact]
        public void Action_MissingRoom_Throws()
        {
            var manager = new RoomManager();

            Assert.Throws<KeyNotFoundException>(() => manager.Action(
                "missing", "pause", new Dictionary<string, SessionSnapshot>(), null, DateTimeOffset.UtcNow));
        }

        private sealed class FakeIssuer : ICommandIssuer
        {
            public bool AcceptAll { get; set; } = true;

            public List<(string userId, string command, long? positionTicks)> Issued { get; } =
                new List<(string, string, long?)>();

            public bool TryIssue(
                string roomId,
                string controllingUserId,
                string userId,
                SessionSnapshot snapshot,
                string command,
                long? positionTicks,
                DateTimeOffset now,
                out string error)
            {
                if (!AcceptAll)
                {
                    error = "boom";
                    return false;
                }

                Issued.Add((userId, command, positionTicks));
                error = null;
                return true;
            }
        }
    }

    internal static class TestSnapshots
    {
        public static SessionSnapshot Online(string userId, string itemId = "i1")
        {
            return new SessionSnapshot(
                "session-" + userId, userId, itemId, "m1",
                positionTicks: 0, runTimeTicks: 100 * SessionSnapshot.TicksPerSecond,
                isPaused: false, playbackRate: 1.0, stopped: false,
                supportsRemoteControl: true,
                capabilities: new SessionCapabilityReport(true, new[] { "Pause", "Unpause", "Seek" }));
        }

        public static SessionSnapshot Offline(string userId)
        {
            return new SessionSnapshot(
                "", userId, "", "", 0, 0, false, 1.0, stopped: true, false,
                new SessionCapabilityReport(false, Array.Empty<string>()));
        }
    }
}

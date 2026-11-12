using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MediaBrowser.Model.Serialization;
using Moq;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class RoomNamingTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "wt-names-" + Guid.NewGuid().ToString("N"));
        private string FilePath => Path.Combine(directory, "rooms.json");
        public RoomNamingTests() { Directory.CreateDirectory(directory); }
        public void Dispose() { Directory.Delete(directory, true); }
        private static Room Create(RoomManager manager, string name, int id) =>
            manager.CreateRoom("server", "", name, "admin", new[] { "a" + id, "b" + id }, "a" + id);
        private static Mock<IJsonSerializer> Serializer()
        {
            var mock = new Mock<IJsonSerializer>();
            mock.Setup(s => s.SerializeToString(It.IsAny<object>())).Returns<object>(o => JsonSerializer.Serialize(o));
            mock.Setup(s => s.DeserializeFromString<List<RoomDto>>(It.IsAny<string>()))
                .Returns<string>(s => JsonSerializer.Deserialize<List<RoomDto>>(s));
            return mock;
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" \t\u3000")]
        public void BothPaths_GenerateFixedNamesForBlankInput(string input)
        {
            var manager = new RoomManager();
            Assert.Equal("房间 #0000", Create(manager, input, 0).Name);
            Assert.Equal("房间 #0001", manager.CreateSelfServiceRoom("server", "", input, "c", "d").Name);
        }

        [Fact]
        public void ExplicitNamesRemainUnchanged_AndGeneratedNamesSkipExistingNames()
        {
            var manager = new RoomManager();
            Assert.Equal(" 房间 #0000 ", Create(manager, " 房间 #0000 ", 0).Name);
            Assert.Equal("周末电影", manager.CreateSelfServiceRoom("server", "", "周末电影", "c", "d").Name);
            Assert.Equal("房间 #0001", Create(manager, null, 1).Name);
            // Explicit duplicate names were allowed before and remain allowed.
            Assert.Equal("周末电影", Create(manager, "周末电影", 2).Name);
        }

        [Fact]
        public void ConcurrentManualAndInvitationAccepts_DoNotCollide()
        {
            var manager = new RoomManager();
            using var invitations = new RoomInvitationManager();
            var codes = Enumerable.Range(0, 30).Select(i => invitations.Create("creator" + i, "").Code).ToArray();
            Parallel.For(0, 60, i =>
            {
                if (i < 30) Create(manager, null, i);
                else Assert.True(invitations.Accept(codes[i - 30], "accepter" + i, invite =>
                    manager.CreateSelfServiceRoom("server", "", invite.Name, invite.CreatorUserId, "accepter" + i)).Succeeded);
            });
            Assert.Equal(60, manager.ListRooms().Count);
            Assert.Equal(60, manager.ListRooms().Select(r => r.Name).Distinct().Count());
            Assert.All(manager.ListRooms(), r => Assert.Matches("^房间 #[0-9]{4}$", r.Name));
        }

        [Fact]
        public void NamesPersistAcrossReload_AndDeletedNumbersCanBeReused()
        {
            var manager = new RoomManager(new RoomStore(FilePath, Serializer().Object));
            var first = Create(manager, null, 0);
            manager = new RoomManager(new RoomStore(FilePath, Serializer().Object));
            Assert.Equal("房间 #0000", manager.GetRoom(first.Id).Name);
            Assert.Equal("房间 #0001", Create(manager, " ", 1).Name);
            manager.DeleteRoom(first.Id);
            Assert.Equal("房间 #0000", Create(manager, null, 2).Name);
        }

        [Fact]
        public void ExhaustionIsBounded_LeavesInvitationUsable_AndExplicitNamesStillWork()
        {
            // Load the real persisted registry rather than performing 10,000 O(n) inserts.
            var seeded = Enumerable.Range(0, 10000).Select(i => RoomDto.From(new Room(
                "r" + i, "server", "", "房间 #" + i.ToString("D4", CultureInfo.InvariantCulture),
                "admin", "x" + i, new[] { "x" + i, "y" + i }, DateTimeOffset.UtcNow))).ToList();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(seeded));
            var manager = new RoomManager(new RoomStore(FilePath, Serializer().Object));
            Assert.Throws<InvalidOperationException>(() => Create(manager, null, 0));
            using var invitations = new RoomInvitationManager();
            var code = invitations.Create("creator", "").Code;
            Func<RoomInvitation, Room> accept = invite => manager.CreateSelfServiceRoom("server", "", invite.Name, "creator", "accepter");
            Assert.Equal(RoomInvitationManager.RoomUnavailableStatus, invitations.Accept(code, "accepter", accept).Status);
            Assert.Single(invitations.List("creator"));
            Assert.Equal(10000, manager.ListRooms().Count);
            Assert.Equal("自定义", Create(manager, "自定义", 1).Name);
            manager.DeleteRoom("r372");
            var accepted = invitations.Accept(code, "accepter", accept);
            Assert.True(accepted.Succeeded);
            Assert.Equal("房间 #0372", accepted.Room.Name);
            Assert.Empty(invitations.List("creator"));
        }

        [Fact]
        public void StoreFailureDoesNotReserveNameOrMembers()
        {
            var serializer = Serializer();
            var manager = new RoomManager(new RoomStore(FilePath, serializer.Object));
            serializer.Setup(s => s.SerializeToString(It.IsAny<object>())).Throws(new IOException("write failed"));
            Assert.Throws<RoomStoreException>(() => Create(manager, null, 0));
            Assert.Empty(manager.ListRooms());
            serializer.Setup(s => s.SerializeToString(It.IsAny<object>())).Returns<object>(o => JsonSerializer.Serialize(o));
            Assert.Equal("房间 #0000", Create(manager, null, 0).Name);
        }

        [Theory]
        [InlineData("")]
        [InlineData("周末电影")]
        public void InvitationChoosesNameOnlyOnAccept_AndRemainsSingleUse(string name)
        {
            var manager = new RoomManager();
            using var invitations = new RoomInvitationManager();
            var code = invitations.Create("creator", name).Code;
            Create(manager, null, 0);
            var accepted = invitations.Accept(code, "accepter", invite =>
                manager.CreateSelfServiceRoom("server", "", invite.Name, invite.CreatorUserId, "accepter"));
            Assert.True(accepted.Succeeded);
            Assert.Equal(name.Length == 0 ? "房间 #0001" : name, accepted.Room.Name);
            Assert.False(invitations.Accept(code, "third", _ => throw new Exception("must not run")).Succeeded);
        }
    }
}

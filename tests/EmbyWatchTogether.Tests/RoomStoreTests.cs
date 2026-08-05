using System;
using System.IO;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class RoomStoreTests : IDisposable
    {
        private readonly string _directory;
        private readonly string _filePath;

        public RoomStoreTests()
        {
            _directory = Path.Combine(Path.GetTempPath(), "wt-store-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _filePath = Path.Combine(_directory, "rooms.json");
        }

        [Fact]
        public void Create_ThenNewInstance_LoadsPersistedRooms()
        {
            var store = new RoomStore(_filePath);
            var room = NewRoom("r1", new[] { "u1", "u2" }, "u1");
            store.Create(room);

            var reloaded = new RoomStore(_filePath);
            var loaded = reloaded.GetRoom("r1");

            Assert.NotNull(loaded);
            Assert.Equal("u1", loaded.PrimaryUserId);
            Assert.Equal(2, loaded.ParticipantUserIds.Count);
            Assert.Equal("admin-1", loaded.AdminUserId);
        }

        [Fact]
        public void Delete_PersistsRemoval()
        {
            var store = new RoomStore(_filePath);
            store.Create(NewRoom("r1", new[] { "u1", "u2" }, "u1"));

            Assert.True(store.Delete("r1"));
            Assert.False(store.Delete("r1"));
            Assert.Empty(new RoomStore(_filePath).ListRooms());
        }

        [Fact]
        public void Create_DuplicateId_Throws()
        {
            var store = new RoomStore(_filePath);
            store.Create(NewRoom("r1", new[] { "u1", "u2" }, "u1"));

            Assert.Throws<RoomStoreException>(() => store.Create(NewRoom("r1", new[] { "u3", "u4" }, "u3")));
        }

        [Fact]
        public void Create_MemberOverlap_Throws()
        {
            var store = new RoomStore(_filePath);
            store.Create(NewRoom("r1", new[] { "u1", "u2" }, "u1"));

            Assert.Throws<RoomStoreException>(() => store.Create(NewRoom("r2", new[] { "u2", "u3" }, "u2")));
        }

        [Fact]
        public void CorruptFile_ThrowsWithoutOverwriting()
        {
            File.WriteAllText(_filePath, "{ not json");

            Assert.Throws<RoomStoreException>(() => new RoomStore(_filePath));
            Assert.Equal("{ not json", File.ReadAllText(_filePath));
        }

        [Fact]
        public void MissingFile_StartsEmpty()
        {
            var store = new RoomStore(_filePath);

            Assert.Empty(store.ListRooms());
        }

        [Fact]
        public void RoomManager_WithStore_LoadsRoomsAndPersistsCreateDelete()
        {
            var store = new RoomStore(_filePath);
            var manager = new RoomManager(store);
            var room = manager.CreateRoom(
                "server-1", "http://emby", "n", "admin-1", new[] { "u1", "u2" }, "u1");

            var reloadedManager = new RoomManager(new RoomStore(_filePath));
            Assert.NotNull(reloadedManager.GetRoom(room.Id));

            manager.DeleteRoom(room.Id);
            Assert.Empty(new RoomStore(_filePath).ListRooms());
        }

        private static Room NewRoom(string id, string[] participants, string primary)
        {
            return new Room(
                id, "server-1", "http://emby", "room-" + id, "admin-1", primary,
                participants, DateTimeOffset.UtcNow);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }
    }
}

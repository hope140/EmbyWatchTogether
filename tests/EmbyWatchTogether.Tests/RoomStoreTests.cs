using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MediaBrowser.Model.Serialization;
using Moq;
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
            var store = new RoomStore(_filePath, NewSerializer());
            var room = NewRoom("r1", new[] { "u1", "u2" }, "u1");
            store.Create(room);

            var reloaded = new RoomStore(_filePath, NewSerializer());
            var loaded = reloaded.GetRoom("r1");

            Assert.NotNull(loaded);
            Assert.Equal("u1", loaded.PrimaryUserId);
            Assert.Equal(2, loaded.ParticipantUserIds.Count);
            Assert.Equal("admin-1", loaded.AdminUserId);
        }

        [Fact]
        public void Delete_PersistsRemoval()
        {
            var store = new RoomStore(_filePath, NewSerializer());
            store.Create(NewRoom("r1", new[] { "u1", "u2" }, "u1"));

            Assert.True(store.Delete("r1"));
            Assert.False(store.Delete("r1"));
            Assert.Empty(new RoomStore(_filePath, NewSerializer()).ListRooms());
        }

        [Fact]
        public void Update_AtomicallyReplacesFile_AndKeepsBackup()
        {
            var store = new RoomStore(_filePath, NewSerializer());
            store.Create(NewRoom("r1", new[] { "u1", "u2" }, "u1"));
            string previousPayload = File.ReadAllText(_filePath);

            store.Update(new Room(
                "r1", "server-1", "http://emby", "room-r1", "admin-1", "u1",
                new[] { "u1", "u2" }, new[] { "u1" }, DateTimeOffset.UtcNow));

            Assert.NotEqual(previousPayload, File.ReadAllText(_filePath));
            Assert.Equal(previousPayload, File.ReadAllText(_filePath + ".bak"));
            Assert.Single(new RoomStore(_filePath, NewSerializer()).GetRoom("r1").JoinedParticipantUserIds);

            string firstUpdatedPayload = File.ReadAllText(_filePath);
            store.Update(new Room(
                "r1", "server-1", "http://emby", "room-r1-again", "admin-1", "u1",
                new[] { "u1", "u2" }, new[] { "u1", "u2" }, DateTimeOffset.UtcNow));

            Assert.NotEqual(firstUpdatedPayload, File.ReadAllText(_filePath));
            Assert.Equal(firstUpdatedPayload, File.ReadAllText(_filePath + ".bak"));
        }

        [Fact]
        public void Delete_WriteFailure_LeavesStoreAndDiskUnchanged()
        {
            var serializer = NewSerializerMock();
            var store = new RoomStore(_filePath, serializer.Object);
            var room = NewRoom("r1", new[] { "u1", "u2" }, "u1");
            store.Create(room);
            string previousPayload = File.ReadAllText(_filePath);

            serializer
                .Setup(s => s.SerializeToString(It.IsAny<object>()))
                .Throws(new IOException("simulated disk full"));

            Assert.Throws<RoomStoreException>(() => store.Delete(room.Id));

            Assert.Same(room, store.GetRoom(room.Id));
            Assert.Equal(previousPayload, File.ReadAllText(_filePath));
            Assert.NotNull(new RoomStore(_filePath, NewSerializer()).GetRoom(room.Id));
        }

        [Fact]
        public void RoomManager_WriteFailure_LeavesMemoryRuntimeAndDiskUnchanged()
        {
            var serializer = NewSerializerMock();
            var store = new RoomStore(_filePath, serializer.Object);
            var manager = new RoomManager(store);
            var room = manager.CreateRoom(
                "server-1", "http://emby", "room", "admin-1", new[] { "u1", "u2" }, "u1");
            manager.GetRuntime(room.Id).State = RoomState.Watching;
            string previousPayload = File.ReadAllText(_filePath);

            serializer
                .Setup(s => s.SerializeToString(It.IsAny<object>()))
                .Throws(new IOException("simulated disk full"));

            Assert.Throws<RoomStoreException>(() => manager.SetParticipantJoined(room.Id, "u2", false));

            Assert.True(room.IsJoined("u2"));
            Assert.Equal(RoomState.Watching, manager.GetRuntime(room.Id).State);
            Assert.Equal(previousPayload, File.ReadAllText(_filePath));
            Assert.Equal("u2", new RoomStore(_filePath, NewSerializer())
                .GetRoom(room.Id).JoinedParticipantUserIds.Single(user => user == "u2"));
        }

        [Fact]
        public void Create_DuplicateId_Throws()
        {
            var store = new RoomStore(_filePath, NewSerializer());
            store.Create(NewRoom("r1", new[] { "u1", "u2" }, "u1"));

            Assert.Throws<RoomStoreException>(() => store.Create(NewRoom("r1", new[] { "u3", "u4" }, "u3")));
        }

        [Fact]
        public void Create_MemberOverlap_Throws()
        {
            var store = new RoomStore(_filePath, NewSerializer());
            store.Create(NewRoom("r1", new[] { "u1", "u2" }, "u1"));

            Assert.Throws<RoomStoreException>(() => store.Create(NewRoom("r2", new[] { "u2", "u3" }, "u2")));
        }

        [Fact]
        public void CorruptFile_ThrowsWithoutOverwriting()
        {
            File.WriteAllText(_filePath, "{ not json");

            Assert.Throws<RoomStoreException>(() => new RoomStore(_filePath, NewSerializer()));
            Assert.Equal("{ not json", File.ReadAllText(_filePath));
        }

        [Fact]
        public void InvalidRoomData_ThrowsWithoutOverwriting()
        {
            string invalidPayload = JsonSerializer.Serialize(new[]
            {
                new RoomDto
                {
                    Id = "r1",
                    ServerId = "server-1",
                    AdminUserId = "admin-1",
                    PrimaryUserId = "u1",
                    ParticipantUserIds = new List<string> { "u1" },
                    JoinedParticipantUserIds = new List<string> { "u1" },
                    CreatedAtUtc = DateTimeOffset.UtcNow.ToString("o"),
                },
            });
            File.WriteAllText(_filePath, invalidPayload);

            Assert.Throws<RoomStoreException>(() => new RoomStore(_filePath, NewSerializer()));
            Assert.Equal(invalidPayload, File.ReadAllText(_filePath));
        }

        [Fact]
        public void MissingFile_StartsEmpty()
        {
            var store = new RoomStore(_filePath, NewSerializer());

            Assert.Empty(store.ListRooms());
        }

        [Fact]
        public void RoomManager_WithStore_LoadsRoomsAndPersistsCreateDelete()
        {
            var store = new RoomStore(_filePath, NewSerializer());
            var manager = new RoomManager(store);
            var room = manager.CreateRoom(
                "server-1", "http://emby", "n", "admin-1", new[] { "u1", "u2" }, "u1");

            var reloadedManager = new RoomManager(new RoomStore(_filePath, NewSerializer()));
            Assert.NotNull(reloadedManager.GetRoom(room.Id));

            manager.DeleteRoom(room.Id);
            Assert.Empty(new RoomStore(_filePath, NewSerializer()).ListRooms());
        }

        private static IJsonSerializer NewSerializer()
        {
            return NewSerializerMock().Object;
        }

        private static Mock<IJsonSerializer> NewSerializerMock()
        {
            var mock = new Mock<IJsonSerializer>();
            mock.Setup(s => s.SerializeToString(It.IsAny<object>()))
                .Returns<object>(o => JsonSerializer.Serialize(o, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            mock.Setup(s => s.DeserializeFromString<List<RoomDto>>(It.IsAny<string>()))
                .Returns<string>(json => JsonSerializer.Deserialize<List<RoomDto>>(
                    json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }));
            return mock;
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

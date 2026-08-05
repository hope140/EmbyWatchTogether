using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Model.Serialization;

namespace Emby.Plugins.WatchTogether
{
    public sealed class RoomStoreException : Exception
    {
        public RoomStoreException(string message, Exception innerException = null)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// JSON persistence for room metadata. Runtime state is never persisted.
    /// A corrupt file raises RoomStoreException instead of being overwritten
    /// (same policy as the Python reference store).
    /// </summary>
    public sealed class RoomStore
    {
        private readonly string _filePath;
        private readonly IJsonSerializer _serializer;
        private readonly object _lock = new object();
        private readonly Dictionary<string, Room> _rooms =
            new Dictionary<string, Room>(StringComparer.OrdinalIgnoreCase);

        public RoomStore(string filePath, IJsonSerializer serializer)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("filePath is required", nameof(filePath));
            }

            _filePath = filePath;
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            Reload();
        }

        public string FilePath => _filePath;

        public IReadOnlyList<Room> ListRooms()
        {
            lock (_lock)
            {
                return _rooms.Values.ToList();
            }
        }

        public Room GetRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                return null;
            }

            lock (_lock)
            {
                _rooms.TryGetValue(roomId, out var room);
                return room;
            }
        }

        public void Create(Room room)
        {
            if (room == null)
            {
                throw new ArgumentNullException(nameof(room));
            }

            lock (_lock)
            {
                if (_rooms.ContainsKey(room.Id))
                {
                    throw new RoomStoreException($"room id already exists: {room.Id}");
                }

                ValidateMemberOverlap(room);
                _rooms[room.Id] = room;
                Write();
            }
        }

        public bool Delete(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                return false;
            }

            lock (_lock)
            {
                if (!_rooms.Remove(roomId))
                {
                    return false;
                }

                Write();
                return true;
            }
        }

        public void Update(Room room)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            lock (_lock)
            {
                if (!_rooms.ContainsKey(room.Id)) throw new RoomStoreException($"room not found: {room.Id}");
                _rooms[room.Id] = room;
                Write();
            }
        }

        private void Reload()
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            try
            {
                var dtos = _serializer.DeserializeFromString<List<RoomDto>>(File.ReadAllText(_filePath));
                if (dtos == null)
                {
                    return;
                }

                var loaded = new Dictionary<string, Room>(StringComparer.OrdinalIgnoreCase);
                foreach (var dto in dtos)
                {
                    var room = dto.ToRoom();
                    if (loaded.ContainsKey(room.Id))
                    {
                        throw new RoomStoreException($"duplicate room id in store: {room.Id}");
                    }

                    ValidateMemberOverlap(room, loaded.Values);
                    loaded[room.Id] = room;
                }

                _rooms.Clear();
                foreach (var pair in loaded)
                {
                    _rooms[pair.Key] = pair.Value;
                }
            }
            catch (RoomStoreException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new RoomStoreException($"failed to load rooms file: {_filePath}", ex);
            }
        }

        private void Write()
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var payload = _serializer.SerializeToString(_rooms.Values.Select(RoomDto.From).ToList());
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, payload);
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }

            File.Move(tempPath, _filePath);
        }

        private void ValidateMemberOverlap(Room candidate, IEnumerable<Room> existing = null)
        {
            var rooms = existing ?? _rooms.Values;
            foreach (var member in candidate.ParticipantUserIds)
            {
                if (rooms.Any(r => !string.Equals(r.Id, candidate.Id, StringComparison.OrdinalIgnoreCase) &&
                                   r.HasParticipant(member)))
                {
                    throw new RoomStoreException($"user {member} is already a member of another room");
                }
            }
        }
    }

    public sealed class RoomDto
    {
        public string Id { get; set; }

        public string ServerId { get; set; }

        public string ServerUrl { get; set; }

        public string Name { get; set; }

        public string AdminUserId { get; set; }

        public string PrimaryUserId { get; set; }

        public List<string> ParticipantUserIds { get; set; }

        public List<string> JoinedParticipantUserIds { get; set; }

        public string CreatedAtUtc { get; set; }

        public static RoomDto From(Room room)
        {
            return new RoomDto
            {
                Id = room.Id,
                ServerId = room.ServerId,
                ServerUrl = room.ServerUrl,
                Name = room.Name,
                AdminUserId = room.AdminUserId,
                PrimaryUserId = room.PrimaryUserId,
                ParticipantUserIds = room.ParticipantUserIds.ToList(),
                JoinedParticipantUserIds = room.JoinedParticipantUserIds.ToList(),
                CreatedAtUtc = room.CreatedAtUtc.ToString("o"),
            };
        }

        public Room ToRoom()
        {
            return new Room(
                Id,
                ServerId,
                ServerUrl,
                Name,
                AdminUserId,
                PrimaryUserId,
                ParticipantUserIds ?? new List<string>(),
                JoinedParticipantUserIds ?? ParticipantUserIds,
                DateTimeOffset.Parse(CreatedAtUtc));
        }
    }
}

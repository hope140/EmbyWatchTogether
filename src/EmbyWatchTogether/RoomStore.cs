using System;
using System.Collections.Generic;
using System.Globalization;
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

                ValidateRoom(room);
                ValidateMemberOverlap(room);
                var candidate = _rooms.Values.Concat(new[] { room }).ToList();
                Write(candidate);
                _rooms[room.Id] = room;
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
                if (!_rooms.ContainsKey(roomId))
                {
                    return false;
                }

                var candidate = _rooms.Values
                    .Where(room => !string.Equals(room.Id, roomId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                Write(candidate);
                _rooms.Remove(roomId);
                return true;
            }
        }

        public void Update(Room room)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            lock (_lock)
            {
                if (!_rooms.ContainsKey(room.Id)) throw new RoomStoreException($"room not found: {room.Id}");
                ValidateRoom(room);
                ValidateMemberOverlap(room);
                var candidate = _rooms.Values
                    .Select(existing => string.Equals(existing.Id, room.Id, StringComparison.OrdinalIgnoreCase)
                        ? room
                        : existing)
                    .ToList();
                Write(candidate);
                _rooms[room.Id] = room;
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
                    throw new RoomStoreException("rooms file must contain a JSON array");
                }

                var loaded = new Dictionary<string, Room>(StringComparer.OrdinalIgnoreCase);
                foreach (var dto in dtos)
                {
                    ValidateDto(dto);
                    var room = dto.ToRoom();
                    ValidateRoom(room);
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

        private void Write(IEnumerable<Room> rooms)
        {
            string payload;
            try
            {
                var snapshot = rooms.Select(RoomDto.From).ToList();
                payload = _serializer.SerializeToString(snapshot);
            }
            catch (Exception ex)
            {
                throw new RoomStoreException($"failed to serialize rooms file: {_filePath}", ex);
            }

            var tempPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(tempPath, payload);
                if (File.Exists(_filePath))
                {
                    File.Replace(tempPath, _filePath, _filePath + ".bak", ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, _filePath);
                }
            }
            catch (Exception ex)
            {
                throw new RoomStoreException($"failed to persist rooms file: {_filePath}", ex);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // A failed cleanup must not hide the original persistence error.
                }
            }
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

        private static void ValidateDto(RoomDto dto)
        {
            if (dto == null)
            {
                throw new RoomStoreException("room entry must not be null");
            }

            ValidateRequired(dto.Id, "id");
            ValidateRequired(dto.ServerId, "serverId", dto.Id);
            ValidateRequired(dto.AdminUserId, "adminUserId", dto.Id);
            ValidateRequired(dto.PrimaryUserId, "primaryUserId", dto.Id);

            if (dto.ParticipantUserIds == null || dto.ParticipantUserIds.Count != 2 ||
                dto.ParticipantUserIds.Any(string.IsNullOrWhiteSpace) ||
                dto.ParticipantUserIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2)
            {
                throw new RoomStoreException($"room {dto.Id} must contain exactly two distinct participants");
            }

            if (!dto.ParticipantUserIds.Contains(dto.PrimaryUserId, StringComparer.OrdinalIgnoreCase))
            {
                throw new RoomStoreException($"room {dto.Id} primary user must be a participant");
            }

            if (dto.JoinedParticipantUserIds != null &&
                (dto.JoinedParticipantUserIds.Any(string.IsNullOrWhiteSpace) ||
                 dto.JoinedParticipantUserIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                 dto.JoinedParticipantUserIds.Count ||
                 dto.JoinedParticipantUserIds.Any(user =>
                     !dto.ParticipantUserIds.Contains(user, StringComparer.OrdinalIgnoreCase))))
            {
                throw new RoomStoreException($"room {dto.Id} has invalid joined participants");
            }

            if (!DateTimeOffset.TryParse(
                dto.CreatedAtUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out _))
            {
                throw new RoomStoreException($"room {dto.Id} has invalid createdAtUtc");
            }
        }

        private static void ValidateRoom(Room room)
        {
            ValidateRequired(room.Id, "id");
            ValidateRequired(room.ServerId, "serverId", room.Id);
            ValidateRequired(room.AdminUserId, "adminUserId", room.Id);
            ValidateRequired(room.PrimaryUserId, "primaryUserId", room.Id);

            if (room.ParticipantUserIds == null || room.ParticipantUserIds.Count != 2 ||
                room.ParticipantUserIds.Any(string.IsNullOrWhiteSpace) ||
                room.ParticipantUserIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2)
            {
                throw new RoomStoreException($"room {room.Id} must contain exactly two distinct participants");
            }

            if (!room.ParticipantUserIds.Contains(room.PrimaryUserId, StringComparer.OrdinalIgnoreCase))
            {
                throw new RoomStoreException($"room {room.Id} primary user must be a participant");
            }

            if (room.JoinedParticipantUserIds == null ||
                room.JoinedParticipantUserIds.Any(string.IsNullOrWhiteSpace) ||
                room.JoinedParticipantUserIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                room.JoinedParticipantUserIds.Count ||
                room.JoinedParticipantUserIds.Any(user =>
                    !room.ParticipantUserIds.Contains(user, StringComparer.OrdinalIgnoreCase)))
            {
                throw new RoomStoreException($"room {room.Id} has invalid joined participants");
            }

            if (room.CreatedAtUtc == default(DateTimeOffset))
            {
                throw new RoomStoreException($"room {room.Id} has invalid createdAtUtc");
            }
        }

        private static void ValidateRequired(string value, string field, string roomId = null)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string prefix = string.IsNullOrWhiteSpace(roomId) ? "room" : $"room {roomId}";
            throw new RoomStoreException($"{prefix} {field} is required");
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
                CreatedAtUtc = room.CreatedAtUtc.ToString("o", CultureInfo.InvariantCulture),
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
                DateTimeOffset.Parse(CreatedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        }
    }
}

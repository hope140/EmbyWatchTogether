using System;
using System.Collections.Generic;
using System.Linq;

namespace Emby.Plugins.WatchTogether
{
    public sealed class RoomActionResult
    {
        public string RoomId { get; set; }

        public RoomState State { get; set; }

        public string Command { get; set; }

        public IReadOnlyList<string> Users { get; set; } = Array.Empty<string>();

        public string Error { get; set; }
    }

    /// <summary>
    /// In-memory room registry and lifecycle. Persistence is added by the S5
    /// stack; runtime state is intentionally not persisted.
    /// </summary>
    public sealed class RoomManager
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, Room> _rooms = new Dictionary<string, Room>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RoomRuntime> _runtimes = new Dictionary<string, RoomRuntime>(StringComparer.OrdinalIgnoreCase);

        public Room CreateRoom(
            string serverId,
            string serverUrl,
            string name,
            string adminUserId,
            IReadOnlyList<string> participantUserIds,
            string primaryUserId,
            DateTimeOffset? now = null)
        {
            if (string.IsNullOrWhiteSpace(serverId))
            {
                throw new ArgumentException("serverId is required", nameof(serverId));
            }

            if (string.IsNullOrWhiteSpace(adminUserId))
            {
                throw new ArgumentException("adminUserId is required", nameof(adminUserId));
            }

            var members = (participantUserIds ?? Array.Empty<string>())
                .Select(u => (u ?? string.Empty).Trim())
                .Where(u => u.Length > 0)
                .ToList();

            if (members.Count != 2 || members.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2)
            {
                throw new ArgumentException("participantUserIds must contain exactly two distinct users", nameof(participantUserIds));
            }

            if (string.IsNullOrWhiteSpace(primaryUserId) ||
                !members.Contains(primaryUserId, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException("primaryUserId must be one of the participants", nameof(primaryUserId));
            }

            lock (_lock)
            {
                foreach (var member in members)
                {
                    if (_rooms.Values.Any(r => r.HasParticipant(member)))
                    {
                        throw new InvalidOperationException(
                            $"user {member} is already a member of another room");
                    }
                }

                var room = new Room(
                    id: Guid.NewGuid().ToString("N"),
                    serverId: serverId,
                    serverUrl: serverUrl ?? string.Empty,
                    name: name ?? string.Empty,
                    adminUserId: adminUserId,
                    primaryUserId: primaryUserId,
                    participantUserIds: members,
                    createdAtUtc: now ?? DateTimeOffset.UtcNow);

                _rooms[room.Id] = room;
                _runtimes[room.Id] = new RoomRuntime();
                return room;
            }
        }

        public bool DeleteRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                return false;
            }

            lock (_lock)
            {
                bool removed = _rooms.Remove(roomId);
                _runtimes.Remove(roomId);
                return removed;
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

        public IReadOnlyList<Room> ListRooms()
        {
            lock (_lock)
            {
                return _rooms.Values.ToList();
            }
        }

        public RoomRuntime GetRuntime(string roomId)
        {
            lock (_lock)
            {
                if (!_runtimes.TryGetValue(roomId, out var runtime))
                {
                    runtime = new RoomRuntime();
                    _runtimes[roomId] = runtime;
                }

                return runtime;
            }
        }

        /// <summary>
        /// Manual room action: pause, resume or resync (ported from Python action()).
        /// Pause/resume issue the matching command to every online participant;
        /// resync resets the runtime to waiting, allowing a new barrier.
        /// </summary>
        public RoomActionResult Action(
            string roomId,
            string action,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            ICommandIssuer issuer,
            DateTimeOffset now)
        {
            var room = GetRoom(roomId);
            if (room == null)
            {
                throw new KeyNotFoundException("room not found");
            }

            action = (action ?? string.Empty).ToLowerInvariant();
            if (action != "pause" && action != "resume" && action != "resync")
            {
                throw new ArgumentException($"unknown room action: {action}", nameof(action));
            }

            var runtime = GetRuntime(roomId);
            lock (_lock)
            {
                if (action == "resync")
                {
                    runtime.ResetToWaiting();
                    return new RoomActionResult { RoomId = roomId, State = runtime.State };
                }

                string command = action == "pause" ? RemoteCommands.Pause : RemoteCommands.Unpause;
                var issued = new List<string>();
                foreach (var user in room.ParticipantUserIds)
                {
                    if (!snapshots.TryGetValue(user, out var snapshot) || snapshot == null || !snapshot.Online)
                    {
                        continue;
                    }

                    if (issuer == null)
                    {
                        continue;
                    }

                    if (issuer.TryIssue(roomId, room.AdminUserId, user, snapshot, command, positionTicks: null, now, out string error))
                    {
                        runtime.Pending[user] = new PendingCommand
                        {
                            UserId = user,
                            Command = command,
                            PositionTicks = null,
                            IssuedAtUtc = now,
                            Retries = 0,
                        };
                        issued.Add(user);
                    }
                    else
                    {
                        runtime.Error = $"{command} command failed: {error}";
                    }
                }

                return new RoomActionResult
                {
                    RoomId = roomId,
                    State = runtime.State,
                    Command = command,
                    Users = issued,
                    Error = runtime.Error,
                };
            }
        }
    }
}

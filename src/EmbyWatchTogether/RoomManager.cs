using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

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

    public sealed class ParticipantStateChangeResult
    {
        public string RoomId { get; set; }

        public string UserId { get; set; }

        public bool Joined { get; set; }

        public bool Changed { get; set; }

        public RoomState PreviousState { get; set; }

        public string ServerId { get; set; }
    }

    /// <summary>
    /// In-memory room registry and lifecycle. Runtime state is intentionally not
    /// persisted; metadata changes are committed to the store before the
    /// corresponding in-memory transition.
    /// </summary>
    public sealed class RoomManager
    {
        private readonly object _lock = new object();
        private readonly RoomStore _store;
        private readonly Dictionary<string, Room> _rooms = new Dictionary<string, Room>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RoomRuntime> _runtimes = new Dictionary<string, RoomRuntime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, object> _roomGates = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Shared registry lock used by the manager and the sync engine for
        /// short-lived room lookup/revalidation. Runtime operations use the
        /// per-room gate so external command I/O never runs under this lock.
        /// </summary>
        public object SyncRoot => _lock;

        public RoomManager(RoomStore store = null)
        {
            _store = store;
            if (_store != null)
            {
                foreach (var room in _store.ListRooms())
                {
                    _rooms[room.Id] = room;
                    _runtimes[room.Id] = new RoomRuntime();
                    _roomGates[room.Id] = new object();
                }
            }
        }

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
                    joinedParticipantUserIds: members,
                    createdAtUtc: now ?? DateTimeOffset.UtcNow);

                _store?.Create(room);
                _rooms[room.Id] = room;
                _runtimes[room.Id] = new RoomRuntime();
                _roomGates[room.Id] = new object();
                return room;
            }
        }

        public bool DeleteRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                return false;
            }

            object roomGate;
            lock (_lock)
            {
                if (!_rooms.ContainsKey(roomId))
                {
                    return false;
                }

                roomGate = _roomGates[roomId];
            }

            lock (roomGate)
            {
                lock (_lock)
                {
                    if (!_rooms.ContainsKey(roomId))
                    {
                        return false;
                    }

                    if (_store != null && !_store.Delete(roomId))
                    {
                        return false;
                    }

                    _rooms.Remove(roomId);
                    _runtimes.Remove(roomId);
                    _roomGates.Remove(roomId);
                    return true;
                }
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
            if (string.IsNullOrEmpty(roomId))
            {
                return null;
            }

            lock (_lock)
            {
                if (!_rooms.ContainsKey(roomId))
                {
                    return null;
                }

                _runtimes.TryGetValue(roomId, out var runtime);
                return runtime;
            }
        }

        /// <summary>
        /// Enters the atomic operation scope for an existing room. The room
        /// and runtime are revalidated after acquiring the per-room gate so a
        /// stale ListRooms result cannot create or operate on a deleted room.
        /// </summary>
        internal RoomAccess TryEnterRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                return null;
            }

            object roomGate;
            lock (_lock)
            {
                if (!_rooms.ContainsKey(roomId) || !_roomGates.TryGetValue(roomId, out roomGate))
                {
                    return null;
                }
            }

            Monitor.Enter(roomGate);
            lock (_lock)
            {
                if (!_rooms.TryGetValue(roomId, out var room) ||
                    !_runtimes.TryGetValue(roomId, out var runtime))
                {
                    Monitor.Exit(roomGate);
                    return null;
                }

                return new RoomAccess(roomGate, room, runtime);
            }
        }

        public bool SetParticipantJoined(string roomId, string userId, bool joined)
        {
            return SetParticipantJoinedResult(roomId, userId, joined).Changed;
        }

        public bool LeaveParticipant(string roomId, string userId)
        {
            return LeaveParticipantResult(roomId, userId).Changed;
        }

        public ParticipantStateChangeResult LeaveParticipantResult(string roomId, string userId)
        {
            return SetParticipantJoinedResult(roomId, userId, joined: false);
        }

        public ParticipantStateChangeResult SetParticipantJoinedResult(string roomId, string userId, bool joined)
        {
            using (var access = TryEnterRoom(roomId))
            {
                if (access == null || !access.Room.HasParticipant(userId))
                {
                    throw new KeyNotFoundException("room participant not found");
                }

                lock (_lock)
                {
                    var room = access.Room;
                    var runtime = access.Runtime;
                    var result = new ParticipantStateChangeResult
                    {
                        RoomId = room.Id,
                        UserId = userId,
                        Joined = joined,
                        Changed = room.IsJoined(userId) != joined,
                        PreviousState = runtime.State,
                        ServerId = room.ServerId,
                    };

                    if (!result.Changed)
                    {
                        return result;
                    }

                    if (_store != null)
                    {
                        var joinedUsers = room.JoinedParticipantUserIds.ToList();
                        if (joined)
                        {
                            joinedUsers.Add(userId);
                        }
                        else
                        {
                            joinedUsers.RemoveAll(u => string.Equals(u, userId, StringComparison.OrdinalIgnoreCase));
                        }

                        var candidate = new Room(
                            room.Id,
                            room.ServerId,
                            room.ServerUrl,
                            room.Name,
                            room.AdminUserId,
                            room.PrimaryUserId,
                            room.ParticipantUserIds,
                            joinedUsers,
                            room.CreatedAtUtc);
                        _store.Update(candidate);
                    }

                    if (!room.SetJoined(userId, joined))
                    {
                        throw new InvalidOperationException("room participant state changed unexpectedly");
                    }

                    runtime.ResetToWaiting();
                    return result;
                }
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
            action = (action ?? string.Empty).ToLowerInvariant();
            if (action != "pause" && action != "resume" && action != "resync")
            {
                throw new ArgumentException($"unknown room action: {action}", nameof(action));
            }

            using (var access = TryEnterRoom(roomId))
            {
                if (access == null)
                {
                    throw new KeyNotFoundException("room not found");
                }

                var room = access.Room;
                var runtime = access.Runtime;
                if (action == "resync")
                {
                    lock (_lock)
                    {
                        runtime.ResetToWaiting();
                        return new RoomActionResult { RoomId = roomId, State = runtime.State };
                    }
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
                        lock (_lock)
                        {
                            runtime.Pending[user] = new PendingCommand
                            {
                                UserId = user,
                                SessionId = snapshot.SessionId,
                                ItemId = snapshot.ItemId,
                                Command = command,
                                PositionTicks = null,
                                IssuedAtUtc = now,
                                Retries = 0,
                            };
                            issued.Add(user);
                        }
                    }
                    else
                    {
                        lock (_lock)
                        {
                            runtime.Error = $"{command} command failed: {error}";
                        }
                    }
                }

                lock (_lock)
                {
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

        internal sealed class RoomAccess : IDisposable
        {
            private readonly object _gate;
            private bool _disposed;

            internal RoomAccess(object gate, Room room, RoomRuntime runtime)
            {
                _gate = gate;
                Room = room;
                Runtime = runtime;
            }

            internal Room Room { get; }

            internal RoomRuntime Runtime { get; }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                Monitor.Exit(_gate);
            }
        }
    }
}

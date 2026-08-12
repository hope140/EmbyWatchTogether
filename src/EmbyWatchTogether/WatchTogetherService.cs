using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MediaBrowser.Controller.Api;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace Emby.Plugins.WatchTogether
{
    [Route("/WatchTogether/Rooms", "GET")]
    public class GetRoomsRequest { }

    [Route("/WatchTogether/Rooms", "POST")]
    public class CreateRoomRequest
    {
        public string Name { get; set; }

        public string[] ParticipantUserIds { get; set; }

        public string PrimaryUserId { get; set; }
    }

    [Route("/WatchTogether/Rooms/{Id}", "DELETE")]
    public class DeleteRoomRequest
    {
        public string Id { get; set; }
    }

    [Route("/WatchTogether/Rooms/{Id}/Action", "POST")]
    public class ControlRoomRequest
    {
        public string Id { get; set; }

        public string Action { get; set; }
    }

    [Route("/WatchTogether/Rooms/{Id}/State", "GET")]
    public class GetRoomStateRequest
    {
        public string Id { get; set; }
    }

    [Route("/WatchTogether/Rooms/{Id}/Join", "POST")]
    public class JoinRoomRequest
    {
        public string Id { get; set; }
    }

    [Route("/WatchTogether/Rooms/{Id}/Leave", "POST")]
    public class LeaveRoomRequest
    {
        public string Id { get; set; }
    }

    [Route("/WatchTogether/Rooms/{Id}/Message", "POST")]
    public class SendRoomMessageRequest
    {
        public string Id { get; set; }

        public string Text { get; set; }
    }

    [Route("/WatchTogether/Users", "GET")]
    public class GetUsersRequest { }

    [Route("/WatchTogether/Info", "GET")]
    public class GetPluginInfoRequest { }

    /// <summary>
    /// REST API for room management and remote control. Admin endpoints verify
    /// the caller's user policy; participant endpoints verify membership.
    /// </summary>
    [Authenticated]
    public class WatchTogetherService : BaseApiService, IService
    {
        public object Get(GetRoomsRequest request)
        {
            var plugin = RequirePlugin();
            if (plugin.Rooms == null)
            {
                // The entry point may still be starting (server restart);
                // return an empty list instead of failing the poll.
                return new List<object>();
            }

            string currentUserId = CurrentUserId();
            bool admin = IsAdmin();
            return plugin.Rooms.ListRooms().Select(r =>
            {
                if (!admin && !r.HasParticipant(currentUserId)) return null;
                var runtime = plugin.Rooms.GetRuntime(r.Id);
                if (runtime == null) return null;
                return new
                {
                    RoomId = r.Id,
                    Name = r.Name,
                    State = runtime.State.ToString(),
                    Error = runtime.Error,
                    StatusReason = GetStatusReason(plugin, r, runtime),
                    PrimaryUserId = r.PrimaryUserId,
                    ParticipantUserIds = r.ParticipantUserIds,
                    JoinedParticipantUserIds = r.JoinedParticipantUserIds,
                    CurrentUserJoined = r.IsJoined(currentUserId),
                    IsAdmin = admin,
                    CreatedAtUtc = r.CreatedAtUtc,
                };
            }).Where(x => x != null).ToList();
        }

        public object Post(CreateRoomRequest request)
        {
            RequireAdmin();
            var plugin = RequirePlugin();
            string serverId = plugin.ResolveServerId();
            if (string.IsNullOrWhiteSpace(serverId))
            {
                throw new ArgumentException("server identity is not resolved yet");
            }

            var room = plugin.Rooms.CreateRoom(
                serverId: serverId,
                serverUrl: string.Empty,
                name: request.Name,
                adminUserId: CurrentUserId(),
                participantUserIds: request.ParticipantUserIds ?? Array.Empty<string>(),
                primaryUserId: request.PrimaryUserId);

            return new
            {
                RoomId = room.Id,
                State = "Waiting",
                Name = room.Name,
                PrimaryUserId = room.PrimaryUserId,
                ParticipantUserIds = room.ParticipantUserIds,
            };
        }

        public object Delete(DeleteRoomRequest request)
        {
            RequireAdmin();
            var plugin = RequirePlugin();
            return new { Deleted = plugin.Rooms.DeleteRoom(request.Id) };
        }

        public object Post(ControlRoomRequest request)
        {
            RequireAdmin();
            var plugin = RequirePlugin();
            var room = plugin.Rooms.GetRoom(request.Id);
            if (room == null)
            {
                throw new KeyNotFoundException("room not found");
            }

            IReadOnlyDictionary<string, SessionSnapshot> actionSnapshots = null;
            var result = plugin.Rooms.Action(
                request.Id,
                request.Action,
                null,
                plugin.Issuer,
                DateTimeOffset.UtcNow,
                plugin.ResolveServerId,
                currentRoom => actionSnapshots = BuildSnapshots(plugin, currentRoom));
            if ((request.Action ?? string.Empty).Equals("pause", StringComparison.OrdinalIgnoreCase) ||
                (request.Action ?? string.Empty).Equals("resume", StringComparison.OrdinalIgnoreCase))
            {
                NotifyAdminPlaybackAction(plugin, result, request.Action, actionSnapshots);
            }
            else if ((request.Action ?? string.Empty).Equals("resync", StringComparison.OrdinalIgnoreCase))
            {
                NotifyAdminResync(plugin, request.Id);
            }
            return new
            {
                RoomId = result.RoomId,
                State = result.State.ToString(),
                Command = result.Command,
                Users = result.Users,
                Error = result.Error,
            };
        }

        public object Get(GetRoomStateRequest request)
        {
            var plugin = RequirePlugin();
            var room = plugin.Rooms.GetRoom(request.Id);
            if (room == null)
            {
                throw new KeyNotFoundException("room not found");
            }

            bool admin = IsAdmin();
            bool participant = room.HasParticipant(CurrentUserId());
            if (!admin && !participant)
            {
                throw new UnauthorizedAccessException("not a room participant");
            }

            var runtime = plugin.Rooms.GetRuntime(request.Id);
            if (runtime == null)
            {
                throw new KeyNotFoundException("room not found");
            }

            var snapshots = BuildSnapshots(plugin, room);
            return new
            {
                RoomId = room.Id,
                Name = room.Name,
                State = runtime.State.ToString(),
                Error = runtime.Error,
                StatusReason = GetStatusReason(plugin, room, runtime),
                Eligible = RoomEligibility.IsPairEligible(snapshots),
                SyncItemId = runtime.SyncItemId,
                PrimaryUserId = room.PrimaryUserId,
                ParticipantUserIds = room.ParticipantUserIds,
                JoinedParticipantUserIds = room.JoinedParticipantUserIds,
                CurrentUserJoined = room.IsJoined(CurrentUserId()),
                Sessions = snapshots.Values.Select(s => new
                {
                    UserId = s.UserId,
                    SessionId = s.SessionId,
                    ItemId = s.ItemId,
                    PositionTicks = s.PositionTicks,
                    IsPaused = s.IsPaused,
                    Online = s.Online,
                    SupportsRemoteControl = s.SupportsRemoteControl,
                }),
            };
        }

        public object Post(JoinRoomRequest request)
        {
            var plugin = RequirePlugin();
            string userId = CurrentUserId();
            var room = plugin.Rooms.GetRoom(request.Id);
            if (room == null || !room.HasParticipant(userId)) throw new UnauthorizedAccessException("not a room participant");
            var transition = plugin.Rooms.SetParticipantJoinedResult(request.Id, userId, true);
            if (transition.Changed)
            {
                NotifyMembershipChange(plugin, request.Id, userId,
                    "对方已加入房间，请打开同一视频");
            }
            return Get(new GetRoomStateRequest { Id = request.Id });
        }

        public object Post(LeaveRoomRequest request)
        {
            var plugin = RequirePlugin();
            string userId = CurrentUserId();
            var room = plugin.Rooms.GetRoom(request.Id);
            if (room == null || !room.HasParticipant(userId)) throw new UnauthorizedAccessException("not a room participant");

            var beforeLeaveSnapshots = BuildSnapshots(plugin, room);
            var transition = plugin.Rooms.LeaveParticipantResult(request.Id, userId);
            if (transition.Changed)
            {
                NotifyMembershipChange(plugin, request.Id, userId,
                    "对方已退出房间，自动同步已停止");
            }
            bool activeBeforeLeave = transition.PreviousState == RoomState.Barrier ||
                transition.PreviousState == RoomState.Watching;
            bool sameServer = !string.IsNullOrWhiteSpace(transition.ServerId) &&
                string.Equals(transition.ServerId, plugin.ResolveServerId(), StringComparison.OrdinalIgnoreCase);

            if (transition.Changed && activeBeforeLeave && sameServer)
            {
                using (var access = plugin.Rooms.TryEnterRoom(request.Id))
                {
                    if (access != null && plugin.Rooms.IsCurrentRoom(access.Room) &&
                        !access.Room.IsJoined(userId) &&
                        IsSameServer(access.Room.ServerId, plugin.ResolveServerId()))
                    {
                        var currentRoom = access.Room;
                        var currentSnapshots = BuildSnapshots(plugin, currentRoom);
                        if (!currentRoom.IsJoined(userId))
                        {
                            IReadOnlyList<string> targetUserIds = string.Equals(
                                userId,
                                currentRoom.PrimaryUserId,
                                StringComparison.OrdinalIgnoreCase)
                                ? currentRoom.JoinedParticipantUserIds
                                    .Where(targetUserId => !string.Equals(
                                        targetUserId,
                                        userId,
                                        StringComparison.OrdinalIgnoreCase))
                                    .ToList()
                                : new[] { currentRoom.PrimaryUserId }
                                    .Where(targetUserId => !string.Equals(
                                        targetUserId,
                                        userId,
                                        StringComparison.OrdinalIgnoreCase))
                                    .ToList();

                            foreach (var targetUserId in targetUserIds)
                            {
                                if (!plugin.Rooms.IsCurrentRoom(currentRoom) ||
                                    !currentRoom.IsJoined(targetUserId) ||
                                    !IsSameServer(currentRoom.ServerId, plugin.ResolveServerId()))
                                {
                                    break;
                                }

                                if (!beforeLeaveSnapshots.TryGetValue(targetUserId, out var beforeLeave) ||
                                    beforeLeave == null ||
                                    !currentSnapshots.TryGetValue(targetUserId, out var current) ||
                                    current == null ||
                                    !current.Online ||
                                    !HasSameSessionIdentity(beforeLeave, current))
                                {
                                    continue;
                                }

                                plugin.Issuer?.TryIssue(
                                    currentRoom.Id,
                                    currentRoom.AdminUserId,
                                    targetUserId,
                                    current,
                                    RemoteCommands.Pause,
                                    null,
                                    DateTimeOffset.UtcNow,
                                    out _);
                            }
                        }
                    }
                }
            }

            return new { RoomId = request.Id, Joined = false, Changed = transition.Changed };
        }

        public object Post(SendRoomMessageRequest request)
        {
            RequireAdmin();
            var plugin = RequirePlugin();
            int sent = 0;
            Room room;
            using (var access = plugin.Rooms.TryEnterRoom(request.Id))
            {
                if (access == null)
                {
                    throw new KeyNotFoundException("room not found");
                }

                room = access.Room;
                if (plugin.Bridge == null)
                {
                    throw new InvalidOperationException("session bridge is not initialized");
                }

                if (!plugin.Rooms.IsCurrentRoom(room) ||
                    !IsSameServer(room.ServerId, plugin.ResolveServerId()))
                {
                    return new { RoomId = room.Id, Sent = 0 };
                }

                foreach (var userId in room.JoinedParticipantUserIds.ToList())
                {
                    if (!plugin.Rooms.IsCurrentRoom(room) ||
                        !room.IsJoined(userId) ||
                        !IsSameServer(room.ServerId, plugin.ResolveServerId()))
                    {
                        break;
                    }

                    var snapshots = BuildSnapshots(plugin, room);
                    if (!snapshots.TryGetValue(userId, out var snapshot) ||
                        snapshot == null ||
                        !snapshot.Online ||
                        snapshot.Capabilities == null ||
                        !snapshot.Capabilities.CanDisplayMessage)
                    {
                        continue;
                    }

                    if (!plugin.Rooms.IsCurrentRoom(room) ||
                        !room.IsJoined(userId) ||
                        !IsSameServer(room.ServerId, plugin.ResolveServerId()))
                    {
                        break;
                    }

                    plugin.Bridge.SendDisplayMessageAsync(
                            room.AdminUserId,
                            snapshot.SessionId,
                            "Watch Together",
                            request.Text ?? string.Empty,
                            timeoutMs: 3000,
                            cancellationToken: CancellationToken.None)
                        .GetAwaiter().GetResult();
                    sent++;
                }
            }

            return new { RoomId = room.Id, Sent = sent };
        }

        public object Get(GetUsersRequest request)
        {
            RequireAdmin();
#pragma warning disable CS0618 // IUserManager.Users is obsolete but fine for an admin-only picker.
            return UserManager.Users
                .Select(u => new { Id = u.Id.ToString("N"), Name = u.Name })
                .OrderBy(u => u.Name)
                .ToList();
#pragma warning restore CS0618
        }

        public object Get(GetPluginInfoRequest request)
        {
            RequireAdmin();
            var plugin = RequirePlugin();
            return new
            {
                CurrentVersion = plugin.Version?.ToString(),
                RepositoryUrl = GitHubReleaseClient.RepositoryUrl,
            };
        }

        private static Dictionary<string, SessionSnapshot> BuildSnapshots(Plugin plugin, Room room)
        {
            var candidates = plugin.Bridge == null
                ? new List<SessionSnapshot>()
                : new SessionBridgeSnapshotProvider(plugin.Bridge).GetSessionSnapshots();
            var now = DateTimeOffset.UtcNow;
            return SessionSelector.Select(candidates, room.JoinedParticipantUserIds, now);
        }

        private static void NotifyMembershipChange(Plugin plugin, string roomId, string changedUserId, string text)
        {
            if (!IsSyncNotificationsEnabled(plugin) || plugin.Bridge == null)
            {
                return;
            }

            using (var access = plugin.Rooms.TryEnterRoom(roomId))
            {
                if (access == null)
                {
                    return;
                }
                var room = access.Room;
                if (!plugin.Rooms.IsCurrentRoom(room) || !IsSameServer(room.ServerId, plugin.ResolveServerId()))
                {
                    return;
                }
                foreach (var userId in room.JoinedParticipantUserIds.ToList())
                {
                    if (string.Equals(userId, changedUserId, StringComparison.OrdinalIgnoreCase) ||
                        !plugin.Rooms.IsCurrentRoom(room) || !room.IsJoined(userId))
                    {
                        continue;
                    }
                    var snapshots = BuildSnapshots(plugin, room);
                    if (!snapshots.TryGetValue(userId, out var snapshot) || snapshot == null || !snapshot.Online ||
                        snapshot.Capabilities == null || !snapshot.Capabilities.CanDisplayMessage)
                    {
                        continue;
                    }
                    if (!plugin.Rooms.IsCurrentRoom(room) || !room.IsJoined(userId) || !IsSameServer(room.ServerId, plugin.ResolveServerId()))
                    {
                        continue;
                    }
                    try
                    {
                        plugin.Bridge.SendDisplayMessageAsync(room.AdminUserId, snapshot.SessionId, "一起观看", text, 3000, CancellationToken.None)
                            .GetAwaiter().GetResult();
                    }
                    catch
                    {
                        // Advisory notification failures must not affect membership state.
                    }
                }
            }
        }

        private static void NotifyAdminPlaybackAction(Plugin plugin, RoomActionResult result, string action, IReadOnlyDictionary<string, SessionSnapshot> actionSnapshots)
        {
            if (!IsSyncNotificationsEnabled(plugin) || plugin.Bridge == null || result?.Users == null) return;
            using (var access = plugin.Rooms.TryEnterRoom(result.RoomId))
            {
                if (access == null) return;
                var room = access.Room;
                if (!plugin.Rooms.IsCurrentRoom(room) || !IsSameServer(room.ServerId, plugin.ResolveServerId())) return;
                string text = string.Equals(action, "pause", StringComparison.OrdinalIgnoreCase)
                    ? "管理员已暂停房间播放" : "管理员已继续房间播放";
                foreach (var userId in result.Users)
                {
                    if (!room.IsJoined(userId)) continue;
                    var snapshots = BuildSnapshots(plugin, room);
                    if (!snapshots.TryGetValue(userId, out var snapshot) || snapshot == null || !snapshot.Online || snapshot.Capabilities?.CanDisplayMessage != true) continue;
                    if (actionSnapshots == null || !actionSnapshots.TryGetValue(userId, out var actionSnapshot) ||
                        !HasSameSessionIdentity(actionSnapshot, snapshot))
                    {
                        continue;
                    }
                    if (!plugin.Rooms.IsCurrentRoom(room) ||
                        !room.IsJoined(userId) ||
                        !IsSameServer(room.ServerId, plugin.ResolveServerId()))
                    {
                        continue;
                    }
                    try
                    {
                        plugin.Bridge.SendDisplayMessageAsync(room.AdminUserId, snapshot.SessionId, "一起观看", text, 3000, CancellationToken.None).GetAwaiter().GetResult();
                    }
                    catch
                    {
                        // Advisory notification failures must not affect action state.
                    }
                }
            }
        }

        private static void NotifyAdminResync(Plugin plugin, string roomId)
        {
            NotifyMembershipChange(plugin, roomId, null, "管理员已发起重新同步，请稍候");
        }

        private static bool IsSyncNotificationsEnabled(Plugin plugin)
        {
            try
            {
                return plugin?.Configuration?.NotifyOnSyncActions != false;
            }
            catch
            {
                // Test doubles and partially initialized plugins have no
                // configuration object; retain the enabled default.
                return true;
            }
        }

        private static string GetStatusReason(Plugin plugin, Room room, RoomRuntime runtime)
        {
            if (room == null || runtime == null) return "waiting_for_playback";
            if (!IsSameServer(room.ServerId, plugin.ResolveServerId())) return "server_unavailable";
            if (runtime.SnapshotUnavailable) return "snapshot_unavailable";
            if (string.Equals(runtime.Error, "两位参与者打开了不同视频，暂不发送同步指令", StringComparison.Ordinal)) return "different_video";
            if (string.Equals(runtime.Error, "播放已停止，等待双方重新打开同一视频", StringComparison.Ordinal)) return "playback_stopped";
            if (!string.IsNullOrEmpty(runtime.Error)) return "command_failed";
            if (room.JoinedParticipantUserIds.Count < room.ParticipantUserIds.Count) return "member_left";
            if (runtime.State == RoomState.Barrier) return "aligning";
            if (runtime.State == RoomState.Watching) return "watching";
            switch (runtime.LastEligibilityFailureReason)
            {
                case RoomEligibilityFailureReason.RemoteControlUnsupportedOrMismatch: return "remote_control_unavailable";
                case RoomEligibilityFailureReason.InvalidOrDifferentRuntime: return "media_mismatch";
                case RoomEligibilityFailureReason.PlaybackRateNotOne: return "unsupported_playback_rate";
                default: return "waiting_for_playback";
            }
        }

        private static bool HasSameSessionIdentity(SessionSnapshot expected, SessionSnapshot current)
        {
            return expected != null && current != null &&
                string.Equals(expected.UserId, current.UserId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(expected.SessionId, current.SessionId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(expected.ItemId, current.ItemId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSameServer(string roomServerId, string currentServerId)
        {
            return !string.IsNullOrWhiteSpace(roomServerId) &&
                !string.IsNullOrWhiteSpace(currentServerId) &&
                string.Equals(roomServerId, currentServerId, StringComparison.OrdinalIgnoreCase);
        }

        private static Plugin RequirePlugin()
        {
            return Plugin.Instance ?? throw new InvalidOperationException("plugin is not initialized");
        }

        private string CurrentUserId()
        {
            var auth = AuthorizationContext.GetAuthorizationInfo(Request);
            return auth.User?.Id.ToString("N");
        }

        private bool IsAdmin()
        {
            var auth = AuthorizationContext.GetAuthorizationInfo(Request);
            return auth.User?.Policy?.IsAdministrator == true;
        }

        private void RequireAdmin()
        {
            if (!IsAdmin())
            {
                throw new UnauthorizedAccessException("administrator privileges required");
            }
        }
    }
}

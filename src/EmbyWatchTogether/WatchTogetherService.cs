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
                return new
                {
                    RoomId = r.Id,
                    Name = r.Name,
                    State = runtime.State.ToString(),
                    Error = runtime.Error,
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

            var snapshots = BuildSnapshots(plugin, room);
            var result = plugin.Rooms.Action(
                request.Id, request.Action, snapshots, plugin.Issuer, DateTimeOffset.UtcNow);
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
            var snapshots = BuildSnapshots(plugin, room);
            return new
            {
                RoomId = room.Id,
                Name = room.Name,
                State = runtime.State.ToString(),
                Error = runtime.Error,
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
            plugin.Rooms.SetParticipantJoined(request.Id, userId, true);
            return Get(new GetRoomStateRequest { Id = request.Id });
        }

        public object Post(LeaveRoomRequest request)
        {
            var plugin = RequirePlugin();
            string userId = CurrentUserId();
            var room = plugin.Rooms.GetRoom(request.Id);
            if (room == null || !room.HasParticipant(userId)) throw new UnauthorizedAccessException("not a room participant");

            plugin.Rooms.SetParticipantJoined(request.Id, userId, false);
            var snapshots = BuildSnapshots(plugin, room);
            if (string.Equals(userId, room.PrimaryUserId, StringComparison.OrdinalIgnoreCase))
            {
                PauseOnlineParticipants(plugin, room, snapshots);
            }
            else if (snapshots.TryGetValue(room.PrimaryUserId, out var primary) && primary != null && primary.Online)
            {
                plugin.Issuer?.TryIssue(room.Id, room.AdminUserId, room.PrimaryUserId, primary,
                    RemoteCommands.Pause, null, DateTimeOffset.UtcNow, out _);
            }

            return new { RoomId = request.Id, Joined = false };
        }

        public object Post(SendRoomMessageRequest request)
        {
            RequireAdmin();
            var plugin = RequirePlugin();
            var room = plugin.Rooms.GetRoom(request.Id);
            if (room == null)
            {
                throw new KeyNotFoundException("room not found");
            }

            if (plugin.Bridge == null)
            {
                throw new InvalidOperationException("session bridge is not initialized");
            }

            int sent = 0;
            foreach (var snapshot in BuildSnapshots(plugin, room).Values)
            {
                if (!snapshot.Online || !snapshot.Capabilities.CanDisplayMessage)
                {
                    continue;
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
            return SessionSelector.Select(candidates, room.JoinedParticipantUserIds);
        }

        private static void PauseOnlineParticipants(Plugin plugin, Room room, Dictionary<string, SessionSnapshot> snapshots)
        {
            foreach (var snapshot in snapshots.Values)
            {
                if (snapshot != null && snapshot.Online)
                {
                    plugin.Issuer?.TryIssue(room.Id, room.AdminUserId, snapshot.UserId, snapshot,
                        RemoteCommands.Pause, null, DateTimeOffset.UtcNow, out _);
                }
            }
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

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

    [Route("/WatchTogether/Rooms/{Id}/Message", "POST")]
    public class SendRoomMessageRequest
    {
        public string Id { get; set; }

        public string Text { get; set; }
    }

    [Route("/WatchTogether/Users", "GET")]
    public class GetUsersRequest { }

    /// <summary>
    /// REST API for room management and remote control. Admin endpoints verify
    /// the caller's user policy; participant endpoints verify membership.
    /// </summary>
    [Authenticated]
    public class WatchTogetherService : BaseApiService, IService
    {
        public object Get(GetRoomsRequest request)
        {
            RequireAdmin();
            var plugin = RequirePlugin();
            return plugin.Rooms.ListRooms().Select(r =>
            {
                var runtime = plugin.Rooms.GetRuntime(r.Id);
                return new
                {
                    RoomId = r.Id,
                    Name = r.Name,
                    State = runtime.State.ToString(),
                    Error = runtime.Error,
                    PrimaryUserId = r.PrimaryUserId,
                    ParticipantUserIds = r.ParticipantUserIds,
                    CreatedAtUtc = r.CreatedAtUtc,
                };
            }).ToList();
        }

        public object Post(CreateRoomRequest request)
        {
            RequireAdmin();
            var plugin = RequirePlugin();
            if (string.IsNullOrWhiteSpace(plugin.ServerId))
            {
                throw new ArgumentException("server identity is not resolved yet");
            }

            var room = plugin.Rooms.CreateRoom(
                serverId: plugin.ServerId,
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
                PrimaryUserId = room.PrimaryUserId,
                ParticipantUserIds = room.ParticipantUserIds,
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
            return Get(new GetRoomStateRequest { Id = request.Id });
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
                        timeoutMs: null,
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

        private static Dictionary<string, SessionSnapshot> BuildSnapshots(Plugin plugin, Room room)
        {
            var candidates = plugin.Bridge == null
                ? new List<SessionSnapshot>()
                : new SessionBridgeSnapshotProvider(plugin.Bridge).GetSessionSnapshots();
            return SessionSelector.Select(candidates, room.ParticipantUserIds);
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

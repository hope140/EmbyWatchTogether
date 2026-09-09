using System;
using System.Linq;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public sealed class RoomInvitationManagerTests
    {
        [Fact]
        public void CreateAndAccept_ConsumesCodeOnceAndCreatesSelfServiceRoom()
        {
            var now = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
            var manager = new RoomManager();
            var invitations = new RoomInvitationManager(() => now, () => "invite-code");
            var created = invitations.Create("u1", "movie");

            Assert.Equal("invite-code", created.Code);
            Assert.Empty(invitations.List("u2"));
            var accepted = invitations.Accept(
                created.Code,
                "u2",
                invitation => manager.CreateSelfServiceRoom("server-1", "", invitation.Name, invitation.CreatorUserId, "u2", now));

            Assert.True(accepted.Succeeded);
            Assert.True(accepted.Room.IsSelfService);
            Assert.Equal("u1", accepted.Room.CreatorUserId);
            Assert.Equal("u1", accepted.Room.AdminUserId);
            Assert.False(invitations.Accept(created.Code, "u3", _ => throw new InvalidOperationException()).Succeeded);
        }

        [Fact]
        public void Accept_ExpiredAndCreatorAreStableFailures()
        {
            var now = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
            var invitations = new RoomInvitationManager(() => now, () => "invite-code");
            var created = invitations.Create("u1", "movie");

            var own = invitations.Accept(created.Code, "u1", _ => null, now);
            Assert.Equal(RoomInvitationManager.CreatorCannotAcceptStatus, own.Status);
            var expired = invitations.Accept(created.Code, "u2", _ => null, now.AddMinutes(16));
            Assert.Equal(RoomInvitationManager.InvalidOrExpiredStatus, expired.Status);
        }

        [Fact]
        public void Accept_RoomFailureKeepsInvitationForRetry()
        {
            var invitations = new RoomInvitationManager(() => DateTimeOffset.UtcNow, () => "invite-code");
            var created = invitations.Create("u1", "movie");
            var failed = invitations.Accept(created.Code, "u2", _ => throw new InvalidOperationException());
            Assert.Equal(RoomInvitationManager.RoomUnavailableStatus, failed.Status);
            Assert.Single(invitations.List("u1"));
        }

        [Fact]
        public void Accept_EnforcesFiveAttemptsPerUserPerMinute()
        {
            var now = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
            var invitations = new RoomInvitationManager(() => now, () => "invite-code");

            for (var i = 0; i < RoomInvitationManager.AttemptsPerUserPerMinute; i++)
            {
                var result = invitations.Accept("wrong-code", "u2", _ => null, now);
                Assert.Equal(RoomInvitationManager.InvalidOrExpiredStatus, result.Status);
            }

            var limited = invitations.Accept("wrong-code", "u2", _ => null, now);
            Assert.Equal(RoomInvitationManager.RateLimitedStatus, limited.Status);

            var afterWindow = invitations.Accept(
                "wrong-code", "u2", _ => null, now.AddMinutes(1).AddSeconds(1));
            Assert.Equal(RoomInvitationManager.InvalidOrExpiredStatus, afterWindow.Status);
        }

        [Fact]
        public void Create_RejectsCreatorAlreadyInRoomAndLegacyRoomFallsBackCreator()
        {
            var rooms = new RoomManager();
            rooms.CreateRoom("server-1", "", "legacy", "admin", new[] { "u1", "u2" }, "u1");
            var invitations = new RoomInvitationManager(creatorInRoom: rooms.IsUserInAnyRoom);
            Assert.Throws<InvalidOperationException>(() => invitations.Create("u1", "new"));
            var legacy = rooms.ListRooms().Single();
            Assert.False(legacy.IsSelfService);
            Assert.Equal("admin", legacy.CreatorUserId);
        }
    }
}

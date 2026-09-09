using System;

namespace Emby.Plugins.WatchTogether
{
    public sealed class RoomInvitation
    {
        internal RoomInvitation(
            string id,
            string codeHash,
            string creatorUserId,
            string name,
            DateTimeOffset createdAtUtc,
            DateTimeOffset expiresAtUtc)
        {
            Id = id;
            CodeHash = codeHash;
            CreatorUserId = creatorUserId;
            Name = name;
            CreatedAtUtc = createdAtUtc;
            ExpiresAtUtc = expiresAtUtc;
        }

        public string Id { get; }

        internal string CodeHash { get; }

        public string CreatorUserId { get; }

        public string Name { get; }

        public DateTimeOffset CreatedAtUtc { get; }

        public DateTimeOffset ExpiresAtUtc { get; }

        public bool IsExpired(DateTimeOffset now)
        {
            return now >= ExpiresAtUtc;
        }
    }

    public sealed class RoomInvitationCreated
    {
        public string Id { get; internal set; }

        public string Code { get; internal set; }

        public string Name { get; internal set; }

        public DateTimeOffset CreatedAtUtc { get; internal set; }

        public DateTimeOffset ExpiresAtUtc { get; internal set; }
    }

    public sealed class RoomInvitationAcceptResult
    {
        public string Status { get; internal set; }

        public string Reason { get; internal set; }

        public Room Room { get; internal set; }

        public bool Succeeded => Room != null && string.Equals(Status, "accepted", StringComparison.Ordinal);
    }
}

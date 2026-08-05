using System;
using System.Collections.Generic;
using System.Linq;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Immutable room entity. Mirrors the Python store room schema plus the
    /// plugin-specific admin user who created the room.
    /// </summary>
    public sealed class Room
    {
        public Room(
            string id,
            string serverId,
            string serverUrl,
            string name,
            string adminUserId,
            string primaryUserId,
            IEnumerable<string> participantUserIds,
            DateTimeOffset createdAtUtc)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            ServerId = serverId ?? throw new ArgumentNullException(nameof(serverId));
            ServerUrl = serverUrl ?? string.Empty;
            Name = name ?? string.Empty;
            AdminUserId = adminUserId ?? throw new ArgumentNullException(nameof(adminUserId));
            PrimaryUserId = primaryUserId ?? throw new ArgumentNullException(nameof(primaryUserId));
            ParticipantUserIds = (participantUserIds ?? Enumerable.Empty<string>())
                .Select(u => u ?? string.Empty)
                .ToList();
            CreatedAtUtc = createdAtUtc;
        }

        public string Id { get; }

        public string ServerId { get; }

        public string ServerUrl { get; }

        public string Name { get; }

        public string AdminUserId { get; }

        public string PrimaryUserId { get; }

        /// <summary>
        /// Exactly two distinct participant user ids; the primary user is one of them.
        /// </summary>
        public IReadOnlyList<string> ParticipantUserIds { get; }

        public DateTimeOffset CreatedAtUtc { get; }

        public bool HasParticipant(string userId)
        {
            return !string.IsNullOrEmpty(userId) &&
                ParticipantUserIds.Contains(userId, StringComparer.OrdinalIgnoreCase);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Process-local invitation state. Codes are never retained in plaintext;
    /// accept is serialized with room creation and invitation consumption.
    /// </summary>
    public sealed class RoomInvitationManager : IDisposable
    {
        public const int MaxInvitationsPerCreator = 3;
        public const int MaxInvitations = 100;
        public const int ExpiryMinutes = 15;
        public const int AttemptsPerUserPerMinute = 5;
        public const int AttemptsPerMinute = 100;
        public const string InvalidOrExpiredStatus = "invalid_or_expired";
        public const string RateLimitedStatus = "rate_limited";
        public const string CreatorCannotAcceptStatus = "creator_cannot_accept";
        public const string RoomUnavailableStatus = "room_unavailable";
        public const string CreatorAlreadyInRoomStatus = "creator_already_in_room";
        public const string InvitationUnavailableStatus = "invitation_unavailable";

        private readonly object _lock = new object();
        private readonly Dictionary<string, RoomInvitation> _invitations =
            new Dictionary<string, RoomInvitation>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Queue<DateTimeOffset>> _userAttempts =
            new Dictionary<string, Queue<DateTimeOffset>>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<DateTimeOffset> _globalAttempts = new Queue<DateTimeOffset>();
        private readonly Func<DateTimeOffset> _now;
        private readonly Func<string> _codeGenerator;
        private readonly Func<string, bool> _creatorInRoom;
        private bool _disposed;

        public RoomInvitationManager(
            Func<DateTimeOffset> now = null,
            Func<string> codeGenerator = null,
            Func<string, bool> creatorInRoom = null)
        {
            _now = now ?? (() => DateTimeOffset.UtcNow);
            _codeGenerator = codeGenerator ?? GenerateCode;
            _creatorInRoom = creatorInRoom;
        }

        public RoomInvitationCreated Create(string creatorUserId, string name, DateTimeOffset? now = null)
        {
            if (string.IsNullOrWhiteSpace(creatorUserId)) throw new ArgumentException("creatorUserId is required", nameof(creatorUserId));
            var createdAt = now ?? _now();
            lock (_lock)
            {
                ThrowIfDisposed();
                RemoveExpired(createdAt);
                if (_creatorInRoom != null && _creatorInRoom(creatorUserId))
                {
                    throw new InvalidOperationException("creator already belongs to a room");
                }
                var count = _invitations.Values.Count(i => string.Equals(i.CreatorUserId, creatorUserId, StringComparison.OrdinalIgnoreCase));
                if (count >= MaxInvitationsPerCreator) throw new InvalidOperationException("invitation limit reached");
                if (_invitations.Count >= MaxInvitations) throw new InvalidOperationException("invitation limit reached");

                string code;
                string hash;
                var attempts = 0;
                do
                {
                    if (++attempts > 100) throw new InvalidOperationException("code generator produced duplicate codes");
                    code = _codeGenerator();
                    if (string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("code generator returned an empty code");
                    hash = HashCode(code);
                } while (_invitations.Values.Any(i => FixedEquals(i.CodeHash, hash)));

                var invitation = new RoomInvitation(
                    Guid.NewGuid().ToString("N"),
                    hash,
                    creatorUserId,
                    (name ?? string.Empty).Trim(),
                    createdAt,
                    createdAt.AddMinutes(ExpiryMinutes));
                _invitations.Add(invitation.Id, invitation);
                return new RoomInvitationCreated
                {
                    Id = invitation.Id,
                    Code = code,
                    Name = invitation.Name,
                    CreatedAtUtc = invitation.CreatedAtUtc,
                    ExpiresAtUtc = invitation.ExpiresAtUtc,
                };
            }
        }

        public IReadOnlyList<RoomInvitation> List(string creatorUserId, DateTimeOffset? now = null)
        {
            var at = now ?? _now();
            lock (_lock)
            {
                ThrowIfDisposed();
                RemoveExpired(at);
                return _invitations.Values
                    .Where(i => string.Equals(i.CreatorUserId, creatorUserId, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(i => i.CreatedAtUtc)
                    .ToList();
            }
        }

        public bool Revoke(string invitationId, string requesterUserId, bool administrator, DateTimeOffset? now = null)
        {
            lock (_lock)
            {
                ThrowIfDisposed();
                if (!_invitations.TryGetValue(invitationId ?? string.Empty, out var invitation)) return false;
                if (!administrator && !string.Equals(invitation.CreatorUserId, requesterUserId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new UnauthorizedAccessException("invitation owner required");
                }
                return _invitations.Remove(invitation.Id);
            }
        }

        public RoomInvitationAcceptResult Accept(
            string code,
            string accepterUserId,
            Func<RoomInvitation, Room> createRoom,
            DateTimeOffset? now = null)
        {
            var at = now ?? _now();
            lock (_lock)
            {
                ThrowIfDisposed();
                if (!TryRecordAttempt(accepterUserId, at))
                {
                    return Failure(RateLimitedStatus, "too_many_attempts");
                }

                string hash = HashCode(code ?? string.Empty);
                var invitation = _invitations.Values.FirstOrDefault(i => FixedEquals(i.CodeHash, hash));
                if (invitation == null || invitation.IsExpired(at))
                {
                    if (invitation != null && invitation.IsExpired(at)) _invitations.Remove(invitation.Id);
                    return Failure(InvalidOrExpiredStatus, InvalidOrExpiredStatus);
                }

                if (string.Equals(invitation.CreatorUserId, accepterUserId, StringComparison.OrdinalIgnoreCase))
                {
                    return Failure(CreatorCannotAcceptStatus, CreatorCannotAcceptStatus);
                }

                try
                {
                    var room = createRoom?.Invoke(invitation);
                    if (room == null) return Failure(RoomUnavailableStatus, RoomUnavailableStatus);
                    _invitations.Remove(invitation.Id);
                    return new RoomInvitationAcceptResult { Status = "accepted", Room = room };
                }
                catch (InvalidOperationException)
                {
                    return Failure(RoomUnavailableStatus, RoomUnavailableStatus);
                }
                catch (ArgumentException)
                {
                    return Failure(RoomUnavailableStatus, RoomUnavailableStatus);
                }
                catch (RoomStoreException)
                {
                    return Failure(RoomUnavailableStatus, RoomUnavailableStatus);
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _disposed = true;
                _invitations.Clear();
                _userAttempts.Clear();
                _globalAttempts.Clear();
            }
        }

        private bool TryRecordAttempt(string userId, DateTimeOffset now)
        {
            if (string.IsNullOrWhiteSpace(userId)) return false;
            TrimAttempts(now);
            if (_globalAttempts.Count >= AttemptsPerMinute) return false;
            if (!_userAttempts.TryGetValue(userId, out var attempts))
            {
                attempts = new Queue<DateTimeOffset>();
                _userAttempts[userId] = attempts;
            }
            if (attempts.Count >= AttemptsPerUserPerMinute) return false;
            attempts.Enqueue(now);
            _globalAttempts.Enqueue(now);
            return true;
        }

        private void TrimAttempts(DateTimeOffset now)
        {
            var cutoff = now.Subtract(TimeSpan.FromMinutes(1));
            while (_globalAttempts.Count > 0 && _globalAttempts.Peek() <= cutoff) _globalAttempts.Dequeue();
            foreach (var pair in _userAttempts.ToList())
            {
                while (pair.Value.Count > 0 && pair.Value.Peek() <= cutoff) pair.Value.Dequeue();
                if (pair.Value.Count == 0) _userAttempts.Remove(pair.Key);
            }
        }

        private void RemoveExpired(DateTimeOffset now)
        {
            foreach (var id in _invitations.Values.Where(i => i.IsExpired(now)).Select(i => i.Id).ToList())
            {
                _invitations.Remove(id);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RoomInvitationManager));
        }

        private static RoomInvitationAcceptResult Failure(string status, string reason)
        {
            return new RoomInvitationAcceptResult { Status = status, Reason = reason };
        }

        private static string HashCode(string code)
        {
            using (var sha = SHA256.Create())
            {
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(code ?? string.Empty)));
            }
        }

        private static bool FixedEquals(string left, string right)
        {
            var a = Encoding.UTF8.GetBytes(left ?? string.Empty);
            var b = Encoding.UTF8.GetBytes(right ?? string.Empty);
            int diff = a.Length ^ b.Length;
            int length = Math.Max(a.Length, b.Length);
            for (var i = 0; i < length; i++)
            {
                byte leftByte = i < a.Length ? a[i] : (byte)0;
                byte rightByte = i < b.Length ? b[i] : (byte)0;
                diff |= leftByte ^ rightByte;
            }
            return diff == 0;
        }

        private static string GenerateCode()
        {
            var bytes = new byte[18];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}

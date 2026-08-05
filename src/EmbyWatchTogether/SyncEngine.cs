using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Emby.Plugins.WatchTogether
{
    public sealed class RoomPollResult
    {
        public string RoomId { get; set; }

        public RoomState State { get; set; }

        public bool Eligible { get; set; }

        public string Error { get; set; }
    }

    /// <summary>
    /// Background coordinator. Polls sessions every interval and drives each
    /// room through the waiting/barrier/watching state machine, ported from the
    /// Python reference coordinator's poll_once / barrier / watching logic.
    /// </summary>
    public sealed class SyncEngine : IDisposable
    {
        private const string StoppedPlaybackError = "播放已停止，等待双方重新打开同一视频";

        private readonly RoomManager _roomManager;
        private readonly ISessionSnapshotProvider _snapshotProvider;
        private readonly ICommandIssuer _issuer;
        private readonly Func<string> _serverIdProvider;
        private readonly Func<DateTimeOffset> _clock;
        private readonly double _pollIntervalSeconds;
        private readonly bool _pauseOtherOnPlaybackStop;
        private readonly object _lock = new object();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Thread _thread;
        private bool _disposed;

        public SyncEngine(
            RoomManager roomManager,
            ISessionSnapshotProvider snapshotProvider,
            ICommandIssuer issuer,
            Func<string> serverIdProvider,
            Func<DateTimeOffset> clock = null,
            double pollIntervalSeconds = 1.0,
            bool pauseOtherOnPlaybackStop = true)
        {
            _roomManager = roomManager ?? throw new ArgumentNullException(nameof(roomManager));
            _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
            _issuer = issuer;
            _serverIdProvider = serverIdProvider ?? (() => string.Empty);
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _pollIntervalSeconds = Math.Max(0.05, pollIntervalSeconds);
            _pauseOtherOnPlaybackStop = pauseOtherOnPlaybackStop;
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_thread != null)
                {
                    return;
                }

                _thread = new Thread(Loop)
                {
                    IsBackground = true,
                    Name = "watch-together-sync-engine",
                };
                _thread.Start();
            }
        }

        public void Stop()
        {
            _cts.Cancel();
        }

        public IReadOnlyList<RoomPollResult> PollOnce(DateTimeOffset now)
        {
            var results = new List<RoomPollResult>();
            var rooms = _roomManager.ListRooms();
            if (rooms.Count == 0)
            {
                return results;
            }

            string currentServerId = _serverIdProvider();
            var validRooms = new List<Room>();
            foreach (var room in rooms)
            {
                var runtime = _roomManager.GetRuntime(room.Id);
                if (!string.Equals(room.ServerId, currentServerId, StringComparison.OrdinalIgnoreCase))
                {
                    runtime.State = RoomState.Unavailable;
                    runtime.Error = "room server is unavailable";
                    runtime.Barrier = null;
                    runtime.Pending.Clear();
                    results.Add(new RoomPollResult
                    {
                        RoomId = room.Id,
                        State = RoomState.Unavailable,
                        Eligible = false,
                        Error = runtime.Error,
                    });
                }
                else
                {
                    validRooms.Add(room);
                }
            }

            if (validRooms.Count == 0)
            {
                return results;
            }

            IReadOnlyList<SessionSnapshot> candidates;
            try
            {
                candidates = _snapshotProvider.GetSessionSnapshots();
            }
            catch
            {
                return results;
            }

            lock (_lock)
            {
                foreach (var room in validRooms)
                {
                    var runtime = _roomManager.GetRuntime(room.Id);
                    if (runtime.State == RoomState.Unavailable)
                    {
                        runtime.State = RoomState.Waiting;
                        runtime.Error = null;
                    }

                    var snapshots = SessionSelector.Select(candidates, room.JoinedParticipantUserIds);
                    bool eligible = RoomEligibility.IsPairEligible(snapshots);
                    bool sameItem = snapshots.Count == 2 &&
                        snapshots.Values.All(s => s != null) &&
                        snapshots.Values.Select(s => s.ItemId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;

                    // Emby may briefly retain the old ItemId after a player is closed
                    // while reporting PositionTicks = 0. Treat that transition as a
                    // stop, not as a user-issued seek, before observing pending commands.
                    if (TryGetStoppedUsers(runtime, room, snapshots, out var stoppedUsers))
                    {
                        if (_pauseOtherOnPlaybackStop)
                        {
                            PauseOtherAfterPlaybackStopped(runtime, room, snapshots, stoppedUsers, now);
                        }

                        runtime.ResetToWaiting();
                        runtime.Error = StoppedPlaybackError;
                        results.Add(Result(room, runtime, eligible));
                        continue;
                    }

                    // Do not immediately restart a stale old-item snapshot after the
                    // stop transition. Once both sides are back near the same position,
                    // the next poll is allowed to start a fresh barrier.
                    if (runtime.State == RoomState.Waiting &&
                        runtime.Error == StoppedPlaybackError)
                    {
                        if (sameItem && !HasLargePositionGap(snapshots))
                        {
                            runtime.Error = null;
                        }
                        else if (sameItem)
                        {
                            results.Add(Result(room, runtime, eligible));
                            continue;
                        }
                    }

                    bool pendingFailed = ObservePending(runtime, room, snapshots, now);
                    if (!sameItem && runtime.State != RoomState.Waiting)
                    {
                        runtime.ResetToWaiting();
                    }

                    if (!sameItem && snapshots.Count == 2)
                    {
                        runtime.Error = "两位参与者打开了不同视频，暂不发送同步指令";
                    }
                    else if (sameItem && runtime.Error == "两位参与者打开了不同视频，暂不发送同步指令")
                    {
                        runtime.Error = null;
                    }

                    if (pendingFailed)
                    {
                        runtime.State = RoomState.Waiting;
                        runtime.Barrier = null;
                        runtime.Previous.Clear();
                        runtime.DriftRounds = 0;
                        results.Add(Result(room, runtime, eligible));
                        continue;
                    }

                    if (runtime.State == RoomState.Watching)
                    {
                        if (eligible)
                        {
                            WatchingTick(runtime, room, snapshots, now);
                        }
                        else
                        {
                            if (sameItem) PauseOtherWhenWaiting(runtime, room, snapshots, now);
                            runtime.State = RoomState.Waiting;
                            runtime.Barrier = null;
                            runtime.Previous.Clear();
                            runtime.DriftRounds = 0;
                        }
                    }
                    else if (eligible)
                    {
                        if (runtime.State == RoomState.Waiting && runtime.Error != null)
                        {
                            // A command failure requires an explicit resync (or a
                            // fresh room action) before another barrier issues
                            // commands; prevents one-second command storms.
                            results.Add(Result(room, runtime, eligible));
                            continue;
                        }

                        if (runtime.State != RoomState.Barrier)
                        {
                            StartBarrier(runtime, room, snapshots, now);
                        }

                        BarrierTick(runtime, room, snapshots, now);
                    }
                    else
                    {
                        if (sameItem) PauseOtherWhenWaiting(runtime, room, snapshots, now);
                        runtime.State = RoomState.Waiting;
                        runtime.Barrier = null;
                        runtime.Previous.Clear();
                        runtime.DriftRounds = 0;
                    }

                    results.Add(Result(room, runtime, eligible));
                }
            }

            return results;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
            _cts.Dispose();
        }

        private void Loop()
        {
            var token = _cts.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    PollOnce(_clock());
                }
                catch
                {
                    // Polling must never kill the background thread.
                }

                try
                {
                    token.WaitHandle.WaitOne(TimeSpan.FromSeconds(_pollIntervalSeconds));
                }
                catch
                {
                    return;
                }
            }
        }

        private static bool TryGetStoppedUsers(
            RoomRuntime runtime,
            Room room,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            out HashSet<string> stoppedUsers)
        {
            stoppedUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (runtime == null || room == null || snapshots == null ||
                runtime.State == RoomState.Waiting ||
                runtime.State == RoomState.Unavailable ||
                room.JoinedParticipantUserIds.Count != 2)
            {
                return false;
            }

            var members = room.JoinedParticipantUserIds;
            foreach (var userId in members)
            {
                if (!snapshots.TryGetValue(userId, out var current) || current == null ||
                    !current.Online || current.Stopped)
                {
                    stoppedUsers.Add(userId);
                }
            }

            if (stoppedUsers.Count > 0)
            {
                return true;
            }

            if (runtime.State == RoomState.Barrier && runtime.Barrier != null &&
                snapshots.TryGetValue(room.PrimaryUserId, out var primary) &&
                primary != null &&
                string.Equals(primary.ItemId, runtime.Barrier.ItemId, StringComparison.OrdinalIgnoreCase) &&
                IsPositionReset(runtime.Barrier.PrimaryPositionTicks, primary.PositionTicks))
            {
                stoppedUsers.Add(room.PrimaryUserId);
                return true;
            }

            foreach (var userId in members)
            {
                if (!snapshots.TryGetValue(userId, out var current) || current == null ||
                    !runtime.Previous.TryGetValue(userId, out var previous) || previous == null ||
                    !string.Equals(previous.ItemId, current.ItemId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IsPositionReset(previous.PositionTicks, current.PositionTicks))
                {
                    stoppedUsers.Add(userId);
                    return true;
                }
            }

            return false;
        }

        private void PauseOtherAfterPlaybackStopped(
            RoomRuntime runtime,
            Room room,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            ISet<string> stoppedUsers,
            DateTimeOffset now)
        {
            foreach (var pair in snapshots)
            {
                var snapshot = pair.Value;
                if (snapshot == null || !snapshot.Online || snapshot.Stopped ||
                    (stoppedUsers != null && stoppedUsers.Contains(pair.Key)))
                {
                    continue;
                }

                Issue(runtime, room, pair.Key, snapshot, RemoteCommands.Pause, null, now);
            }
        }

        private static bool IsPositionReset(long previousPositionTicks, long currentPositionTicks)
        {
            return previousPositionTicks > 2 * SyncConstants.TicksPerSecond &&
                currentPositionTicks <= SyncConstants.TicksPerSecond &&
                previousPositionTicks - currentPositionTicks >= SyncConstants.DriftThresholdTicks;
        }

        private static bool HasLargePositionGap(IReadOnlyDictionary<string, SessionSnapshot> snapshots)
        {
            if (snapshots == null || snapshots.Count != 2 || snapshots.Values.Any(s => s == null))
            {
                return false;
            }

            var positions = snapshots.Values.Select(s => s.PositionTicks).ToList();
            return Math.Abs(positions[0] - positions[1]) > SyncConstants.DriftThresholdTicks;
        }
        private static RoomPollResult Result(Room room, RoomRuntime runtime, bool eligible)
        {
            return new RoomPollResult
            {
                RoomId = room.Id,
                State = runtime.State,
                Eligible = eligible,
                Error = runtime.Error,
            };
        }

        private bool ObservePending(RoomRuntime runtime, Room room, IReadOnlyDictionary<string, SessionSnapshot> snapshots, DateTimeOffset now)
        {
            bool failed = false;
            foreach (var pair in runtime.Pending.ToList())
            {
                string userId = pair.Key;
                var pending = pair.Value;
                snapshots.TryGetValue(userId, out var snapshot);

                if (PendingMatcher.Matches(pending, snapshot))
                {
                    runtime.Pending.Remove(userId);
                    runtime.Suppressed[userId] = new SuppressedCommand
                    {
                        Command = pending.Command,
                        PositionTicks = pending.PositionTicks,
                        UntilUtc = now + TimeSpan.FromSeconds(SyncConstants.SuppressSeconds),
                    };
                    continue;
                }

                if ((now - pending.IssuedAtUtc).TotalSeconds < SyncConstants.PendingTimeoutSeconds)
                {
                    continue;
                }

                if (pending.Retries == 0 && snapshot != null)
                {
                    long? positionTicks = pending.PositionTicks;
                    runtime.Pending.Remove(userId);
                    if (Issue(runtime, room, userId, snapshot, pending.Command, positionTicks, now))
                    {
                        if (runtime.Pending.TryGetValue(userId, out var retry))
                        {
                            retry.Retries = 1;
                            if (runtime.State == RoomState.Barrier && runtime.Barrier != null)
                            {
                                runtime.Barrier.StartedAtUtc = now;
                            }
                        }
                    }
                }
                else
                {
                    runtime.Pending.Remove(userId);
                    failed = true;
                }
            }

            if (failed)
            {
                runtime.State = RoomState.Waiting;
                runtime.Error = "playback command was not acknowledged";
            }

            return failed;
        }

        private bool Issue(
            RoomRuntime runtime,
            Room room,
            string userId,
            SessionSnapshot snapshot,
            string command,
            long? positionTicks,
            DateTimeOffset now)
        {
            if (runtime.Pending.TryGetValue(userId, out var existing) &&
                string.Equals(existing.Command, command, StringComparison.Ordinal))
            {
                if (command != RemoteCommands.Seek ||
                    Math.Abs((positionTicks ?? 0) - snapshot.PositionTicks) <= SyncConstants.SeekToleranceTicks)
                {
                    return true;
                }
            }

            bool ok;
            string error;
            if (_issuer == null)
            {
                ok = false;
                error = "no command issuer configured";
            }
            else
            {
                ok = _issuer.TryIssue(room.Id, room.AdminUserId, userId, snapshot, command, positionTicks, now, out error);
            }

            if (!ok)
            {
                runtime.Error = $"{command} command failed: {error}";
                runtime.State = RoomState.Waiting;
                return false;
            }

            runtime.Pending[userId] = new PendingCommand
            {
                UserId = userId,
                Command = command,
                PositionTicks = positionTicks,
                IssuedAtUtc = now,
                Retries = 0,
            };
            return true;
        }

        private static void StartBarrier(
            RoomRuntime runtime,
            Room room,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            DateTimeOffset now)
        {
            var primary = snapshots[room.PrimaryUserId];
            runtime.State = RoomState.Barrier;
            runtime.Error = null;
            runtime.Barrier = new BarrierState
            {
                Stage = BarrierStage.Pause,
                StartedAtUtc = now,
                PrimaryPositionTicks = primary.PositionTicks,
                PrimaryPaused = primary.IsPaused,
                ItemId = primary.ItemId,
                PauseSent = false,
                SeekSent = false,
                RestoreSent = false,
            };
            runtime.SyncItemId = primary.ItemId;
            runtime.Pending.Clear();
            runtime.Suppressed.Clear();
        }

        private void BarrierTick(
            RoomRuntime runtime,
            Room room,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            DateTimeOffset now)
        {
            if (runtime.Barrier == null)
            {
                StartBarrier(runtime, room, snapshots, now);
            }

            var barrier = runtime.Barrier;
            var members = room.JoinedParticipantUserIds;

            if (!snapshots.Values.All(s => s != null) ||
                snapshots.Values.Any(s => !string.Equals(s.ItemId, barrier.ItemId, StringComparison.OrdinalIgnoreCase)))
            {
                runtime.ResetToWaiting();
                return;
            }

            switch (barrier.Stage)
            {
                case BarrierStage.Pause:
                    if (!barrier.PauseSent)
                    {
                        foreach (var user in members)
                        {
                            Issue(runtime, room, user, snapshots[user], RemoteCommands.Pause, null, now);
                        }

                        barrier.PauseSent = true;
                        return;
                    }

                    if (members.All(u => snapshots[u].IsPaused))
                    {
                        barrier.Stage = BarrierStage.Seek;
                        barrier.StartedAtUtc = now;
                        return;
                    }

                    if ((now - barrier.StartedAtUtc).TotalSeconds >= SyncConstants.BarrierTimeoutSeconds)
                    {
                        runtime.State = RoomState.Waiting;
                        runtime.Error = "barrier pause timed out";
                        runtime.Barrier = null;
                    }

                    return;

                case BarrierStage.Seek:
                    var secondary = members.First(u => u != room.PrimaryUserId);
                    long target = barrier.PrimaryPositionTicks;
                    if (!barrier.SeekSent)
                    {
                        Issue(runtime, room, secondary, snapshots[secondary], RemoteCommands.Seek, target, now);
                        barrier.SeekSent = true;
                        return;
                    }

                    if (Math.Abs(snapshots[secondary].PositionTicks - target) <= SyncConstants.SeekToleranceTicks)
                    {
                        barrier.Stage = BarrierStage.Restore;
                        barrier.StartedAtUtc = now;
                        return;
                    }

                    if ((now - barrier.StartedAtUtc).TotalSeconds >= SyncConstants.BarrierTimeoutSeconds)
                    {
                        runtime.State = RoomState.Waiting;
                        runtime.Error = "barrier seek timed out";
                        runtime.Barrier = null;
                    }

                    return;

                case BarrierStage.Restore:
                    if (!barrier.RestoreSent)
                    {
                        string command = barrier.PrimaryPaused ? RemoteCommands.Pause : RemoteCommands.Unpause;
                        foreach (var user in members)
                        {
                            Issue(runtime, room, user, snapshots[user], command, null, now);
                        }

                        barrier.RestoreSent = true;
                        return;
                    }

                    bool desired = barrier.PrimaryPaused;
                    if (members.All(u => snapshots[u].IsPaused == desired))
                    {
                        runtime.State = RoomState.Watching;
                        runtime.Barrier = null;
                        runtime.Pending.Clear();
                        runtime.Suppressed.Clear();
                        runtime.Previous.Clear();
                        foreach (var pair in snapshots)
                        {
                            runtime.Previous[pair.Key] = pair.Value;
                        }

                        runtime.PreviousAtUtc = now;
                        runtime.DriftRounds = 0;
                        runtime.SyncItemId = barrier.ItemId;
                    }
                    else if ((now - barrier.StartedAtUtc).TotalSeconds >= SyncConstants.BarrierTimeoutSeconds)
                    {
                        runtime.State = RoomState.Waiting;
                        runtime.Error = "barrier restore timed out";
                        runtime.Barrier = null;
                    }

                    return;
            }
        }

        private void PauseOtherWhenWaiting(
            RoomRuntime runtime,
            Room room,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            DateTimeOffset now)
        {
            var onlinePlaying = snapshots
                .Where(p => p.Value != null && p.Value.Online && !p.Value.IsPaused)
                .ToList();

            if (onlinePlaying.Count == 0)
            {
                return;
            }

            if (onlinePlaying.Count == 1)
            {
                // A room may wait for the second participant while the first is
                // already playing; never interrupt a solo player (solo protection).
                return;
            }

            var previous = runtime.Previous;
            var changed = new HashSet<string>(
                onlinePlaying
                    .Where(p =>
                        previous.TryGetValue(p.Key, out var old) &&
                        !string.Equals(old.ItemId, p.Value.ItemId, StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.Key),
                StringComparer.OrdinalIgnoreCase);

            bool itemMismatch = onlinePlaying.Select(p => p.Value.ItemId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
            bool ratesOk = onlinePlaying.All(p =>
                Math.Abs(p.Value.PlaybackRate - 1.0) <= SyncConstants.PlaybackRateTolerance);
            var runtimes = onlinePlaying.Select(p => p.Value.RunTimeTicks).ToList();
            bool runtimeOk = runtimes.Count == 2 &&
                runtimes.All(r => r > 0) &&
                Math.Abs(runtimes[0] - runtimes[1]) <= SyncConstants.MaxRuntimeDifferenceTicks;
            bool unambiguousItemChange = itemMismatch && ratesOk && runtimeOk;

            var counterparts = onlinePlaying.Where(p => !changed.Contains(p.Key)).ToList();
            var targets = unambiguousItemChange && changed.Count == 1 && counterparts.Count == 1
                ? counterparts
                : onlinePlaying;

            foreach (var pair in targets)
            {
                Issue(runtime, room, pair.Key, pair.Value, RemoteCommands.Pause, null, now);
            }
        }

        private void WatchingTick(
            RoomRuntime runtime,
            Room room,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            DateTimeOffset now)
        {
            var members = room.JoinedParticipantUserIds;
            if (runtime.SyncItemId == null ||
                snapshots.Count != members.Count ||
                snapshots.Values.Any(s => s == null || !string.Equals(s.ItemId, runtime.SyncItemId, StringComparison.OrdinalIgnoreCase)))
            {
                runtime.ResetToWaiting();
                return;
            }
            var previous = runtime.Previous;
            if (runtime.PreviousAtUtc == null || previous.Count == 0)
            {
                runtime.Previous.Clear();
                foreach (var pair in snapshots)
                {
                    runtime.Previous[pair.Key] = pair.Value;
                }

                runtime.PreviousAtUtc = now;
                return;
            }

            double elapsedSeconds = Math.Max(0, (now - runtime.PreviousAtUtc.Value).TotalSeconds);
            string primary = room.PrimaryUserId;
            var pauseChanges = new List<(string userId, bool paused)>();
            var seekChanges = new List<(string userId, long positionTicks)>();

            foreach (var user in members)
            {
                if (!snapshots.TryGetValue(user, out var current) || current == null ||
                    !previous.TryGetValue(user, out var old) || old == null)
                {
                    continue;
                }

                runtime.Pending.TryGetValue(user, out var pending);
                runtime.Suppressed.TryGetValue(user, out var suppressed);
                bool suppressPause = false;
                bool suppressSeek = false;

                if (suppressed != null)
                {
                    if (suppressed.UntilUtc <= now)
                    {
                        runtime.Suppressed.Remove(user);
                    }
                    else if (PendingMatcher.Matches(suppressed.Command, suppressed.PositionTicks, current))
                    {
                        suppressPause = suppressed.Command == RemoteCommands.Pause ||
                                        suppressed.Command == RemoteCommands.Unpause;
                        suppressSeek = suppressed.Command == RemoteCommands.Seek;
                        runtime.Suppressed.Remove(user);
                    }
                }

                if (current.IsPaused != old.IsPaused)
                {
                    bool alreadyPending = pending != null &&
                        (pending.Command == RemoteCommands.Pause || pending.Command == RemoteCommands.Unpause) &&
                        PendingMatcher.Matches(pending, current);
                    if (!suppressPause && !alreadyPending)
                    {
                        pauseChanges.Add((user, current.IsPaused));
                    }
                }

                long expected = old.PositionTicks;
                if (!old.IsPaused)
                {
                    expected += (long)(elapsedSeconds * old.PlaybackRate * SyncConstants.TicksPerSecond);
                }

                if (Math.Abs(current.PositionTicks - expected) >= SyncConstants.DriftThresholdTicks)
                {
                    bool alreadyPending = pending != null &&
                        pending.Command == RemoteCommands.Seek &&
                        PendingMatcher.Matches(pending, current);
                    if (!suppressSeek && !alreadyPending)
                    {
                        seekChanges.Add((user, current.PositionTicks));
                    }
                }
            }

            if (pauseChanges.Count > 0)
            {
                var winner = pauseChanges.FirstOrDefault(c => c.userId == primary);
                if (winner.userId == null)
                {
                    winner = pauseChanges[0];
                }

                string command = winner.paused ? RemoteCommands.Pause : RemoteCommands.Unpause;
                foreach (var user in members)
                {
                    if (user != winner.userId && snapshots.TryGetValue(user, out var snapshot) && snapshot != null)
                    {
                        Issue(runtime, room, user, snapshot, command, null, now);
                    }
                }
            }

            if (seekChanges.Count > 0)
            {
                var winner = seekChanges.FirstOrDefault(c => c.userId == primary);
                if (winner.userId == null)
                {
                    winner = seekChanges[0];
                }

                foreach (var user in members)
                {
                    if (user != winner.userId && snapshots.TryGetValue(user, out var snapshot) && snapshot != null)
                    {
                        Issue(runtime, room, user, snapshot, RemoteCommands.Seek, winner.positionTicks, now);
                    }
                }
            }

            var positions = members
                .Where(u => snapshots.TryGetValue(u, out var s) && s != null)
                .Select(u => snapshots[u].PositionTicks)
                .ToList();
            if (positions.Count == 2 && Math.Abs(positions[0] - positions[1]) > SyncConstants.SeekToleranceTicks)
            {
                runtime.DriftRounds++;
            }
            else
            {
                runtime.DriftRounds = 0;
            }

            if (runtime.DriftRounds >= SyncConstants.DriftRoundsBeforeSeek && seekChanges.Count == 0)
            {
                var secondary = members.First(u => u != primary);
                if (snapshots.TryGetValue(primary, out var primarySnapshot) && primarySnapshot != null &&
                    snapshots.TryGetValue(secondary, out var secondarySnapshot) && secondarySnapshot != null)
                {
                    Issue(runtime, room, secondary, secondarySnapshot, RemoteCommands.Seek, primarySnapshot.PositionTicks, now);
                }

                runtime.DriftRounds = 0;
            }

            runtime.Previous.Clear();
            foreach (var pair in snapshots)
            {
                runtime.Previous[pair.Key] = pair.Value;
            }

            runtime.PreviousAtUtc = now;
        }
    }
}

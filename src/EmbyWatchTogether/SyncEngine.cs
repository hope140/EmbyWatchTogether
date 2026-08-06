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
        private const string StoppedPlaybackMessageHeader = "一起观看";
        private const string StoppedPlaybackMessageText = "对方已停止播放，请重新打开视频";
        private const string AutomaticResyncMessageHeader = "一起观看";
        private const string AutomaticResyncMessageText = "正在自动重新同步，请稍候";

        private readonly RoomManager _roomManager;
        private readonly ISessionSnapshotProvider _snapshotProvider;
        private readonly ICommandIssuer _issuer;
        private readonly IMessageIssuer _messageIssuer;
        private readonly Func<string> _serverIdProvider;
        private readonly Func<DateTimeOffset> _clock;
        private readonly double _pollIntervalSeconds;
        private readonly bool _pauseOtherOnPlaybackStop;
        private readonly bool _notifyOtherOnPlaybackStop;
        private readonly object _lock = new object();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly AutoResetEvent _wakeEvent = new AutoResetEvent(false);
        private Thread _thread;
        private bool _disposed;

        public SyncEngine(
            RoomManager roomManager,
            ISessionSnapshotProvider snapshotProvider,
            ICommandIssuer issuer,
            Func<string> serverIdProvider,
            Func<DateTimeOffset> clock = null,
            double pollIntervalSeconds = 1.0,
            bool pauseOtherOnPlaybackStop = true,
            bool notifyOtherOnPlaybackStop = true,
            IMessageIssuer messageIssuer = null)
        {
            _roomManager = roomManager ?? throw new ArgumentNullException(nameof(roomManager));
            _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
            _issuer = issuer;
            _messageIssuer = messageIssuer;
            _serverIdProvider = serverIdProvider ?? (() => string.Empty);
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _pollIntervalSeconds = Math.Max(0.05, pollIntervalSeconds);
            _pauseOtherOnPlaybackStop = pauseOtherOnPlaybackStop;
            _notifyOtherOnPlaybackStop = notifyOtherOnPlaybackStop;
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_disposed || _thread != null)
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
            Thread thread;
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                _cts.Cancel();
                _wakeEvent.Set();
                thread = _thread;
            }

            JoinThread(thread);
        }

        /// <summary>
        /// Requests one immediate poll. AutoResetEvent coalesces concurrent
        /// requests so a burst of session events cannot create a poll storm.
        /// </summary>
        public void RequestImmediatePoll()
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                try
                {
                    _wakeEvent.Set();
                }
                catch (ObjectDisposedException)
                {
                    // A concurrent disposal has already completed the wake-up
                    // path; callers must not observe a disposal race.
                }
            }
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

                    // While actively watching, Emby may briefly retain the old
                    // ItemId after a player is closed while reporting
                    // PositionTicks = 0. Treat that transition as a stop, not as
                    // a user-issued seek, before observing pending commands.
                    if (TryGetStoppedUsers(runtime, room, snapshots, out var stoppedUsers))
                    {
                        // SessionSelector omits stopped/offline sessions, so the
                        // same stop condition can be observed on every poll until
                        // the participant starts again. Apply stop side effects
                        // only on the transition into the stopped state.
                        bool stopAlreadyHandled = string.Equals(
                            runtime.Error,
                            StoppedPlaybackError,
                            StringComparison.Ordinal);
                        if (!stopAlreadyHandled)
                        {
                            if (_pauseOtherOnPlaybackStop)
                            {
                                PauseOtherAfterPlaybackStopped(runtime, room, snapshots, stoppedUsers, now);
                            }

                            if (_notifyOtherOnPlaybackStop)
                            {
                                NotifyOtherAfterPlaybackStopped(runtime, room, snapshots, stoppedUsers, now);
                            }
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
                        ScheduleBarrierRetry(runtime, "playback command was not acknowledged", now);
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
                            // A participant may have switched to the next episode or
                            // another item. Do not seek across item boundaries; pause
                            // any active peer(s) once the pair is no longer eligible.
                            // PauseOtherWhenWaiting keeps the solo-player guard.
                            PauseOtherWhenWaiting(runtime, room, snapshots, now);
                            runtime.State = RoomState.Waiting;
                            runtime.Barrier = null;
                            runtime.Previous.Clear();
                        }
                    }
                    else if (eligible)
                    {
                        bool automaticRetry = false;
                        if (runtime.State == RoomState.Waiting && runtime.Error != null)
                        {
                            bool retryReady = runtime.BarrierRetryAtUtc.HasValue &&
                                now >= runtime.BarrierRetryAtUtc.Value;
                            if (retryReady && sameItem)
                            {
                                runtime.Error = null;
                                runtime.BarrierRetryAtUtc = null;
                                automaticRetry = true;
                            }
                            else
                            {
                                results.Add(Result(room, runtime, eligible));
                                continue;
                            }
                        }

                        if (runtime.State != RoomState.Barrier)
                        {
                            StartBarrier(runtime, room, snapshots, now);
                        }

                        if (automaticRetry)
                        {
                            NotifyAutomaticBarrierRetry(room, snapshots, now);
                        }

                        BarrierTick(runtime, room, snapshots, now);
                    }
                    else
                    {
                        // Waiting is also the safe state for a different-item pair:
                        // pause active sessions instead of trying to seek one item to
                        // the other. The helper avoids interrupting a solo player.
                        PauseOtherWhenWaiting(runtime, room, snapshots, now);
                        runtime.State = RoomState.Waiting;
                        runtime.Barrier = null;
                        runtime.Previous.Clear();
                    }

                    results.Add(Result(room, runtime, eligible));
                }
            }

            return results;
        }

        public void Dispose()
        {
            Thread thread;
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _cts.Cancel();
                _wakeEvent.Set();
                thread = _thread;
            }

            JoinThread(thread);
            _wakeEvent.Dispose();
            _cts.Dispose();
        }

        private void Loop()
        {
            var token = _cts.Token;
            var waitHandles = new WaitHandle[] { token.WaitHandle, _wakeEvent };
            var timeoutMilliseconds = GetPollTimeoutMilliseconds();

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
                    WaitHandle.WaitAny(waitHandles, timeoutMilliseconds);
                }
                catch
                {
                    return;
                }
            }
        }

        private int GetPollTimeoutMilliseconds()
        {
            var milliseconds = _pollIntervalSeconds * 1000.0;
            if (milliseconds >= int.MaxValue)
            {
                return int.MaxValue;
            }

            return Math.Max(1, (int)Math.Ceiling(milliseconds));
        }

        private static void JoinThread(Thread thread)
        {
            if (thread == null || thread == Thread.CurrentThread)
            {
                return;
            }

            try
            {
                thread.Join();
            }
            catch
            {
                // A stopping/disposal path must not surface thread races.
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
                runtime.State != RoomState.Watching ||
                room.JoinedParticipantUserIds.Count != 2)
            {
                // A barrier is only the pause/seek/restore handshake. A
                // participant can close the video before playback ever starts;
                // that must reset the handshake, not create a persistent
                // "playback stopped" error.
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

        private void NotifyOtherAfterPlaybackStopped(
            RoomRuntime runtime,
            Room room,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            ISet<string> stoppedUsers,
            DateTimeOffset now)
        {
            if (_messageIssuer == null || room == null || snapshots == null)
            {
                return;
            }

            foreach (var pair in snapshots)
            {
                var snapshot = pair.Value;
                if (snapshot == null || !snapshot.Online || snapshot.Stopped ||
                    (stoppedUsers != null && stoppedUsers.Contains(pair.Key)))
                {
                    continue;
                }

                try
                {
                    // Display messages are advisory: a client that rejects the
                    // capability or fails to receive the message must not alter
                    // the stop transition or its existing pause behavior.
                    _messageIssuer.TryIssueMessage(
                        room.Id,
                        room.AdminUserId,
                        pair.Key,
                        snapshot,
                        StoppedPlaybackMessageHeader,
                        StoppedPlaybackMessageText,
                        timeoutMs: null,
                        now: now,
                        out _);
                }
                catch
                {
                    // Message delivery is best effort and must never block the
                    // playback state machine.
                }
            }
        }

        private void NotifyAutomaticBarrierRetry(
            Room room,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            DateTimeOffset now)
        {
            if (_messageIssuer == null || room == null || snapshots == null)
            {
                return;
            }

            foreach (var userId in room.JoinedParticipantUserIds)
            {
                if (!snapshots.TryGetValue(userId, out var snapshot) || snapshot == null)
                {
                    continue;
                }

                try
                {
                    // Automatic retry notices are advisory. Delivery failures
                    // must not prevent the new barrier from being started.
                    _messageIssuer.TryIssueMessage(
                        room.Id,
                        room.AdminUserId,
                        userId,
                        snapshot,
                        AutomaticResyncMessageHeader,
                        AutomaticResyncMessageText,
                        timeoutMs: null,
                        now: now,
                        out _);
                }
                catch
                {
                    // Message delivery is best effort and must not block sync.
                }
            }
        }

        private static bool IsManualSeek(
            SessionSnapshot previous,
            SessionSnapshot current,
            DateTimeOffset previousAtUtc,
            DateTimeOffset now)
        {
            if (previous == null || current == null)
            {
                return false;
            }

            // A position change observed together with play/pause or playback-rate
            // changes belongs to that user action. Do not reinterpret the elapsed
            // movement as a seek, especially when the next poll is delayed.
            if (previous.IsPaused != current.IsPaused ||
                Math.Abs(previous.PlaybackRate - current.PlaybackRate) > SyncConstants.PlaybackRateTolerance)
            {
                return false;
            }

            double elapsedSeconds = Math.Max(0, (now - previousAtUtc).TotalSeconds);
            double expectedPosition = previous.PositionTicks;
            if (!previous.IsPaused)
            {
                expectedPosition += elapsedSeconds * previous.PlaybackRate * SessionSnapshot.TicksPerSecond;
            }

            double difference = Math.Abs(current.PositionTicks - expectedPosition);
            return difference >= SyncConstants.DriftThresholdTicks;
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

                double timeoutSeconds = SyncConstants.PendingTimeoutSeconds;
                if (runtime.State == RoomState.Barrier &&
                    pending.Retries >= SyncConstants.MaxPendingRetries)
                {
                    timeoutSeconds += SyncConstants.PendingRetryGraceSeconds;
                }

                if ((now - pending.IssuedAtUtc).TotalSeconds < timeoutSeconds)
                {
                    continue;
                }

                if (pending.Retries < SyncConstants.MaxPendingRetries && snapshot != null)
                {
                    long? positionTicks = pending.PositionTicks;
                    runtime.Pending.Remove(userId);
                    if (Issue(runtime, room, userId, snapshot, pending.Command, positionTicks, now))
                    {
                        if (runtime.Pending.TryGetValue(userId, out var retry))
                        {
                            retry.Retries = pending.Retries + 1;
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

        private static void ScheduleBarrierRetry(
            RoomRuntime runtime,
            string error,
            DateTimeOffset now)
        {
            runtime.ResetToWaiting();
            runtime.Error = error;
            runtime.BarrierRetryAtUtc = now.AddSeconds(SyncConstants.AutomaticBarrierRetryDelaySeconds);
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
                return true;
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
                        // The primary may continue playing until Emby applies the
                        // pause command. Re-anchor after both acknowledgements so
                        // the secondary does not seek to the stale start snapshot.
                        barrier.PrimaryPositionTicks = snapshots[room.PrimaryUserId].PositionTicks;
                        barrier.Stage = BarrierStage.Seek;
                        barrier.StartedAtUtc = now;
                        return;
                    }

                    if (runtime.Pending.Count == 0 &&
                        (now - barrier.StartedAtUtc).TotalSeconds >= SyncConstants.BarrierTimeoutSeconds)
                    {
                        ScheduleBarrierRetry(runtime, "barrier pause timed out", now);
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

                    if (runtime.Pending.Count == 0 &&
                        (now - barrier.StartedAtUtc).TotalSeconds >= SyncConstants.BarrierTimeoutSeconds)
                    {
                        ScheduleBarrierRetry(runtime, "barrier seek timed out", now);
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
                        // Unpause commands are delivered sequentially. When the
                        // original state was playing, give the secondary one final
                        // position correction before declaring the barrier complete.
                        if (!barrier.PrimaryPaused)
                        {
                            barrier.Stage = BarrierStage.FinalAlign;
                            barrier.StartedAtUtc = now;
                            return;
                        }

                        EnterWatching(runtime, barrier, snapshots, now);
                    }
                    else if (runtime.Pending.Count == 0 &&
                             (now - barrier.StartedAtUtc).TotalSeconds >= SyncConstants.BarrierTimeoutSeconds)
                    {
                        ScheduleBarrierRetry(runtime, "barrier restore timed out", now);
                    }

                    return;

                case BarrierStage.FinalAlign:
                    var finalSecondary = members.First(u => u != room.PrimaryUserId);
                    if (!barrier.FinalAlignSent)
                    {
                        long primaryPosition = snapshots[room.PrimaryUserId].PositionTicks;
                        long secondaryPosition = snapshots[finalSecondary].PositionTicks;
                        if (Math.Abs(primaryPosition - secondaryPosition) <= SyncConstants.StartupAlignToleranceTicks)
                        {
                            EnterWatching(runtime, barrier, snapshots, now);
                            return;
                        }

                        barrier.FinalAlignPositionTicks = primaryPosition;
                        Issue(
                            runtime,
                            room,
                            finalSecondary,
                            snapshots[finalSecondary],
                            RemoteCommands.Seek,
                            primaryPosition,
                            now);
                        barrier.FinalAlignSent = true;
                        return;
                    }

                    if (Math.Abs(
                            snapshots[finalSecondary].PositionTicks - barrier.FinalAlignPositionTicks) <=
                        SyncConstants.SeekToleranceTicks)
                    {
                        EnterWatching(runtime, barrier, snapshots, now);
                    }
                    else if (runtime.Pending.Count == 0 &&
                             (now - barrier.StartedAtUtc).TotalSeconds >= SyncConstants.BarrierTimeoutSeconds)
                    {
                        runtime.State = RoomState.Waiting;
                        runtime.Error = "barrier final alignment timed out";
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

        private static void EnterWatching(
            RoomRuntime runtime,
            BarrierState barrier,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            DateTimeOffset now)
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
            runtime.SyncItemId = barrier.ItemId;
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

                // Compare the current position with where the old snapshot says
                // playback should have reached during this polling interval. The
                // previous implementation compared only raw deltas, so a delayed
                // snapshot or a long poll interval could look like a manual seek.
                // Normal playback drift is intentionally not corrected here; only a
                // single, clearly out-of-band position jump is propagated.
                if (IsManualSeek(old, current, runtime.PreviousAtUtc.Value, now))
                {
                    bool pendingSeek = pending != null &&
                        pending.Command == RemoteCommands.Seek;
                    if (!suppressSeek && !pendingSeek)
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

            runtime.Previous.Clear();
            foreach (var pair in snapshots)
            {
                runtime.Previous[pair.Key] = pair.Value;
            }

            runtime.PreviousAtUtc = now;
        }
    }
}

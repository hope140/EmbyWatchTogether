using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MediaBrowser.Model.Logging;

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
        private const string BarrierSeekRetryBudgetError = "barrier seek retry budget exhausted";
        private const string WaitingPauseRetryLimitError = "waiting pause retry limit reached";
        private const int NotificationTimeoutMs = 3000;
        private const double RoomPollErrorLogIntervalSeconds = 30.0;
        private static readonly TimeSpan ExternalCallTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);
        private const double AckLatencyEmaAlpha = 0.3;
        private const double MissingSessionDebounceSeconds = 2;

        private readonly RoomManager _roomManager;
        private readonly ISessionSnapshotProvider _snapshotProvider;
        private readonly ICommandIssuer _issuer;
        private readonly IMessageIssuer _messageIssuer;
        private readonly ILogger _logger;
        private readonly Func<string> _serverIdProvider;
        private readonly Func<DateTimeOffset> _clock;
        private double _pollIntervalSeconds;
        private bool _pauseOtherOnPlaybackStop;
        private bool _notifyOtherOnPlaybackStop;
        private readonly object _lock = new object();
        private readonly object _roomPollLogLock = new object();
        private readonly Dictionary<string, DateTimeOffset> _lastRoomPollErrorAtUtc =
            new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly AutoResetEvent _wakeEvent = new AutoResetEvent(false);
        private Thread _thread;
        private bool _disposed;
        private bool _resourcesDisposed;
        private bool _threadExited;

        public SyncEngine(
            RoomManager roomManager,
            ISessionSnapshotProvider snapshotProvider,
            ICommandIssuer issuer,
            Func<string> serverIdProvider,
            Func<DateTimeOffset> clock = null,
            double pollIntervalSeconds = 1.0,
            bool pauseOtherOnPlaybackStop = true,
            bool notifyOtherOnPlaybackStop = true,
            IMessageIssuer messageIssuer = null,
            ILogManager logManager = null)
        {
            _roomManager = roomManager ?? throw new ArgumentNullException(nameof(roomManager));
            _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
            _issuer = issuer;
            _messageIssuer = messageIssuer;
            try
            {
                _logger = logManager?.GetLogger("WatchTogether.SyncEngine");
            }
            catch
            {
                _logger = null;
            }

            _serverIdProvider = serverIdProvider ?? (() => string.Empty);
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            var options = new SyncEngineOptions(
                pollIntervalSeconds,
                pauseOtherOnPlaybackStop,
                notifyOtherOnPlaybackStop);
            _pollIntervalSeconds = options.PollIntervalSeconds;
            _pauseOtherOnPlaybackStop = options.PauseOtherOnPlaybackStop;
            _notifyOtherOnPlaybackStop = options.NotifyOtherOnPlaybackStop;
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
                _logger?.Info(
                    $"WatchTogether sync engine started: pollInterval={_pollIntervalSeconds:0.##}s, " +
                    $"pauseOtherOnPlaybackStop={_pauseOtherOnPlaybackStop}, " +
                    $"notifyOtherOnPlaybackStop={_notifyOtherOnPlaybackStop}");
            }
        }

        /// <summary>
        /// Applies the live synchronization settings and wakes a sleeping loop
        /// so the next poll observes them without waiting for the old interval.
        /// </summary>
        public void UpdateOptions(SyncEngineOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                _pollIntervalSeconds = SyncEngineOptions.NormalizePollIntervalSeconds(
                    options.PollIntervalSeconds);
                _pauseOtherOnPlaybackStop = options.PauseOtherOnPlaybackStop;
                _notifyOtherOnPlaybackStop = options.NotifyOtherOnPlaybackStop;
                SignalWakeLocked();
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

                CancelLocked();
                thread = _thread;
            }

            if (!JoinThread(thread))
            {
                _logger?.Warn("WatchTogether sync engine stop timed out after 10s");
            }
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

                SignalWakeLocked();
            }
        }

        public IReadOnlyList<RoomPollResult> PollOnce(DateTimeOffset now)
        {
            var options = GetOptionsSnapshot();
            var results = new List<RoomPollResult>();
            var rooms = _roomManager.ListRooms();
            if (rooms.Count == 0)
            {
                return results;
            }

            string currentServerId = _serverIdProvider();
            var validRoomIds = new List<string>();
            foreach (var listedRoom in rooms)
            {
                try
                {
                    using (var access = _roomManager.TryEnterRoom(listedRoom.Id))
                    {
                        if (access == null)
                        {
                            continue;
                        }

                        var room = access.Room;
                        var runtime = access.Runtime;
                        if (!string.Equals(room.ServerId, currentServerId, StringComparison.OrdinalIgnoreCase))
                        {
                            runtime.State = RoomState.Unavailable;
                            runtime.Error = "room server is unavailable";
                            runtime.Barrier = null;
                            runtime.Pending.Clear();
                            _logger?.Info($"Room {room.Id}: marked unavailable (server mismatch)");
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
                            validRoomIds.Add(room.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogRoomPollException(listedRoom?.Id, now, ex);
                }
            }

            if (validRoomIds.Count == 0)
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

            foreach (var roomId in validRoomIds)
            {
                try
                {
                    using (var access = _roomManager.TryEnterRoom(roomId))
                    {
                        if (access == null)
                        {
                            continue;
                        }

                    var room = access.Room;
                    var runtime = access.Runtime;
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

                    if (TryGetStoppedUsers(runtime, room, snapshots, now, out var stoppedUsers))
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
                            if (options.PauseOtherOnPlaybackStop)
                            {
                                PauseOtherAfterPlaybackStopped(runtime, room, snapshots, stoppedUsers, now);
                            }

                            if (options.NotifyOtherOnPlaybackStop)
                            {
                                NotifyOtherAfterPlaybackStopped(runtime, room, snapshots, stoppedUsers, now);
                            }

                            _logger?.Info(
                                $"Room {room.Id}: playback stopped by {string.Join(",", stoppedUsers)}; " +
                                $"pausedOther={options.PauseOtherOnPlaybackStop}, notifiedOther={options.NotifyOtherOnPlaybackStop}");
                        }

                        runtime.ResetToWaiting();
                        runtime.Previous.Clear();
                        runtime.PreviousAtUtc = null;
                        runtime.MissingSessionSinceUtc = null;
                        runtime.Error = StoppedPlaybackError;
                        results.Add(Result(room, runtime, eligible));
                        continue;
                    }

                    if (runtime.State == RoomState.Watching &&
                        runtime.MissingSessionSinceUtc.HasValue)
                    {
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

                    PruneWaitingPauseRetries(runtime, snapshots);
                    bool staleDeferredCommand = DiscardStaleDeferredCommands(
                        runtime,
                        snapshots,
                        sameItem);
                    bool pendingFailed = ObservePending(
                        runtime,
                        room,
                        snapshots,
                        sameItem,
                        now,
                        out var stalePendingCommand);
                    if (staleDeferredCommand || stalePendingCommand)
                    {
                        // A command captured for a different session or item
                        // must never be acknowledged or retried against the
                        // current snapshot. Rebuild the barrier from the
                        // current identities instead.
                        runtime.ResetToWaiting();
                    }
                    if (!sameItem && runtime.State != RoomState.Waiting &&
                        !runtime.MissingSessionSinceUtc.HasValue)
                    {
                        runtime.ResetToWaiting();
                    }

                    if (!sameItem && snapshots.Count == 2 &&
                        !runtime.WaitingPauseRetries.Values.Any(state => state.Exhausted))
                    {
                        runtime.Error = "两位参与者打开了不同视频，暂不发送同步指令";
                    }
                    else if (sameItem && runtime.Error == "两位参与者打开了不同视频，暂不发送同步指令")
                    {
                        runtime.Error = null;
                    }

                    if (pendingFailed &&
                        !(runtime.State == RoomState.Barrier &&
                          runtime.Barrier?.Stage == BarrierStage.Seek) &&
                        !string.Equals(runtime.Error, BarrierSeekRetryBudgetError, StringComparison.Ordinal))
                    {
                        ScheduleBarrierRetry(runtime, room.Id, "playback command was not acknowledged", now);
                        _logger?.Warn($"Room {room.Id}: pending playback command not acknowledged; waiting for retry");
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
                            if (runtime.MissingSessionSinceUtc.HasValue)
                            {
                                results.Add(Result(room, runtime, eligible));
                                continue;
                            }

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
                catch (Exception ex)
                {
                    // A broken room must not prevent the remaining rooms from
                    // being polled in this cycle. The per-room logger is
                    // rate-limited so a persistent client failure is visible
                    // without flooding the server log.
                    LogRoomPollException(roomId, now, ex);
                }
            }

            return results;
        }

        public void Dispose()
        {
            Thread thread;
            lock (_lock)
            {
                if (_resourcesDisposed)
                {
                    return;
                }

                _disposed = true;
                CancelLocked();
                thread = _thread;
            }

            if (!JoinThread(thread))
            {
                _logger?.Warn("WatchTogether sync engine dispose timed out after 10s");
                return;
            }

            DisposeResources();
        }

        private void Loop()
        {
            var token = _cts.Token;
            var waitHandles = new WaitHandle[] { token.WaitHandle, _wakeEvent };
            try
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        PollOnce(_clock());
                    }
                    catch
                    {
                        // Polling must never kill the background thread. Room
                        // failures are isolated and logged by PollOnce; this
                        // guard remains for failures outside a room context.
                    }

                    try
                    {
                        WaitHandle.WaitAny(waitHandles, GetPollTimeoutMilliseconds());
                    }
                    catch
                    {
                        return;
                    }
                }
            }
            finally
            {
                bool disposeResources;
                lock (_lock)
                {
                    _threadExited = true;
                    disposeResources = _disposed && !_resourcesDisposed;
                    if (disposeResources)
                    {
                        _resourcesDisposed = true;
                    }
                }

                if (disposeResources)
                {
                    DisposeWaitHandles();
                }
            }
        }

        private int GetPollTimeoutMilliseconds()
        {
            double pollIntervalSeconds;
            lock (_lock)
            {
                pollIntervalSeconds = _pollIntervalSeconds;
            }

            var milliseconds = pollIntervalSeconds * 1000.0;
            if (milliseconds >= int.MaxValue)
            {
                return int.MaxValue;
            }

            return Math.Max(1, (int)Math.Ceiling(milliseconds));
        }

        private bool JoinThread(Thread thread)
        {
            if (thread == null)
            {
                return true;
            }

            if (thread == Thread.CurrentThread)
            {
                return false;
            }

            try
            {
                return thread.Join(StopTimeout);
            }
            catch
            {
                // A stopping/disposal path must not surface thread races.
                return false;
            }
        }

        private void CancelLocked()
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Disposal has already completed the cancellation path.
            }

            SignalWakeLocked();
        }

        private void SignalWakeLocked()
        {
            try
            {
                _wakeEvent.Set();
            }
            catch (ObjectDisposedException)
            {
                // A completed disposal must not escape into an event callback.
            }
        }

        private SyncEngineOptions GetOptionsSnapshot()
        {
            lock (_lock)
            {
                return new SyncEngineOptions(
                    _pollIntervalSeconds,
                    _pauseOtherOnPlaybackStop,
                    _notifyOtherOnPlaybackStop);
            }
        }

        private void DisposeResources()
        {
            lock (_lock)
            {
                if (_resourcesDisposed || (_thread != null && !_threadExited))
                {
                    return;
                }

                _resourcesDisposed = true;
            }

            DisposeWaitHandles();
        }

        private void DisposeWaitHandles()
        {
            try
            {
                _wakeEvent.Dispose();
            }
            catch
            {
                // Dispose is best effort after the thread has exited.
            }

            try
            {
                _cts.Dispose();
            }
            catch
            {
                // Dispose is best effort after the thread has exited.
            }
        }

        private void LogRoomPollException(string roomId, DateTimeOffset now, Exception exception)
        {
            string key = roomId ?? "<unknown>";
            bool shouldLog;
            lock (_roomPollLogLock)
            {
                if (!_lastRoomPollErrorAtUtc.TryGetValue(key, out var lastLoggedAt) ||
                    (now - lastLoggedAt).TotalSeconds >= RoomPollErrorLogIntervalSeconds)
                {
                    _lastRoomPollErrorAtUtc[key] = now;
                    shouldLog = true;
                }
                else
                {
                    shouldLog = false;
                }
            }

            if (shouldLog)
            {
                _logger?.Warn(
                    $"Room {key}: poll failed ({exception?.GetType().Name ?? "unknown"}): " +
                    $"{exception?.Message ?? "unknown error"}");
            }
        }

        private static bool TryGetStoppedUsers(
            RoomRuntime runtime,
            Room room,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            DateTimeOffset now,
            out HashSet<string> stoppedUsers)
        {
            stoppedUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (runtime == null || room == null || snapshots == null ||
                (runtime.State != RoomState.Watching && !runtime.MissingSessionSinceUtc.HasValue) ||
                room.JoinedParticipantUserIds.Count != 2)
            {
                // A barrier is only the pause/seek/restore handshake. A
                // participant can close the video before playback ever starts;
                // that must reset the handshake, not create a persistent
                // "playback stopped" error.
                return false;
            }

            var members = room.JoinedParticipantUserIds;
            bool stopObserved = false;
            foreach (var userId in members)
            {
                if (!snapshots.TryGetValue(userId, out var current) || current == null || !current.Online)
                {
                    stoppedUsers.Add(userId);
                    stopObserved = true;
                }
                else if (current.Stopped)
                {
                    stoppedUsers.Add(userId);
                    stopObserved = true;
                }
            }

            if (!stopObserved)
            {
                runtime.MissingSessionSinceUtc = null;
                return false;
            }

            if (!runtime.MissingSessionSinceUtc.HasValue)
            {
                runtime.MissingSessionSinceUtc = now;
                return false;
            }

            return (now - runtime.MissingSessionSinceUtc.Value).TotalSeconds >= MissingSessionDebounceSeconds;
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
                    TryIssueMessage(
                        room.Id,
                        room.AdminUserId,
                        pair.Key,
                        snapshot,
                        StoppedPlaybackMessageHeader,
                        StoppedPlaybackMessageText,
                        timeoutMs: NotificationTimeoutMs,
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
                    TryIssueMessage(
                        room.Id,
                        room.AdminUserId,
                        userId,
                        snapshot,
                        AutomaticResyncMessageHeader,
                        AutomaticResyncMessageText,
                        timeoutMs: NotificationTimeoutMs,
                        now: now,
                        out _);
                }
                catch
                {
                    // Message delivery is best effort and must not block sync.
                }
            }
        }

        private static void UpdateAckLatency(RoomRuntime runtime, string userId, double latencySeconds)
        {
            double clamped = Math.Min(Math.Max(0, latencySeconds), SyncConstants.AckLatencyMaxSeconds);
            if (!runtime.AckLatencySeconds.TryGetValue(userId, out var current))
            {
                runtime.AckLatencySeconds[userId] = clamped;
                return;
            }

            runtime.AckLatencySeconds[userId] =
                Math.Min(SyncConstants.AckLatencyMaxSeconds, current * (1 - AckLatencyEmaAlpha) + clamped * AckLatencyEmaAlpha);
        }

        private static long GetSeekDetectionThresholdTicks(RoomRuntime runtime, string userId)
        {
            double latencySeconds = 0;
            if (runtime != null &&
                runtime.AckLatencySeconds.TryGetValue(userId, out var measured))
            {
                latencySeconds = Math.Max(0, measured);
            }

            double thresholdSeconds = Math.Max(SyncConstants.SeekDetectionFloorSeconds, latencySeconds);
            return (long)(thresholdSeconds * SessionSnapshot.TicksPerSecond);
        }

        private static bool IsManualSeek(
            SessionSnapshot previous,
            SessionSnapshot current,
            DateTimeOffset previousAtUtc,
            DateTimeOffset now,
            DateTimeOffset? lastSeekAtUtc,
            long seekDetectionThresholdTicks)
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

            // External players often report a small position rewind (a few
            // seconds) shortly after a remote seek lands while re-basing their
            // clock. Ignore such small rewinds only inside the seek calibration
            // window; outside it a backward jump is a real user seek.
            if (current.PositionTicks < previous.PositionTicks &&
                previous.PositionTicks - current.PositionTicks < SyncConstants.ManualSeekBackwardToleranceTicks &&
                lastSeekAtUtc.HasValue &&
                (now - lastSeekAtUtc.Value).TotalSeconds < SyncConstants.SeekCalibrationWindowSeconds)
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
            return difference >= seekDetectionThresholdTicks;
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

        private static bool HasSameIdentity(
            string sessionId,
            string itemId,
            SessionSnapshot snapshot)
        {
            return snapshot != null &&
                !string.IsNullOrEmpty(sessionId) &&
                !string.IsNullOrEmpty(itemId) &&
                string.Equals(sessionId, snapshot.SessionId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(itemId, snapshot.ItemId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCurrentCommandIdentity(
            RoomRuntime runtime,
            bool sameItem,
            string userId,
            string sessionId,
            string itemId,
            SessionSnapshot snapshot)
        {
            if (!sameItem || snapshot == null ||
                !string.Equals(snapshot.UserId, userId, StringComparison.OrdinalIgnoreCase) ||
                !HasSameIdentity(sessionId, itemId, snapshot))
            {
                return false;
            }

            string expectedItem = runtime?.Barrier?.ItemId ?? runtime?.SyncItemId;
            return string.IsNullOrEmpty(expectedItem) ||
                string.Equals(expectedItem, snapshot.ItemId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool DiscardStaleDeferredCommands(
            RoomRuntime runtime,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            bool sameItem)
        {
            bool discarded = false;

            foreach (var pair in runtime.Suppressed.ToList())
            {
                snapshots.TryGetValue(pair.Key, out var snapshot);
                if (IsCurrentCommandIdentity(
                    runtime,
                    sameItem,
                    pair.Key,
                    pair.Value?.SessionId,
                    pair.Value?.ItemId,
                    snapshot))
                {
                    continue;
                }

                runtime.Suppressed.Remove(pair.Key);
                discarded = true;
            }

            foreach (var pair in runtime.PauseAlign.ToList())
            {
                string userId = pair.Key;
                var align = pair.Value;
                snapshots.TryGetValue(userId, out var follower);
                SessionSnapshot anchor = null;
                if (!string.IsNullOrEmpty(align?.AnchorUserId))
                {
                    snapshots.TryGetValue(align.AnchorUserId, out anchor);
                }

                bool targetCurrent = align != null && IsCurrentCommandIdentity(
                    runtime,
                    sameItem,
                    userId,
                    align.SessionId,
                    align.ItemId,
                    follower);
                bool anchorCurrent = align != null && IsCurrentCommandIdentity(
                    runtime,
                    sameItem,
                    align.AnchorUserId,
                    align.AnchorSessionId,
                    align.AnchorItemId,
                    anchor);
                if (targetCurrent && anchorCurrent)
                {
                    continue;
                }

                runtime.PauseAlign.Remove(userId);
                discarded = true;
            }

            return discarded;
        }

        private static string GetPauseCapabilityKey(SessionSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return string.Empty;
            }

            var capabilities = snapshot.Capabilities;
            string commands = capabilities?.SupportedCommands == null
                ? string.Empty
                : string.Join(",", capabilities.SupportedCommands.OrderBy(
                    command => command,
                    StringComparer.OrdinalIgnoreCase));
            return string.Concat(
                snapshot.SupportsRemoteControl ? "1" : "0",
                "|",
                capabilities?.SupportsRemoteControl == true ? "1" : "0",
                "|",
                capabilities?.CanPause == true ? "1" : "0",
                "|",
                commands);
        }

        private static bool IsCurrentWaitingPauseRetry(
            WaitingPauseRetryState retry,
            SessionSnapshot snapshot)
        {
            return retry != null && snapshot != null && snapshot.Online && !snapshot.IsPaused &&
                string.Equals(retry.SessionId, snapshot.SessionId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(retry.ItemId, snapshot.ItemId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(retry.CapabilityKey, GetPauseCapabilityKey(snapshot), StringComparison.Ordinal);
        }

        private static void PruneWaitingPauseRetries(
            RoomRuntime runtime,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots)
        {
            bool clearedCondition = false;
            foreach (var pair in runtime.WaitingPauseRetries.ToList())
            {
                snapshots.TryGetValue(pair.Key, out var snapshot);
                if (IsCurrentWaitingPauseRetry(pair.Value, snapshot))
                {
                    continue;
                }

                runtime.WaitingPauseRetries.Remove(pair.Key);
                clearedCondition = true;
            }

            if (clearedCondition &&
                !runtime.WaitingPauseRetries.Values.Any(state => state.Exhausted) &&
                string.Equals(runtime.Error, WaitingPauseRetryLimitError, StringComparison.Ordinal))
            {
                runtime.Error = null;
            }
        }

        private bool ObservePending(
            RoomRuntime runtime,
            Room room,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            bool sameItem,
            DateTimeOffset now,
            out bool stalePendingCommand)
        {
            bool failed = false;
            bool seekFailed = false;
            bool nonSeekFailed = false;
            stalePendingCommand = false;
            foreach (var pair in runtime.Pending.ToList())
            {
                string userId = pair.Key;
                var pending = pair.Value;
                snapshots.TryGetValue(userId, out var snapshot);

                if (!IsCurrentCommandIdentity(
                    runtime,
                    sameItem,
                    userId,
                    pending?.SessionId,
                    pending?.ItemId,
                    snapshot))
                {
                    runtime.Pending.Remove(userId);
                    stalePendingCommand = true;
                    continue;
                }

                if (PendingMatcher.Matches(pending, snapshot))
                {
                    double latencySeconds = Math.Max(0, (now - pending.IssuedAtUtc).TotalSeconds);
                    UpdateAckLatency(runtime, userId, latencySeconds);
                    runtime.Pending.Remove(userId);
                    runtime.Suppressed[userId] = new SuppressedCommand
                    {
                        SessionId = pending.SessionId,
                        ItemId = pending.ItemId,
                        Command = pending.Command,
                        PositionTicks = pending.PositionTicks,
                        UntilUtc = now + TimeSpan.FromSeconds(SyncConstants.SuppressSeconds),
                    };
                    _logger?.Info(
                        $"Room {room.Id}: pending {pending.Command} acknowledged by {userId} " +
                        $"(position {FormatPosition(pending.PositionTicks)}s, latency {latencySeconds:0.0}s), " +
                        $"suppressing {SyncConstants.SuppressSeconds:0}s");
                    continue;
                }

                bool seekBudgetExpired = pending.Command == RemoteCommands.Seek &&
                    runtime.State == RoomState.Barrier &&
                    runtime.Barrier?.Stage == BarrierStage.Seek &&
                    IsSeekRetryBudgetExpired(runtime.Barrier, now);
                if (seekBudgetExpired)
                {
                    runtime.Pending.Remove(userId);
                    failed = true;
                    seekFailed = true;
                    _logger?.Warn(
                        $"Room {room.Id}: pending Seek for {userId} reached the barrier retry deadline");
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
                    _logger?.Info(
                        $"Room {room.Id}: pending {pending.Command} for {userId} timed out; " +
                        $"retry {pending.Retries + 1}/{SyncConstants.MaxPendingRetries}");
                    if (Issue(runtime, room, userId, snapshot, pending.Command, positionTicks, now, out _))
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
                    else
                    {
                        failed = true;
                        seekFailed |= pending.Command == RemoteCommands.Seek;
                        nonSeekFailed |= pending.Command != RemoteCommands.Seek;
                    }
                }
                else
                {
                    runtime.Pending.Remove(userId);
                    failed = true;
                    seekFailed |= pending.Command == RemoteCommands.Seek;
                    nonSeekFailed |= pending.Command != RemoteCommands.Seek;
                    _logger?.Warn(
                        $"Room {room.Id}: pending {pending.Command} for {userId} failed " +
                        $"after {SyncConstants.MaxPendingRetries + 1} attempts");
                }
            }

            if (failed)
            {
                if (seekFailed &&
                    !nonSeekFailed &&
                    runtime.State == RoomState.Barrier &&
                    runtime.Barrier?.Stage == BarrierStage.Seek)
                {
                    if (IsSeekRetryBudgetExpired(runtime.Barrier, now))
                    {
                        FailSeekBarrier(runtime, room.Id);
                    }
                    else
                    {
                        ScheduleSeekBarrierRetry(
                            runtime,
                            room.Id,
                            "playback command was not acknowledged",
                            now);
                    }
                }
                else
                {
                    runtime.State = RoomState.Waiting;
                    runtime.Error = "playback command was not acknowledged";
                }
            }

            return failed;
        }

        private void ScheduleBarrierRetry(
            RoomRuntime runtime,
            string roomId,
            string error,
            DateTimeOffset now)
        {
            runtime.ResetToWaiting();
            runtime.Error = error;
            runtime.BarrierRetryAtUtc = now.AddSeconds(SyncConstants.AutomaticBarrierRetryDelaySeconds);
            _logger?.Info($"Room {roomId}: barrier retry scheduled: {error}");
        }

        private void ScheduleSeekBarrierRetry(
            RoomRuntime runtime,
            string roomId,
            string error,
            DateTimeOffset now)
        {
            var barrier = runtime.Barrier;
            if (runtime.State != RoomState.Barrier || barrier?.Stage != BarrierStage.Seek)
            {
                ScheduleBarrierRetry(runtime, roomId, error, now);
                return;
            }

            runtime.Error = error;
            barrier.SeekSent = false;
            EnsureSeekRetryDeadline(barrier, now);
            if (IsSeekRetryBudgetExpired(barrier, now))
            {
                FailSeekBarrier(runtime, roomId);
                return;
            }

            DateTimeOffset retryAt = now.AddSeconds(SyncConstants.AutomaticBarrierRetryDelaySeconds);
            if (barrier.SeekRetryDeadlineAtUtc.HasValue &&
                retryAt > barrier.SeekRetryDeadlineAtUtc.Value)
            {
                retryAt = barrier.SeekRetryDeadlineAtUtc.Value;
            }

            barrier.SeekRetryAtUtc = retryAt;
            _logger?.Info($"Room {roomId}: seek retry scheduled: {error}");
        }

        private static void EnsureSeekRetryDeadline(BarrierState barrier, DateTimeOffset now)
        {
            if (barrier != null && !barrier.SeekRetryDeadlineAtUtc.HasValue)
            {
                barrier.SeekRetryDeadlineAtUtc = now.AddSeconds(
                    SyncConstants.BarrierSeekRetryBudgetSeconds);
            }
        }

        private static bool IsSeekRetryBudgetExpired(BarrierState barrier, DateTimeOffset now)
        {
            return barrier?.SeekRetryDeadlineAtUtc.HasValue == true &&
                now >= barrier.SeekRetryDeadlineAtUtc.Value;
        }

        private void FailSeekBarrier(RoomRuntime runtime, string roomId)
        {
            runtime.ResetToWaiting();
            runtime.Error = BarrierSeekRetryBudgetError;
            runtime.BarrierRetryAtUtc = null;
            _logger?.Warn($"Room {roomId}: {BarrierSeekRetryBudgetError}");
        }

        private bool TryIssueCommand(
            string roomId,
            string controllingUserId,
            string userId,
            SessionSnapshot snapshot,
            string command,
            long? positionTicks,
            DateTimeOffset now,
            out string error)
        {
            if (_issuer == null)
            {
                error = "no command issuer configured";
                return false;
            }

            var cancellable = _issuer as ICancellableCommandIssuer;
            if (cancellable == null)
            {
                // Keep legacy test doubles and third-party implementations
                // source-compatible while the built-in issuer uses the
                // cancellation-aware path below.
                return _issuer.TryIssue(
                    roomId,
                    controllingUserId,
                    userId,
                    snapshot,
                    command,
                    positionTicks,
                    now,
                    out error);
            }

            using (var timeout = new CancellationTokenSource(ExternalCallTimeout))
            {
                return cancellable.TryIssue(
                    roomId,
                    controllingUserId,
                    userId,
                    snapshot,
                    command,
                    positionTicks,
                    now,
                    timeout.Token,
                    out error);
            }
        }

        private bool TryIssueMessage(
            string roomId,
            string controllingUserId,
            string userId,
            SessionSnapshot snapshot,
            string header,
            string text,
            int? timeoutMs,
            DateTimeOffset now,
            out string error)
        {
            if (_messageIssuer == null)
            {
                error = "no message issuer configured";
                return false;
            }

            var cancellable = _messageIssuer as ICancellableMessageIssuer;
            if (cancellable == null)
            {
                // Keep legacy test doubles and third-party implementations
                // source-compatible while the built-in issuer uses the
                // cancellation-aware path below.
                return _messageIssuer.TryIssueMessage(
                    roomId,
                    controllingUserId,
                    userId,
                    snapshot,
                    header,
                    text,
                    timeoutMs,
                    now,
                    out error);
            }

            using (var timeout = new CancellationTokenSource(ExternalCallTimeout))
            {
                return cancellable.TryIssueMessage(
                    roomId,
                    controllingUserId,
                    userId,
                    snapshot,
                    header,
                    text,
                    timeoutMs,
                    now,
                    timeout.Token,
                    out error);
            }
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
            return Issue(runtime, room, userId, snapshot, command, positionTicks, now, out _);
        }

        private bool Issue(
            RoomRuntime runtime,
            Room room,
            string userId,
            SessionSnapshot snapshot,
            string command,
            long? positionTicks,
            DateTimeOffset now,
            out string failure)
        {
            failure = null;
            if (runtime.Pending.TryGetValue(userId, out var existing))
            {
                if (!HasSameIdentity(existing.SessionId, existing.ItemId, snapshot))
                {
                    // Never let a command captured for an old device/item suppress
                    // a command for the current snapshot.
                    runtime.Pending.Remove(userId);
                }
                else if (string.Equals(existing.Command, command, StringComparison.Ordinal))
                {
                    return true;
                }
                else
                {
                    // A Seek waiting for acknowledgement must not be replaced by
                    // Pause/Unpause (or vice versa) for the same session and item.
                    // Callers can rebuild a Barrier explicitly when the command
                    // sequence has to change.
                    failure =
                        $"{existing.Command} command is pending for {userId}; cannot issue {command}";
                    return false;
                }
            }

            bool ok = TryIssueCommand(
                room.Id,
                room.AdminUserId,
                userId,
                snapshot,
                command,
                positionTicks,
                now,
                out var error);

            if (!ok)
            {
                failure = $"{command} command failed: {error}";
                _logger?.Warn($"Room {room.Id}: {failure}");
                return false;
            }

            runtime.Pending[userId] = new PendingCommand
            {
                UserId = userId,
                SessionId = snapshot.SessionId,
                ItemId = snapshot.ItemId,
                Command = command,
                PositionTicks = positionTicks,
                IssuedAtUtc = now,
                Retries = 0,
            };
            if (string.Equals(command, RemoteCommands.Seek, StringComparison.Ordinal))
            {
                runtime.LastSeekAtUtc[userId] = now;
            }

            _logger?.Info(
                $"Room {room.Id}: issue {command} to {userId} (position {FormatPosition(positionTicks)}s)");
            return true;
        }

        private static bool HasPendingSeekForCurrentIdentity(
            RoomRuntime runtime,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots)
        {
            return runtime.Pending.Any(pair =>
            {
                if (pair.Value == null ||
                    !string.Equals(pair.Value.Command, RemoteCommands.Seek, StringComparison.Ordinal) ||
                    !snapshots.TryGetValue(pair.Key, out var snapshot))
                {
                    return false;
                }

                return IsCurrentCommandIdentity(
                    runtime,
                    true,
                    pair.Key,
                    pair.Value.SessionId,
                    pair.Value.ItemId,
                    snapshot);
            });
        }

        private static bool HasCurrentRemoteSeek(
            RoomRuntime runtime,
            string userId,
            SessionSnapshot snapshot)
        {
            if (runtime == null || snapshot == null)
            {
                return false;
            }

            if (runtime.Pending.TryGetValue(userId, out var pending) &&
                string.Equals(pending?.Command, RemoteCommands.Seek, StringComparison.Ordinal) &&
                IsCurrentCommandIdentity(
                    runtime,
                    true,
                    userId,
                    pending.SessionId,
                    pending.ItemId,
                    snapshot))
            {
                return true;
            }

            return runtime.Suppressed.TryGetValue(userId, out var suppressed) &&
                string.Equals(suppressed?.Command, RemoteCommands.Seek, StringComparison.Ordinal) &&
                IsCurrentCommandIdentity(
                    runtime,
                    true,
                    userId,
                    suppressed.SessionId,
                    suppressed.ItemId,
                    snapshot);
        }

        private static bool HasCurrentSuppressedCommand(
            RoomRuntime runtime,
            string userId,
            SessionSnapshot snapshot,
            string command)
        {
            return runtime != null &&
                snapshot != null &&
                runtime.Suppressed.TryGetValue(userId, out var suppressed) &&
                string.Equals(suppressed?.Command, command, StringComparison.Ordinal) &&
                IsCurrentCommandIdentity(
                    runtime,
                    true,
                    userId,
                    suppressed.SessionId,
                    suppressed.ItemId,
                    snapshot) &&
                PendingMatcher.Matches(suppressed.Command, suppressed.PositionTicks, snapshot);
        }

        private static void ClearAnchorPositionCandidate(BarrierState barrier)
        {
            if (barrier == null)
            {
                return;
            }

            barrier.AnchorPositionCandidateTicks = null;
            barrier.AnchorPositionCandidateSessionId = null;
            barrier.AnchorPositionCandidateItemId = null;
            barrier.AnchorPositionCandidatePaused = false;
        }

        private static bool IsClearlyOutsideBarrierTarget(
            RoomRuntime runtime,
            BarrierState barrier,
            long positionTicks)
        {
            return Math.Abs(positionTicks - barrier.PrimaryPositionTicks) >=
                GetSeekDetectionThresholdTicks(runtime, barrier.AnchorUserId);
        }

        private static void CaptureAnchorPositionCandidate(
            RoomRuntime runtime,
            BarrierState barrier,
            SessionSnapshot anchor,
            DateTimeOffset now)
        {
            if (runtime == null || barrier == null || anchor == null ||
                HasCurrentRemoteSeek(runtime, barrier.AnchorUserId, anchor))
            {
                return;
            }

            if (barrier.AnchorPositionCandidateTicks.HasValue)
            {
                if (IsCurrentAnchorPositionCandidate(runtime, barrier, anchor) &&
                    !anchor.IsPaused &&
                    IsClearlyOutsideBarrierTarget(runtime, barrier, anchor.PositionTicks))
                {
                    barrier.AnchorPositionCandidateTicks = anchor.PositionTicks;
                }

                return;
            }

            if (!runtime.Previous.TryGetValue(barrier.AnchorUserId, out var previous) ||
                previous == null ||
                (!previous.IsPaused && !anchor.IsPaused) ||
                !IsClearlyOutsideBarrierTarget(runtime, barrier, anchor.PositionTicks))
            {
                return;
            }

            double elapsedSeconds = Math.Max(0, (now - runtime.PreviousAtUtc.GetValueOrDefault()).TotalSeconds);
            double expectedPosition = previous.PositionTicks;
            if (!previous.IsPaused)
            {
                expectedPosition += elapsedSeconds * previous.PlaybackRate * SessionSnapshot.TicksPerSecond;
            }

            if (Math.Abs(anchor.PositionTicks - expectedPosition) <
                GetSeekDetectionThresholdTicks(runtime, barrier.AnchorUserId))
            {
                return;
            }

            barrier.AnchorPositionCandidateTicks = anchor.PositionTicks;
            barrier.AnchorPositionCandidateSessionId = anchor.SessionId;
            barrier.AnchorPositionCandidateItemId = anchor.ItemId;
            barrier.AnchorPositionCandidatePaused = anchor.IsPaused;
        }

        private static bool IsCurrentAnchorPositionCandidate(
            RoomRuntime runtime,
            BarrierState barrier,
            SessionSnapshot anchor)
        {
            return barrier?.AnchorPositionCandidateTicks.HasValue == true &&
                anchor != null &&
                string.Equals(
                    barrier.AnchorPositionCandidateSessionId,
                    anchor.SessionId,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    barrier.AnchorPositionCandidateItemId,
                    anchor.ItemId,
                    StringComparison.OrdinalIgnoreCase) &&
                IsCurrentCommandIdentity(
                    runtime,
                    true,
                    barrier.AnchorUserId,
                    barrier.AnchorPositionCandidateSessionId,
                    barrier.AnchorPositionCandidateItemId,
                    anchor);
        }

        private static bool IsReadyToPromoteAnchorPositionCandidate(
            RoomRuntime runtime,
            BarrierState barrier,
            SessionSnapshot anchor)
        {
            if (!IsAnchorPositionCandidateAtCurrentPosition(runtime, barrier, anchor) ||
                !anchor.IsPaused ||
                !HasCurrentSuppressedCommand(runtime, barrier.AnchorUserId, anchor, RemoteCommands.Pause))
            {
                return false;
            }

            return true;
        }

        private static bool IsAnchorPositionCandidateAtCurrentPosition(
            RoomRuntime runtime,
            BarrierState barrier,
            SessionSnapshot anchor)
        {
            return IsCurrentAnchorPositionCandidate(runtime, barrier, anchor) &&
                !HasCurrentRemoteSeek(runtime, barrier.AnchorUserId, anchor) &&
                Math.Abs(anchor.PositionTicks - barrier.AnchorPositionCandidateTicks.Value) <=
                    SyncConstants.SeekToleranceTicks &&
                IsClearlyOutsideBarrierTarget(runtime, barrier, anchor.PositionTicks);
        }

        private static bool IsExplicitBarrierAnchorSeek(
            RoomRuntime runtime,
            BarrierState barrier,
            SessionSnapshot anchor,
            DateTimeOffset now)
        {
            if (runtime == null || barrier == null || anchor == null ||
                HasCurrentRemoteSeek(runtime, barrier.AnchorUserId, anchor))
            {
                return false;
            }

            if (IsReadyToPromoteAnchorPositionCandidate(runtime, barrier, anchor))
            {
                return true;
            }

            if (!runtime.Previous.TryGetValue(barrier.AnchorUserId, out var previous) ||
                previous == null ||
                previous.IsPaused != anchor.IsPaused)
            {
                // A pause transition can include normal position movement while
                // the client applies the hold request; keep that movement as
                // the new observation baseline instead of re-anchoring.
                return false;
            }

            if (!IsClearlyOutsideBarrierTarget(runtime, barrier, anchor.PositionTicks))
            {
                // Returning to the locked target is convergence, not a new
                // anchor operation, even if the preceding snapshot was far
                // away because of normal pause latency.
                return false;
            }

            long threshold = GetSeekDetectionThresholdTicks(runtime, barrier.AnchorUserId);
            double elapsedSeconds = Math.Max(0, (now - runtime.PreviousAtUtc.GetValueOrDefault()).TotalSeconds);
            double expectedPosition = previous.PositionTicks;
            if (!previous.IsPaused)
            {
                expectedPosition += elapsedSeconds * previous.PlaybackRate * SessionSnapshot.TicksPerSecond;
            }

            return Math.Abs(anchor.PositionTicks - expectedPosition) >= threshold;
        }

        private static void RememberBarrierSnapshots(
            RoomRuntime runtime,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            DateTimeOffset now)
        {
            runtime.Previous.Clear();
            foreach (var pair in snapshots)
            {
                runtime.Previous[pair.Key] = pair.Value;
            }

            runtime.PreviousAtUtc = now;
        }

        private void StartBarrier(
            RoomRuntime runtime,
            Room room,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            DateTimeOffset now,
            string anchorUserId = null,
            bool? primaryPausedOverride = null)
        {
            string anchorUser = anchorUserId ?? room.PrimaryUserId;
            var anchor = snapshots[anchorUser];
            runtime.State = RoomState.Barrier;
            runtime.Error = null;
            runtime.Barrier = new BarrierState
            {
                Stage = BarrierStage.Pause,
                StartedAtUtc = now,
                AnchorUserId = anchorUser,
                PrimaryPositionTicks = anchor.PositionTicks,
                PrimaryPaused = primaryPausedOverride ?? anchor.IsPaused,
                ItemId = anchor.ItemId,
                PauseSent = false,
                SeekSent = false,
                SeekRetryDeadlineAtUtc = null,
                RestoreSent = false,
            };
            runtime.SyncItemId = anchor.ItemId;
            foreach (var pair in snapshots)
            {
                if (pair.Value != null)
                {
                    runtime.Barrier.SessionIds[pair.Key] = pair.Value.SessionId;
                }
            }
            runtime.Pending.Clear();
            runtime.WaitingPauseRetries.Clear();
            runtime.Suppressed.Clear();
            runtime.PauseAlign.Clear();
            RememberBarrierSnapshots(runtime, snapshots, now);
            _logger?.Info(
                $"Room {room.Id}: barrier started, anchor={anchorUser}, " +
                $"target={FormatPosition(anchor.PositionTicks)}s, paused={anchor.IsPaused}");
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
                snapshots.Values.Any(s => !string.Equals(s.ItemId, barrier.ItemId, StringComparison.OrdinalIgnoreCase)) ||
                barrier.SessionIds.Count != members.Count ||
                members.Any(user =>
                    !snapshots.TryGetValue(user, out var snapshot) ||
                    !barrier.SessionIds.TryGetValue(user, out var sessionId) ||
                    !HasSameIdentity(sessionId, barrier.ItemId, snapshot)))
            {
                runtime.ResetToWaiting();
                return;
            }

            switch (barrier.Stage)
            {
                case BarrierStage.Pause:
                    if (!barrier.PauseSent)
                    {
                        bool allIssued = true;
                        string issueFailure = null;
                        foreach (var user in members)
                        {
                            if (!Issue(
                                runtime,
                                room,
                                user,
                                snapshots[user],
                                RemoteCommands.Pause,
                                null,
                                now,
                                out var failure))
                            {
                                allIssued = false;
                                issueFailure ??= failure;
                            }
                        }

                        if (!allIssued)
                        {
                            ScheduleBarrierRetry(
                                runtime,
                                room.Id,
                                issueFailure ?? "barrier pause command failed",
                                now);
                            return;
                        }

                        barrier.PauseSent = true;
                        return;
                    }

                    if (members.All(u => snapshots[u].IsPaused))
                    {
                        // The primary may continue playing until Emby applies the
                        // pause command. Re-anchor after both acknowledgements so
                        // the secondary does not seek to the stale start snapshot.
                        barrier.PrimaryPositionTicks = snapshots[barrier.AnchorUserId].PositionTicks;
                        barrier.Stage = BarrierStage.Seek;
                        barrier.StartedAtUtc = now;
                        EnsureSeekRetryDeadline(barrier, now);
                        RememberBarrierSnapshots(runtime, snapshots, now);
                        return;
                    }

                    if (runtime.Pending.Count == 0 &&
                        (now - barrier.StartedAtUtc).TotalSeconds >= SyncConstants.BarrierTimeoutSeconds)
                    {
                        ScheduleBarrierRetry(runtime, room.Id, "barrier pause timed out", now);
                    }

                    return;

                case BarrierStage.Seek:
                    var follower = members.First(u => !string.Equals(u, barrier.AnchorUserId, StringComparison.OrdinalIgnoreCase));
                    var anchor = snapshots[barrier.AnchorUserId];
                    long target = barrier.PrimaryPositionTicks;
                    bool anyUnpaused = snapshots.Values.Any(snapshot => !snapshot.IsPaused);
                    EnsureSeekRetryDeadline(barrier, now);
                    CaptureAnchorPositionCandidate(runtime, barrier, anchor, now);
                    bool candidateAtPosition = IsAnchorPositionCandidateAtCurrentPosition(runtime, barrier, anchor);
                    bool candidateReady = IsReadyToPromoteAnchorPositionCandidate(runtime, barrier, anchor);
                    if (candidateReady || IsExplicitBarrierAnchorSeek(runtime, barrier, anchor, now))
                    {
                        // A new, clearly out-of-band anchor seek replaces the
                        // whole sequence. This deliberately clears the old
                        // follower Seek instead of overwriting it with a
                        // different command through Issue.
                        StartBarrier(
                            runtime,
                            room,
                            snapshots,
                            now,
                            barrier.AnchorUserId,
                            candidateAtPosition ? (bool?)barrier.AnchorPositionCandidatePaused : null);
                        _logger?.Info(
                            $"Room {room.Id}: anchor moved during barrier Seek; " +
                            "rebuilding barrier from the new anchor position");
                        return;
                    }

                    RememberBarrierSnapshots(runtime, snapshots, now);

                    bool anchorReached =
                        Math.Abs(anchor.PositionTicks - target) <= SyncConstants.SeekToleranceTicks;
                    bool targetReached =
                        Math.Abs(snapshots[follower].PositionTicks - target) <= SyncConstants.SeekToleranceTicks;

                    if (anchorReached && targetReached && !anyUnpaused)
                    {
                        ClearAnchorPositionCandidate(barrier);
                        barrier.Stage = BarrierStage.Restore;
                        barrier.StartedAtUtc = now;
                        return;
                    }

                    if (IsSeekRetryBudgetExpired(barrier, now))
                    {
                        FailSeekBarrier(runtime, room.Id);
                        return;
                    }

                    foreach (var user in members)
                    {
                        if (!snapshots[user].IsPaused &&
                            !(runtime.Pending.TryGetValue(user, out var pending) &&
                              pending.Command == RemoteCommands.Seek))
                        {
                            if (!Issue(
                                runtime,
                                room,
                                user,
                                snapshots[user],
                                RemoteCommands.Pause,
                                null,
                                now,
                                out var pauseFailure))
                            {
                                ScheduleBarrierRetry(
                                    runtime,
                                    room.Id,
                                    pauseFailure ?? "barrier pause command failed",
                                    now);
                                return;
                            }
                        }
                    }

                    // A pause issued in this round is only a hold request; the
                    // snapshot is still unpaused until the next poll. Never
                    // clear the seek cooldown or issue/replace a seek until all
                    // members have confirmed paused.
                    if (anyUnpaused)
                    {
                        return;
                    }

                    if (anchorReached && targetReached)
                    {
                        ClearAnchorPositionCandidate(barrier);
                        barrier.Stage = BarrierStage.Restore;
                        barrier.StartedAtUtc = now;
                        return;
                    }

                    if (barrier.SeekRetryAtUtc.HasValue)
                    {
                        if (now < barrier.SeekRetryAtUtc.Value)
                        {
                            return;
                        }

                        barrier.SeekRetryAtUtc = null;
                        barrier.StartedAtUtc = now;
                        runtime.Error = null;
                        NotifyAutomaticBarrierRetry(room, snapshots, now);
                    }

                    if (!barrier.SeekSent)
                    {
                        if (!Issue(
                            runtime,
                            room,
                            follower,
                            snapshots[follower],
                            RemoteCommands.Seek,
                            target,
                            now,
                            out var failure))
                        {
                            ScheduleSeekBarrierRetry(
                                runtime,
                                room.Id,
                                failure ?? "barrier seek command failed",
                                now);
                            return;
                        }

                        barrier.SeekSent = true;
                        return;
                    }

                    if (runtime.Pending.Count == 0 &&
                        (now - barrier.StartedAtUtc).TotalSeconds >= SyncConstants.BarrierTimeoutSeconds)
                    {
                        ScheduleSeekBarrierRetry(runtime, room.Id, "barrier seek timed out", now);
                    }

                    return;

                case BarrierStage.Restore:
                    if (!barrier.RestoreSent)
                    {
                        string command = barrier.PrimaryPaused ? RemoteCommands.Pause : RemoteCommands.Unpause;
                        bool allIssued = true;
                        string issueFailure = null;
                        foreach (var user in members)
                        {
                            if (!Issue(
                                runtime,
                                room,
                                user,
                                snapshots[user],
                                command,
                                null,
                                now,
                                out var failure))
                            {
                                allIssued = false;
                                issueFailure ??= failure;
                            }
                        }

                        if (!allIssued)
                        {
                            ScheduleBarrierRetry(
                                runtime,
                                room.Id,
                                issueFailure ?? $"barrier {command} command failed",
                                now);
                            return;
                        }

                        barrier.RestoreSent = true;
                        return;
                    }

                    bool desired = barrier.PrimaryPaused;
                    if (members.All(u => snapshots[u].IsPaused == desired))
                    {
                        EnterWatching(runtime, room, barrier, snapshots, now);
                    }
                    else if (runtime.Pending.Count == 0 &&
                             (now - barrier.StartedAtUtc).TotalSeconds >= SyncConstants.BarrierTimeoutSeconds)
                    {
                        ScheduleBarrierRetry(runtime, room.Id, "barrier restore timed out", now);
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
            PruneWaitingPauseRetries(runtime, snapshots);
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
                if (runtime.Pending.TryGetValue(pair.Key, out var pending) &&
                    HasSameIdentity(pending.SessionId, pending.ItemId, pair.Value) &&
                    string.Equals(pending.Command, RemoteCommands.Pause, StringComparison.Ordinal))
                {
                    // The successful issue is already waiting for acknowledgement;
                    // do not turn the normal poll interval into another attempt.
                    runtime.WaitingPauseRetries.Remove(pair.Key);
                    continue;
                }

                if (runtime.WaitingPauseRetries.TryGetValue(pair.Key, out var retry))
                {
                    if (!IsCurrentWaitingPauseRetry(retry, pair.Value))
                    {
                        runtime.WaitingPauseRetries.Remove(pair.Key);
                        retry = null;
                    }
                    else if (retry.Exhausted || now < retry.NextAttemptAtUtc)
                    {
                        continue;
                    }
                }

                if (Issue(
                    runtime,
                    room,
                    pair.Key,
                    pair.Value,
                    RemoteCommands.Pause,
                    null,
                    now,
                    out _))
                {
                    runtime.WaitingPauseRetries.Remove(pair.Key);
                    if (!runtime.WaitingPauseRetries.Values.Any(state => state.Exhausted) &&
                        string.Equals(runtime.Error, WaitingPauseRetryLimitError, StringComparison.Ordinal))
                    {
                        runtime.Error = null;
                    }
                    continue;
                }

                RecordWaitingPauseFailure(runtime, pair.Key, pair.Value, now);
            }
        }

        private static void RecordWaitingPauseFailure(
            RoomRuntime runtime,
            string userId,
            SessionSnapshot snapshot,
            DateTimeOffset now)
        {
            if (!runtime.WaitingPauseRetries.TryGetValue(userId, out var retry) ||
                !IsCurrentWaitingPauseRetry(retry, snapshot))
            {
                retry = new WaitingPauseRetryState
                {
                    SessionId = snapshot.SessionId,
                    ItemId = snapshot.ItemId,
                    CapabilityKey = GetPauseCapabilityKey(snapshot),
                };
                runtime.WaitingPauseRetries[userId] = retry;
            }

            retry.Attempts++;
            retry.Exhausted = retry.Attempts >= SyncConstants.MaxWaitingPauseAttempts;
            retry.NextAttemptAtUtc = now.AddSeconds(SyncConstants.WaitingPauseRetryDelaySeconds);
            if (retry.Exhausted)
            {
                runtime.Error = WaitingPauseRetryLimitError;
            }
        }

        private void EnterWatching(
            RoomRuntime runtime,
            Room room,
            BarrierState barrier,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            DateTimeOffset now)
        {
            runtime.State = RoomState.Watching;
            runtime.Barrier = null;
            runtime.Pending.Clear();
            runtime.WaitingPauseRetries.Clear();
            runtime.Suppressed.Clear();
            runtime.PauseAlign.Clear();
            runtime.Previous.Clear();
            foreach (var pair in snapshots)
            {
                runtime.Previous[pair.Key] = pair.Value;
            }

            runtime.PreviousAtUtc = now;
            runtime.SyncItemId = barrier.ItemId;
            _logger?.Info(
                $"Room {room.Id}: entered Watching, members={string.Join(",", snapshots.Keys)}, " +
                $"primaryPaused={barrier.PrimaryPaused}");
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

            foreach (var user in members)
            {
                if (!snapshots.TryGetValue(user, out var current) || current == null ||
                    !previous.TryGetValue(user, out var old) || old == null ||
                    !string.Equals(old.SessionId, current.SessionId, StringComparison.OrdinalIgnoreCase))
                {
                    // A reconnect can keep the same item and position while
                    // still representing a different device session. Do not
                    // interpret that identity change as a seek or reuse the
                    // previous Watching barrier state.
                    runtime.ResetToWaiting();
                    return;
                }
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
                    else if (IsCurrentCommandIdentity(
                        runtime,
                        true,
                        user,
                        suppressed.SessionId,
                        suppressed.ItemId,
                        current) &&
                        PendingMatcher.Matches(suppressed.Command, suppressed.PositionTicks, current))
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
                        IsCurrentCommandIdentity(
                            runtime,
                            true,
                            user,
                            pending.SessionId,
                            pending.ItemId,
                            current) &&
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
                runtime.LastSeekAtUtc.TryGetValue(user, out var lastSeekAtUtc);
                long seekThresholdTicks = GetSeekDetectionThresholdTicks(runtime, user);
                if (IsManualSeek(old, current, runtime.PreviousAtUtc.Value, now, lastSeekAtUtc, seekThresholdTicks))
                {
                    bool pendingSeek = pending != null &&
                        pending.Command == RemoteCommands.Seek;
                    if (!suppressSeek && !pendingSeek)
                    {
                        seekChanges.Add((user, current.PositionTicks));
                        _logger?.Info(
                            $"Room {room.Id}: manual seek detected for {user} " +
                            $"({FormatPosition(old.PositionTicks)}s -> {FormatPosition(current.PositionTicks)}s)");
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

                // A manual seek is handled as a fresh alignment barrier: pause
                // both sides, seek the other side to the seeker's position, wait
                // until it is in place, then restore. This matches the observed
                // reliable flow (pause -> drag -> both in place -> play) and
                // avoids live lead/stuck decisions that caused repeated
                // pause/seeks in real sessions.
                StartBarrier(runtime, room, snapshots, now, anchorUserId: winner.userId);
                _logger?.Info(
                    $"Room {room.Id}: manual seek from {winner.userId} to " +
                    $"{FormatPosition(winner.positionTicks)}s; starting align barrier");
            }
            else if (pauseChanges.Count > 0)
            {
                var winner = pauseChanges.FirstOrDefault(c => c.userId == primary);
                if (winner.userId == null)
                {
                    winner = pauseChanges[0];
                }

                if (HasPendingSeekForCurrentIdentity(runtime, snapshots))
                {
                    // Do not let a resume overwrite a Seek whose acknowledgement
                    // has not arrived. Rebuild the explicit Pause -> Seek ->
                    // confirmation -> Restore sequence from current snapshots.
                    StartBarrier(runtime, room, snapshots, now, winner.userId);
                    _logger?.Info(
                        $"Room {room.Id}: pause change while Seek is pending; rebuilding align barrier");
                    return;
                }

                CancelSupersededPausePending(runtime, snapshots, members, winner.paused);

                if (winner.paused)
                {
                    // Defer alignment of the other side to the paused anchor's
                    // position (borrowed from syncplay's pause-snap idea); the
                    // target is stable because the anchor is paused.
                    snapshots.TryGetValue(winner.userId, out var winnerSnapshot);
                    long anchorPositionTicks = winnerSnapshot?.PositionTicks ?? 0;
                    foreach (var user in members)
                    {
                        if (user != winner.userId &&
                            !runtime.PauseAlign.ContainsKey(user) &&
                            snapshots.TryGetValue(user, out var followerSnapshot) &&
                            followerSnapshot != null &&
                            winnerSnapshot != null)
                        {
                            runtime.PauseAlign[user] = new PauseAlignState
                            {
                                AnchorUserId = winner.userId,
                                AnchorSessionId = winnerSnapshot.SessionId,
                                AnchorItemId = winnerSnapshot.ItemId,
                                SessionId = followerSnapshot.SessionId,
                                ItemId = followerSnapshot.ItemId,
                                TargetPositionTicks = anchorPositionTicks,
                                CreatedAtUtc = now,
                            };
                        }
                    }
                }
                else
                {
                    // A resume invalidates any pending paused-position target.
                    runtime.PauseAlign.Clear();
                }

                string command = winner.paused ? RemoteCommands.Pause : RemoteCommands.Unpause;
                bool allIssued = true;
                string issueFailure = null;
                foreach (var user in members)
                {
                    if (user != winner.userId && snapshots.TryGetValue(user, out var snapshot) && snapshot != null)
                    {
                        if (!Issue(runtime, room, user, snapshot, command, null, now, out var failure))
                        {
                            allIssued = false;
                            issueFailure ??= failure;
                        }
                    }
                }

                if (!allIssued)
                {
                    ScheduleBarrierRetry(
                        runtime,
                        room.Id,
                        issueFailure ?? $"{command} command failed",
                        now);
                    return;
                }

                _logger?.Info(
                    $"Room {room.Id}: pause change from {winner.userId} ({winner.paused}) propagated");
            }

            AlignPausedPeers(runtime, room, snapshots, now);

            runtime.Previous.Clear();
            foreach (var pair in snapshots)
            {
                runtime.Previous[pair.Key] = pair.Value;
            }

            runtime.PreviousAtUtc = now;
        }

        private static void CancelSupersededPausePending(
            RoomRuntime runtime,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            IReadOnlyList<string> members,
            bool desiredPaused)
        {
            string desiredCommand = desiredPaused ? RemoteCommands.Pause : RemoteCommands.Unpause;
            foreach (var user in members)
            {
                if (!runtime.Pending.TryGetValue(user, out var pending) ||
                    !snapshots.TryGetValue(user, out var snapshot) ||
                    !HasSameIdentity(pending.SessionId, pending.ItemId, snapshot) ||
                    (pending.Command != RemoteCommands.Pause && pending.Command != RemoteCommands.Unpause) ||
                    string.Equals(pending.Command, desiredCommand, StringComparison.Ordinal))
                {
                    continue;
                }

                // This is an explicit user pause/resume transition observed in
                // WatchingTick, so a stale opposite Pause/Unpause may be
                // cancelled deliberately. Seek is never cancelled here.
                runtime.Pending.Remove(user);
            }
        }

        private void AlignPausedPeers(
            RoomRuntime runtime,
            Room room,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            DateTimeOffset now)
        {
            foreach (var pair in runtime.PauseAlign.ToList())
            {
                string userId = pair.Key;
                var align = pair.Value;

                if (align == null ||
                    string.IsNullOrEmpty(align.AnchorUserId) ||
                    !snapshots.TryGetValue(align.AnchorUserId, out var anchor) ||
                    !snapshots.TryGetValue(userId, out var follower) ||
                    !HasSameIdentity(align.AnchorSessionId, align.AnchorItemId, anchor) ||
                    !HasSameIdentity(align.SessionId, align.ItemId, follower))
                {
                    runtime.PauseAlign.Remove(userId);
                    continue;
                }

                if ((now - align.CreatedAtUtc).TotalSeconds >= SyncConstants.PauseAlignTimeoutSeconds)
                {
                    runtime.PauseAlign.Remove(userId);
                    continue;
                }

                // Only seek while the anchor is still paused; a moving target
                // would make the follower miss by the command latency.
                if (!anchor.IsPaused)
                {
                    runtime.PauseAlign.Remove(userId);
                    continue;
                }

                if (!follower.IsPaused)
                {
                    // Wait for the propagated pause to land before seeking.
                    continue;
                }

                if (runtime.Pending.ContainsKey(userId))
                {
                    // Wait for the pause acknowledgement (or an earlier seek)
                    // before issuing another command.
                    continue;
                }

                if (Math.Abs(follower.PositionTicks - align.TargetPositionTicks) <= SyncConstants.SeekToleranceTicks)
                {
                    runtime.PauseAlign.Remove(userId);
                    continue;
                }

                if (!Issue(
                    runtime,
                    room,
                    userId,
                    follower,
                    RemoteCommands.Seek,
                    align.TargetPositionTicks,
                    now,
                    out var failure))
                {
                    ScheduleBarrierRetry(
                        runtime,
                        room.Id,
                        failure ?? "Seek command failed",
                        now);
                    return;
                }

                runtime.PauseAlign.Remove(userId);
                _logger?.Info(
                    $"Room {room.Id}: aligned paused follower {userId} to " +
                    $"{FormatPosition(align.TargetPositionTicks)}s (anchor {align.AnchorUserId})");
            }
        }

        private static string FormatPosition(long? positionTicks)
        {
            if (positionTicks == null)
            {
                return "-";
            }

            return (positionTicks.Value / (double)SessionSnapshot.TicksPerSecond).ToString("0.0");
        }
    }
}

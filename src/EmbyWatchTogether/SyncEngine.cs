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
        private const string StoppedPositionConfirmationError = "停止后播放位置相差较大，请使用重新同步确认恢复";
        private const string HandoffHoldRequiresResyncError = "media handoff hold requires explicit resync";
        private const string StoppedPlaybackMessageHeader = "一起观看";
        private const string StoppedPlaybackMessageText = "对方已停止播放，请重新打开视频";
        private const string AutomaticResyncMessageHeader = "一起观看";
        private const string AutomaticResyncMessageText = "正在自动重新同步，请稍候";
        private const string BarrierSeekRetryBudgetError = "barrier seek retry budget exhausted";
        private const string WaitingPauseRetryLimitError = "waiting pause retry limit reached";
        private const string MediaHandoffIssuerUnavailableError = "media handoff play item issuer unavailable";
        private const string MediaHandoffPlayItemFailedError = "media handoff play item failed";
        private const string MediaHandoffTimedOutError = "media handoff timed out";
        private const string MediaHandoffInvalidError = "media handoff identity invalid";
        private const string MediaHandoffParticipantChangedError = "media handoff participant changed item";
        private const string ParticipantResyncTimedOutError = "participant resync timed out";
        private const string ParticipantResyncUnavailableError = "participant resync unavailable";
        private const int NotificationTimeoutMs = 3000;
        private const double RoomPollErrorLogIntervalSeconds = 30.0;
        private static readonly TimeSpan ExternalCallTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);
        private const double AckLatencyEmaAlpha = 0.3;
        private const double MissingSessionDebounceSeconds = 2;
        private const double SnapshotProviderDebounceSeconds = 2;

        private readonly RoomManager _roomManager;
        private readonly ISessionSnapshotProvider _snapshotProvider;
        private readonly ICommandIssuer _issuer;
        private readonly IMessageIssuer _messageIssuer;
        private readonly SyncNotificationDispatcher _notifications = new SyncNotificationDispatcher();

        internal System.Threading.Tasks.Task NotificationsIdle => _notifications.WhenIdle;
        private readonly ILogger _logger;
        private readonly Func<string> _serverIdProvider;
        private readonly Func<DateTimeOffset> _clock;
        private double _pollIntervalSeconds;
        private bool _pauseOtherOnPlaybackStop;
        private bool _notifyOtherOnPlaybackStop;
        private bool _notifyOnSyncActions;
        private readonly HashSet<string> _differentVideoNoticeRooms =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new object();
        private readonly object _roomPollLogLock = new object();
        private readonly Dictionary<string, DateTimeOffset> _lastRoomPollErrorAtUtc =
            new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        private DateTimeOffset? _snapshotProviderFailureSinceUtc;
        private DateTimeOffset? _snapshotProviderRecoverySinceUtc;
        private DateTimeOffset? _lastSnapshotProviderErrorAtUtc;
        private bool _snapshotProviderProtectionActive;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly CancellationToken _stopToken;
        private readonly AutoResetEvent _wakeEvent = new AutoResetEvent(false);
        private Thread _thread;
        private volatile bool _stopRequested;
        private bool _disposed;
        private bool _resourcesDisposed;
        private bool _threadExited;
        private bool _cancellationCompleted;
        private bool _cancellationSourceDisposed;
        private readonly List<CancellationTokenSource> _deferredCommandTimeoutDisposals =
            new List<CancellationTokenSource>();

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
            ILogManager logManager = null,
            bool notifyOnSyncActions = true)
        {
            _stopToken = _cts.Token;
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
                notifyOtherOnPlaybackStop,
                notifyOnSyncActions);
            _pollIntervalSeconds = options.PollIntervalSeconds;
            _pauseOtherOnPlaybackStop = options.PauseOtherOnPlaybackStop;
            _notifyOtherOnPlaybackStop = options.NotifyOtherOnPlaybackStop;
            _notifyOnSyncActions = options.NotifyOnSyncActions;
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_disposed || _stopRequested || _thread != null)
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
                    $"notifyOtherOnPlaybackStop={_notifyOtherOnPlaybackStop}, " +
                    $"notifyOnSyncActions={_notifyOnSyncActions}");
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
                _notifyOnSyncActions = options.NotifyOnSyncActions;
                SignalWakeLocked();
            }
        }

        public void Stop()
        {
            _notifications.Stop();
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
            var results = new List<RoomPollResult>();
            if (_stopRequested) return results;
            var options = GetOptionsSnapshot();
            var rooms = _roomManager.ListRooms();
            var roomIds = new HashSet<string>(rooms.Select(r => r.Id), StringComparer.OrdinalIgnoreCase);
            _differentVideoNoticeRooms.RemoveWhere(id => !roomIds.Contains(id));
            if (rooms.Count == 0)
            {
                return results;
            }

            string currentServerId = _serverIdProvider();
            var validRoomIds = new List<string>();
            foreach (var listedRoom in rooms)
            {
                if (_stopRequested) return results;
                try
                {
                    using (var access = _roomManager.TryEnterRoom(listedRoom.Id))
                    {
                        if (_stopRequested) return results;
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
                            runtime.ClearMediaHandoff();
                            runtime.ClearPrimaryItemTransitionCandidate();
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

            if (_stopRequested || validRoomIds.Count == 0)
            {
                return results;
            }

            IReadOnlyList<SessionSnapshot> candidates;
            try
            {
                candidates = _snapshotProvider.GetSessionSnapshots();
            }
            catch (Exception ex)
            {
                if (_stopRequested) return results;
                HandleSnapshotProviderFailure(now, validRoomIds, results, ex);
                return results;
            }

            if (_stopRequested) return results;
            if (HandleSnapshotProviderSuccess(now, validRoomIds, results))
            {
                // A protected room is intentionally not allowed to process
                // snapshots until the full recovery observation window has
                // elapsed. The successful poll that clears protection falls
                // through and is processed exactly once below.
                return results;
            }

            foreach (var roomId in validRoomIds)
            {
                if (_stopRequested) break;
                try
                {
                    using (var access = _roomManager.TryEnterRoom(roomId))
                    {
                        if (_stopRequested) break;
                        if (access == null)
                        {
                            continue;
                        }

                        var room = access.Room;
                        var runtime = access.Runtime;
                        if (runtime.SnapshotUnavailable)
                        {
                            runtime.ExitSnapshotUnavailableProtection();
                        }
                        runtime.ConsumeSnapshotRecoveryError();
                        if (runtime.State == RoomState.Unavailable)
                        {
                            runtime.State = RoomState.Waiting;
                            runtime.Error = null;
                        }

                        var selection = SessionSelector.SelectWithPreviousDiagnostics(
                            candidates,
                            room.JoinedParticipantUserIds,
                            now,
                            SessionSelector.StaleSessionTimeoutSeconds,
                            runtime.Previous,
                            runtime.State == RoomState.Watching ||
                            runtime.State == RoomState.Handoff);
                        var snapshots = selection.Selected;
                        // LastActivity is not a playback sequence. Reject only
                        // provably older snapshots for the exact bound identity;
                        // equal/newer timestamps must retain real user controls.
                        if (runtime.State == RoomState.Watching && runtime.Pending.Count == 0 &&
                            snapshots.Count == room.JoinedParticipantUserIds.Count &&
                            RoomEligibility.IsPairEligible(snapshots) &&
                            snapshots.All(pair => pair.Value != null && pair.Value.Online &&
                                runtime.Previous.TryGetValue(pair.Key, out var bound) && bound != null &&
                                HasSameIdentity(bound.SessionId, bound.ItemId, pair.Value)) &&
                            snapshots.Any(pair => pair.Value != null &&
                            runtime.Previous.TryGetValue(pair.Key, out var prior) && prior != null &&
                            HasSameIdentity(prior.SessionId, prior.ItemId, pair.Value) &&
                            prior.LastActivityDateUtc != default && pair.Value.LastActivityDateUtc != default &&
                            pair.Value.LastActivityDateUtc < prior.LastActivityDateUtc))
                        {
                            runtime.RecordDiagnosticEvent("snapshot_ignored", null, null,
                                "older_activity", null, null, now);
                            results.Add(Result(room, runtime, RoomEligibility.IsPairEligible(snapshots)));
                            continue;
                        }
                        runtime.RecordDiagnosticSnapshots(snapshots, now);
                        var eligibility = RoomEligibility.Evaluate(snapshots);
                        bool eligible = eligibility.IsEligible;
                        LogMultipleSessionDiagnostics(runtime, room, selection, eligibility, now);
                        LogEligibilityFailureIfChanged(
                            runtime,
                            room,
                            snapshots,
                            eligibility,
                            now);
                        bool sameItem = snapshots.Count == 2 &&
                            snapshots.Values.All(s => s != null) &&
                            snapshots.Values.Select(s => s.ItemId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;

                        if (runtime.State == RoomState.Waiting &&
                            !string.IsNullOrEmpty(runtime.ParticipantResyncRequestedUserId) &&
                            TryProcessParticipantResync(
                                runtime,
                                room,
                                snapshots,
                                eligibility,
                                sameItem,
                                now))
                        {
                            results.Add(Result(room, runtime, eligible));
                            continue;
                        }

                        ReleaseTransientRecoveryForPrimaryIdentityChange(
                            runtime,
                            room,
                            snapshots,
                            now);

                        if (runtime.HandoffRequiresResync)
                        {
                            runtime.Error = HandoffHoldRequiresResyncError;
                            results.Add(Result(room, runtime, eligible));
                            continue;
                        }

                        // Once a stop has been observed, only the identity-bound
                        // candidate may authorize its late (or early) replacement.
                        // Process it before the ordinary Watching handoff path.
                        if (TryProcessLatePrimaryItemTransition(
                                runtime,
                                room,
                                snapshots,
                                eligibility,
                                now))
                        {
                            results.Add(Result(room, runtime, eligible));
                            continue;
                        }

                        bool primaryItemTransition = !runtime.MissingSessionSinceUtc.HasValue &&
                            IsPrimaryItemTransition(runtime, room, snapshots, now);
                        if ((runtime.State == RoomState.Watching && primaryItemTransition) ||
                            runtime.State == RoomState.Handoff)
                        {
                            if (TryProcessMediaHandoff(
                                runtime,
                                room,
                                snapshots,
                                eligibility,
                                primaryItemTransition,
                                now))
                            {
                                results.Add(Result(room, runtime, eligible));
                                continue;
                            }
                        }

                        if (TryProcessTransientSessionRecovery(
                            runtime,
                            room,
                            candidates,
                            snapshots,
                            eligibility,
                            now))
                        {
                            results.Add(Result(room, runtime, eligible));
                            continue;
                        }

                        if (TryGetStoppedUsers(runtime, room, snapshots, now, out var stoppedUsers))
                        {
                            // SessionSelector omits stopped/offline sessions, so the
                            // same stop condition can be observed on every poll until
                            // the participant starts again. Apply stop side effects
                            // only on the transition into the stopped state.
                            bool stopAlreadyHandled = IsStoppedPlaybackWaitingError(runtime.Error);
                            if (!stopAlreadyHandled)
                            {
                                // Confirmed Stop terminates the old playback sequence.
                                // Its pending Unpause/Seek must not suppress the peer Pause.
                                runtime.Pending.Clear();
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

                            runtime.ResetToWaitingPreservingPrimaryItemTransitionCandidate();
                            runtime.Previous.Clear();
                            runtime.PreviousAtUtc = null;
                            runtime.MissingSessionSinceUtc = null;
                            runtime.Error = StoppedPlaybackError;
                            runtime.RecordDiagnosticEvent(
                                "stop_confirmed", null, RemoteCommands.Stop, "stopped", null, null, now);
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
                            IsStoppedPlaybackWaitingError(runtime.Error))
                        {
                            if (!eligible)
                                runtime.Error = StoppedPlaybackError;
                            if (sameItem && !HasLargePositionGap(snapshots))
                            {
                                runtime.Error = null;
                            }
                            else if (sameItem)
                            {
                                if (eligible && runtime.Error != StoppedPositionConfirmationError)
                                {
                                    runtime.Error = StoppedPositionConfirmationError;
                                    var positions = snapshots.Values.Select(s => s.PositionTicks).ToArray();
                                    runtime.RecordDriftDiagnosticEvent("stop_resume_confirmation_required",
                                        "pending", Math.Abs((double)positions[0] - positions[1]) / SessionSnapshot.TicksPerSecond, now);
                                }
                                results.Add(Result(room, runtime, eligible));
                                continue;
                            }
                        }

                        if (sameItem && IsMediaHandoffError(runtime.Error))
                        {
                            runtime.Error = null;
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
                        // Waiting commands are pruned per target; resetting here
                        // would renew an unchanged peer's exhausted Pause budget.
                        if ((staleDeferredCommand || stalePendingCommand) &&
                            runtime.State != RoomState.Waiting)
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

                        bool differentVideo = !sameItem && snapshots.Count == 2 &&
                            snapshots.Values.All(s => s != null) &&
                            snapshots.Values.All(s => !string.IsNullOrEmpty(s.ItemId)) &&
                            snapshots.Values.Select(s => s.ItemId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
                        if (differentVideo && _notifyOnSyncActions && _differentVideoNoticeRooms.Add(room.Id))
                        {
                            NotifyParticipants(
                                room,
                                snapshots,
                                "双方打开了不同视频，已暂停同步；请打开同一视频",
                                now,
                                includeAll: true);
                        }
                        else if (sameItem)
                        {
                            _differentVideoNoticeRooms.Remove(room.Id);
                        }

                        if (differentVideo &&
                            !runtime.WaitingPauseRetries.Values.Any(state => state.Exhausted))
                        {
                            runtime.Error = "两位参与者打开了不同视频，暂不发送同步指令";
                        }
                        else if (sameItem && runtime.Error == "两位参与者打开了不同视频，暂不发送同步指令")
                        {
                            runtime.Error = null;
                        }

                        if (eligible &&
                            string.Equals(runtime.Error, WaitingPauseRetryLimitError, StringComparison.Ordinal))
                        {
                            // Same-item snapshots do not prove that the failure
                            // which caused Waiting has recovered (remote control
                            // may still be unavailable). Renew the budget only
                            // when the complete pair is eligible for a Barrier.
                            runtime.ResetToWaiting();
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
                                runtime.ClearRemoteControlRecovery();
                                WatchingTick(runtime, room, snapshots, now);
                            }
                            else if (TryHoldRemoteControlRecovery(
                                runtime,
                                room,
                                snapshots,
                                eligibility,
                                now))
                            {
                                // Keep the established Watching state and all
                                // previous/pending state intact while a known
                                // short-lived remote-control flap recovers.
                                results.Add(Result(room, runtime, eligible));
                                continue;
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
                                NotifyAutomaticBarrierRetry(runtime, room, snapshots, now);
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
                catch (OperationCanceledException) when (_stopRequested)
                {
                    break;
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
            _notifications.Stop();
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
            var token = _stopToken;
            var waitHandles = new WaitHandle[] { token.WaitHandle, _wakeEvent };
            try
            {
                while (!_stopRequested)
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
            if (!_stopRequested)
            {
                _stopRequested = true;
                // Transport callbacks can block or throw. Publish shutdown
                // immediately, but never run those callbacks on this caller
                // or under the engine lock. Repeated shutdown queues no work.
                System.Threading.Tasks.Task.Run(() =>
                {
                    try { _cts.Cancel(); }
                    catch { /* Shutdown cancellation is best effort. */ }
                    finally
                    {
                        lock (_lock)
                        {
                            _cancellationCompleted = true;
                            foreach (var timeout in _deferredCommandTimeoutDisposals)
                            {
                                timeout.Dispose();
                            }
                            _deferredCommandTimeoutDisposals.Clear();
                            DisposeCancellationSourceLocked();
                        }
                    }
                });
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
                    _notifyOtherOnPlaybackStop,
                    _notifyOnSyncActions);
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

            lock (_lock)
            {
                DisposeCancellationSourceLocked();
            }
        }

        private void DisposeCancellationSourceLocked()
        {
            if (_resourcesDisposed && _cancellationCompleted && !_cancellationSourceDisposed)
            {
                _cancellationSourceDisposed = true;
                _cts.Dispose();
            }
        }

        private void ThrowIfStopping()
        {
            if (_stopRequested) throw new OperationCanceledException(_stopToken);
        }

        private void DisposeCommandTimeout(CancellationTokenSource timeout)
        {
            lock (_lock)
            {
                // Disposing a linked registration can wait for a cancellation
                // callback that needs the room gate currently held by this poll.
                // Serialize ordinary cleanup against cancellation startup; once
                // stopping, the one cancellation worker owns unfinished cleanup.
                if (_stopRequested && !_cancellationCompleted)
                {
                    _deferredCommandTimeoutDisposals.Add(timeout);
                    return;
                }

                timeout.Dispose();
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
                    $"Room {key}: poll failed; exceptionType={exception?.GetType().Name ?? "unknown"}, " +
                    $"failureDetailPresent={HasFailureDetail(exception?.Message)}");
            }
        }

        private void HandleSnapshotProviderFailure(
            DateTimeOffset now,
            IReadOnlyList<string> validRoomIds,
            IList<RoomPollResult> results,
            Exception exception)
        {
            if (!_snapshotProviderFailureSinceUtc.HasValue)
            {
                _snapshotProviderFailureSinceUtc = now;
                if (_snapshotProviderProtectionActive)
                {
                    _snapshotProviderRecoverySinceUtc = null;
                }
            }

            if (!_lastSnapshotProviderErrorAtUtc.HasValue ||
                (now - _lastSnapshotProviderErrorAtUtc.Value).TotalSeconds >= RoomPollErrorLogIntervalSeconds)
            {
                _lastSnapshotProviderErrorAtUtc = now;
                _logger?.Warn(
                    $"Session snapshot provider failed; exceptionType={exception?.GetType().Name ?? "unknown"}, " +
                    $"failureDetailPresent={HasFailureDetail(exception?.Message)}");
            }

            bool thresholdReached =
                (now - _snapshotProviderFailureSinceUtc.Value).TotalSeconds >= SnapshotProviderDebounceSeconds;
            if (thresholdReached)
            {
                if (!_snapshotProviderProtectionActive)
                {
                    _snapshotProviderProtectionActive = true;
                    _snapshotProviderRecoverySinceUtc = null;
                    _logger?.Warn("Session snapshot provider protection entered");
                }

                foreach (var roomId in validRoomIds)
                {
                    using (var access = _roomManager.TryEnterRoom(roomId))
                    {
                        if (access == null)
                        {
                            continue;
                        }

                        bool wasUnavailable = access.Runtime.SnapshotUnavailable;
                        access.Runtime.EnterSnapshotUnavailableProtection();
                        if (!wasUnavailable)
                        {
                            access.Runtime.RecordDiagnosticEvent(
                                "snapshot_protection_entered", null, null, "entered", null, null, now);
                        }
                        results.Add(Result(access.Room, access.Runtime, eligible: false));
                    }
                }
            }
            else
            {
                // A short provider failure is observable to callers but does
                // not mutate any room state or issue commands.
                foreach (var roomId in validRoomIds)
                {
                    using (var access = _roomManager.TryEnterRoom(roomId))
                    {
                        if (access != null)
                        {
                            results.Add(Result(access.Room, access.Runtime, eligible: false));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Returns true while a protected provider is still in its recovery
        /// observation window. The first successful sample starts the window;
        /// another failure resets it through HandleSnapshotProviderFailure.
        /// </summary>
        private bool HandleSnapshotProviderSuccess(
            DateTimeOffset now,
            IReadOnlyList<string> validRoomIds,
            IList<RoomPollResult> results)
        {
            _snapshotProviderFailureSinceUtc = null;
            if (!_snapshotProviderProtectionActive)
            {
                return false;
            }

            if (!_snapshotProviderRecoverySinceUtc.HasValue)
            {
                _snapshotProviderRecoverySinceUtc = now;
                foreach (var roomId in validRoomIds)
                {
                    using (var access = _roomManager.TryEnterRoom(roomId))
                    {
                        if (access != null)
                        {
                            if (!access.Runtime.SnapshotUnavailable)
                            {
                                // Rooms created or becoming server-valid
                                // while the global provider is protected join
                                // the same recovery window.
                                access.Runtime.EnterSnapshotUnavailableProtection();
                            }
                            results.Add(Result(access.Room, access.Runtime, eligible: false));
                        }
                    }
                }

                return true;
            }

            if ((now - _snapshotProviderRecoverySinceUtc.Value).TotalSeconds < SnapshotProviderDebounceSeconds)
            {
                foreach (var roomId in validRoomIds)
                {
                    using (var access = _roomManager.TryEnterRoom(roomId))
                    {
                        if (access != null)
                        {
                            if (!access.Runtime.SnapshotUnavailable)
                            {
                                access.Runtime.EnterSnapshotUnavailableProtection();
                            }
                            results.Add(Result(access.Room, access.Runtime, eligible: false));
                        }
                    }
                }

                return true;
            }

            _snapshotProviderProtectionActive = false;
            _snapshotProviderRecoverySinceUtc = null;
            _logger?.Info("Session snapshot provider protection cleared");
            foreach (var roomId in validRoomIds)
            {
                using (var access = _roomManager.TryEnterRoom(roomId))
                {
                    access?.Runtime.RecordDiagnosticEvent(
                        "snapshot_protection_recovered", null, null, "recovered", null, null, now);
                }
            }
            return false;
        }

        private static bool IsPrimaryItemTransition(
            RoomRuntime runtime,
            Room room,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            DateTimeOffset now)
        {
            if (runtime == null || room == null || snapshots == null ||
                (runtime.State != RoomState.Watching && runtime.State != RoomState.Recovering) ||
                room.JoinedParticipantUserIds == null || room.JoinedParticipantUserIds.Count != 2 ||
                (runtime.MissingSessionSinceUtc.HasValue &&
                 (now - runtime.MissingSessionSinceUtc.Value).TotalSeconds >
                     SyncConstants.PrimaryItemTransitionGraceSeconds) ||
                !snapshots.TryGetValue(room.PrimaryUserId, out var current) ||
                current == null || !current.Online || !current.SupportsRemoteControl ||
                string.IsNullOrEmpty(current.ItemId) ||
                !runtime.Previous.TryGetValue(room.PrimaryUserId, out var previous) ||
                previous == null || !previous.Online || string.IsNullOrEmpty(previous.ItemId))
            {
                return false;
            }

            return string.Equals(current.UserId, room.PrimaryUserId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(previous.UserId, room.PrimaryUserId, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(previous.ItemId, current.ItemId, StringComparison.OrdinalIgnoreCase);
        }

        private static void ReleaseTransientRecoveryForPrimaryIdentityChange(
            RoomRuntime runtime,
            Room room,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            DateTimeOffset now)
        {
            if (runtime?.State != RoomState.Recovering ||
                runtime.Recovery == null ||
                room?.JoinedParticipantUserIds == null ||
                !runtime.Recovery.Participants.TryGetValue(room.PrimaryUserId, out var expected))
            {
                return;
            }

            SessionSnapshot current = null;
            bool primaryIdentityValid =
                snapshots != null &&
                snapshots.TryGetValue(room.PrimaryUserId, out current) &&
                current != null &&
                current.Online &&
                !current.Stopped &&
                string.Equals(current.UserId, room.PrimaryUserId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(current.SessionId, expected.ExpectedSessionId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(current.ItemId, expected.ExpectedItemId, StringComparison.OrdinalIgnoreCase);
            if (primaryIdentityValid &&
                !runtime.Recovery.MissingUserIds.Contains(
                    room.PrimaryUserId,
                    StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            string eventType = "recovery_session_changed";
            if (current != null &&
                current.Online &&
                !current.Stopped &&
                string.Equals(current.UserId, room.PrimaryUserId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(current.SessionId, expected.ExpectedSessionId, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(current.ItemId, expected.ExpectedItemId, StringComparison.OrdinalIgnoreCase))
            {
                eventType = "recovery_item_changed";
            }

            runtime.RecordDiagnosticEvent(
                eventType,
                room.PrimaryUserId,
                null,
                "changed",
                null,
                null,
                now);
            runtime.ClearTransientSessionRecovery();
            runtime.State = RoomState.Watching;
            runtime.MissingSessionSinceUtc = null;
        }

        private bool TryProcessLatePrimaryItemTransition(
            RoomRuntime runtime,
            Room room,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            RoomEligibilityEvaluation eligibility,
            DateTimeOffset now)
        {
            var candidate = runtime?.PrimaryItemTransitionCandidate;
            if (candidate == null)
            {
                return false;
            }

            if (room == null || room.JoinedParticipantUserIds == null ||
                room.JoinedParticipantUserIds.Count != 2 ||
                runtime.State != RoomState.Watching &&
                !(runtime.State == RoomState.Waiting &&
                  IsStoppedPlaybackWaitingError(runtime.Error)))
            {
                runtime.RecordDiagnosticEvent("primary_transition_invalidated", null, null, "changed", null, null, now);
                runtime.ClearPrimaryItemTransitionCandidate();
                return false;
            }

            if (now < candidate.StartedAtUtc ||
                (now - candidate.StartedAtUtc).TotalSeconds >=
                SyncConstants.PrimaryItemTransitionRecoveryGraceSeconds)
            {
                runtime.RecordDiagnosticEvent("primary_transition_expired", null, null, "expired", null, null, now);
                runtime.ClearPrimaryItemTransitionCandidate();
                return false;
            }

            string participantUserId = room.JoinedParticipantUserIds.FirstOrDefault(
                userId => !string.Equals(userId, room.PrimaryUserId, StringComparison.OrdinalIgnoreCase));
            if (!string.Equals(candidate.PrimaryUserId, room.PrimaryUserId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(candidate.ParticipantUserId, participantUserId, StringComparison.OrdinalIgnoreCase))
            {
                runtime.RecordDiagnosticEvent("primary_transition_invalidated", null, null, "changed", null, null, now);
                runtime.ClearPrimaryItemTransitionCandidate();
                return false;
            }

            if (!snapshots.TryGetValue(candidate.ParticipantUserId, out var participant) ||
                !CanRetainPrimaryTransitionParticipant(participant, candidate.ParticipantUserId,
                    candidate.ParticipantSessionId, candidate.SourceItemId, candidate.ParticipantRunTimeTicks))
            {
                // Retention requires a current bound playback observation.
                // Cached capabilities permit waiting, never sending while raw=false.
                runtime.RecordDiagnosticEvent("primary_transition_invalidated", null, null, "changed", null, null, now);
                runtime.ClearPrimaryItemTransitionCandidate();
                return false;
            }

            bool stoppedWaiting = runtime.State == RoomState.Waiting &&
                IsStoppedPlaybackWaitingError(runtime.Error);
            if (!snapshots.TryGetValue(room.PrimaryUserId, out var primary) ||
                primary == null || !primary.Online || primary.Stopped ||
                !string.Equals(primary.UserId, candidate.PrimaryUserId, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(primary.SessionId) ||
                string.IsNullOrEmpty(primary.ItemId))
            {
                // Watching must still pass through the two-second stop debounce.
                // After confirmation, avoid generic Waiting resets/error changes.
                if (!participant.SupportsRemoteControl) RecordPrimaryTransitionControlWait(runtime, candidate, now);
                return stoppedWaiting;
            }

            if (string.IsNullOrEmpty(candidate.SourceItemId) ||
                string.Equals(primary.ItemId, candidate.SourceItemId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(participant.ItemId, primary.ItemId, StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(primary.ItemId, candidate.SourceItemId, StringComparison.OrdinalIgnoreCase))
                {
                    runtime.RecordDiagnosticEvent("primary_transition_invalidated", null, null, "source_resumed", null, null, now);
                    runtime.ClearPrimaryItemTransitionCandidate();
                }
                return false;
            }

            if (!HasEffectivePlaybackControl(primary))
            {
                runtime.RecordDiagnosticEvent("primary_transition_invalidated", null, null, "changed", null, null, now);
                runtime.ClearPrimaryItemTransitionCandidate();
                return false;
            }
            if (!primary.SupportsRemoteControl || !participant.SupportsRemoteControl)
            {
                RecordPrimaryTransitionControlWait(runtime, candidate, now);
                return stoppedWaiting;
            }

            runtime.RecordDiagnosticEvent(
                "primary_item_changed", room.PrimaryUserId, RemoteCommands.PlayItem,
                "recovered", null, null, now);
            StartMediaHandoff(
                runtime,
                room,
                primary,
                participant,
                now,
                candidate.SourceItemId,
                isParticipantResync: false);
            return TryProcessMediaHandoff(
                runtime,
                room,
                snapshots,
                eligibility,
                primaryItemTransition: false,
                now);
        }

        private static void RecordPrimaryTransitionControlWait(RoomRuntime runtime, PrimaryItemTransitionCandidate candidate, DateTimeOffset now)
        {
            if (candidate.ControlWaitReported) return;
            candidate.ControlWaitReported = true;
            runtime.RecordDiagnosticEvent("primary_transition_waiting", null, null, "pending", null, null, now);
        }

        private static bool HasEffectivePlaybackControl(SessionSnapshot snapshot)
        {
            return snapshot != null && snapshot.Online && !snapshot.Stopped &&
                snapshot.Capabilities?.CanControlPlayback == true &&
                snapshot.RunTimeTicks > 0 &&
                Math.Abs(snapshot.PlaybackRate - 1.0) <= SyncConstants.PlaybackRateTolerance;
        }

        private static bool CanRetainPrimaryTransitionParticipant(
            SessionSnapshot snapshot, string userId, string sessionId, string sourceItemId, long runtimeTicks)
        {
            return HasEffectivePlaybackControl(snapshot) &&
                !string.IsNullOrEmpty(sessionId) && !string.IsNullOrEmpty(sourceItemId) &&
                string.Equals(snapshot.UserId, userId, StringComparison.OrdinalIgnoreCase) &&
                HasSameIdentity(sessionId, sourceItemId, snapshot) && runtimeTicks > 0 &&
                Math.Abs(snapshot.RunTimeTicks - runtimeTicks) <= SyncConstants.MaxRuntimeDifferenceTicks;
        }

        private bool TryProcessTransientSessionRecovery(
            RoomRuntime runtime,
            Room room,
            IReadOnlyList<SessionSnapshot> candidates,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            RoomEligibilityEvaluation eligibility,
            DateTimeOffset now)
        {
            if (runtime == null || room == null || snapshots == null)
            {
                return false;
            }

            if (runtime.State == RoomState.Recovering)
            {
                return ContinueTransientSessionRecovery(
                    runtime, room, candidates, snapshots, eligibility, now);
            }

            if (runtime.State != RoomState.Watching ||
                runtime.Recovery != null ||
                runtime.Barrier != null ||
                runtime.Handoff != null ||
                runtime.Pending.Count != 0 ||
                room.JoinedParticipantUserIds == null ||
                room.JoinedParticipantUserIds.Count != 2 ||
                runtime.Previous.Count != room.JoinedParticipantUserIds.Count ||
                string.IsNullOrEmpty(runtime.SyncItemId))
            {
                return false;
            }

            var missingUserIds = room.JoinedParticipantUserIds
                .Where(userId => !snapshots.ContainsKey(userId))
                .ToList();
            if (missingUserIds.Count == 0)
            {
                return false;
            }

            // Primary disappearance remains owned by the beta stop debounce
            // and late-primary handoff candidate. Recovery is only for a
            // temporarily missing non-primary participant.
            if (missingUserIds.Contains(room.PrimaryUserId, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            // SessionSelector intentionally omits stopped and offline records.
            // A missing selected session is a transient disappearance only when
            // the raw provider result contains no record for that user at all.
            // This preserves the existing explicit-Stop debounce and avoids
            // treating selector ambiguity or an offline record as recovery.
            if (missingUserIds.Any(userId => HasRawCandidateForUser(candidates, userId)))
            {
                return false;
            }

            foreach (var userId in room.JoinedParticipantUserIds)
            {
                if (missingUserIds.Contains(userId, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!snapshots.TryGetValue(userId, out var current) ||
                    current == null ||
                    !runtime.Previous.TryGetValue(userId, out var previous) ||
                    previous == null ||
                    !HasSameIdentity(previous.SessionId, previous.ItemId, current) ||
                    !string.Equals(current.ItemId, runtime.SyncItemId, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            runtime.BeginTransientSessionRecovery(now, missingUserIds, runtime.Previous);
            if (runtime.Recovery == null ||
                runtime.Recovery.MissingUserIds.Count != missingUserIds.Count ||
                runtime.Recovery.Participants.Count != runtime.Previous.Count)
            {
                runtime.ClearTransientSessionRecovery();
                return false;
            }

            runtime.State = RoomState.Recovering;
            runtime.Error = null;
            runtime.MissingSessionSinceUtc = null;
            runtime.RecordDiagnosticEvent(
                "recovery_started",
                runtime.Recovery.MissingUserId,
                null,
                "entered",
                runtime.Recovery.LastKnownPositionTicks,
                null,
                now);
            _logger?.Info(
                $"Room {room.Id}: transient session recovery started; " +
                $"missing={string.Join(",", missingUserIds)}");
            return true;
        }

        private bool ContinueTransientSessionRecovery(
            RoomRuntime runtime,
            Room room,
            IReadOnlyList<SessionSnapshot> candidates,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            RoomEligibilityEvaluation eligibility,
            DateTimeOffset now)
        {
            var recovery = runtime.Recovery;
            if (recovery == null || recovery.Participants.Count == 0)
            {
                runtime.ResetToWaiting();
                return true;
            }

            if ((now - recovery.StartedAtUtc).TotalSeconds >=
                SyncConstants.TransientRecoveryTimeoutSeconds)
            {
                return FailTransientSessionRecovery(
                    runtime,
                    room,
                    snapshots,
                    "recovery_timed_out",
                    null,
                    now);
            }

            foreach (var expected in recovery.Participants.Values)
            {
                var userCandidates = CandidatesForUser(candidates, expected.UserId);
                if (userCandidates.Any(candidate =>
                    candidate.Stopped &&
                    string.Equals(candidate.SessionId, expected.ExpectedSessionId, StringComparison.OrdinalIgnoreCase)))
                {
                    // Let the established Stop debounce handle an explicit
                    // stopped snapshot. Recovery must not turn Stop into a
                    // successful reconnect.
                    runtime.ClearTransientSessionRecovery();
                    runtime.State = RoomState.Watching;
                    runtime.MissingSessionSinceUtc = null;
                    return false;
                }

                if (userCandidates.Any(candidate =>
                    candidate.Online &&
                    !string.Equals(candidate.SessionId, expected.ExpectedSessionId, StringComparison.OrdinalIgnoreCase)))
                {
                    return FailTransientSessionRecovery(
                        runtime,
                        room,
                        snapshots,
                        "recovery_session_changed",
                        expected.UserId,
                        now);
                }

                if (userCandidates.Any(candidate =>
                    candidate.Online &&
                    string.Equals(candidate.SessionId, expected.ExpectedSessionId, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(candidate.ItemId, expected.ExpectedItemId, StringComparison.OrdinalIgnoreCase)))
                {
                    return FailTransientSessionRecovery(
                        runtime,
                        room,
                        snapshots,
                        "recovery_item_changed",
                        expected.UserId,
                        now);
                }
            }

            foreach (var expected in recovery.Participants.Values)
            {
                if (!snapshots.TryGetValue(expected.UserId, out var current) || current == null)
                {
                    return true;
                }

                if (!string.Equals(current.UserId, expected.UserId, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(current.SessionId, expected.ExpectedSessionId, StringComparison.OrdinalIgnoreCase))
                {
                    return FailTransientSessionRecovery(
                        runtime,
                        room,
                        snapshots,
                        "recovery_session_changed",
                        expected.UserId,
                        now);
                }

                if (!string.Equals(current.ItemId, expected.ExpectedItemId, StringComparison.OrdinalIgnoreCase))
                {
                    return FailTransientSessionRecovery(
                        runtime,
                        room,
                        snapshots,
                        "recovery_item_changed",
                        expected.UserId,
                        now);
                }
            }

            if (!IsTransientRecoveryPlaybackIdentityValid(recovery, snapshots) ||
                eligibility == null ||
                !eligibility.IsEligible)
            {
                return true;
            }

            runtime.RecordDiagnosticEvent(
                "recovery_confirmed",
                recovery.MissingUserId,
                null,
                "confirmed",
                recovery.LastKnownPositionTicks,
                null,
                now);
            runtime.ClearTransientSessionRecovery();
            runtime.MissingSessionSinceUtc = null;
            runtime.Error = null;
            runtime.State = RoomState.Barrier;
            StartBarrier(runtime, room, snapshots, now);
            BarrierTick(runtime, room, snapshots, now);
            return true;
        }

        private bool FailTransientSessionRecovery(
            RoomRuntime runtime,
            Room room,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            string eventType,
            string userId,
            DateTimeOffset now)
        {
            runtime.RecordDiagnosticEvent(eventType, userId, null, "changed", null, null, now);
            runtime.ClearTransientSessionRecovery();
            runtime.MissingSessionSinceUtc = null;
            runtime.ResetToWaiting();
            PauseOtherWhenWaiting(runtime, room, snapshots, now);
            return true;
        }

        private static bool IsTransientRecoveryPlaybackIdentityValid(
            TransientSessionRecoveryState recovery,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots)
        {
            foreach (var expected in recovery.Participants.Values)
            {
                if (!snapshots.TryGetValue(expected.UserId, out var current) ||
                    current == null ||
                    !current.Online ||
                    current.Stopped ||
                    current.RunTimeTicks <= 0 ||
                    Math.Abs(current.PlaybackRate - 1.0) > SyncConstants.PlaybackRateTolerance)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasRawCandidateForUser(
            IReadOnlyList<SessionSnapshot> candidates,
            string userId)
        {
            return CandidatesForUser(candidates, userId).Count != 0;
        }

        private static List<SessionSnapshot> CandidatesForUser(
            IReadOnlyList<SessionSnapshot> candidates,
            string userId)
        {
            return (candidates ?? Array.Empty<SessionSnapshot>())
                .Where(candidate => candidate != null &&
                    string.Equals(candidate.UserId, userId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private bool TryProcessParticipantResync(
            RoomRuntime runtime,
            Room room,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            RoomEligibilityEvaluation eligibility,
            bool sameItem,
            DateTimeOffset now)
        {
            if (runtime == null || room == null || snapshots == null ||
                string.IsNullOrEmpty(runtime.ParticipantResyncRequestedUserId))
            {
                return false;
            }

            if (!runtime.ParticipantResyncRequestedAtUtc.HasValue ||
                (now - runtime.ParticipantResyncRequestedAtUtc.Value).TotalSeconds >=
                    SyncConstants.MediaHandoffTimeoutSeconds)
            {
                FailParticipantResync(runtime, room, ParticipantResyncTimedOutError, now);
                return true;
            }

            string participantUserId = room.JoinedParticipantUserIds?.FirstOrDefault(
                userId => !string.Equals(userId, room.PrimaryUserId, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(participantUserId))
            {
                FailParticipantResync(runtime, room, ParticipantResyncUnavailableError, now);
                return true;
            }

            if (!snapshots.TryGetValue(room.PrimaryUserId, out var primary) ||
                primary == null || !primary.Online ||
                !string.Equals(primary.UserId, room.PrimaryUserId, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(primary.ItemId))
            {
                return true;
            }

            if (!snapshots.TryGetValue(participantUserId, out var participant) ||
                participant == null || !participant.Online ||
                !string.Equals(participant.UserId, participantUserId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(participant.ItemId, primary.ItemId, StringComparison.OrdinalIgnoreCase))
            {
                if (!sameItem || eligibility == null || !eligibility.IsEligible)
                {
                    return true;
                }

                runtime.ParticipantResyncRequestedUserId = null;
                runtime.RecordDiagnosticEvent(
                    "resync_barrier_started", participantUserId, RemoteCommands.PlayItem,
                    "started", null, null, now);
                StartBarrier(runtime, room, snapshots, now);
                BarrierTick(runtime, room, snapshots, now);
                return true;
            }

            runtime.ParticipantResyncRequestedUserId = null;
            StartMediaHandoff(
                runtime,
                room,
                primary,
                participant,
                now,
                participant.ItemId,
                isParticipantResync: true);
            return TryProcessMediaHandoff(
                runtime,
                room,
                snapshots,
                eligibility,
                primaryItemTransition: false,
                now);
        }

        private bool TryProcessMediaHandoff(
            RoomRuntime runtime,
            Room room,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            RoomEligibilityEvaluation eligibility,
            bool primaryItemTransition,
            DateTimeOffset now)
        {
            if (runtime.State == RoomState.Watching)
            {
                if (!primaryItemTransition ||
                    !snapshots.TryGetValue(room.PrimaryUserId, out var primary) ||
                    primary == null || string.IsNullOrEmpty(primary.ItemId))
                {
                    return false;
                }

                string participantUserId = room.JoinedParticipantUserIds.FirstOrDefault(
                    userId => !string.Equals(userId, room.PrimaryUserId, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(participantUserId))
                {
                    FailMediaHandoff(runtime, room, MediaHandoffInvalidError, now);
                    return true;
                }

                snapshots.TryGetValue(participantUserId, out var participant);
                if (participant != null && !participant.SupportsRemoteControl &&
                    (!runtime.Previous.TryGetValue(participantUserId, out var boundParticipant) ||
                     boundParticipant == null || !boundParticipant.SupportsRemoteControl ||
                     !CanRetainPrimaryTransitionParticipant(participant, participantUserId,
                         boundParticipant.SessionId, boundParticipant.ItemId, boundParticipant.RunTimeTicks)))
                {
                    FailMediaHandoff(runtime, room, MediaHandoffInvalidError, now);
                    return true;
                }
                string sourceItemId = participant?.ItemId;
                if (string.IsNullOrEmpty(sourceItemId) &&
                    runtime.Previous.TryGetValue(participantUserId, out var previousParticipant) &&
                    previousParticipant != null)
                {
                    sourceItemId = previousParticipant.ItemId;
                }
                runtime.RecordDiagnosticEvent(
                    "primary_item_changed", room.PrimaryUserId, RemoteCommands.PlayItem,
                    "observed", null, null, now);
                StartMediaHandoff(
                    runtime,
                    room,
                    primary,
                    participant,
                    now,
                    sourceItemId,
                    isParticipantResync: false);
            }

            var handoff = runtime.Handoff;
            if (handoff == null)
            {
                runtime.ResetToWaiting();
                return true;
            }

            // A target/ACK arriving at the deadline cannot reopen this operation.
            if (FailMediaHandoffIfExpired(runtime, room, now)) return true;

            // A raw-control flap cannot consume retries, rebind identities or
            // renew the handoff deadline. Revalidate fresh snapshots on recovery.
            snapshots.TryGetValue(handoff.PrimaryUserId, out var heldPrimary);
            snapshots.TryGetValue(handoff.ParticipantUserId, out var heldParticipant);
            var heldIdentity = runtime.HandoffControlRecoveryParticipant;
            if (heldIdentity != null ||
                heldPrimary != null && !heldPrimary.SupportsRemoteControl ||
                heldParticipant != null && !heldParticipant.SupportsRemoteControl)
            {
                if (FailMediaHandoffIfExpired(runtime, room, now)) return true;
                if (!HasEffectivePlaybackControl(heldPrimary) || !HasEffectivePlaybackControl(heldParticipant) ||
                    !string.Equals(heldPrimary.UserId, handoff.PrimaryUserId, StringComparison.OrdinalIgnoreCase) ||
                    !HasSameIdentity(handoff.PrimarySessionId, handoff.TargetItemId, heldPrimary) ||
                    !string.Equals(heldParticipant.UserId, handoff.ParticipantUserId, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(heldParticipant.SessionId, handoff.ParticipantSessionId, StringComparison.OrdinalIgnoreCase) ||
                    (!string.Equals(heldParticipant.ItemId, handoff.SourceItemId, StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(heldParticipant.ItemId, handoff.TargetItemId, StringComparison.OrdinalIgnoreCase)) ||
                    (heldIdentity != null &&
                     string.Equals(heldIdentity.ItemId, heldParticipant.ItemId, StringComparison.OrdinalIgnoreCase) &&
                     Math.Abs(heldIdentity.RunTimeTicks - heldParticipant.RunTimeTicks) > SyncConstants.MaxRuntimeDifferenceTicks))
                {
                    FailMediaHandoff(runtime, room, MediaHandoffInvalidError, now);
                    return true;
                }
                if (!heldPrimary.SupportsRemoteControl || !heldParticipant.SupportsRemoteControl)
                {
                    runtime.HandoffControlRecoveryParticipant = heldIdentity ?? heldParticipant;
                    return true;
                }
                runtime.HandoffControlRecoveryParticipant = null;
            }

            if (!snapshots.TryGetValue(room.PrimaryUserId, out var currentPrimary) ||
                currentPrimary == null || !currentPrimary.Online ||
                !string.Equals(currentPrimary.UserId, handoff.PrimaryUserId, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(currentPrimary.ItemId))
            {
                if (handoff.PrimaryHold != null)
                {
                    FailMediaHandoff(runtime, room, MediaHandoffInvalidError, now);
                    return true;
                }
                FailMediaHandoffIfExpired(runtime, room, now);
                return true;
            }

            if (handoff.PrimaryHold != null &&
                !string.Equals(currentPrimary.SessionId, handoff.PrimarySessionId, StringComparison.OrdinalIgnoreCase))
            {
                FailMediaHandoff(runtime, room, MediaHandoffInvalidError, now);
                return true;
            }

            if (!string.Equals(currentPrimary.ItemId, handoff.TargetItemId, StringComparison.OrdinalIgnoreCase))
            {
                if (handoff.SupersededTargetItemIds.Contains(currentPrimary.ItemId))
                {
                    FailMediaHandoffIfExpired(runtime, room, now);
                    return true;
                }

                snapshots.TryGetValue(handoff.ParticipantUserId, out var currentParticipant);
                HandoffTrace.TargetSuperseded(
                    _logger, room.Id, handoff, handoff.TargetItemId, now);
                runtime.RecordDiagnosticEvent(
                    "handoff_superseded", null, RemoteCommands.PlayItem, "superseded", null, null, now);
                StartMediaHandoff(
                    runtime,
                    room,
                    currentPrimary,
                    currentParticipant,
                    now,
                    currentParticipant?.ItemId,
                    handoff.IsParticipantResync);
                handoff = runtime.Handoff;
            }
            else if (!string.Equals(currentPrimary.SessionId, handoff.PrimarySessionId, StringComparison.OrdinalIgnoreCase))
            {
                // SessionSelector has already selected this identity using the
                // normal current-candidate rules. A same-user rebind gets a new
                // generation and cannot inherit the old PlayItem pending.
                var oldPrimarySessionId = handoff.PrimarySessionId;
                handoff.PrimarySessionId = currentPrimary.SessionId;
                handoff.Generation++;
                handoff.PlayItemPending = false;
                handoff.PlayItemIssuedAtUtc = null;
                handoff.RetryCount = 0;
                handoff.NextRetryAtUtc = null;
                handoff.LastError = null;
                HandoffTrace.SessionRebind(
                    _logger, room, room.Id, handoff, "primary", handoff.PrimaryUserId,
                    oldPrimarySessionId, handoff.PrimarySessionId, now);
                runtime.RecordDiagnosticEvent(
                    "handoff_superseded", null, RemoteCommands.PlayItem, "superseded", null, null, now);
            }

            if (!snapshots.TryGetValue(handoff.ParticipantUserId, out var participantSnapshot) ||
                participantSnapshot == null || !participantSnapshot.Online)
            {
                MaintainHandoffPrimaryHold(runtime, room, handoff, currentPrimary, now);
                FailMediaHandoffIfExpired(runtime, room, now);
                return true;
            }

            if (!string.Equals(participantSnapshot.UserId, handoff.ParticipantUserId, StringComparison.OrdinalIgnoreCase))
            {
                FailMediaHandoff(runtime, room, MediaHandoffInvalidError, now);
                return true;
            }

            if (handoff.PrimaryHold != null &&
                (participantSnapshot.Capabilities?.CanControlPlayback != true ||
                 !(Math.Abs(participantSnapshot.PlaybackRate - 1.0) <= SyncConstants.PlaybackRateTolerance)))
            {
                FailMediaHandoff(runtime, room, MediaHandoffInvalidError, now);
                return true;
            }

            if (!string.Equals(participantSnapshot.ItemId, handoff.TargetItemId, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(participantSnapshot.ItemId) &&
                !string.Equals(participantSnapshot.ItemId, handoff.SourceItemId, StringComparison.OrdinalIgnoreCase))
            {
                if (handoff.SupersededTargetItemIds.Contains(participantSnapshot.ItemId))
                {
                    // A delayed acknowledgement of an invalidated target is
                    // still a source snapshot for the current operation; it
                    // must never turn that old target into a confirmation.
                    handoff.SourceItemId = participantSnapshot.ItemId;
                }
                else
                {
                    // A participant leaving the source/target pair is a local
                    // opt-out from this automatic handoff. Do not keep retrying
                    // PlayItem and do not control the primary in response.
                    FailMediaHandoff(runtime, room, MediaHandoffParticipantChangedError, now);
                    return true;
                }
            }

            if (!string.Equals(participantSnapshot.SessionId, handoff.ParticipantSessionId, StringComparison.OrdinalIgnoreCase))
            {
                var oldParticipantSessionId = handoff.ParticipantSessionId;
                handoff.ParticipantSessionId = participantSnapshot.SessionId;
                handoff.Generation++;
                handoff.PlayItemPending = false;
                handoff.PlayItemIssuedAtUtc = null;
                handoff.RetryCount = 0;
                handoff.NextRetryAtUtc = null;
                handoff.LastError = null;
                HandoffTrace.SessionRebind(
                    _logger, room, room.Id, handoff, "participant", handoff.ParticipantUserId,
                    oldParticipantSessionId, handoff.ParticipantSessionId, now);
                runtime.RecordDiagnosticEvent(
                    "handoff_superseded", null, RemoteCommands.PlayItem, "superseded", null, null, now);
            }

            if (string.Equals(participantSnapshot.ItemId, handoff.TargetItemId, StringComparison.OrdinalIgnoreCase))
            {
                bool primaryReady = MaintainHandoffPrimaryHold(runtime, room, handoff, currentPrimary, now);
                if (runtime.Handoff != handoff) return true;
                bool participantReady = IsHandoffParticipantStarted(handoff, participantSnapshot, now);
                if (!primaryReady || !participantReady) return true;
                if (!eligibility.IsEligible)
                {
                    FailMediaHandoff(runtime, room, MediaHandoffInvalidError, now);
                    return true;
                }

                HandoffTrace.TargetConfirmed(
                    _logger, room, room.Id, handoff, participantSnapshot, now);
                runtime.RecordDiagnosticEvent(
                    "handoff_target_confirmed", null, RemoteCommands.PlayItem, "confirmed", null, null, now);
                bool? pausedIntent = handoff.PrimaryHold?.PrimaryPaused;
                runtime.ClearMediaHandoff();
                StartBarrier(runtime, room, snapshots, now, primaryPausedOverride: pausedIntent, fromMediaHandoff: true);
                BarrierTick(runtime, room, snapshots, now);
                return true;
            }

            MaintainHandoffPrimaryHold(runtime, room, handoff, currentPrimary, now);
            if (runtime.Handoff != handoff) return true;

            if (FailMediaHandoffIfExpired(runtime, room, now))
            {
                return true;
            }

            if (!(_issuer is IPlayItemIssuer))
            {
                FailMediaHandoff(runtime, room, MediaHandoffIssuerUnavailableError, now);
                return true;
            }

            if (handoff.PlayItemPending)
            {
                if (!handoff.PlayItemIssuedAtUtc.HasValue ||
                    (now - handoff.PlayItemIssuedAtUtc.Value).TotalSeconds < SyncConstants.PendingTimeoutSeconds)
                {
                    return true;
                }

                HandoffTrace.ConfirmationTimeout(_logger, room.Id, handoff, now);
                handoff.PlayItemPending = false;
                handoff.PlayItemIssuedAtUtc = null;
                handoff.LastError = "play item acknowledgement timed out";
            }

            if (handoff.NextRetryAtUtc.HasValue && now < handoff.NextRetryAtUtc.Value)
            {
                return true;
            }

            if (handoff.RetryCount >= SyncConstants.MaxPendingRetries + 1)
            {
                FailMediaHandoff(runtime, room, MediaHandoffPlayItemFailedError, now);
                return true;
            }

            handoff.NextRetryAtUtc = null;
            handoff.RetryCount++;
            HandoffTrace.Attempt(_logger, room.Id, handoff, participantSnapshot, now);
            var accepted = TryIssuePlayItem(room, participantSnapshot, handoff.TargetItemId, now, out var failure);
            HandoffTrace.IssueResult(
                _logger, room.Id, handoff, participantSnapshot, accepted, failure, now);
            if (accepted)
            {
                handoff.PlayItemPending = true;
                handoff.PlayItemIssuedAtUtc = now;
                handoff.LastError = null;
                runtime.RecordDiagnosticEvent(
                    "handoff_play_requested", null, RemoteCommands.PlayItem, "pending", null, null, now);
                return true;
            }

            handoff.LastError = failure ?? MediaHandoffPlayItemFailedError;
            runtime.RecordDiagnosticEvent(
                "handoff_play_requested", null, RemoteCommands.PlayItem, "failed", null, null, now);
            if (handoff.RetryCount < SyncConstants.MaxPendingRetries + 1)
            {
                handoff.NextRetryAtUtc = now.AddSeconds(SyncConstants.PendingRetryGraceSeconds);
                return true;
            }

            FailMediaHandoff(runtime, room, MediaHandoffPlayItemFailedError, now);
            return true;
        }

        private bool MaintainHandoffPrimaryHold(
            RoomRuntime runtime, Room room, MediaHandoffState handoff, SessionSnapshot primary, DateTimeOffset now)
        {
            var hold = handoff.PrimaryHold;
            if (hold == null) return true;
            if (!string.Equals(hold.PrimaryMediaSourceId, primary.MediaSourceId, StringComparison.OrdinalIgnoreCase) ||
                (hold.PrimaryRunTimeTicks > 0 && primary.RunTimeTicks > 0 &&
                 Math.Abs(hold.PrimaryRunTimeTicks - primary.RunTimeTicks) > SyncConstants.MaxRuntimeDifferenceTicks))
            {
                FailMediaHandoff(runtime, room, MediaHandoffInvalidError, now);
                return false;
            }
            if (primary.Capabilities?.CanControlPlayback != true ||
                !primary.SupportsRemoteControl || !primary.Online || primary.Stopped ||
                !(Math.Abs(primary.PlaybackRate - 1.0) <= SyncConstants.PlaybackRateTolerance))
            {
                FailMediaHandoff(runtime, room, MediaHandoffInvalidError, now);
                return false;
            }
            if (primary.RunTimeTicks <= 0)
            {
                if (hold.Acknowledged || hold.PauseAttempts > 0)
                {
                    FailMediaHandoff(runtime, room, MediaHandoffInvalidError, now);
                    return false;
                }
                hold.PrimaryRunTimeTicks = 0;
                hold.ObservedAtUtc = now;
                hold.InitialPositionTicks = primary.PositionTicks;
                return false;
            }
            if (hold.Acknowledged)
            {
                if (!primary.IsPaused ||
                    Math.Abs(primary.PositionTicks - hold.PausedPositionTicks) > SyncConstants.SeekToleranceTicks)
                {
                    runtime.RecordDiagnosticEvent("handoff_primary_hold_interrupted", null, null, "changed", null, null, now);
                    FailMediaHandoff(runtime, room, HandoffHoldRequiresResyncError, now);
                    return false;
                }
                return true;
            }
            if (primary.IsPaused)
            {
                // A pause observed before our request is user/player intent,
                // so it must never inherit an automatic playing restore.
                if (hold.PauseAttempts == 0) hold.PrimaryPaused = true;
                if (hold.PauseIssuedAtUtc.HasValue && now <= hold.PauseIssuedAtUtc.Value) return false;
                hold.Acknowledged = true;
                hold.PausedPositionTicks = primary.PositionTicks;
                runtime.RecordDiagnosticEvent("handoff_primary_paused", room.PrimaryUserId, RemoteCommands.Pause,
                    "acknowledged", primary.PositionTicks,
                    hold.PauseIssuedAtUtc.HasValue ? (double?)(now - hold.PauseIssuedAtUtc.Value).TotalSeconds : null, now);
                return true;
            }
            if (hold.PrimaryRunTimeTicks <= 0)
            {
                hold.PrimaryRunTimeTicks = primary.RunTimeTicks;
                hold.ObservedAtUtc = now;
                hold.InitialPositionTicks = primary.PositionTicks;
                return false;
            }
            if (hold.PauseAttempts == 0)
            {
                if ((now - hold.ObservedAtUtc).TotalSeconds < SyncConstants.MediaHandoffStartupObservationSeconds ||
                    primary.PositionTicks <= hold.InitialPositionTicks) return false;
            }
            else
            {
                double timeout = SyncConstants.PendingTimeoutSeconds +
                    (hold.PauseAttempts > SyncConstants.MaxPendingRetries ? SyncConstants.PendingRetryGraceSeconds : 0);
                if ((now - hold.PauseIssuedAtUtc.Value).TotalSeconds < timeout) return false;
                if (hold.PauseAttempts >= SyncConstants.MaxPendingRetries + 1)
                {
                    FailMediaHandoff(runtime, room, MediaHandoffTimedOutError, now);
                    return false;
                }
            }
            hold.PauseAttempts++;
            hold.PauseIssuedAtUtc = now;
            bool accepted = TryIssueCommand(room.Id, room.AdminUserId, primary.UserId, primary, RemoteCommands.Pause, null, now, out _);
            runtime.RecordDiagnosticEvent("handoff_primary_pause_requested", room.PrimaryUserId, RemoteCommands.Pause,
                accepted ? "pending" : "failed", null, null, now);
            if (!accepted) FailMediaHandoff(runtime, room, MediaHandoffPlayItemFailedError, now);
            return false;
        }

        private static bool IsHandoffParticipantStarted(MediaHandoffState handoff, SessionSnapshot participant, DateTimeOffset now)
        {
            var hold = handoff.PrimaryHold;
            if (hold == null) return true;
            if (participant.RunTimeTicks <= 0) return false;
            if (!string.Equals(hold.ParticipantSessionId, participant.SessionId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(hold.ParticipantMediaSourceId, participant.MediaSourceId, StringComparison.OrdinalIgnoreCase) ||
                Math.Abs(hold.ParticipantRunTimeTicks - participant.RunTimeTicks) > SyncConstants.MaxRuntimeDifferenceTicks)
            {
                hold.ParticipantSessionId = participant.SessionId;
                hold.ParticipantMediaSourceId = participant.MediaSourceId;
                hold.ParticipantRunTimeTicks = participant.RunTimeTicks;
                hold.ParticipantObservedAtUtc = now;
                hold.ParticipantInitialPositionTicks = participant.PositionTicks;
                return false;
            }
            return hold.ParticipantObservedAtUtc.HasValue &&
                (now - hold.ParticipantObservedAtUtc.Value).TotalSeconds >= SyncConstants.MediaHandoffStartupObservationSeconds &&
                (participant.IsPaused || participant.PositionTicks > hold.ParticipantInitialPositionTicks);
        }

        private bool FailMediaHandoffIfExpired(
            RoomRuntime runtime,
            Room room,
            DateTimeOffset now)
        {
            if (runtime.Handoff == null ||
                (now - runtime.Handoff.StartedAtUtc).TotalSeconds < SyncConstants.MediaHandoffTimeoutSeconds)
            {
                return false;
            }

            FailMediaHandoff(runtime, room, MediaHandoffTimedOutError, now);
            return true;
        }

        private static bool IsMediaHandoffError(string error)
        {
            return string.Equals(error, MediaHandoffIssuerUnavailableError, StringComparison.Ordinal) ||
                string.Equals(error, MediaHandoffPlayItemFailedError, StringComparison.Ordinal) ||
                string.Equals(error, MediaHandoffTimedOutError, StringComparison.Ordinal) ||
                string.Equals(error, MediaHandoffInvalidError, StringComparison.Ordinal);
        }

        private void StartMediaHandoff(
            RoomRuntime runtime,
            Room room,
            SessionSnapshot primary,
            SessionSnapshot participant,
            DateTimeOffset now,
            string sourceItemId,
            bool isParticipantResync = false)
        {
            var handoff = runtime.BeginMediaHandoff(
                primary.ItemId,
                sourceItemId,
                now,
                room.PrimaryUserId,
                primary.SessionId,
                room.JoinedParticipantUserIds.First(
                    userId => !string.Equals(userId, room.PrimaryUserId, StringComparison.OrdinalIgnoreCase)),
                participant?.SessionId);
            handoff.IsParticipantResync = isParticipantResync;
            if (!isParticipantResync &&
                !string.Equals(participant?.ItemId, primary.ItemId, StringComparison.OrdinalIgnoreCase))
            {
                handoff.PrimaryHold = new HandoffPrimaryHoldState
                {
                    PrimaryPaused = primary.IsPaused,
                    ObservedAtUtc = now,
                    InitialPositionTicks = primary.PositionTicks,
                    PrimaryMediaSourceId = primary.MediaSourceId,
                    PrimaryRunTimeTicks = primary.RunTimeTicks,
                };
            }
            runtime.State = RoomState.Handoff;
            runtime.Error = null;
            runtime.Barrier = null;
            runtime.Pending.Clear();
            runtime.WaitingPauseRetries.Clear();
            runtime.Suppressed.Clear();
            runtime.PauseAlign.Clear();
            runtime.SyncItemId = null;
            runtime.MissingSessionSinceUtc = null;
            runtime.ClearTransientSessionRecovery();
            runtime.ClearRemoteControlRecovery();
            runtime.ClearPrimaryItemTransitionCandidate();
            HandoffTrace.Started(_logger, room, room.Id, handoff, now);
            runtime.RecordDiagnosticEvent(
                "handoff_started", null, RemoteCommands.PlayItem, "started", null, null, now);
            _logger?.Info(
                $"Room {room.Id}: media handoff started for primary item {ShortIdentity(handoff.TargetItemId)}");
        }

        private void FailMediaHandoff(
            RoomRuntime runtime,
            Room room,
            string error,
            DateTimeOffset now)
        {
            bool heldPrimary = runtime?.Handoff?.PrimaryHold != null;
            HandoffTrace.Failed(_logger, room?.Id, runtime?.Handoff, error, now);
            runtime.RecordDiagnosticEvent(
                "handoff_failed", null, RemoteCommands.PlayItem, "failed", null, null, now);
            runtime.ResetToWaiting();
            runtime.Previous.Clear();
            runtime.PreviousAtUtc = null;
            runtime.MissingSessionSinceUtc = null;
            runtime.SyncItemId = null;
            runtime.BarrierRetryAtUtc = null;
            runtime.Error = error;
            if (heldPrimary) runtime.HandoffRequiresResync = true;
        }

        private static void FailParticipantResync(
            RoomRuntime runtime,
            Room room,
            string error,
            DateTimeOffset now)
        {
            runtime.RecordDiagnosticEvent(
                "resync", null, RemoteCommands.PlayItem, "failed", null, null, now);
            runtime.ParticipantResyncRequestedUserId = null;
            runtime.MissingSessionSinceUtc = null;
            runtime.ResetToWaiting();
            runtime.Error = error;
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
                else if (runtime.MissingSessionSinceUtc.HasValue &&
                    (!runtime.Previous.TryGetValue(userId, out var previous) ||
                     previous == null ||
                     !HasSameIdentity(previous.SessionId, previous.ItemId, current)))
                {
                    // Once a stop observation has started, an online snapshot
                    // is a recovery only when it proves continuity with the
                    // previously bound session. A raw-control failure alone
                    // is not a second stopped user. A replacement session, even
                    // when controllable, must not clear the debounce window.
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
                if (stoppedUsers.Contains(room.PrimaryUserId))
                {
                    BeginPrimaryItemTransitionCandidate(runtime, room, snapshots, stoppedUsers, now);
                }
                return false;
            }

            return (now - runtime.MissingSessionSinceUtc.Value).TotalSeconds >= MissingSessionDebounceSeconds;
        }

        private static void BeginPrimaryItemTransitionCandidate(
            RoomRuntime runtime,
            Room room,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            ISet<string> stoppedUsers,
            DateTimeOffset now)
        {
            if (runtime == null || room == null || room.JoinedParticipantUserIds == null ||
                room.JoinedParticipantUserIds.Count != 2 ||
                stoppedUsers == null || !stoppedUsers.Contains(room.PrimaryUserId) ||
                snapshots == null ||
                !runtime.Previous.TryGetValue(room.PrimaryUserId, out var previousPrimary) ||
                previousPrimary == null || !previousPrimary.Online ||
                string.IsNullOrEmpty(previousPrimary.SessionId) ||
                string.IsNullOrEmpty(previousPrimary.ItemId))
            {
                return;
            }

            string participantUserId = room.JoinedParticipantUserIds.FirstOrDefault(
                userId => !string.Equals(userId, room.PrimaryUserId, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(participantUserId) ||
                !snapshots.TryGetValue(participantUserId, out var currentParticipant) ||
                !runtime.Previous.TryGetValue(participantUserId, out var previousParticipant) ||
                previousParticipant == null || !previousParticipant.Online ||
                string.IsNullOrEmpty(previousParticipant.SessionId) ||
                string.IsNullOrEmpty(previousParticipant.ItemId) ||
                !previousParticipant.SupportsRemoteControl ||
                !string.Equals(previousParticipant.ItemId, previousPrimary.ItemId, StringComparison.OrdinalIgnoreCase) ||
                !CanRetainPrimaryTransitionParticipant(currentParticipant, participantUserId,
                    previousParticipant.SessionId, previousParticipant.ItemId, previousParticipant.RunTimeTicks))
            {
                return;
            }

            runtime.BeginPrimaryItemTransitionCandidate(
                now,
                room.PrimaryUserId,
                previousPrimary.ItemId,
                participantUserId,
                previousParticipant.SessionId,
                previousParticipant.RunTimeTicks);
            runtime.RecordDiagnosticEvent("primary_transition_candidate", null, null, "entered", null, null, now);
            if (!currentParticipant.SupportsRemoteControl)
                RecordPrimaryTransitionControlWait(runtime, runtime.PrimaryItemTransitionCandidate, now);
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
                if (snapshot == null || !snapshot.Online || snapshot.Stopped || !snapshot.SupportsRemoteControl ||
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
                    QueueNotification(
                        room,
                        pair.Key,
                        snapshot,
                        StoppedPlaybackMessageHeader,
                        StoppedPlaybackMessageText,
                        stopNotice: true);
                }
                catch
                {
                    // Message delivery is best effort and must never block the
                    // playback state machine.
                }
            }
        }

        private void NotifyAutomaticBarrierRetry(
            RoomRuntime runtime,
            Room room,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            DateTimeOffset now)
        {
            if (!_notifyOnSyncActions || _messageIssuer == null || room == null || snapshots == null)
            {
                return;
            }

            if (runtime.RetryNotificationSent) return;
            runtime.RetryNotificationSent = true;

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
                    QueueNotificationCore(room, userId, snapshot, AutomaticResyncMessageHeader,
                        AutomaticResyncMessageText, false, runtime.NotificationOperation, RoomState.Barrier, runtime.Barrier?.Stage);
                }
                catch
                {
                    // Message delivery is best effort and must not block sync.
                }
            }
        }

        private void NotifyParticipants(
            Room room,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            string text,
            DateTimeOffset now,
            bool includeAll,
            string excludeUserId = null,
            object operation = null,
            RoomState? expectedState = null)
        {
            if (!_notifyOnSyncActions || _messageIssuer == null || room == null || snapshots == null)
            {
                return;
            }

            foreach (var userId in room.JoinedParticipantUserIds)
            {
                if (!includeAll && string.Equals(userId, excludeUserId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!snapshots.TryGetValue(userId, out var snapshot) ||
                    snapshot == null || !snapshot.Online)
                {
                    continue;
                }

                try
                {
                    QueueNotificationCore(room, userId, snapshot, "一起观看", text,
                        false, operation, expectedState);
                }
                catch
                {
                    // Advisory notification failures must not affect state.
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

            // A playback-rate change is a separate control action. Keep the
            // projection conservative for that case, but do not discard a
            // position jump just because the same poll also observed a
            // play/pause transition: a user can seek and pause/resume in one
            // client update.
            if (Math.Abs(previous.PlaybackRate - current.PlaybackRate) > SyncConstants.PlaybackRateTolerance)
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
            if (previous.IsPaused != current.IsPaused)
            {
                // The exact pause/resume moment is unknown between snapshots.
                // Treat every position between the stopped and fully elapsed
                // natural projections as explainable by that transition.
                double naturalMin = previous.PositionTicks;
                double naturalMax = naturalMin +
                    elapsedSeconds * Math.Max(0, previous.PlaybackRate) *
                    SessionSnapshot.TicksPerSecond;
                if (current.PositionTicks >= naturalMin &&
                    current.PositionTicks <= naturalMax)
                {
                    return false;
                }

                double distanceToNaturalInterval = current.PositionTicks < naturalMin
                    ? naturalMin - current.PositionTicks
                    : current.PositionTicks - naturalMax;
                return distanceToNaturalInterval >= seekDetectionThresholdTicks;
            }

            double expectedPosition = previous.IsPaused
                ? previous.PositionTicks
                : previous.PositionTicks +
                  elapsedSeconds * previous.PlaybackRate * SessionSnapshot.TicksPerSecond;
            double difference = Math.Abs(current.PositionTicks - expectedPosition);
            return difference >= seekDetectionThresholdTicks;
        }

        private static bool IsStoppedPlaybackWaitingError(string error) =>
            string.Equals(error, StoppedPlaybackError, StringComparison.Ordinal) ||
            string.Equals(error, StoppedPositionConfirmationError, StringComparison.Ordinal);

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

        private void LogEligibilityFailureIfChanged(
            RoomRuntime runtime,
            Room room,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            RoomEligibilityEvaluation eligibility,
            DateTimeOffset now)
        {
            if (eligibility.IsEligible)
            {
                if (runtime.LastEligibilityFailureReason.HasValue)
                {
                    runtime.RecordDiagnosticEvent(
                        "eligibility_changed", null, null, "recovered", null, null, now);
                }
                runtime.LastEligibilityFailureReason = null;
                return;
            }

            if (runtime.LastEligibilityFailureReason == eligibility.FailureReason)
            {
                return;
            }

            runtime.LastEligibilityFailureReason = eligibility.FailureReason;
            runtime.RecordDiagnosticEvent(
                "eligibility_changed", null, null, eligibility.FailureReason.ToString(), null, null, now);
            _logger?.Warn(
                $"Room {room.Id}: eligibility failure reason={eligibility.FailureReason}; " +
                FormatEligibilitySnapshotSummary(room, snapshots));
        }

        private void LogMultipleSessionDiagnostics(
            RoomRuntime runtime,
            Room room,
            SessionSelectionDiagnostics selection,
            RoomEligibilityEvaluation eligibility,
            DateTimeOffset now)
        {
            if (selection == null || !selection.HasMultipleCandidates)
            {
                runtime.LastMultipleSessionDiagnosticSignature = null;
                return;
            }

            var candidates = (selection.Candidates ?? Array.Empty<SessionSelectionCandidateDiagnostic>())
                .OrderBy(candidate => candidate.UserId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.Snapshot?.SessionId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.Snapshot?.ItemId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.Disposition, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var selectedByUser = (room.JoinedParticipantUserIds ?? Array.Empty<string>())
                .Select(userId =>
                {
                    SessionSnapshot snapshot = null;
                    selection.Selected?.TryGetValue(userId, out snapshot);
                    return userId + "=" + (snapshot == null
                        ? "-"
                        : snapshot.SessionId + "/" + snapshot.ItemId);
                });
            string signature = string.Join(
                "|",
                candidates.Select(candidate =>
                    candidate.UserId + ":" + candidate.Snapshot?.SessionId + "/" +
                    candidate.Snapshot?.ItemId + ":" + candidate.Disposition + ":" +
                    FormatCapabilitySignature(candidate.Snapshot))
                    .Concat(selectedByUser)
                    .Concat(new[] { eligibility?.FailureReason.ToString() ?? "unknown" }));
            if (string.Equals(runtime.LastMultipleSessionDiagnosticSignature, signature, StringComparison.Ordinal))
            {
                return;
            }

            runtime.LastMultipleSessionDiagnosticSignature = signature;
            var details = candidates.Select(candidate =>
            {
                var snapshot = candidate.Snapshot;
                if (snapshot == null)
                {
                    return $"user={candidate.UserId},session=-,item=-,position=-,paused=-,age=unknown," +
                        "reportedSupportsRemoteControl=-,effectiveSupportsRemoteControl=-," +
                        $"supportedCommandCount=-,disposition={candidate.Disposition}";
                }

                string age = snapshot.LastActivityDateUtc == default
                    ? "unknown"
                    : Math.Max(0, (now - snapshot.LastActivityDateUtc).TotalSeconds).ToString("0.0") + "s";
                return $"user={candidate.UserId},session={ShortMultipleSessionIdentity(snapshot.SessionId)}," +
                    $"item={ShortMultipleSessionIdentity(snapshot.ItemId)},position={snapshot.PositionSeconds:0.0}s," +
                    $"paused={snapshot.IsPaused},age={age},{FormatCapabilitySummary(snapshot)}," +
                    $"disposition={candidate.Disposition}";
            });
            string selectedSummary = string.Join(
                ",",
                (room.JoinedParticipantUserIds ?? Array.Empty<string>()).Select(userId =>
                {
                    SessionSnapshot snapshot = null;
                    selection.Selected?.TryGetValue(userId, out snapshot);
                    return $"{userId}={(snapshot == null ? "-" :
                        ShortMultipleSessionIdentity(snapshot.SessionId) + "/" +
                        ShortMultipleSessionIdentity(snapshot.ItemId))}";
                }));
            _logger?.Warn(
                $"Room {room.Id}: multiple-session selection; candidates=[{string.Join("; ", details)}]; " +
                $"selected=[{selectedSummary}]; eligibilityReason={eligibility?.FailureReason.ToString() ?? "unknown"}");
        }

        private static string FormatCapabilitySignature(SessionSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return "-";
            }

            return string.Concat(
                snapshot.SupportsRemoteControl ? "reported=1" : "reported=0",
                ",effective=",
                snapshot.Capabilities?.SupportsRemoteControl == true ? "1" : "0",
                ",commands=",
                snapshot.Capabilities?.SupportedCommands?.Count ?? 0);
        }

        private static string FormatCapabilitySummary(SessionSnapshot snapshot)
        {
            return string.Concat(
                "reportedSupportsRemoteControl=",
                snapshot.SupportsRemoteControl,
                ",effectiveSupportsRemoteControl=",
                snapshot.Capabilities?.SupportsRemoteControl == true,
                ",supportedCommandCount=",
                snapshot.Capabilities?.SupportedCommands?.Count ?? 0);
        }

        private static string ShortMultipleSessionIdentity(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "-";
            }

            const int edgeLength = 6;
            const int maxLength = edgeLength * 2 + 3;
            return value.Length <= maxLength
                ? value
                : value.Substring(0, edgeLength) + "..." + value.Substring(value.Length - edgeLength);
        }

        private static string FormatEligibilitySnapshotSummary(
            Room room,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots)
        {
            var participants = room.JoinedParticipantUserIds ?? Array.Empty<string>();
            var parts = new List<string>();
            foreach (var userId in participants)
            {
                snapshots.TryGetValue(userId, out var snapshot);
                string role = string.Equals(userId, room.PrimaryUserId, StringComparison.OrdinalIgnoreCase)
                    ? "primary"
                    : "secondary";
                parts.Add($"{role}/user={userId}:{FormatSnapshotSummary(snapshot)}");
            }

            long? runtimeDelta = null;
            var runtimeValues = snapshots.Values.Where(s => s != null).Select(s => s.RunTimeTicks).ToList();
            if (runtimeValues.Count == 2)
            {
                runtimeDelta = Math.Abs(runtimeValues[0] - runtimeValues[1]);
            }

            return $"snapshotCount={snapshots.Count}, missing={string.Join(",", participants.Where(u => !snapshots.ContainsKey(u)))}, " +
                $"runTimeDiff={FormatRuntime(runtimeDelta)}; {string.Join("; ", parts)}";
        }

        private static string FormatSnapshotSummary(SessionSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return "snapshot=null";
            }

            return $"session={ShortIdentity(snapshot.SessionId)}, item={ShortIdentity(snapshot.ItemId)}, " +
                $"online={snapshot.Online}, stopped={snapshot.Stopped}, " +
                $"{FormatCapabilitySummary(snapshot)}, paused={snapshot.IsPaused}, " +
                $"playbackRate={snapshot.PlaybackRate:0.###}, runTimeTicks={FormatRuntime(snapshot.RunTimeTicks)}";
        }

        private static string FormatRuntime(long? runtimeTicks)
        {
            return runtimeTicks.HasValue
                ? $"{runtimeTicks.Value}ticks"
                : "-";
        }

        private static string ShortIdentity(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "-";
            }

            return value.Length <= 8 ? value : value.Substring(0, 8);
        }

        private static bool HasFailureDetail(string error)
        {
            return !string.IsNullOrEmpty(error);
        }

        private void LogPendingUnacknowledged(
            Room room,
            PendingCommand pending,
            SessionSnapshot snapshot,
            string detail)
        {
            _logger?.Warn(
                $"Room {room.Id}: accepted-but-unacknowledged command={pending?.Command}, " +
                $"targetUser={pending?.UserId}, session={ShortIdentity(pending?.SessionId)}, " +
                $"item={ShortIdentity(pending?.ItemId)}, detail={detail}; " +
                (snapshot == null
                    ? "currentSnapshot=missing"
                    : $"currentSnapshot=session={ShortIdentity(snapshot.SessionId)}, " +
                      $"item={ShortIdentity(snapshot.ItemId)}, paused={snapshot.IsPaused}, " +
                      $"position={FormatPosition(snapshot.PositionTicks)}s"));
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
            return retry != null && snapshot != null && snapshot.Online &&
                string.Equals(retry.SessionId, snapshot.SessionId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(retry.ItemId, snapshot.ItemId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(retry.CapabilityKey, GetPauseCapabilityKey(snapshot), StringComparison.Ordinal);
        }

        private static bool IsCurrentWaitingPauseIdentity(
            string userId,
            string sessionId,
            string itemId,
            SessionSnapshot snapshot)
        {
            return snapshot != null &&
                string.Equals(snapshot.UserId, userId, StringComparison.OrdinalIgnoreCase) &&
                HasSameIdentity(sessionId, itemId, snapshot);
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

                bool currentIdentity = string.Equals(
                    pending?.Command,
                    RemoteCommands.Pause,
                    StringComparison.Ordinal) &&
                    runtime.State == RoomState.Waiting
                    ? IsCurrentWaitingPauseIdentity(
                        userId,
                        pending?.SessionId,
                        pending?.ItemId,
                        snapshot)
                    : IsCurrentCommandIdentity(
                        runtime,
                        sameItem,
                        userId,
                        pending?.SessionId,
                        pending?.ItemId,
                        snapshot);

                if (!currentIdentity)
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
                    runtime.RecordDiagnosticEvent(
                        "command_acknowledged", userId, pending.Command, "acknowledged",
                        pending.PositionTicks, latencySeconds, now);
                    if (!(runtime.State == RoomState.Waiting &&
                          string.Equals(pending.Command, RemoteCommands.Pause, StringComparison.Ordinal)))
                    {
                        runtime.Suppressed[userId] = new SuppressedCommand
                        {
                            SessionId = pending.SessionId,
                            ItemId = pending.ItemId,
                            Command = pending.Command,
                            PositionTicks = pending.PositionTicks,
                            UntilUtc = now + TimeSpan.FromSeconds(SyncConstants.SuppressSeconds),
                        };
                    }
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
                    LogPendingUnacknowledged(room, pending, snapshot, "barrier retry deadline");
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

                if (runtime.State == RoomState.Waiting &&
                    string.Equals(pending.Command, RemoteCommands.Pause, StringComparison.Ordinal))
                {
                    runtime.Pending.Remove(userId);
                    if (runtime.WaitingPauseRetries.TryGetValue(userId, out var waitingPauseRetry) &&
                        IsCurrentWaitingPauseRetry(waitingPauseRetry, snapshot) &&
                        waitingPauseRetry.Exhausted)
                    {
                        runtime.Error = WaitingPauseRetryLimitError;
                    }

                    // Waiting Pause has its own bounded lifecycle. Let the
                    // Waiting path apply its cooldown and attempt limit rather
                    // than routing this command through generic Pending retry.
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
                        runtime.RecordDiagnosticEvent(
                            "retry_scheduled", userId, pending.Command, "retry_scheduled",
                            pending.PositionTicks, null, now);
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
                    LogPendingUnacknowledged(
                        room,
                        pending,
                        snapshot,
                        $"after {SyncConstants.MaxPendingRetries + 1} attempts");
                    runtime.RecordDiagnosticEvent(
                        "command_failed", userId, pending.Command, "failed",
                        pending.PositionTicks, null, now);
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
                        FailSeekBarrier(runtime, room.Id, now);
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
            RecordAutomaticDriftRepairFailure(runtime, now);
            runtime.ResetToWaiting();
            runtime.Error = error;
            runtime.BarrierRetryAtUtc = now.AddSeconds(SyncConstants.AutomaticBarrierRetryDelaySeconds);
            runtime.RecordDiagnosticEvent(
                "retry_scheduled", null, null, "retry_scheduled", null, null, now);
            _logger?.Info(
                $"Room {roomId}: barrier retry scheduled; failureDetailPresent={HasFailureDetail(error)}");
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
                FailSeekBarrier(runtime, roomId, now);
                return;
            }

            DateTimeOffset retryAt = now.AddSeconds(SyncConstants.AutomaticBarrierRetryDelaySeconds);
            if (barrier.SeekRetryDeadlineAtUtc.HasValue &&
                retryAt > barrier.SeekRetryDeadlineAtUtc.Value)
            {
                retryAt = barrier.SeekRetryDeadlineAtUtc.Value;
            }

            barrier.SeekRetryAtUtc = retryAt;
            _logger?.Info(
                $"Room {roomId}: seek retry scheduled; failureDetailPresent={HasFailureDetail(error)}");
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

        private void FailSeekBarrier(
            RoomRuntime runtime,
            string roomId,
            DateTimeOffset now)
        {
            RecordAutomaticDriftRepairFailure(runtime, now);
            runtime.ResetToWaiting();
            runtime.Error = BarrierSeekRetryBudgetError;
            runtime.BarrierRetryAtUtc = null;
            _logger?.Warn($"Room {roomId}: {BarrierSeekRetryBudgetError}");
        }

        private static void RecordAutomaticDriftRepairFailure(
            RoomRuntime runtime,
            DateTimeOffset now)
        {
            if (runtime?.Barrier?.FromAutomaticDriftRepair != true)
            {
                return;
            }

            runtime.RecordDriftDiagnosticEvent(
                "drift_auto_repair_failed",
                "failed",
                runtime.Barrier.AutomaticDriftSeconds.GetValueOrDefault(),
                now);
        }

        private bool TryIssuePlayItem(
            Room room,
            SessionSnapshot snapshot,
            string itemId,
            DateTimeOffset now,
            out string error)
        {
            ThrowIfStopping();
            error = null;
            if (room == null || snapshot == null || !snapshot.Online ||
                string.IsNullOrEmpty(itemId) ||
                !string.Equals(snapshot.UserId, room.JoinedParticipantUserIds.FirstOrDefault(
                    userId => !string.Equals(userId, room.PrimaryUserId, StringComparison.OrdinalIgnoreCase)),
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "invalid_argument";
                return false;
            }

            if (!snapshot.SupportsRemoteControl)
            {
                error = "remote_control_unsupported";
                return false;
            }

            var issuer = _issuer as IPlayItemIssuer;
            if (issuer == null)
            {
                error = MediaHandoffIssuerUnavailableError;
                return false;
            }

            var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stopToken);
            try
            {
                timeout.CancelAfter(ExternalCallTimeout);
                try
                {
                    ThrowIfStopping();
                    bool accepted = issuer.TryIssuePlayItem(
                        room.Id,
                        room.AdminUserId,
                        snapshot.UserId,
                        snapshot,
                        itemId,
                        now,
                        timeout.Token,
                        out error);
                    ThrowIfStopping();
                    if (!accepted && string.IsNullOrEmpty(error))
                    {
                        error = MediaHandoffPlayItemFailedError;
                    }

                    return accepted;
                }
                catch (OperationCanceledException)
                {
                    ThrowIfStopping();
                    error = "command_timeout";
                    return false;
                }
                catch
                {
                    ThrowIfStopping();
                    error = "command_failed";
                    return false;
                }
            }
            finally { DisposeCommandTimeout(timeout); }
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
            ThrowIfStopping();
            if (snapshot == null || !snapshot.Online || !snapshot.SupportsRemoteControl)
            {
                error = "remote_control_unsupported";
                return false;
            }
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
                bool accepted = _issuer.TryIssue(
                    roomId,
                    controllingUserId,
                    userId,
                    snapshot,
                    command,
                    positionTicks,
                    now,
                    out error);
                ThrowIfStopping();
                return accepted;
            }

            var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stopToken);
            try
            {
                timeout.CancelAfter(ExternalCallTimeout);
                ThrowIfStopping();
                bool accepted = cancellable.TryIssue(
                    roomId,
                    controllingUserId,
                    userId,
                    snapshot,
                    command,
                    positionTicks,
                    now,
                    timeout.Token,
                    out error);
                ThrowIfStopping();
                return accepted;
            }
            finally { DisposeCommandTimeout(timeout); }
        }

        private void QueueNotification(
            Room room,
            string userId,
            SessionSnapshot original,
            string header,
            string text,
            bool stopNotice = false)
        {
            QueueNotificationCore(room, userId, original, header, text, stopNotice, null, null);
        }

        private void QueueNotificationCore(Room room, string userId, SessionSnapshot original,
            string header, string text, bool stopNotice, object operation, RoomState? expectedState,
            BarrierStage? expectedStage = null)
        {
            // All captured identities are immutable; selection is repeated for
            // each message, after queueing, rather than reusing poll authority.
            string sessionId = original.SessionId;
            string itemId = original.ItemId;
            string adminUserId = room.AdminUserId;
            _notifications.TryEnqueueFresh((token, isFresh) =>
            {
                if (!isFresh() || token.IsCancellationRequested || _stopRequested) return;
                DateTimeOffset now;
                SessionSnapshot target;
                using (var access = _roomManager.TryEnterRoom(room.Id))
                {
                    if (access == null || !ReferenceEquals(access.Room, room) ||
                        access.Runtime.SnapshotUnavailable || token.IsCancellationRequested ||
                        !isFresh() || _stopRequested ||
                        !string.Equals(room.ServerId, _serverIdProvider(), StringComparison.OrdinalIgnoreCase) ||
                        !room.JoinedParticipantUserIds.Contains(userId, StringComparer.OrdinalIgnoreCase)) return;

                    var options = GetOptionsSnapshot();
                    if (stopNotice ? !options.NotifyOtherOnPlaybackStop : !options.NotifyOnSyncActions) return;
                    var runtime = access.Runtime;
                    if (operation != null && (!ReferenceEquals(runtime.NotificationOperation, operation) ||
                        runtime.State != expectedState ||
                        (expectedStage.HasValue && runtime.Barrier?.Stage != expectedStage.Value))) return;
                    // Gate contention can outlive a session/item or capability
                    // change. Sample only after obtaining current room authority.
                    var candidates = _snapshotProvider.GetSessionSnapshots();
                    if (!isFresh() || token.IsCancellationRequested || _stopRequested) return;
                    now = _clock();
                    var selected = SessionSelector.SelectWithPreviousDiagnostics(
                        candidates, room.JoinedParticipantUserIds, now,
                        SessionSelector.StaleSessionTimeoutSeconds, runtime.Previous,
                        runtime.State == RoomState.Watching || runtime.State == RoomState.Handoff).Selected;
                    if (!selected.TryGetValue(userId, out target) || target == null || !target.Online ||
                        !string.Equals(target.UserId, userId, StringComparison.OrdinalIgnoreCase) ||
                        !HasSameIdentity(sessionId, itemId, target) ||
                        target.Capabilities?.CanDisplayMessage != true || !isFresh() ||
                        token.IsCancellationRequested || _stopRequested) return;
                }

                if (!isFresh() || token.IsCancellationRequested || _stopRequested) return;
                TryIssueMessage(room.Id, adminUserId, userId, target, header, text,
                    NotificationTimeoutMs, now, token, out _);
            });
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
            CancellationToken stopToken,
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

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopToken))
            {
                timeout.CancelAfter(ExternalCallTimeout);
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
            ThrowIfStopping();
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
            ThrowIfStopping();

            if (!ok)
            {
                failure = $"{command} command failed: {error}";
                runtime.RecordDiagnosticEvent(
                    "command_failed", userId, command, "failed", positionTicks, null, now);
                _logger?.Warn(
                    $"Room {room.Id}: immediate-issue-failure command={command}, targetUser={userId}, " +
                    $"failureDetailPresent={HasFailureDetail(error)}");
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
            runtime.RecordDiagnosticAction(
                "command_issued", userId, command, "pending", positionTicks, null, now);
            runtime.RecordDiagnosticEvent(
                "command_issued", userId, command, "pending", positionTicks, null, now);
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
            bool? primaryPausedOverride = null,
            bool fromMediaHandoff = false,
            bool fromAutomaticDriftRepair = false,
            double? automaticDriftSeconds = null)
        {
            string anchorUser = anchorUserId ?? room.PrimaryUserId;
            var anchor = snapshots[anchorUser];
            runtime.ClearMediaHandoff();
            runtime.ClearDriftTelemetry(clearAutoRepairHistory: false);
            runtime.State = RoomState.Barrier;
            runtime.Error = null;
            runtime.NotificationOperation = new object();
            runtime.RetryNotificationSent = false;
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
                FromMediaHandoff = fromMediaHandoff,
                FromAutomaticDriftRepair = fromAutomaticDriftRepair,
                AutomaticDriftSeconds = automaticDriftSeconds,
            };
            runtime.SyncItemId = anchor.ItemId;
            runtime.RecordDiagnosticEvent(
                "barrier_started", anchorUser, null, "entered", anchor.PositionTicks, null, now);
            if (fromMediaHandoff)
            {
                runtime.RecordDiagnosticEvent(
                    "handoff_barrier_started", null, RemoteCommands.PlayItem, "started", null, null, now);
            }
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
                RecordAutomaticDriftRepairFailure(runtime, now);
                runtime.ResetToWaiting();
                return;
            }

            switch (barrier.Stage)
            {
                case BarrierStage.Pause:
                    if (!barrier.PauseSent)
                    {
                        bool waitingForCommand = false;
                        bool allIssued = true;
                        string issueFailure = null;
                        foreach (var user in members)
                        {
                            if (snapshots[user].IsPaused && !runtime.Pending.ContainsKey(user)) continue;
                            waitingForCommand = true;
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
                        // A command sent against this snapshot still needs a new
                        // poll acknowledgement, even if the transport returned.
                        if (waitingForCommand) return;
                    }

                    if (runtime.Pending.Count == 0 && members.All(u => snapshots[u].IsPaused))
                    {
                        // The primary may continue playing until Emby applies the
                        // pause command. Re-anchor after both acknowledgements so
                        // the secondary does not seek to the stale start snapshot.
                        barrier.PrimaryPositionTicks = snapshots[barrier.AnchorUserId].PositionTicks;
                        barrier.Stage = BarrierStage.Seek;
                        barrier.StartedAtUtc = now;
                        EnsureSeekRetryDeadline(barrier, now);
                        RememberBarrierSnapshots(runtime, snapshots, now);
                        runtime.RecordDiagnosticEvent(
                            "barrier_stage_changed", barrier.AnchorUserId, null, "changed",
                            barrier.PrimaryPositionTicks, null, now);
                        goto case BarrierStage.Seek;
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

                    if (anchorReached && targetReached && !anyUnpaused && runtime.Pending.Count == 0)
                    {
                        ClearAnchorPositionCandidate(barrier);
                        barrier.Stage = BarrierStage.Restore;
                        barrier.StartedAtUtc = now;
                        runtime.RecordDiagnosticEvent(
                            "barrier_stage_changed", barrier.AnchorUserId, null, "changed",
                            barrier.PrimaryPositionTicks, null, now);
                        goto case BarrierStage.Restore;
                    }

                    if (IsSeekRetryBudgetExpired(barrier, now))
                    {
                        FailSeekBarrier(runtime, room.Id, now);
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

                    if (anchorReached && targetReached && runtime.Pending.Count == 0)
                    {
                        ClearAnchorPositionCandidate(barrier);
                        barrier.Stage = BarrierStage.Restore;
                        barrier.StartedAtUtc = now;
                        runtime.RecordDiagnosticEvent(
                            "barrier_stage_changed", barrier.AnchorUserId, null, "changed",
                            barrier.PrimaryPositionTicks, null, now);
                        goto case BarrierStage.Restore;
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
                        NotifyAutomaticBarrierRetry(runtime, room, snapshots, now);
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
                        bool waitingForCommand = false;
                        bool allIssued = true;
                        string issueFailure = null;
                        foreach (var user in members)
                        {
                            if (snapshots[user].IsPaused == barrier.PrimaryPaused &&
                                !runtime.Pending.ContainsKey(user)) continue;
                            waitingForCommand = true;
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
                        if (waitingForCommand) return;
                    }

                    bool desired = barrier.PrimaryPaused;
                    if (runtime.Pending.Count == 0 && members.All(u => snapshots[u].IsPaused == desired))
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
                    RecordWaitingPauseAttempt(runtime, pair.Key, pair.Value, now);
                    continue;
                }

                RecordWaitingPauseFailure(runtime, pair.Key, pair.Value, now);
            }
        }

        private static void RecordWaitingPauseAttempt(
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
            bool completedAutomaticDriftRepair = barrier?.FromAutomaticDriftRepair == true;
            double? automaticDriftSeconds = barrier?.AutomaticDriftSeconds;
            runtime.ClearTransientSessionRecovery();
            runtime.ClearRemoteControlRecovery();
            runtime.ClearDriftTelemetry(clearAutoRepairHistory: false);
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
            runtime.RecordDiagnosticEvent(
                "entered_watching", null, null, "entered", barrier.PrimaryPositionTicks, null, now);
            if (barrier.FromMediaHandoff)
            {
                runtime.RecordDiagnosticEvent(
                    "handoff_completed", null, RemoteCommands.PlayItem, "completed", null, null, now);
            }
            if (completedAutomaticDriftRepair)
            {
                runtime.RecordDriftDiagnosticEvent(
                    "drift_auto_repair_completed",
                    "completed",
                    automaticDriftSeconds.GetValueOrDefault(),
                    now);
            }
            NotifyParticipants(
                room,
                snapshots,
                "同步已完成，可以继续观看",
                now,
                includeAll: true,
                operation: runtime.NotificationOperation,
                expectedState: RoomState.Watching);
            _logger?.Info(
                $"Room {room.Id}: entered Watching, members={string.Join(",", snapshots.Keys)}, " +
                $"primaryPaused={barrier.PrimaryPaused}");
        }

        private static bool TryHoldRemoteControlRecovery(
            RoomRuntime runtime,
            Room room,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            RoomEligibilityEvaluation eligibility,
            DateTimeOffset now)
        {
            if (runtime == null || room == null || snapshots == null ||
                runtime.State != RoomState.Watching ||
                eligibility == null ||
                eligibility.IsEligible ||
                eligibility.FailureReason != RoomEligibilityFailureReason.RemoteControlUnsupportedOrMismatch)
            {
                runtime?.ClearRemoteControlRecovery();
                return false;
            }

            // A command already in flight must be handled by the normal
            // acknowledgement/retry path; do not hide it behind this grace
            // period.
            if (runtime.Pending.Count != 0)
            {
                runtime.ClearRemoteControlRecovery();
                return false;
            }

            if (!TryBuildRemoteControlRecoverySignature(
                runtime, room, snapshots, out var signature, out var affectedUserIds))
            {
                runtime.ClearRemoteControlRecovery();
                return false;
            }

            if (!runtime.RemoteControlRecoveryStartedAtUtc.HasValue)
            {
                runtime.StartRemoteControlRecovery(now, signature, affectedUserIds);
                return true;
            }

            if (!string.Equals(
                runtime.RemoteControlRecoverySignature,
                signature,
                StringComparison.Ordinal))
            {
                runtime.ClearRemoteControlRecovery();
                return false;
            }

            if ((now - runtime.RemoteControlRecoveryStartedAtUtc.Value).TotalSeconds >=
                SyncConstants.RemoteControlRecoveryGraceSeconds)
            {
                runtime.ClearRemoteControlRecovery();
                return false;
            }

            return true;
        }

        private static bool TryBuildRemoteControlRecoverySignature(
            RoomRuntime runtime,
            Room room,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            out string signature,
            out IReadOnlyList<string> affectedUserIds)
        {
            signature = null;
            affectedUserIds = Array.Empty<string>();
            var members = room.JoinedParticipantUserIds;
            if (members == null || members.Count != 2 ||
                snapshots.Count != members.Count ||
                runtime.Previous.Count != members.Count ||
                string.IsNullOrEmpty(runtime.SyncItemId))
            {
                return false;
            }

            var affectedUsers = new List<string>();
            var identityParts = new List<string>();
            var currentValues = new List<SessionSnapshot>();
            foreach (var userId in members.OrderBy(user => user, StringComparer.OrdinalIgnoreCase))
            {
                if (!snapshots.TryGetValue(userId, out var current) || current == null ||
                    !runtime.Previous.TryGetValue(userId, out var previous) || previous == null ||
                    !current.Online || current.Stopped ||
                    string.IsNullOrEmpty(current.ItemId) ||
                    !string.Equals(current.ItemId, runtime.SyncItemId, StringComparison.OrdinalIgnoreCase) ||
                    !HasSameIdentity(previous.SessionId, previous.ItemId, current) ||
                    current.Capabilities?.SupportsRemoteControl != true ||
                    Math.Abs(current.PlaybackRate - 1.0) > SyncConstants.PlaybackRateTolerance ||
                    current.RunTimeTicks <= 0)
                {
                    return false;
                }

                currentValues.Add(current);
                if (previous.SupportsRemoteControl && !current.SupportsRemoteControl)
                {
                    affectedUsers.Add(userId);
                }

                identityParts.Add(
                    userId + "=" + current.SessionId + ":" + current.ItemId);
            }

            if (affectedUsers.Count == 0 ||
                Math.Abs(currentValues[0].RunTimeTicks - currentValues[1].RunTimeTicks) >
                    SyncConstants.MaxRuntimeDifferenceTicks)
            {
                return false;
            }

            signature = string.Join(
                "|",
                identityParts) +
                ";affected=" + string.Join(",", affectedUsers.OrderBy(user => user, StringComparer.OrdinalIgnoreCase));
            affectedUserIds = affectedUsers;
            return true;
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
                        suppressSeek = suppressed.Command == RemoteCommands.Seek ||
                                       suppressPause;
                        runtime.Suppressed.Remove(user);
                    }
                }

                bool alreadyPendingPause = pending != null &&
                    (pending.Command == RemoteCommands.Pause || pending.Command == RemoteCommands.Unpause) &&
                    IsCurrentCommandIdentity(
                        runtime,
                        true,
                        user,
                        pending.SessionId,
                        pending.ItemId,
                        current) &&
                    PendingMatcher.Matches(pending, current);
                if (alreadyPendingPause)
                {
                    // A remote pause/resume acknowledgement may expose a
                    // large position movement in the same snapshot. It is
                    // still the command landing, not a local seek.
                    suppressSeek = true;
                }

                if (current.IsPaused != old.IsPaused)
                {
                    if (!suppressPause && !alreadyPendingPause)
                    {
                        pauseChanges.Add((user, current.IsPaused));
                        runtime.RecordDiagnosticEvent(
                            "manual_action", user,
                            current.IsPaused ? RemoteCommands.Pause : RemoteCommands.Unpause,
                            "observed", current.PositionTicks, null, now);
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
                        runtime.RecordDiagnosticEvent(
                            "manual_action", user, RemoteCommands.Seek, "observed",
                            current.PositionTicks, null, now);
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
                BarrierTick(runtime, room, snapshots, now);
                // A seek cycle reports its result once, instead of each stage.
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
                // Pause/resume control is unchanged; routine stage toasts are omitted.
            }

            if (seekChanges.Count > 0 || pauseChanges.Count > 0)
            {
                runtime.CurrentDriftSeconds = null;
                runtime.DriftAboveRepairThresholdSinceUtc = null;
            }
            else if (TryObserveDriftAndMaybeRepair(runtime, room, snapshots, now))
            {
                return;
            }

            AlignPausedPeers(runtime, room, snapshots, now);

            runtime.Previous.Clear();
            foreach (var pair in snapshots)
            {
                runtime.Previous[pair.Key] = pair.Value;
            }

            runtime.PreviousAtUtc = now;
        }

        private bool TryObserveDriftAndMaybeRepair(
            RoomRuntime runtime,
            Room room,
            IReadOnlyDictionary<string, SessionSnapshot> snapshots,
            DateTimeOffset now)
        {
            if (runtime == null || room == null || snapshots == null ||
                runtime.State != RoomState.Watching ||
                runtime.Barrier != null ||
                runtime.Handoff != null ||
                runtime.Pending.Count != 0 ||
                runtime.Suppressed.Count != 0 ||
                runtime.Recovery != null ||
                room.JoinedParticipantUserIds == null ||
                room.JoinedParticipantUserIds.Count != 2)
            {
                if (runtime != null)
                {
                    runtime.DriftAboveRepairThresholdSinceUtc = null;
                    runtime.CurrentDriftSeconds = null;
                }
                return false;
            }

            string primaryUserId = room.PrimaryUserId;
            string participantUserId = room.JoinedParticipantUserIds.FirstOrDefault(
                userId => !string.Equals(userId, primaryUserId, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(participantUserId) ||
                !snapshots.TryGetValue(primaryUserId, out var primary) ||
                !snapshots.TryGetValue(participantUserId, out var participant) ||
                primary == null ||
                participant == null ||
                !primary.Online ||
                !participant.Online ||
                primary.Stopped ||
                participant.Stopped ||
                primary.IsPaused ||
                participant.IsPaused ||
                Math.Abs(primary.PlaybackRate - 1.0) > SyncConstants.PlaybackRateTolerance ||
                Math.Abs(participant.PlaybackRate - 1.0) > SyncConstants.PlaybackRateTolerance ||
                !string.Equals(primary.ItemId, participant.ItemId, StringComparison.OrdinalIgnoreCase) ||
                primary.RunTimeTicks <= 0 ||
                participant.RunTimeTicks <= 0)
            {
                runtime.DriftAboveRepairThresholdSinceUtc = null;
                runtime.CurrentDriftSeconds = null;
                return false;
            }

            double previousAbsoluteDrift = runtime.CurrentDriftSeconds.HasValue
                ? Math.Abs(runtime.CurrentDriftSeconds.Value)
                : 0;
            double driftSeconds =
                (participant.PositionTicks - primary.PositionTicks) /
                (double)SessionSnapshot.TicksPerSecond;
            double absoluteDriftSeconds = Math.Abs(driftSeconds);
            runtime.CurrentDriftSeconds = driftSeconds;
            runtime.MaxObservedAbsoluteDriftSeconds = Math.Max(
                runtime.MaxObservedAbsoluteDriftSeconds,
                absoluteDriftSeconds);

            if (previousAbsoluteDrift < SyncConstants.DriftObserveThresholdSeconds &&
                absoluteDriftSeconds >= SyncConstants.DriftObserveThresholdSeconds)
            {
                runtime.RecordDriftDiagnosticEvent(
                    "drift_threshold_entered", "entered", driftSeconds, now);
            }
            else if (previousAbsoluteDrift >= SyncConstants.DriftObserveThresholdSeconds &&
                     absoluteDriftSeconds < SyncConstants.DriftObserveThresholdSeconds)
            {
                runtime.RecordDriftDiagnosticEvent(
                    "drift_threshold_cleared", "cleared", driftSeconds, now);
            }

            if (absoluteDriftSeconds < SyncConstants.DriftRepairThresholdSeconds)
            {
                runtime.DriftAboveRepairThresholdSinceUtc = null;
                return false;
            }

            if (runtime.LastAutoRepairAtUtc.HasValue &&
                (now - runtime.LastAutoRepairAtUtc.Value).TotalSeconds <
                    SyncConstants.DriftRepairCooldownSeconds)
            {
                runtime.DriftAboveRepairThresholdSinceUtc = null;
                return false;
            }

            if (!runtime.DriftAboveRepairThresholdSinceUtc.HasValue)
            {
                runtime.DriftAboveRepairThresholdSinceUtc = now;
                return false;
            }

            if ((now - runtime.DriftAboveRepairThresholdSinceUtc.Value).TotalSeconds <
                SyncConstants.DriftRepairHoldSeconds)
            {
                return false;
            }

            runtime.DriftAboveRepairThresholdSinceUtc = null;
            runtime.RecordAutoRepairStarted(now, driftSeconds);
            runtime.RecordDriftDiagnosticEvent(
                "drift_auto_repair_started", "started", driftSeconds, now);
            StartBarrier(
                runtime,
                room,
                snapshots,
                now,
                fromAutomaticDriftRepair: true,
                automaticDriftSeconds: driftSeconds);
            BarrierTick(runtime, room, snapshots, now);
            return true;
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

define(['baseView', 'dom', 'loading', 'globalize', 'emby-input', 'emby-select', 'emby-button', 'mainTabsManager'], function (BaseView, dom, loading, globalize, embyInput, embySelect, embyButton, mainTabsManager) {
    'use strict';

    var pluginId = '0f8d1c2e-3b4a-4c5d-8e6f-7a8b9c0d1e2f';
    var stateLabels = {
        Waiting: '等待参与者',
        Barrier: '正在对齐',
        Watching: '同步中',
        Handoff: '正在同步视频',
        Recovering: '正在等待临时掉线的参与者恢复',
        Unavailable: '暂不可用'
    };
    var stateDescriptions = {
        Waiting: '等待两位参与者打开同一视频',
        Barrier: '正在对齐两位参与者的播放位置',
        Watching: '两位参与者已连接，播放会自动同步',
        Handoff: '正在让另一位参与者打开主用户当前视频',
        Recovering: '正在等待临时掉线的参与者恢复',
        Unavailable: '当前房间暂时无法使用，请刷新后重试'
    };
    var actionLabels = {
        pause: '暂停播放',
        resume: '继续播放',
        resync: '重新同步',
        participantResync: '请求重新同步'
    };

    var maxDiagnosticEvents = 50;

    function apiUrl(path) {
        return ApiClient.getUrl(path);
    }

    function pageIdentity() {
        try {
            return {
                user: typeof ApiClient.getCurrentUserId === 'function' ? String(ApiClient.getCurrentUserId() || '') : '',
                server: String(ApiClient.serverAddress() || ''),
                serverId: typeof ApiClient.serverId === 'function' ? String(ApiClient.serverId() || '') : '',
                client: ApiClient
            };
        } catch (error) {
            return null;
        }
    }

    function clearPageData(page) {
        releaseNativeTabs(page);
        clearInterval(page._wtTimer);
        clearAllRoomFeedback(page);
        clearTimeout(page._wtStatusTimer);
        Object.keys(page._wtStateExpiryTimers || {}).forEach(function (roomId) {
            clearRoomStateExpiry(page, roomId);
        });
        page._wtStateExpiryTimers = {};
        page._wtStatusTimer = null;
        page._wtUsers = [];
        page._wtRooms = null;
        page._wtInvitations = [];
        page._wtInvitationsLoaded = false;
        page._wtInvitationCode = null;
        page._wtInvitationCopied = false;
        page._wtPendingCreatedFeedback = null;
        setCreateStatus(page, '', false);
        // Retired request continuations cannot reset controls in a new lifecycle.
        ['#wtCreate', '#wtCreateInvitation', '#wtAcceptInvitation'].forEach(function (selector) {
            setButtonBusy(page.querySelector(selector), false);
        });
        Object.keys(page._wtRoomViews || {}).forEach(function (roomId) {
            clearRoomBackdrop(page._wtRoomViews[roomId]);
            clearRoomAvatars(page._wtRoomViews[roomId]);
        });
        page._wtAvatarFailures = {};
        page._wtRoomViews = {};
        page._wtRoomStates = {};
        page._wtRoomSequences = {};
        page._wtMedia = {};
        page._wtMediaFlights = {};
        page._wtMetadataFlights = {};
        page._wtMediaTargets = {};
        page._wtResources = {};
        page._wtDiagnostics = {};
        page._wtDiagnosticPanels = {};
        page._wtDiagnosticRequests = {};
        page._wtDiagnosticBusy = {};
        page._wtDiagnosticOpen = {};
        page._wtRoomBusy = {};
        page._wtRoomsLoading = false;
        page._wtRoomsSignature = null;
        page._wtIsAdmin = undefined;
        page._wtTabsPermissionReady = false;
        page._wtConfigReady = false;
        page._wtPluginConfiguration = null;
        page._wtGitHubTokenReady = false;
        page._wtGitHubTokenCanClear = false;
        page._wtNativeTabsKey = null;
        clearGitHubTokenInput(page);
        ['#wtRoomName', '#wtInvitationName', '#wtInvitationAcceptCode'].forEach(function (selector) {
            var input = page.querySelector(selector);
            if (input) input.value = '';
        });
        ['#wtUserA', '#wtUserB', '#wtPrimary', '#wtRooms', '#wtInvitations'].forEach(function (selector) {
            clearChildren(page.querySelector(selector));
        });
        renderInvitationCode(page);
        setAdminVisibility(page, false);
        setConfigBusy(page, true);
        setGitHubTokenBusy(page, true, 'read');
    }

    function pageActionAllowed(page) {
        if (!page._wtActive) return false;
        var current = pageIdentity();
        var owner = page._wtIdentity;
        if (current && owner && current.user && current.user.toLowerCase() === owner.user.toLowerCase() &&
            current.server === owner.server && current.serverId === owner.serverId && current.client === owner.client) return true;
        page._wtActive = false;
        page._wtGeneration += 1;
        clearPageData(page);
        loading.hide();
        setStatus(page, '账号或服务器已变化，请重新打开一起看。', true);
        return false;
    }

    // Guard every continuation, including catch/finally and chained mutations.
    // A retired generation never completes into the next page lifecycle.
    function scopedPromise(page, promise, generation) {
        generation = generation === undefined ? page._wtGeneration : generation;
        function guard(callback) {
            return function (value) {
                if (!pageActionAllowed(page) || page._wtGeneration !== generation) return new Promise(function () {});
                return callback(value);
            };
        }
        return {
            then: function (fulfilled, rejected) {
                return scopedPromise(page, Promise.resolve(promise).then(
                    guard(typeof fulfilled === 'function' ? fulfilled : function (value) { return value; }),
                    guard(typeof rejected === 'function' ? rejected : function (error) { throw error; })), generation);
            },
            catch: function (rejected) { return this.then(null, rejected); },
            finally: function (callback) {
                return this.then(function (value) { callback(); return value; }, function (error) { callback(); throw error; });
            }
        };
    }

    function pageRequest(page, key, request) {
        if (!pageActionAllowed(page)) return scopedPromise(page, new Promise(function () {}));
        var resources = page._wtResources;
        if (resources[key]) return resources[key];
        var generation = page._wtGeneration;
        var promise = Promise.resolve().then(function () {
            if (!pageActionAllowed(page) || page._wtGeneration !== generation) return new Promise(function () {});
            return request();
        });
        resources[key] = scopedPromise(page, promise, generation);
        function release() { delete resources[key]; }
        promise.then(release, release);
        return resources[key];
    }

    function apiGet(page, path) {
        return pageRequest(page, 'GET:' + path, function () { return ApiClient.getJSON(apiUrl(path)); });
    }

    function apiSend(page, path, method, body) {
        return pageRequest(page, method + ':' + path, function () { return ApiClient.fetch({
            url: apiUrl(path),
            type: method,
            data: body === undefined ? null : JSON.stringify(body),
            contentType: 'application/json',
            dataType: 'json'
        }); });
    }

    function errorMessage(error) {
        var status = error && (error.status || error.statusCode);
        if (status === 401 || status === 403) {
            return '权限不足，请使用管理员账号重试。';
        }
        if (status >= 500) {
            return '服务器暂时不可用，请稍后重试。';
        }
        if (error && error.name === 'TypeError') {
            return '网络连接失败，请稍后重试。';
        }
        return '操作未完成，请稍后重试。';
    }

    var diagnosticStateLabels = {
        Waiting: '等待参与者',
        Barrier: '正在对齐',
        Watching: '同步中',
        Handoff: '正在同步视频',
        Recovering: '临时掉线恢复中',
        Unavailable: '暂不可用'
    };
    var diagnosticReasonLabels = {
        server_unavailable: '服务器暂时不可用',
        snapshot_unavailable: '播放会话暂时不可读',
        snapshot_stale: '播放会话数据已过期',
        different_video: '两位参与者打开了不同视频',
        playback_stopped: '播放已停止，等待重新打开视频',
        action_conflict: '检测到手动操作冲突',
        barrier_retry_exhausted: '对齐重试次数已用尽',
        waiting_pause_retry_limit: '等待暂停重试次数已用尽',
        media_handoff: '正在让参与者打开主用户当前视频',
        handoff_failed: '媒体切换同步未完成',
        participant_resync_failed: '参与者重新同步未完成',
        play_item_timeout: '打开视频确认超时',
        command_timeout: '远程控制超时',
        session_offline: '目标会话已离线',
        invalid_argument: '媒体切换参数无效',
        command_failed: '播放控制未完成',
        aligning: '正在对齐播放位置',
        watching: '两位参与者已连接',
        remote_control_unavailable: '当前客户端不支持远程控制',
        unsupported_playback_rate: '播放速度不是 1 倍',
        waiting_for_playback: '等待双方打开同一视频并开始播放',
        transient_recovery: '正在等待临时掉线的参与者恢复'
    };
    var diagnosticHealthLabels = {
        fresh: '新鲜',
        stale: '过期',
        unavailable: '不可用',
        unknown: '未知'
    };
    var diagnosticResultLabels = {
        success: '成功',
        failed: '失败',
        retry_scheduled: '已安排重试',
        pending: '等待确认',
        stopped: '已停止',
        entered: '已进入',
        recovered: '已恢复',
        changed: '已变化',
        cleared: '已清除',
        older_activity: '活动时间较旧',
        observed: '已记录'
    };
    var diagnosticBarrierStageLabels = {
        Pause: '暂停阶段',
        Seek: '定位阶段',
        Restore: '恢复播放阶段'
    };
    var diagnosticCommandLabels = {
        Pause: '暂停',
        Unpause: '继续',
        PlayPause: '播放/暂停',
        Seek: '定位',
        Stop: '停止',
        PlayItem: '打开视频',
        DisplayMessage: '提示'
    };
    var diagnosticEventLabels = {
        barrier_started: '开始对齐',
        barrier_stage_changed: '对齐阶段变化',
        entered_watching: '进入同步中',
        command_issued: '发送控制',
        command_acknowledged: '控制已确认',
        command_failed: '控制失败',
        retry_scheduled: '安排重试',
        stop_confirmed: '确认停止',
        snapshot_protection_entered: '进入保护状态',
        snapshot_protection_recovered: '保护状态恢复',
        snapshot_ignored: '忽略过期快照',
        eligibility_changed: '同步条件变化',
        manual_action: '手动操作',
        resync: '重新同步',
        resync_barrier_started: '重新同步开始对齐',
        handoff_started: '开始媒体切换',
        handoff_play_requested: '请求打开视频',
        handoff_target_confirmed: '目标视频已确认',
        handoff_barrier_started: '媒体切换开始对齐',
        handoff_completed: '媒体切换完成',
        handoff_failed: '媒体切换失败',
        handoff_superseded: '媒体切换目标变化',
        primary_item_changed: '主用户视频变化',
        recovery_started: '开始临时掉线恢复',
        recovery_confirmed: '临时掉线恢复成功',
        recovery_timed_out: '临时掉线恢复超时',
        recovery_session_changed: '恢复会话发生变化',
        recovery_item_changed: '恢复视频发生变化',
        drift_threshold_entered: '进入漂移阈值',
        drift_threshold_cleared: '漂移阈值已清除',
        drift_auto_repair_started: '开始漂移自动纠偏',
        drift_auto_repair_completed: '漂移自动纠偏完成',
        drift_auto_repair_failed: '漂移自动纠偏失败',
        observed: '观察事件'
    };

    var diagnosticHandoffErrorLabels = {
        command_timeout: '远程控制超时',
        command_failed: '远程控制失败',
        remote_control_unsupported: '当前客户端不支持远程控制',
        session_offline: '目标会话已离线',
        invalid_argument: '媒体切换参数无效',
        play_item_timeout: '打开视频确认超时',
        handoff_failed: '媒体切换失败'
    };

    function diagnosticFiniteNumber(value) {
        return typeof value === 'number' && isFinite(value) ? value : null;
    }

    function diagnosticAlias(value) {
        return value === 'userA' || value === 'userB' ? value : 'unknown';
    }

    function diagnosticCommand(value) {
        var names = Object.keys(diagnosticCommandLabels);
        for (var i = 0; i < names.length; i++) {
            if (String(value || '').toLowerCase() === names[i].toLowerCase()) {
                return names[i];
            }
        }
        return null;
    }

    function diagnosticResult(value) {
        return diagnosticResultLabels[value] ? value : null;
    }

    function diagnosticBarrierStage(value) {
        return diagnosticBarrierStageLabels[value] ? value : null;
    }

    function diagnosticSecondsFromTicks(value) {
        var ticks = diagnosticFiniteNumber(value);
        return ticks === null ? null : Math.max(0, ticks / 10000000);
    }

    function sanitizeDiagnosticSession(session) {
        session = session || {};
        return {
            alias: diagnosticAlias(session.Alias),
            positionSeconds: diagnosticFiniteNumber(session.PositionSeconds),
            online: session.Online === true,
            paused: session.Paused === true,
            playbackRate: diagnosticFiniteNumber(session.PlaybackRate),
            runtimeSeconds: diagnosticFiniteNumber(session.RuntimeSeconds),
            lastActivityAgeSeconds: diagnosticFiniteNumber(session.LastActivityAgeSeconds),
            ackLatencySeconds: diagnosticFiniteNumber(session.AckLatencySeconds),
            reportedRemoteControl: session.ReportedSupportsRemoteControl === true,
            effectiveRemoteControl: session.EffectiveSupportsRemoteControl === true,
            canPause: session.CanPause === true,
            canUnpause: session.CanUnpause === true,
            canSeek: session.CanSeek === true,
            canDisplayMessage: session.CanDisplayMessage === true,
            supportedCommands: (Array.isArray(session.SupportedCommandNames) ? session.SupportedCommandNames : [])
                .map(diagnosticCommand).filter(function (command, index, commands) {
                    return command && commands.indexOf(command) === index;
                })
        };
    }

    function sanitizeDiagnosticEvent(event) {
        event = event || {};
        return {
            type: diagnosticEventLabels[event.Type] ? event.Type : 'observed',
            command: diagnosticCommand(event.Command),
            alias: diagnosticAlias(event.Alias),
            result: diagnosticResult(event.Result),
            positionSeconds: event.PositionTicks === null || event.PositionTicks === undefined
                ? null : diagnosticSecondsFromTicks(event.PositionTicks),
            latencySeconds: diagnosticFiniteNumber(event.LatencySeconds),
            driftSeconds: diagnosticFiniteNumber(event.DriftSeconds),
            atUtc: typeof event.AtUtc === 'string' ? event.AtUtc : null
        };
    }

    function sanitizeDiagnostic(raw) {
        if (!raw || typeof raw !== 'object') {
            return null;
        }
        var participants = Array.isArray(raw.Participants) ? raw.Participants.slice(0, 2).map(function (participant) {
            participant = participant || {};
            return {
                alias: diagnosticAlias(participant.Alias),
                primary: participant.IsPrimary === true,
                joined: participant.Joined === true
            };
        }) : [];
        var sessions = Array.isArray(raw.Sessions) ? raw.Sessions.slice(0, 2).map(sanitizeDiagnosticSession) : [];
        var pending = Array.isArray(raw.Pending) ? raw.Pending.slice(0, 2).map(function (item) {
            item = item || {};
            return {
                alias: diagnosticAlias(item.Alias),
                command: diagnosticCommand(item.Command),
                positionSeconds: item.PositionTicks === null || item.PositionTicks === undefined
                    ? null : diagnosticSecondsFromTicks(item.PositionTicks),
                ageSeconds: diagnosticFiniteNumber(item.AgeSeconds),
                retries: diagnosticFiniteNumber(item.Retries)
            };
        }) : [];
        var barrier = raw.Barrier && typeof raw.Barrier === 'object' ? {
            stage: diagnosticBarrierStage(raw.Barrier.Stage),
            anchorAlias: diagnosticAlias(raw.Barrier.AnchorAlias),
            targetPositionSeconds: diagnosticFiniteNumber(raw.Barrier.TargetPositionSeconds),
            ageSeconds: diagnosticFiniteNumber(raw.Barrier.AgeSeconds),
            pauseSent: raw.Barrier.PauseSent === true,
            seekSent: raw.Barrier.SeekSent === true,
            restoreSent: raw.Barrier.RestoreSent === true,
            seekRetryPending: raw.Barrier.SeekRetryPending === true
        } : null;
        var handoff = raw.Handoff && typeof raw.Handoff === 'object' ? {
            targetItemHash: typeof raw.Handoff.TargetItemHash === 'string' ? raw.Handoff.TargetItemHash : null,
            sourceItemHash: typeof raw.Handoff.SourceItemHash === 'string' ? raw.Handoff.SourceItemHash : null,
            primaryAlias: diagnosticAlias(raw.Handoff.PrimaryAlias),
            participantAlias: diagnosticAlias(raw.Handoff.ParticipantAlias),
            playItemPending: raw.Handoff.PlayItemPending === true,
            retryCount: diagnosticFiniteNumber(raw.Handoff.RetryCount),
            generation: diagnosticFiniteNumber(raw.Handoff.Generation),
            ageSeconds: diagnosticFiniteNumber(raw.Handoff.AgeSeconds),
            lastError: diagnosticHandoffErrorLabels[raw.Handoff.LastError] ? raw.Handoff.LastError : null,
            participantResync: raw.Handoff.IsParticipantResync === true
        } : null;
        var recovery = raw.RecoveryWindow && typeof raw.RecoveryWindow === 'object' ? {
            active: raw.RecoveryWindow.Active === true,
            ageSeconds: diagnosticFiniteNumber(raw.RecoveryWindow.AgeSeconds),
            affectedAliases: (Array.isArray(raw.RecoveryWindow.AffectedAliases) ? raw.RecoveryWindow.AffectedAliases : [])
                .slice(0, 2).map(diagnosticAlias)
        } : { active: false, ageSeconds: null, affectedAliases: [] };
        var transientRecovery = raw.TransientRecovery && typeof raw.TransientRecovery === 'object' ? {
            active: raw.TransientRecovery.Active === true,
            ageSeconds: diagnosticFiniteNumber(raw.TransientRecovery.AgeSeconds),
            remainingSeconds: diagnosticFiniteNumber(raw.TransientRecovery.RemainingSeconds),
            missingAliases: (Array.isArray(raw.TransientRecovery.MissingAliases) ? raw.TransientRecovery.MissingAliases : [])
                .slice(0, 2).map(diagnosticAlias),
            participants: (Array.isArray(raw.TransientRecovery.Participants) ? raw.TransientRecovery.Participants : [])
                .slice(0, 2).map(function (participant) {
                    participant = participant || {};
                    return {
                        alias: diagnosticAlias(participant.Alias),
                        missing: participant.Missing === true,
                        expectedSessionHash: typeof participant.ExpectedSessionHash === 'string' ? participant.ExpectedSessionHash : null,
                        expectedItemHash: typeof participant.ExpectedItemHash === 'string' ? participant.ExpectedItemHash : null,
                        lastKnownPositionSeconds: diagnosticFiniteNumber(participant.LastKnownPositionSeconds),
                        lastKnownPaused: participant.LastKnownPaused === true
                    };
                })
        } : { active: false, ageSeconds: null, remainingSeconds: null, missingAliases: [], participants: [] };
        var drift = raw.Drift && typeof raw.Drift === 'object' ? {
            currentSeconds: diagnosticFiniteNumber(raw.Drift.CurrentDriftSeconds),
            maxAbsoluteSeconds: diagnosticFiniteNumber(raw.Drift.MaxObservedAbsoluteDriftSeconds),
            holdAgeSeconds: diagnosticFiniteNumber(raw.Drift.HoldAgeSeconds),
            lastRepairAtUtc: typeof raw.Drift.LastAutoRepairAtUtc === 'string' ? raw.Drift.LastAutoRepairAtUtc : null,
            repairCount: diagnosticFiniteNumber(raw.Drift.AutoRepairCount),
            lastRepairDriftSeconds: diagnosticFiniteNumber(raw.Drift.LastAutoRepairDriftSeconds),
            cooldownRemainingSeconds: diagnosticFiniteNumber(raw.Drift.CooldownRemainingSeconds)
        } : {
            currentSeconds: null, maxAbsoluteSeconds: null, holdAgeSeconds: null,
            lastRepairAtUtc: null, repairCount: null, lastRepairDriftSeconds: null,
            cooldownRemainingSeconds: null
        };
        return {
            schemaVersion: typeof raw.SchemaVersion === 'string' ? raw.SchemaVersion : null,
            pluginVersion: typeof raw.PluginVersion === 'string' ? raw.PluginVersion : null,
            roomState: diagnosticStateLabels[raw.RoomState] ? raw.RoomState : 'Unavailable',
            statusReason: diagnosticReasonLabels[raw.StatusReason] ? raw.StatusReason : null,
            snapshotHealth: diagnosticHealthLabels[raw.SnapshotHealth] ? raw.SnapshotHealth : 'unknown',
            participants: participants,
            sessions: sessions,
            pending: pending,
            handoff: handoff,
            barrier: barrier,
            recovery: recovery,
            transientRecovery: transientRecovery,
            drift: drift,
            lastAction: raw.LastAction && typeof raw.LastAction === 'object' ? sanitizeDiagnosticEvent(raw.LastAction) : null,
            lastError: diagnosticReasonLabels[raw.LastError] ? raw.LastError : null,
            events: (Array.isArray(raw.Events) ? raw.Events : [])
                .slice(-maxDiagnosticEvents)
                .reverse()
                .map(sanitizeDiagnosticEvent),
            generatedAtUtc: typeof raw.GeneratedAtUtc === 'string' ? raw.GeneratedAtUtc : null
        };
    }

    function diagnosticLabel(map, value, fallback) {
        return map[value] || fallback || '未知';
    }

    function diagnosticFormatNumber(value, suffix) {
        var number = diagnosticFiniteNumber(value);
        return number === null ? '—' : number.toFixed(1) + (suffix || '');
    }

    function diagnosticFormatPosition(value) {
        var seconds = diagnosticFiniteNumber(value);
        if (seconds === null) return '—';
        var total = Math.max(0, Math.floor(seconds));
        var minutes = Math.floor(total / 60);
        var remainingNumber = total % 60;
        var remaining = remainingNumber < 10 ? '0' + remainingNumber : String(remainingNumber);
        return minutes + ':' + remaining;
    }

    function diagnosticFormatTime(value) {
        if (!value) return '时间未知';
        var date = new Date(value);
        return isNaN(date.getTime()) ? '时间未知' : date.toLocaleString();
    }

    function diagnosticField(parent, label, value) {
        var field = document.createElement('div');
        var labelEl = document.createElement('span');
        labelEl.className = 'wt-diagnosticLabel';
        labelEl.textContent = label + '：';
        var valueEl = document.createElement('span');
        valueEl.className = 'wt-diagnosticValue';
        valueEl.textContent = value;
        field.appendChild(labelEl);
        field.appendChild(valueEl);
        parent.appendChild(field);
    }

    function diagnosticGroup(parent, title) {
        var group = document.createElement('section');
        group.className = 'wt-diagnosticGroup';
        var heading = document.createElement('h4');
        heading.textContent = title;
        group.appendChild(heading);
        parent.appendChild(group);
        return group;
    }

    function renderDiagnosticDetails(page, roomId, diagnostic, body) {
        clearChildren(body);
        var toolbar = document.createElement('div');
        toolbar.className = 'wt-diagnosticToolbar';
        var exportButton = document.createElement('button', { is: 'emby-button' });
        exportButton.type = 'button';
        exportButton.setAttribute('is', 'emby-button');
        exportButton.classList.add('raised', 'raised-outline');
        exportButton.dataset.action = 'exportDiagnostics';
        exportButton.textContent = '导出诊断 JSON';
        exportButton.addEventListener('click', function () {
            exportDiagnostic(page, roomId);
        });
        toolbar.appendChild(exportButton);
        var timestamp = document.createElement('span');
        timestamp.className = 'fieldDescription';
        timestamp.textContent = '读取时间：' + diagnosticFormatTime(diagnostic.generatedAtUtc);
        toolbar.appendChild(timestamp);
        body.appendChild(toolbar);

        var summary = document.createElement('div');
        summary.className = 'wt-diagnosticSummary';
        diagnosticField(summary, '状态', diagnosticLabel(diagnosticStateLabels, diagnostic.roomState, '暂不可用'));
        diagnosticField(summary, '状态原因', diagnosticLabel(diagnosticReasonLabels, diagnostic.statusReason, '当前状态需要检查'));
        diagnosticField(summary, '快照健康', diagnosticLabel(diagnosticHealthLabels, diagnostic.snapshotHealth, '未知'));
        diagnosticField(summary, '事件数量', String(diagnostic.events.length));
        body.appendChild(summary);

        var sessions = diagnosticGroup(body, '参与者会话');
        if (diagnostic.sessions.length === 0) {
            var noSessions = document.createElement('p');
            noSessions.className = 'fieldDescription';
            noSessions.textContent = '暂无可用会话。';
            sessions.appendChild(noSessions);
        } else {
            diagnostic.sessions.forEach(function (session) {
                var sessionSummary = document.createElement('div');
                sessionSummary.className = 'wt-diagnosticSummary';
                diagnosticField(sessionSummary, session.alias, (session.online ? '在线' : '离线') + '，位置 ' + diagnosticFormatPosition(session.positionSeconds));
                diagnosticField(sessionSummary, '播放状态', session.online ? (session.paused ? '已暂停' : '播放中') : '状态未知');
                diagnosticField(sessionSummary, '能力', '上报远控 ' + (session.reportedRemoteControl ? '支持' : '不支持') + '；有效远控 ' + (session.effectiveRemoteControl ? '支持' : '不支持'));
                diagnosticField(sessionSummary, '确认延迟', diagnosticFormatNumber(session.ackLatencySeconds, ' 秒'));
                diagnosticField(sessionSummary, '播放速率', diagnosticFormatNumber(session.playbackRate, ' 倍'));
                diagnosticField(sessionSummary, '可用控制', session.supportedCommands.length > 0 ? session.supportedCommands.map(function (command) {
                    return diagnosticCommandLabels[command];
                }).join('、') : '无');
                sessions.appendChild(sessionSummary);
            });
        }

        var pending = diagnosticGroup(body, '等待中的控制');
        if (diagnostic.pending.length === 0) {
            var noPending = document.createElement('p');
            noPending.className = 'fieldDescription';
            noPending.textContent = '当前没有等待确认的控制。';
            pending.appendChild(noPending);
        } else {
            var pendingList = document.createElement('ul');
            pendingList.className = 'wt-diagnosticList';
            diagnostic.pending.forEach(function (item) {
                var pendingItem = document.createElement('li');
                pendingItem.textContent = item.alias + '：' + (diagnosticCommandLabels[item.command] || '未知控制') +
                    '，位置 ' + diagnosticFormatPosition(item.positionSeconds) +
                    '，等待 ' + diagnosticFormatNumber(item.ageSeconds, ' 秒') +
                    '，重试 ' + (item.retries === null ? '—' : String(Math.max(0, Math.floor(item.retries))) + ' 次');
                pendingList.appendChild(pendingItem);
            });
            pending.appendChild(pendingList);
        }

        if (diagnostic.handoff) {
            var handoffGroup = diagnosticGroup(body, '媒体切换');
            var handoffSummary = document.createElement('div');
            handoffSummary.className = 'wt-diagnosticSummary';
            diagnosticField(handoffSummary, '类型', diagnostic.handoff.participantResync ? '参与者重新同步' : '主用户切换媒体');
            diagnosticField(handoffSummary, '目标视频标识', diagnostic.handoff.targetItemHash || '—');
            diagnosticField(handoffSummary, '来源视频标识', diagnostic.handoff.sourceItemHash || '—');
            diagnosticField(handoffSummary, '参与者', diagnostic.handoff.participantAlias);
            diagnosticField(handoffSummary, '控制状态', diagnostic.handoff.playItemPending ? '等待打开视频确认' : '准备发送');
            diagnosticField(handoffSummary, '重试次数', diagnostic.handoff.retryCount === null ? '—' : String(Math.max(0, Math.floor(diagnostic.handoff.retryCount))));
            diagnosticField(handoffSummary, '操作代际', diagnostic.handoff.generation === null ? '—' : String(Math.max(0, Math.floor(diagnostic.handoff.generation))));
            diagnosticField(handoffSummary, '持续时间', diagnosticFormatNumber(diagnostic.handoff.ageSeconds, ' 秒'));
            if (diagnostic.handoff.lastError) {
                diagnosticField(handoffSummary, '最近错误', diagnosticLabel(diagnosticHandoffErrorLabels, diagnostic.handoff.lastError, '媒体切换失败'));
            }
            handoffGroup.appendChild(handoffSummary);
        }

        var barrier = diagnosticGroup(body, '对齐与恢复');
        var barrierSummary = document.createElement('div');
        barrierSummary.className = 'wt-diagnosticSummary';
        diagnosticField(barrierSummary, '对齐阶段', diagnostic.barrier ? diagnosticLabel(diagnosticBarrierStageLabels, diagnostic.barrier.stage, '未知') : '未进行对齐');
        diagnosticField(barrierSummary, '目标位置', diagnostic.barrier ? diagnosticFormatPosition(diagnostic.barrier.targetPositionSeconds) : '—');
        diagnosticField(barrierSummary, '命令进度', diagnostic.barrier ?
            ['暂停 ' + (diagnostic.barrier.pauseSent ? '已发送' : '未发送'),
                '定位 ' + (diagnostic.barrier.seekSent ? '已发送' : '未发送'),
                '恢复 ' + (diagnostic.barrier.restoreSent ? '已发送' : '未发送')].join('、') : '—');
        diagnosticField(barrierSummary, '恢复窗口', diagnostic.recovery.active ? '活动中' : '未活动');
        diagnosticField(barrierSummary, '恢复窗口时长', diagnosticFormatNumber(diagnostic.recovery.ageSeconds, ' 秒'));
        diagnosticField(barrierSummary, '受影响参与者', diagnostic.recovery.affectedAliases.length > 0 ? diagnostic.recovery.affectedAliases.join('、') : '—');
        barrier.appendChild(barrierSummary);

        var transientRecoveryGroup = diagnosticGroup(body, '临时掉线恢复');
        var transientRecoverySummary = document.createElement('div');
        transientRecoverySummary.className = 'wt-diagnosticSummary';
        diagnosticField(transientRecoverySummary, '状态', diagnostic.transientRecovery.active ? '恢复中' : '未活动');
        diagnosticField(transientRecoverySummary, '已等待', diagnosticFormatNumber(diagnostic.transientRecovery.ageSeconds, ' 秒'));
        diagnosticField(transientRecoverySummary, '剩余窗口', diagnosticFormatNumber(diagnostic.transientRecovery.remainingSeconds, ' 秒'));
        diagnosticField(transientRecoverySummary, '缺失参与者', diagnostic.transientRecovery.missingAliases.length > 0 ? diagnostic.transientRecovery.missingAliases.join('、') : '—');
        transientRecoveryGroup.appendChild(transientRecoverySummary);
        diagnostic.transientRecovery.participants.forEach(function (participant) {
            var participantSummary = document.createElement('div');
            participantSummary.className = 'wt-diagnosticSummary';
            diagnosticField(participantSummary, participant.alias, participant.missing ? '等待该会话恢复' : '会话仍在线');
            diagnosticField(participantSummary, '预期 Session 标识', participant.expectedSessionHash || '—');
            diagnosticField(participantSummary, '预期视频标识', participant.expectedItemHash || '—');
            diagnosticField(participantSummary, '上次位置', diagnosticFormatPosition(participant.lastKnownPositionSeconds));
            diagnosticField(participantSummary, '上次播放状态', participant.lastKnownPaused ? '已暂停' : '播放中');
            transientRecoveryGroup.appendChild(participantSummary);
        });

        var driftGroup = diagnosticGroup(body, '播放漂移');
        var driftSummary = document.createElement('div');
        driftSummary.className = 'wt-diagnosticSummary';
        diagnosticField(driftSummary, '当前漂移', diagnosticFormatNumber(diagnostic.drift.currentSeconds, ' 秒'));
        diagnosticField(driftSummary, '最大绝对漂移', diagnosticFormatNumber(diagnostic.drift.maxAbsoluteSeconds, ' 秒'));
        diagnosticField(driftSummary, '纠偏保持计时', diagnosticFormatNumber(diagnostic.drift.holdAgeSeconds, ' 秒'));
        diagnosticField(driftSummary, '自动纠偏次数', diagnostic.drift.repairCount === null ? '—' : String(Math.max(0, Math.floor(diagnostic.drift.repairCount))));
        diagnosticField(driftSummary, '最近自动纠偏', diagnostic.drift.lastRepairAtUtc ? diagnosticFormatTime(diagnostic.drift.lastRepairAtUtc) : '—');
        diagnosticField(driftSummary, '最近纠偏漂移', diagnosticFormatNumber(diagnostic.drift.lastRepairDriftSeconds, ' 秒'));
        diagnosticField(driftSummary, '冷却剩余', diagnosticFormatNumber(diagnostic.drift.cooldownRemainingSeconds, ' 秒'));
        driftGroup.appendChild(driftSummary);

        var last = diagnosticGroup(body, '最近结果');
        var lastSummary = document.createElement('div');
        lastSummary.className = 'wt-diagnosticSummary';
        diagnosticField(lastSummary, '最近动作', diagnostic.lastAction ? diagnosticLabel(diagnosticEventLabels, diagnostic.lastAction.type, '观察事件') : '—');
        diagnosticField(lastSummary, '动作目标', diagnostic.lastAction && diagnostic.lastAction.alias !== 'unknown' ? diagnostic.lastAction.alias : '—');
        diagnosticField(lastSummary, '动作控制', diagnostic.lastAction && diagnostic.lastAction.command ? (diagnosticCommandLabels[diagnostic.lastAction.command] || '未知控制') : '—');
        diagnosticField(lastSummary, '动作结果', diagnostic.lastAction && diagnostic.lastAction.result ? diagnosticLabel(diagnosticResultLabels, diagnostic.lastAction.result, '已记录') : '—');
        diagnosticField(lastSummary, '最近错误', diagnostic.lastError ? diagnosticLabel(diagnosticReasonLabels, diagnostic.lastError, '播放控制未完成') : '无');
        last.appendChild(lastSummary);

        var events = diagnosticGroup(body, '事件记录');
        var eventList = document.createElement('ul');
        eventList.className = 'wt-diagnosticList';
        if (diagnostic.events.length === 0) {
            var noEvents = document.createElement('li');
            noEvents.textContent = '暂无事件记录。';
            eventList.appendChild(noEvents);
        } else {
            diagnostic.events.forEach(function (event) {
                var eventItem = document.createElement('li');
                var detail = diagnosticLabel(diagnosticEventLabels, event.type, '观察事件');
                if (event.alias !== 'unknown') detail += '（' + event.alias + '）';
                if (event.command) detail += '：' + (diagnosticCommandLabels[event.command] || '未知控制');
                if (event.result) detail += '，' + diagnosticLabel(diagnosticResultLabels, event.result, '已记录');
                if (event.driftSeconds !== null) detail += '，漂移 ' + diagnosticFormatNumber(event.driftSeconds, ' 秒');
                eventItem.textContent = diagnosticFormatTime(event.atUtc) + ' · ' + detail;
                eventList.appendChild(eventItem);
            });
        }
        events.appendChild(eventList);
    }

    function diagnosticErrorMessage(error) {
        var status = error && (error.status || error.statusCode);
        if (status === 401 || status === 403) return '没有权限查看此房间的诊断。';
        if (status >= 500) return '服务器暂时不可用，请稍后重试。';
        if (error && error.name === 'TypeError') return '网络连接失败，请稍后重试。';
        return '诊断读取失败，请稍后重试。';
    }

    function createDiagnosticPanel(page, room) {
        var generation = page._wtGeneration;
        page._wtDiagnosticPanels = page._wtDiagnosticPanels || {};
        page._wtDiagnosticOpen = page._wtDiagnosticOpen || {};
        var details = document.createElement('details');
        details.className = 'wt-diagnostics';
        var summary = document.createElement('summary');
        summary.textContent = '诊断详情';
        details.appendChild(summary);
        var body = document.createElement('div');
        body.className = 'wt-diagnosticBody';
        details.appendChild(body);
        details.addEventListener('toggle', function () {
            var current = page._wtDiagnosticPanels && page._wtDiagnosticPanels[room.RoomId];
            if (!pageActionAllowed(page) || page._wtGeneration !== generation || !current || current.details !== details) return;
            page._wtDiagnosticOpen[room.RoomId] = details.open;
            details.hidden = !details.open;
            if (details.open && !page._wtDiagnostics[room.RoomId]) loadRoomDiagnostics(page, room.RoomId);
        });
        page._wtDiagnosticPanels[room.RoomId] = { details: details, body: body };
        var diagnostic = page._wtDiagnostics && page._wtDiagnostics[room.RoomId];
        if (diagnostic) {
            renderDiagnosticDetails(page, room.RoomId, diagnostic, body);
        }
        details.open = page._wtDiagnosticOpen[room.RoomId] === true;
        details.hidden = !details.open;
        return details;
    }

    function loadRoomDiagnostics(page, roomId, button) {
        if (!pageActionAllowed(page)) return;
        var panel = page._wtDiagnosticPanels && page._wtDiagnosticPanels[roomId];
        if (!panel || (page._wtDiagnosticBusy && page._wtDiagnosticBusy[roomId])) return;
        page._wtDiagnosticBusy = page._wtDiagnosticBusy || {};
        page._wtDiagnosticRequests = page._wtDiagnosticRequests || {};
        var requestToken = String(Date.now()) + ':' + String(Math.random());
        page._wtDiagnosticRequests[roomId] = requestToken;
        page._wtDiagnosticBusy[roomId] = true;
        panel.details.hidden = false;
        panel.details.open = true;
        clearChildren(panel.body);
        var loadingText = document.createElement('p');
        loadingText.className = 'fieldDescription';
        loadingText.textContent = '正在读取诊断…';
        panel.body.appendChild(loadingText);
        setButtonBusy(button, true, '读取中…');
        return apiGet(page, 'WatchTogether/Rooms/' + encodeURIComponent(roomId) + '/Diagnostics').then(function (raw) {
            var diagnostic = sanitizeDiagnostic(raw);
            if (!diagnostic) {
                throw { name: 'InvalidDiagnostic' };
            }
            if (page._wtDiagnosticRequests[roomId] !== requestToken ||
                !page._wtDiagnosticPanels[roomId]) {
                return;
            }
            page._wtDiagnostics = page._wtDiagnostics || {};
            page._wtDiagnostics[roomId] = diagnostic;
            page._wtDiagnosticOpen = page._wtDiagnosticOpen || {};
            page._wtDiagnosticOpen[roomId] = true;
            var currentPanel = page._wtDiagnosticPanels[roomId];
            renderDiagnosticDetails(page, roomId, diagnostic, currentPanel.body);
            currentPanel.details.hidden = false;
            currentPanel.details.open = true;
        }).catch(function (error) {
            if (page._wtDiagnosticRequests[roomId] !== requestToken ||
                !page._wtDiagnosticPanels[roomId]) {
                return;
            }
            var currentPanel = page._wtDiagnosticPanels[roomId];
            clearChildren(currentPanel.body);
            var errorText = document.createElement('p');
            errorText.className = 'wt-diagnosticError';
            errorText.setAttribute('role', 'alert');
            errorText.textContent = diagnosticErrorMessage(error);
            currentPanel.body.appendChild(errorText);
            currentPanel.details.hidden = false;
            currentPanel.details.open = true;
        }).finally(function () {
            if (page._wtDiagnosticRequests[roomId] === requestToken) {
                delete page._wtDiagnosticBusy[roomId];
                setButtonBusy(button, false);
            }
        });
    }

    function exportDiagnostic(page, roomId) {
        if (!pageActionAllowed(page)) return;
        var diagnostic = page._wtDiagnostics && page._wtDiagnostics[roomId];
        if (!diagnostic || typeof Blob === 'undefined' || !window.URL || !window.URL.createObjectURL) {
            setTransientStatus(page, '当前浏览器不支持导出诊断 JSON。', true);
            return;
        }
        var blob = new Blob([JSON.stringify(diagnostic, null, 2)], { type: 'application/json' });
        var url = window.URL.createObjectURL(blob);
        var link = document.createElement('a');
        link.href = url;
        link.download = 'watch-together-diagnostics-' + new Date().toISOString().replace(/[^0-9]/g, '').slice(0, 14) + '.json';
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        window.setTimeout(function () {
            window.URL.revokeObjectURL(url);
        }, 1000);
    }

    function setStatus(page, text, isError) {
        if (page._wtStatusTimer) {
            clearTimeout(page._wtStatusTimer);
            page._wtStatusTimer = null;
        }
        var el = page.querySelector('#wtStatus');
        if (el) {
            el.textContent = text;
            el.classList.toggle('error', !!isError);
        }
    }

    function setFormHint(page, text, isError) {
        var el = page.querySelector('#wtFormHint');
        if (el) {
            el.textContent = text;
            el.classList.toggle('error', !!isError);
        }
    }

    function setCreateStatus(page, text, isError) {
        var el = page.querySelector('#wtCreateStatus');
        if (el) {
            el.textContent = text || '';
            el.hidden = !text;
            el.classList.toggle('error', !!isError);
            el.setAttribute('role', isError ? 'alert' : 'status');
        }
    }

    function setInvitationStatus(page, text, isError) {
        page._wtInvitationFeedback = { text: text || '', isError: !!isError };
        var el = page.querySelector('#wtInvitationStatus');
        if (el) {
            el.textContent = text || '';
            el.classList.toggle('error', !!isError);
        }
    }

    function invitationDate(value) {
        var date = value ? new Date(value) : null;
        if (!date || isNaN(date.getTime())) {
            return '有效期暂时无法显示';
        }
        try {
            return '有效期至：' + date.toLocaleString();
        } catch (error) {
            return '有效期至：' + date.toISOString();
        }
    }

    function renderInvitationCode(page) {
        var panel = page.querySelector('#wtInvitationCodePanel');
        var code = page.querySelector('#wtInvitationCode');
        var expires = page.querySelector('#wtInvitationExpires');
        var copy = page.querySelector('#wtCopyInvitationCode');
        var value = page._wtInvitationCode;
        if (!panel || !code || !expires) {
            return;
        }
        panel.hidden = !value;
        if (!value) {
            code.textContent = '';
            expires.textContent = '';
            return;
        }
        code.textContent = value.code;
        expires.textContent = invitationDate(value.expiresAtUtc);
        if (copy) {
            copy.disabled = false;
            copy.textContent = page._wtInvitationCopied ? '已复制' : '复制邀请码';
        }
    }

    function renderInvitations(page, invitations) {
        var container = page.querySelector('#wtInvitations');
        if (!container) {
            return;
        }
        clearChildren(container);
        var list = Array.isArray(invitations) ? invitations : [];
        if (list.length === 0) {
            var empty = document.createElement('p');
            empty.className = 'fieldDescription';
            empty.textContent = '当前没有有效的邀请码。';
            container.appendChild(empty);
            renderInvitationCode(page);
            return;
        }
        var title = document.createElement('h3');
        title.className = 'wt-subHeading';
        title.textContent = '我创建的邀请码';
        container.appendChild(title);
        list.forEach(function (invitation) {
            var item = document.createElement('div');
            item.className = 'wt-roomMeta';
            var name = document.createElement('div');
            name.textContent = invitation.Name || '未命名房间';
            var expiry = document.createElement('div');
            expiry.textContent = invitationDate(invitation.ExpiresAtUtc);
            var revoke = document.createElement('button', { is: 'emby-button' });
            revoke.type = 'button';
            revoke.setAttribute('is', 'emby-button');
            revoke.classList.add('raised', 'raised-outline', 'wt-action', 'wt-action--danger');
            revoke.textContent = '撤销邀请码';
            revoke.addEventListener('click', function () {
                revokeInvitation(page, invitation, revoke);
            });
            item.appendChild(name);
            item.appendChild(expiry);
            item.appendChild(revoke);
            container.appendChild(item);
        });
        renderInvitationCode(page);
    }

    function loadInvitations(page) {
        return apiGet(page, 'WatchTogether/Invitations').then(function (list) {
            page._wtInvitations = Array.isArray(list) ? list : [];
            page._wtInvitationsLoaded = true;
            renderInvitations(page, page._wtInvitations);
            return page._wtInvitations;
        }).catch(function (error) {
            page._wtInvitations = [];
            renderInvitations(page, []);
            if (!page._wtInvitationsLoaded) {
                setInvitationStatus(page, '邀请码列表暂时无法加载，请稍后重试。', true);
            }
            return [];
        });
    }

    function createInvitation(page) {
        if (!pageActionAllowed(page)) return;
        var input = page.querySelector('#wtInvitationName');
        var button = page.querySelector('#wtCreateInvitation');
        var name = input ? input.value.trim() : '';
        setButtonBusy(button, true, '创建中…');
        setInvitationStatus(page, '正在创建邀请码…', false);
        apiSend(page, 'WatchTogether/Invitations', 'POST', { Name: name || null }).then(function (result) {
            result = result || {};
            if (result.Status === 'creator_already_in_room') {
                setInvitationStatus(page, '你已属于一个房间，请先结束当前房间后再创建邀请码。', true);
                return null;
            }
            if (result.Status === 'invitation_unavailable') {
                setInvitationStatus(page, '当前邀请码数量已达上限，请稍后再试。', true);
                return null;
            }
            if (result.Created !== true || !result.Code || result.Status !== 'created') {
                throw new Error('invitation_unavailable');
            }
            page._wtInvitationCode = {
                code: String(result.Code),
                expiresAtUtc: result.ExpiresAtUtc
            };
            page._wtInvitationCopied = false;
            if (input) {
                input.value = '';
            }
            renderInvitationCode(page);
            setInvitationStatus(page, '邀请码已创建，请立即复制并发送给对方。', false);
            return loadInvitations(page);
        }).catch(function (error) {
            setInvitationStatus(page, error && error.message === 'invitation_unavailable'
                ? '当前邀请码数量已达上限，请稍后再试。'
                : '邀请码创建失败，请稍后重试。', true);
        }).then(function () {
            setButtonBusy(button, false);
        });
    }

    function copyInvitationCode(page, button) {
        if (!pageActionAllowed(page)) return;
        var value = page._wtInvitationCode && page._wtInvitationCode.code;
        if (!value) {
            setInvitationStatus(page, '当前没有可复制的邀请码。', true);
            return;
        }
        var copied = window.navigator && window.navigator.clipboard && window.navigator.clipboard.writeText
            ? window.navigator.clipboard.writeText(value)
            : Promise.reject(new Error('clipboard_unavailable'));
        scopedPromise(page, copied).then(function () {
            page._wtInvitationCopied = true;
            renderInvitationCode(page);
            setInvitationStatus(page, '邀请码已复制。', false);
        }).catch(function () {
            page._wtInvitationCopied = false;
            if (button) {
                button.textContent = '复制邀请码';
            }
            setInvitationStatus(page, '复制失败，请手动选择并复制邀请码。', true);
        });
    }

    function revokeInvitation(page, invitation, button) {
        if (!pageActionAllowed(page)) return;
        if (!invitation || !invitation.InvitationId) {
            return;
        }
        setButtonBusy(button, true, '撤销中…');
        apiSend(page, 'WatchTogether/Invitations/' + encodeURIComponent(invitation.InvitationId), 'DELETE').then(function () {
            setInvitationStatus(page, '邀请码已撤销。', false);
            return loadInvitations(page);
        }).catch(function (error) {
            setInvitationStatus(page, '邀请码撤销失败：' + errorMessage(error), true);
        }).then(function () {
            setButtonBusy(button, false);
        });
    }

    function invitationAcceptMessage(status) {
        return {
            accepted: '邀请已接受，房间已创建，请与对方打开同一视频。',
            invalid_or_expired: '邀请码无效或已过期，请向对方索取新邀请码。',
            creator_cannot_accept: '不能接受自己创建的邀请码。',
            rate_limited: '尝试次数过多，请稍后再试。',
            room_unavailable: '暂时无法创建房间，请稍后重试。'
        }[status] || '邀请码未能接受，请稍后重试。';
    }

    function acceptInvitation(page) {
        if (!pageActionAllowed(page)) return;
        var input = page.querySelector('#wtInvitationAcceptCode');
        var button = page.querySelector('#wtAcceptInvitation');
        var code = input ? input.value.trim() : '';
        if (!code) {
            setInvitationStatus(page, '请输入邀请码。', true);
            if (input) {
                input.focus();
            }
            return;
        }
        setButtonBusy(button, true, '接受中…');
        setInvitationStatus(page, '正在接受邀请…', false);
        apiSend(page, 'WatchTogether/Invitations/' + encodeURIComponent(code) + '/Accept', 'POST').then(function (result) {
            result = result || {};
            setInvitationStatus(page, invitationAcceptMessage(result.Status), result.Status !== 'accepted');
            if (result.Status === 'accepted') {
                if (input) {
                    input.value = '';
                }
                return Promise.all([loadRooms(page, false), loadInvitations(page)]);
            }
            return null;
        }).catch(function (error) {
            setInvitationStatus(page, '邀请接受失败：' + errorMessage(error), true);
        }).then(function () {
            setButtonBusy(button, false);
        });
    }

    function setConfigStatus(page, text, isError) {
        var el = page.querySelector('#wtConfigStatus');
        if (el) {
            el.textContent = text;
            el.classList.toggle('error', !!isError);
        }
    }

    function setGitHubTokenStatus(page, text, isError) {
        var el = page.querySelector('#wtGitHubTokenStatus');
        if (el) {
            el.textContent = text;
            el.classList.toggle('error', !!isError);
        }
    }

    function setGitHubTokenBusy(page, isBusy, operation) {
        var input = page.querySelector('#wtGitHubToken');
        var saveButton = page.querySelector('#wtSaveGitHubToken');
        var clearButton = page.querySelector('#wtClearGitHubToken');
        operation = operation || '';
        if (input) {
            input.disabled = isBusy;
        }
        if (saveButton) {
            saveButton.disabled = isBusy || !page._wtGitHubTokenReady;
            saveButton.setAttribute('aria-busy', isBusy && operation === 'save' ? 'true' : 'false');
            saveButton.textContent = isBusy && operation === 'save' ? '保存中…' : '保存 Token';
        }
        if (clearButton) {
            clearButton.disabled = isBusy || !page._wtGitHubTokenCanClear;
            clearButton.setAttribute('aria-busy', isBusy && operation === 'clear' ? 'true' : 'false');
            clearButton.textContent = isBusy && operation === 'clear' ? '清除中…' : '清除 Token';
        }
    }

    function clearGitHubTokenInput(page) {
        var input = page.querySelector('#wtGitHubToken');
        if (input) {
            input.value = '';
        }
    }

    function loadGitHubTokenStatus(page) {
        page._wtGitHubTokenReady = false;
        page._wtGitHubTokenCanClear = false;
        clearGitHubTokenInput(page);
        setGitHubTokenStatus(page, '正在读取 Token 状态…');
        setGitHubTokenBusy(page, true, 'read');
        return apiGet(page, 'WatchTogether/GitHubToken').then(function (result) {
            page._wtGitHubTokenReady = true;
            page._wtGitHubTokenCanClear = true;
            setGitHubTokenStatus(page, result && result.Configured ? 'Token 已配置。' : '尚未配置 Token。');
        }).catch(function (error) {
            page._wtGitHubTokenReady = false;
            page._wtGitHubTokenCanClear = !isPermissionError(error);
            setGitHubTokenStatus(page, isPermissionError(error)
                ? '只有管理员可以查看和修改 Token。'
                : 'Token 状态读取失败，可尝试清除后重新配置。', true);
        }).then(function () {
            setGitHubTokenBusy(page, false);
        });
    }

    function saveGitHubToken(page) {
        if (!pageActionAllowed(page)) return;
        if (page._wtIsAdmin !== true) return;
        var input = page.querySelector('#wtGitHubToken');
        var value = input ? String(input.value || '').trim() : '';
        if (!value) {
            setGitHubTokenStatus(page, '请输入 GitHub Token。', true);
            if (input) {
                input.focus();
            }
            return Promise.resolve();
        }

        setGitHubTokenBusy(page, true, 'save');
        setGitHubTokenStatus(page, '正在保存 Token…');
        return apiSend(page, 'WatchTogether/GitHubToken', 'POST', { Token: value }).then(function (result) {
            clearGitHubTokenInput(page);
            setGitHubTokenStatus(page, result && result.Configured ? 'Token 已保存。' : 'Token 未配置。');
        }).catch(function (error) {
            setGitHubTokenStatus(page,
                isPermissionError(error) ? '保存被拒绝：只有管理员可以修改 Token。' : 'Token 保存失败，请稍后重试。', true);
        }).then(function () {
            clearGitHubTokenInput(page);
            setGitHubTokenBusy(page, false);
        });
    }

    function clearGitHubToken(page) {
        if (!pageActionAllowed(page)) return;
        if (page._wtIsAdmin !== true) return;
        if (!window.confirm('清除 GitHub Token 后，beta 更新查询将恢复使用匿名 API，确认清除吗？')) {
            return;
        }

        setGitHubTokenBusy(page, true, 'clear');
        setGitHubTokenStatus(page, '正在清除 Token…');
        return apiSend(page, 'WatchTogether/GitHubToken', 'DELETE').then(function (result) {
            clearGitHubTokenInput(page);
            if (result && result.Configured === false) {
                page._wtGitHubTokenReady = true;
                page._wtGitHubTokenCanClear = true;
            }
            setGitHubTokenStatus(page, result && result.Configured ? 'Token 仍已配置。' : 'Token 已清除。');
        }).catch(function (error) {
            setGitHubTokenStatus(page,
                isPermissionError(error) ? '清除被拒绝：只有管理员可以修改 Token。' : 'Token 清除失败，请稍后重试。', true);
        }).then(function () {
            clearGitHubTokenInput(page);
            setGitHubTokenBusy(page, false);
        });
    }

    function isPermissionError(error) {
        return !!(error && (error.status === 401 || error.status === 403 ||
            error.statusCode === 401 || error.statusCode === 403));
    }

    var routePageNames = {
        rooms: 'WatchTogetherRoomsV18',
        invite: 'WatchTogetherInviteV18',
        settings: 'WatchTogetherSettingsV18'
    };

    function currentRoute(page) {
        var href = String((page.ownerDocument.defaultView || window).location.href || '');
        if (href.indexOf(routePageNames.settings) >= 0) return 'settings';
        if (href.indexOf(routePageNames.invite) >= 0) return 'invite';
        return 'rooms';
    }

    function configurationPageUrl(page, name) {
        // Match pluginManager's relative href. emby-linkbutton routes the anchor
        // pathname; a hash href incorrectly includes index.html in that route.
        var identity = page._wtIdentity || pageIdentity();
        var href = 'configurationpage?name=' + encodeURIComponent(name);
        if (identity && identity.user) href += '&userId=' + encodeURIComponent(identity.user);
        if (identity && identity.serverId) href += '&serverId=' + encodeURIComponent(identity.serverId);
        return href;
    }

    function nativeTabs(page) {
        var tabs = [
            { href: configurationPageUrl(page, routePageNames.rooms), name: '房间' },
            { href: configurationPageUrl(page, routePageNames.invite), name: page._wtIsAdmin === true ? '创建与邀请' : '邀请' }
        ];
        if (page._wtIsAdmin === true) tabs.push({ href: configurationPageUrl(page, routePageNames.settings), name: '设置' });
        return tabs;
    }

    function ownsNativeTabs(page) {
        return !!(page._wtNativeTabsElement && mainTabsManager &&
            typeof mainTabsManager.getTabsElement === 'function' &&
            mainTabsManager.getTabsElement() === page._wtNativeTabsElement);
    }

    function releaseNativeTabs(page) {
        try {
            // The host replaces the actual tabs element when another view owns it.
            // Never clear a newer owner's tabs from this view's pause or reset.
            if (ownsNativeTabs(page)) mainTabsManager.setTabs(null);
        } catch (error) {
            // A missing host header still has the route-link fallback.
        }
        page._wtNativeTabsElement = null;
        page._wtNativeTabsKey = null;
    }

    function applyRoute(page) {
        var route = page._wtRoute || 'rooms';
        if (route === 'settings' && page._wtIsAdmin !== true) route = 'rooms';
        page._wtVisibleRoute = route;
        page.classList.toggle('wt-page--rooms', route === 'rooms');
        var inviteLayout = page.querySelector('#wtInviteLayout');
        inviteLayout.hidden = route !== 'invite';
        inviteLayout.classList.toggle('wt-pageColumns--single', page._wtIsAdmin !== true);
        page.querySelector('#wtInviteHeading').textContent = page._wtIsAdmin === true ? '创建与邀请' : '邀请';
        page.querySelector('#wtRoomsSection').hidden = route !== 'rooms';
        page.querySelector('#wtHelpSection').hidden = route !== 'rooms';
        page.querySelector('#wtInvitationSection').hidden = route !== 'invite';
        page.querySelector('#wtAdminSection').hidden = route !== 'invite' || page._wtIsAdmin !== true;
        page.querySelector('#wtSettingsSection').hidden = route !== 'settings' || page._wtIsAdmin !== true;
        var fallback = page.querySelector('#wtNavigation');
        if (!fallback) return;
        clearChildren(fallback);
        var tabs = nativeTabs(page);
        tabs.forEach(function (tab) {
            var link = document.createElement('a');
            link.href = tab.href;
            link.textContent = tab.name;
            if (tab.href.indexOf(routePageNames[route]) >= 0) link.setAttribute('aria-current', 'page');
            fallback.appendChild(link);
        });
        fallback.hidden = false;
        var key = route + ':' + String(page._wtIsAdmin);
        fallback.hidden = page._wtNativeTabsKey === key && ownsNativeTabs(page);
        if (!page._wtActive || !page._wtTabsPermissionReady || fallback.hidden) return;
        if (mainTabsManager && typeof mainTabsManager.setTabs === 'function') {
            try {
                // setTabs with the same owner only selects a tab. Rebuild our
                // own element when the resolved permission changes the list.
                releaseNativeTabs(page);
                var selected = route === 'invite' ? 1 : route === 'settings' ? 2 : 0;
                var result = mainTabsManager.setTabs(page, selected, function () { return nativeTabs(page); });
                if (result && result.replaced === false && typeof mainTabsManager.selectedTabIndex === 'function') {
                    mainTabsManager.selectedTabIndex(selected);
                }
                page._wtNativeTabsKey = key;
                page._wtNativeTabsElement = typeof mainTabsManager.getTabsElement === 'function'
                    ? mainTabsManager.getTabsElement() : null;
                // Without a host header the route links keep all permitted pages reachable.
                fallback.hidden = ownsNativeTabs(page);
            } catch (error) {
                fallback.hidden = false;
            }
        }
    }

    function setAdminVisibility(page, isAdmin) {
        var adminSection = page.querySelector('#wtAdminSection');
        var settingsSection = page.querySelector('#wtSettingsSection');
        var roomsHeading = page.querySelector('#wtRoomsHeading');
        var helpSteps = [
            page.querySelector('#wtHelpStep1'),
            page.querySelector('#wtHelpStep2'),
            page.querySelector('#wtHelpStep3')
        ];
        if (adminSection) {
            adminSection.style.display = isAdmin ? '' : 'none';
        }
        if (settingsSection) {
            settingsSection.style.display = isAdmin ? '' : 'none';
        }
        if (roomsHeading) {
            roomsHeading.textContent = isAdmin ? '房间' : '我的房间';
        }
        var helpText = isAdmin ? [
            '创建房间并选择两名参与者。',
            '两人分别登录 Emby，打开同一视频。',
            '看到“同步中”后即可一起观看。'
        ] : [
            '加入房间后，与另一位参与者打开同一视频。',
            '播放、暂停和进度会自动同步。',
            '需要重新对齐时，点击“请求重新同步”。'
        ];
        applyRoute(page);
        helpSteps.forEach(function (step, index) {
            if (step) {
                step.textContent = helpText[index];
            }
        });
    }

    function clearChildren(element) {
        while (element && element.firstChild) {
            element.removeChild(element.firstChild);
        }
    }

    function setConfigBusy(page, isBusy) {
        var pauseCheckbox = page.querySelector('#wtPauseOtherOnPlaybackStop');
        var notifyCheckbox = page.querySelector('#wtNotifyOtherOnPlaybackStop');
        var syncNotifyCheckbox = page.querySelector('#wtNotifyOnSyncActions');
        var updateChannel = page.querySelector('#wtUpdateChannel');
        var saveButton = page.querySelector('#wtSaveConfig');
        if (pauseCheckbox) {
            pauseCheckbox.disabled = isBusy;
        }
        if (notifyCheckbox) {
            notifyCheckbox.disabled = isBusy;
        }
        if (syncNotifyCheckbox) {
            syncNotifyCheckbox.disabled = isBusy;
        }
        if (updateChannel) {
            updateChannel.disabled = isBusy;
        }
        if (saveButton) {
            saveButton.disabled = isBusy || !page._wtConfigReady;
            saveButton.setAttribute('aria-busy', isBusy ? 'true' : 'false');
            saveButton.textContent = isBusy ? '保存中…' : '保存设置';
        }
    }

    function applyPluginConfiguration(page, config) {
        var pauseCheckbox = page.querySelector('#wtPauseOtherOnPlaybackStop');
        var notifyCheckbox = page.querySelector('#wtNotifyOtherOnPlaybackStop');
        var syncNotifyCheckbox = page.querySelector('#wtNotifyOnSyncActions');
        var updateChannel = page.querySelector('#wtUpdateChannel');
        page._wtPluginConfiguration = config || {};
        page._wtConfigReady = true;
        if (pauseCheckbox) {
            pauseCheckbox.checked = page._wtPluginConfiguration.PauseOtherOnPlaybackStop !== false;
            pauseCheckbox.disabled = false;
        }
        if (notifyCheckbox) {
            notifyCheckbox.checked = page._wtPluginConfiguration.NotifyOtherOnPlaybackStop !== false;
            notifyCheckbox.disabled = false;
        }
        if (syncNotifyCheckbox) {
            syncNotifyCheckbox.checked = page._wtPluginConfiguration.NotifyOnSyncActions !== false;
            syncNotifyCheckbox.disabled = false;
        }
        if (updateChannel) {
            updateChannel.value = page._wtPluginConfiguration.UpdateChannel === 'beta' ? 'beta' : 'stable';
            updateChannel.disabled = false;
        }
        var saveButton = page.querySelector('#wtSaveConfig');
        if (saveButton) {
            saveButton.disabled = false;
        }
    }

    function loadPluginConfiguration(page) {
        setConfigStatus(page, '正在读取配置…');
        setConfigBusy(page, true);
        return pageRequest(page, 'config:read', function () { return ApiClient.getPluginConfiguration(pluginId); }).then(function (config) {
            applyPluginConfiguration(page, config);
            setConfigStatus(page, '配置已读取');
            return loadGitHubTokenStatus(page).then(function () {
                return config;
            });
        }).catch(function (error) {
            page._wtConfigReady = false;
            var pauseCheckbox = page.querySelector('#wtPauseOtherOnPlaybackStop');
            var notifyCheckbox = page.querySelector('#wtNotifyOtherOnPlaybackStop');
            var syncNotifyCheckbox = page.querySelector('#wtNotifyOnSyncActions');
            var updateChannel = page.querySelector('#wtUpdateChannel');
            var saveButton = page.querySelector('#wtSaveConfig');
            if (pauseCheckbox) {
                pauseCheckbox.disabled = true;
            }
            if (notifyCheckbox) {
                notifyCheckbox.disabled = true;
            }
            if (syncNotifyCheckbox) {
                syncNotifyCheckbox.disabled = true;
            }
            if (updateChannel) {
                updateChannel.disabled = true;
            }
            if (saveButton) {
                saveButton.disabled = true;
            }
            if (isPermissionError(error)) {
                setAdminVisibility(page, false);
                setConfigStatus(page, '只有管理员可以查看和修改此设置。', true);
            } else {
                setConfigStatus(page, '配置读取失败：' + errorMessage(error), true);
            }
            throw error;
        }).finally(function () {
            if (page._wtConfigReady) {
                setConfigBusy(page, false);
            }
        });
    }

    function savePluginConfiguration(page) {
        if (!pageActionAllowed(page)) return;
        if (page._wtIsAdmin !== true) return;
        var pauseCheckbox = page.querySelector('#wtPauseOtherOnPlaybackStop');
        var notifyCheckbox = page.querySelector('#wtNotifyOtherOnPlaybackStop');
        var syncNotifyCheckbox = page.querySelector('#wtNotifyOnSyncActions');
        var updateChannel = page.querySelector('#wtUpdateChannel');
        if (!pauseCheckbox || !notifyCheckbox || !syncNotifyCheckbox || !updateChannel || !page._wtConfigReady) {
            return Promise.resolve();
        }

        setConfigBusy(page, true);
        setConfigStatus(page, '正在保存配置…');
        return pageRequest(page, 'config:read', function () { return ApiClient.getPluginConfiguration(pluginId); }).then(function (config) {
            config = config || {};
            config.PauseOtherOnPlaybackStop = pauseCheckbox.checked;
            config.NotifyOtherOnPlaybackStop = notifyCheckbox.checked;
            config.NotifyOnSyncActions = syncNotifyCheckbox.checked;
            config.UpdateChannel = updateChannel.value === 'beta' ? 'beta' : 'stable';
            return pageRequest(page, 'config:save', function () { return ApiClient.updatePluginConfiguration(pluginId, config); });
        }).then(function () {
            return pageRequest(page, 'config:read', function () { return ApiClient.getPluginConfiguration(pluginId); });
        }).then(function (config) {
            applyPluginConfiguration(page, config);
            setConfigStatus(page,
                '配置已保存：停止暂停' + (pauseCheckbox.checked ? '开启' : '关闭') + '；停止提示' +
                (notifyCheckbox.checked ? '开启' : '关闭') + '；同步操作提示' +
                (syncNotifyCheckbox.checked ? '开启' : '关闭') + '；更新通道' +
                (updateChannel.value === 'beta' ? '测试版 beta' : '正式版 stable'));
        }).catch(function (error) {
            setConfigStatus(page,
                isPermissionError(error) ? '保存被拒绝：只有管理员可以修改此设置。' : '配置保存失败：' + errorMessage(error),
                true);
        }).then(function () {
            setConfigBusy(page, false);
        });
    }

    function loadPluginInfo(page) {
        return apiGet(page, 'WatchTogether/Info').then(function (info) {
            info = info || {};
            var version = page.querySelector('#wtPluginVersion');
            if (version) {
                version.textContent = info.CurrentVersion || '—';
            }
            var link = page.querySelector('#wtRepositoryLink');
            if (link && info.RepositoryUrl) {
                link.href = info.RepositoryUrl;
            }
        }).catch(function () {
            var version = page.querySelector('#wtPluginVersion');
            if (version) {
                version.textContent = '—';
            }
        });
    }

    function findUser(page, id) {
        return page._wtUsers.filter(function (user) {
            return user.Id === id;
        })[0] || null;
    }

    function userName(page, id) {
        var user = findUser(page, id);
        return user ? user.Name : (id || '未知用户');
    }

    function roomName(room) {
        return room && (room.Name || room.RoomId) || '未命名房间';
    }

    function roomParticipant(room, id) {
        var participants = room && Array.isArray(room.Participants) ? room.Participants : [];
        var normalizedId = String(id || '').toLowerCase();
        return participants.filter(function (participant) {
            return participant && String(participant.Id || '').toLowerCase() === normalizedId;
        })[0] || null;
    }

    function roomUserName(page, room, id) {
        var participant = roomParticipant(room, id);
        if (participant && participant.Name) {
            return participant.Name;
        }
        var user = findUser(page, id);
        return user && user.Name ? user.Name : '未知用户';
    }

    function findRoomForUser(page, userId) {
        var rooms = Array.isArray(page._wtRooms) ? page._wtRooms : [];
        var normalizedId = String(userId || '').toLowerCase();
        return rooms.filter(function (room) {
            return (room.ParticipantUserIds || []).some(function (id) {
                return String(id).toLowerCase() === normalizedId;
            });
        })[0] || null;
    }

    function findRoomConflict(page, userIds) {
        for (var i = 0; i < userIds.length; i++) {
            var room = findRoomForUser(page, userIds[i]);
            if (room) {
                return {
                    userId: userIds[i],
                    room: room
                };
            }
        }
        return null;
    }

    function conflictMessage(page, conflict) {
        return '用户“' + userName(page, conflict.userId) + '”已在房间“' + roomName(conflict.room) +
            '”中，请先退出或删除原房间，或者选择其他用户。';
    }

    function fillSelect(select, options, selected) {
        if (!select) {
            return;
        }

        var unchanged = select.options.length === options.length && options.every(function (user, index) {
            var option = select.options[index];
            return option.value === user.Id && option.textContent === user.Name;
        });
        // Polls and name edits must not replace options under an open native menu.
        if (!unchanged) {
            clearChildren(select);
            options.forEach(function (user) {
                var opt = document.createElement('option');
                opt.value = user.Id;
                opt.textContent = user.Name;
                select.appendChild(opt);
            });
        }

        var selectedExists = options.some(function (user) {
            return user.Id === selected;
        });
        if (selectedExists && select.value !== selected) {
            select.value = selected;
        } else if (!selectedExists && options.length > 0 && select.selectedIndex !== 0) {
            select.selectedIndex = 0;
        }
    }

    function syncPrimarySelect(page) {
        var participantA = page.querySelector('#wtUserA');
        var participantB = page.querySelector('#wtUserB');
        var primary = page.querySelector('#wtPrimary');
        var selectedPrimary = primary ? primary.value : '';
        var ids = [];

        [participantA && participantA.value, participantB && participantB.value].forEach(function (id) {
            if (id && ids.indexOf(id) === -1) {
                ids.push(id);
            }
        });

        fillSelect(primary, ids.map(function (id) { return findUser(page, id); }).filter(function (user) {
            return !!user;
        }), selectedPrimary);
    }

    function syncForm(page) {
        var nameInput = page.querySelector('#wtRoomName');
        var participantA = page.querySelector('#wtUserA');
        var participantB = page.querySelector('#wtUserB');
        var primary = page.querySelector('#wtPrimary');
        var createButton = page.querySelector('#wtCreate');
        var name = nameInput ? nameInput.value.trim() : '';
        var a = participantA ? participantA.value : '';
        var b = participantB ? participantB.value : '';

        syncPrimarySelect(page);
        primary = page.querySelector('#wtPrimary');

        var hint;
        var isError = false;
        if (page._wtUsers.length < 2) {
            hint = '至少需要两名用户才能创建房间。';
            isError = true;
        } else if (!name) {
            hint = '请输入房间名称。';
            isError = true;
        } else if (!a || !b) {
            hint = '请选择两名参与者。';
            isError = true;
        } else if (a === b) {
            hint = '两名参与者必须不同。';
            isError = true;
        } else {
            var conflict = findRoomConflict(page, [a, b]);
            if (conflict) {
                hint = conflictMessage(page, conflict);
                isError = true;
            }
        }
        if (!isError) {
            if (!primary || !primary.value) {
                hint = '请选择主用户。';
                isError = true;
            } else {
                hint = '准备完成，可以创建房间。';
            }
        }

        if (createButton && !createButton.getAttribute('aria-busy')) {
            createButton.disabled = isError;
        }
        setFormHint(page, hint, isError);
    }

    function loadUsers(page) {
        return apiGet(page, 'WatchTogether/Users').then(function (list) {
            page._wtIsAdmin = true;
            page._wtTabsPermissionReady = true;
            setAdminVisibility(page, true);
            page._wtUsers = Array.isArray(list) ? list : [];
            page._wtRooms = null;
            fillSelect(page.querySelector('#wtUserA'), page._wtUsers, page._wtUsers.length > 0 ? page._wtUsers[0].Id : null);
            fillSelect(page.querySelector('#wtUserB'), page._wtUsers, page._wtUsers.length > 1 ? page._wtUsers[1].Id : null);
            syncForm(page);
        }).catch(function (err) {
            page._wtIsAdmin = isPermissionError(err) ? false : undefined;
            page._wtTabsPermissionReady = true;
            page._wtRooms = null;
            page._wtUsers = [];
            fillSelect(page.querySelector('#wtUserA'), [], null);
            fillSelect(page.querySelector('#wtUserB'), [], null);
            syncForm(page);
            setAdminVisibility(page, false);
            if (isPermissionError(err)) {
                setFormHint(page, '只有管理员可以创建房间。', true);
                setStatus(page, '');
            } else {
                setFormHint(page, '用户列表暂时无法加载，请稍后重试。', true);
                setStatus(page, '用户列表加载失败：' + errorMessage(err), true);
            }
        });
    }

    function getStateInfo(state) {
        var knownState = stateLabels[state] ? state : 'Unavailable';
        return {
            label: stateLabels[knownState],
            description: stateDescriptions[knownState],
            className: knownState.toLowerCase()
        };
    }

    var statusReasonMessages = {
        server_unavailable: '房间所属服务器暂不可用，请确认当前服务器连接。',
        snapshot_unavailable: '暂时无法读取播放会话，自动同步已进入保护状态；恢复后会重新对齐。',
        different_video: '两位参与者打开了不同视频，请打开同一视频。',
        playback_stopped: '播放已停止，请双方重新打开同一视频。',
        command_failed: '播放控制未完成，请检查两位参与者的客户端。',
        member_left: '仍有参与者未加入，请先让双方加入房间。',
        aligning: '正在对齐播放位置，请稍候。',
        watching: '两位参与者已连接，播放会自动同步。',
        remote_control_unavailable: '当前客户端不支持远程控制，请更换或更新客户端。',
        media_mismatch: '两位参与者的媒体信息不一致，请打开同一视频。',
        media_handoff: '正在让另一位参与者打开主用户当前视频，请稍候。',
        unsupported_playback_rate: '播放速度不是 1 倍，请恢复正常速度后重试。',
        waiting_for_playback: '等待双方打开同一视频并开始播放。',
        transient_recovery: '正在等待临时掉线的参与者恢复。'
    };

    function statusReasonMessage(reason) {
        return statusReasonMessages[reason] || '当前房间状态需要检查，请刷新后重试。';
    }

    function roomFeedback(page, roomId, text, isError, persistent) {
        page._wtRoomFeedback = page._wtRoomFeedback || {};
        page._wtRoomFeedbackTimers = page._wtRoomFeedbackTimers || {};
        if (page._wtRoomFeedbackTimers[roomId]) {
            clearTimeout(page._wtRoomFeedbackTimers[roomId]);
            delete page._wtRoomFeedbackTimers[roomId];
        }
        var token = String(Date.now()) + ':' + String(Math.random());
        page._wtRoomFeedback[roomId] = { text: text, isError: !!isError, token: token };
        if (!isError && !persistent) {
            page._wtRoomFeedbackTimers[roomId] = setTimeout(function () {
                if (!pageActionAllowed(page)) return;
                if (page._wtRoomFeedback[roomId] && page._wtRoomFeedback[roomId].token === token) {
                    delete page._wtRoomFeedback[roomId];
                    renderRooms(page, page._wtRooms || []);
                }
                delete page._wtRoomFeedbackTimers[roomId];
            }, 8000);
        }
    }

    function clearRoomFeedback(page, roomId) {
        if (page._wtRoomFeedbackTimers && page._wtRoomFeedbackTimers[roomId]) {
            clearTimeout(page._wtRoomFeedbackTimers[roomId]);
            delete page._wtRoomFeedbackTimers[roomId];
        }
        if (page._wtRoomFeedback) {
            delete page._wtRoomFeedback[roomId];
        }
    }

    function clearAllRoomFeedback(page) {
        if (page._wtRoomFeedbackTimers) {
            Object.keys(page._wtRoomFeedbackTimers).forEach(function (roomId) {
                clearTimeout(page._wtRoomFeedbackTimers[roomId]);
            });
        }
        page._wtRoomFeedbackTimers = {};
        page._wtRoomFeedback = {};
    }

    function setTransientStatus(page, text, isError) {
        setStatus(page, text, isError);
        page._wtStatusTimer = setTimeout(function () {
            if (!pageActionAllowed(page)) return;
            page._wtStatusTimer = null;
            var el = page.querySelector('#wtStatus');
            if (el) {
                el.textContent = '';
                el.classList.remove('error');
            }
        }, 8000);
    }

    function setRoomBusy(page, roomId, busy) {
        page._wtRoomBusy = page._wtRoomBusy || {};
        if (busy) {
            page._wtRoomBusy[roomId] = true;
        } else {
            delete page._wtRoomBusy[roomId];
        }
    }

    function createActionButton(page, room, action) {
        var button = document.createElement('button', { is: 'emby-button' });
        button.type = 'button';
        button.setAttribute('is', 'emby-button');
        var toneClass = action === 'join'
            ? 'wt-action--primary'
            : action === 'leave' || action === 'delete'
                ? 'wt-action--danger'
                : action === 'resync' || action === 'participantResync'
                    ? 'wt-action--accent'
                    : '';
        button.classList.add('raised', 'wt-action');
        if (action === 'join') button.classList.add('button-submit');
        else button.classList.add('raised-outline');
        if (toneClass) button.classList.add(toneClass);
        button.dataset.act = action;
        button.dataset.action = action;
        button.textContent = action === 'delete' ? (room.CanEnd ? '结束房间' : '删除房间') : action === 'leave' ? '暂离房间' : action === 'join' ? '加入房间' : action === 'diagnostics' ? '查看诊断' : actionLabels[action];
        button.title = action === 'delete'
            ? (room.CanEnd ? '结束房间会解除双方成员关系；只删除同步关系，不删除媒体' : '删除这个房间；只删除同步关系，不删除媒体')
            : action === 'leave'
                ? '暂离只退出同步状态，不解除成员关系；将尝试暂停仍在房间的一方'
                : action === 'join'
                    ? '加入后需要与另一位参与者打开同一视频'
                    : action === 'diagnostics'
                        ? '读取当前房间的脱敏同步诊断'
                        : action === 'participantResync'
                            ? '请求服务端重新对齐双方播放位置'
                            : actionLabels[action] + '：' + (getStateInfo(room.State).description || '');
        button.addEventListener('click', function () {
            if (action === 'delete') {
                deleteRoom(page, room, button);
            } else if (action === 'join' || action === 'leave') {
                membership(page, room, action, button);
            } else if (action === 'diagnostics') {
                loadRoomDiagnostics(page, room.RoomId, button);
            } else if (action === 'participantResync') {
                participantResync(page, room.RoomId, button);
            } else {
                control(page, room.RoomId, action, button);
            }
        });
        if (action === 'diagnostics' && page._wtDiagnosticBusy && page._wtDiagnosticBusy[room.RoomId]) {
            button.disabled = true;
            button.setAttribute('aria-busy', 'true');
            button.textContent = '读取中…';
        }
        if (page._wtRoomBusy && page._wtRoomBusy[room.RoomId]) {
            button.disabled = true;
        }
        return button;
    }

    function rememberDiagnosticPanelState(page) {
        page._wtDiagnosticOpen = page._wtDiagnosticOpen || {};
        var panels = page._wtDiagnosticPanels || {};
        Object.keys(panels).forEach(function (roomId) {
            if (panels[roomId] && panels[roomId].details) {
                page._wtDiagnosticOpen[roomId] = panels[roomId].details.open;
            }
        });
    }

    function idKey(value) { return value === null || value === undefined ? '' : String(value).toLowerCase(); }

    function mediaItemId(state) {
        if (!state || state.Eligible !== true ||
            !((state.State === 'Watching' && state.StatusReason === 'watching') ||
                (state.State === 'Barrier' && state.StatusReason === 'aligning'))) return null;
        var members = Array.isArray(state.ParticipantUserIds) ? state.ParticipantUserIds.map(idKey) : null;
        var joined = Array.isArray(state.JoinedParticipantUserIds) ? state.JoinedParticipantUserIds.map(idKey) : null;
        var sessions = state.Sessions;
        if (!Array.isArray(members) || members.length !== 2 || !members[0] || !members[1] || members[0] === members[1] ||
            members.indexOf(idKey(state.PrimaryUserId)) < 0 || !Array.isArray(joined) || joined.length !== 2 ||
            joined[0] === joined[1] || !members.every(function (id) { return joined.indexOf(id) >= 0; }) ||
            !Array.isArray(sessions) || sessions.length !== 2 || !state.SyncItemId) return null;
        if (!members.every(function (id) {
            return sessions.filter(function (session) {
                return session && idKey(session.UserId) === id && session.Online === true &&
                    session.ItemId && idKey(session.ItemId) === idKey(state.SyncItemId);
            }).length === 1;
        })) return null;
        return String(state.SyncItemId);
    }

    function mediaTitle(item) {
        function index(value) { return typeof value === 'number' && isFinite(value) && value >= 0 && Math.floor(value) === value; }
        function name(value) { return typeof value === 'string' ? value.trim() : ''; }
        var title = name(item.Name);
        if (item.Type !== 'Episode') return title;
        title = name(item.SeriesName) || title;
        var parts = [];
        if (title) parts.push(title);
        if (index(item.ParentIndexNumber)) parts.push(item.ParentIndexNumber === 0 ? '特别篇' : '第 ' + item.ParentIndexNumber + ' 季');
        if (index(item.IndexNumber)) parts.push('第 ' + item.IndexNumber + ' 集');
        return title ? parts.join(' · ') : '';
    }

    function roomPresentation(page, room) {
        var entry = page._wtRoomStates[room.RoomId];
        var state = entry && entry.state;
        if (!state) return { label: entry && entry.failed ? '状态暂不可用' : '正在读取状态', description: entry && entry.expired ? '播放状态已过期，正在等待重新读取。' : entry && entry.failed ? '状态读取失败，请刷新后重试。' : '正在读取当前播放状态…', className: 'unavailable' };
        var info = getStateInfo(state.State);
        info.description = statusReasonMessage(state.StatusReason);
        if (state.StatusReason === 'different_video' || state.StatusReason === 'media_mismatch') info.label = '视频不一致';
        else if (state.StatusReason === 'transient_recovery' || state.State === 'Recovering') info.label = '等待连接恢复';
        else if (state.StatusReason === 'member_left') info.label = '等待参与者';
        else if (['snapshot_unavailable', 'snapshot_stale', 'server_unavailable'].indexOf(state.StatusReason) >= 0) info.label = '状态暂不可用';
        else if (state.State === 'Waiting') info.label = '等待播放';
        else if (state.State === 'Unavailable') info.label = '状态暂不可用';
        return info;
    }

    function safeImagePart(value) { return typeof value === 'string' && /^[A-Za-z0-9_-]{1,128}$/.test(value); }

    function sameServerImageUrl(page, raw) {
        if (typeof raw !== 'string' || !raw.trim()) return null;
        try {
            var server = new URL(page._wtIdentity.server);
            var url = new URL(raw, server.href);
            return (url.protocol === 'http:' || url.protocol === 'https:') && !url.username && !url.password &&
                url.origin === server.origin ? url.href : null;
        } catch (error) {
            return null;
        }
    }

    function roomBackdropUrl(page, item) {
        if (typeof ApiClient.getImageUrl !== 'function') return null;
        var imageId = item.Id;
        var tag = Array.isArray(item.BackdropImageTags) ? item.BackdropImageTags[0] : null;
        if (!safeImagePart(imageId) || !safeImagePart(tag)) {
            imageId = item.ParentBackdropItemId;
            tag = Array.isArray(item.ParentBackdropImageTags) ? item.ParentBackdropImageTags[0] : null;
        }
        if (!safeImagePart(imageId) || !safeImagePart(tag)) return null;
        try {
            var raw = ApiClient.getImageUrl(imageId, { type: 'Backdrop', index: 0, tag: tag, maxWidth: 1280 });
            return sameServerImageUrl(page, raw);
        } catch (error) {
            return null;
        }
    }

    function roomAvatarUserId(room, userId) {
        if (!safeImagePart(userId) || !room || !Array.isArray(room.ParticipantUserIds) ||
            room.ParticipantUserIds.length !== 2 || idKey(room.ParticipantUserIds[0]) === idKey(room.ParticipantUserIds[1]) ||
            !room.ParticipantUserIds.some(function (id) { return idKey(id) === idKey(userId); })) return null;
        var participant = roomParticipant(room, userId);
        return participant && safeImagePart(participant.Id) ? participant.Id : null;
    }

    function roomAvatarUrl(page, room, userId) {
        if (typeof ApiClient.getUserImageUrl !== 'function') return null;
        userId = roomAvatarUserId(room, userId);
        if (!userId) return null;
        try {
            return sameServerImageUrl(page, ApiClient.getUserImageUrl(userId, { type: 'Primary', maxWidth: 96 }));
        } catch (error) {
            return null;
        }
    }

    function clearRoomAvatars(view) {
        (view && view.avatars || []).forEach(function (record) {
            record.image.onload = null;
            record.image.onerror = null;
            record.image.removeAttribute('src');
            record.image.hidden = true;
            record.fallback.hidden = false;
            record.url = null;
        });
    }

    function startRoomAvatars(page, room, view) {
        var generation = page._wtGeneration;
        function currentMember(record) {
            if (!pageActionAllowed(page) || page._wtGeneration !== generation ||
                page._wtRoomViews[room.RoomId] !== view || view.avatars.indexOf(record) < 0 ||
                record.image.parentNode !== record.element || !page.contains(record.element)) return false;
            var currentRoom = (page._wtRooms || []).filter(function (value) { return value.RoomId === room.RoomId; })[0];
            return !!(roomAvatarUserId(room, record.userId) && roomAvatarUserId(currentRoom, record.userId));
        }
        view.avatars.forEach(function (record) {
            if (!currentMember(record)) return;
            var url = roomAvatarUrl(page, room, record.userId);
            if (!url || record.url === url) return;
            var failureKey = JSON.stringify([room.RoomId, idKey(record.userId), url]);
            if (page._wtAvatarFailures[failureKey]) return;
            record.url = url;
            function current() {
                return currentMember(record) && record.url === url && record.image.getAttribute('src') === url;
            }
            record.image.onload = function () {
                if (!current()) return;
                record.image.hidden = false;
                record.fallback.hidden = true;
            };
            record.image.onerror = function () {
                if (!current()) return;
                // Failures are suppressed only for this page generation.
                page._wtAvatarFailures[failureKey] = true;
                record.image.hidden = true;
                record.image.removeAttribute('src');
                record.fallback.hidden = false;
            };
            record.image.src = url;
        });
    }

    function clearRoomBackdrop(view) {
        if (!view || !view.backdrop) return;
        view.backdrop.onload = null;
        view.backdrop.onerror = null;
        view.backdrop.removeAttribute('src');
        view.backdrop.hidden = true;
        view.presentation.classList.remove('wt-roomPresentation--image');
        view.backdropUrl = null;
        view.backdropTarget = null;
    }

    function updateRoomBackdrop(page, room, view, media) {
        var url = media && media.title && media.backdropUrl;
        var target = page._wtMediaTargets[room.RoomId];
        if (!url || !target) {
            clearRoomBackdrop(view);
            return;
        }
        // Rechecking metadata must not reload an unchanged image every poll.
        if (view.backdropUrl === url && view.backdropTarget === target) return;
        clearRoomBackdrop(view);
        view.backdropUrl = url;
        view.backdropTarget = target;
        var generation = page._wtGeneration;
        function current() {
            var currentMedia = page._wtMedia[room.RoomId];
            var currentState = page._wtRoomStates[room.RoomId];
            return pageActionAllowed(page) && page._wtGeneration === generation &&
                page._wtRoomViews[room.RoomId] === view && page._wtMediaTargets[room.RoomId] === target &&
                currentState && idKey(mediaItemId(currentState.state)) === idKey(media.itemId) &&
                currentMedia && currentMedia.backdropUrl === url && view.backdrop.getAttribute('src') === url;
        }
        view.backdrop.onload = function () {
            if (!current()) return;
            view.backdrop.hidden = false;
            view.presentation.classList.add('wt-roomPresentation--image');
        };
        view.backdrop.onerror = function () {
            if (!current()) return;
            view.backdrop.hidden = true;
            view.backdrop.removeAttribute('src');
            view.presentation.classList.remove('wt-roomPresentation--image');
            // Keep this target/url marked attempted until either changes.
        };
        view.backdrop.src = url;
    }

    function waitingDecoration() {
        var ns = 'http://www.w3.org/2000/svg';
        var svg = document.createElementNS(ns, 'svg');
        svg.setAttribute('viewBox', '0 0 240 160');
        svg.setAttribute('class', 'wt-waitingDecoration');
        svg.setAttribute('aria-hidden', 'true');
        svg.setAttribute('focusable', 'false');
        var paths = [
            'M40 16h160v82H40z M52 103h136 M120 99v12',
            'M51 112q0-12 12-12h24q12 0 12 12v31H51z M46 123v20h58v-20 M58 144v8 M93 144v8',
            'M141 112q0-12 12-12h24q12 0 12 12v31h-48z M136 123v20h58v-20 M148 144v8 M183 144v8',
            'M16 55h9 M20 36l7 6 M20 74l7-6 M215 55h9 M213 42l7-6 M213 68l7 6'
        ];
        paths.forEach(function (d) {
            var path = document.createElementNS(ns, 'path');
            path.setAttribute('d', d);
            path.setAttribute('fill', 'none');
            path.setAttribute('stroke', 'currentColor');
            path.setAttribute('stroke-width', '2');
            path.setAttribute('stroke-linecap', 'round');
            path.setAttribute('stroke-linejoin', 'round');
            svg.appendChild(path);
        });
        return svg;
    }

    function updateRoomPresentation(page, room) {
        var view = page._wtRoomViews[room.RoomId];
        if (!view) return;
        var info = roomPresentation(page, room);
        view.state.textContent = info.label;
        view.state.title = info.description;
        view.state.className = 'wt-roomState wt-roomState-' + info.className;
        view.description.textContent = info.description;
        var media = page._wtMedia[room.RoomId];
        var state = page._wtRoomStates[room.RoomId];
        var prefix = state && state.state && state.state.State === 'Barrier' ? '正在同步\n' : '正在一起观看\n';
        var hasTitle = !!(media && media.title);
        view.presentation.classList.toggle('wt-roomPresentation--media', hasTitle);
        view.waiting.hidden = hasTitle;
        view.waitingTitle.textContent = info.label === '等待播放' ? '等待双方打开视频' : info.label;
        view.waitingTitle.hidden = hasTitle;
        view.mediaLabel.textContent = hasTitle ? prefix : media && media.unavailable ? '媒体信息暂不可用' : '';
        view.mediaTitle.textContent = hasTitle ? media.heading || media.title : '';
        view.mediaDetail.textContent = hasTitle ? media.detail || '' : '';
        view.mediaDetail.hidden = !view.mediaDetail.textContent;
        view.description.hidden = hasTitle;
        view.media.hidden = !hasTitle && !(media && media.unavailable);
        if (view.pauseButton) {
            var primaryPause = !!(state && state.state && state.state.State === 'Watching' && state.state.StatusReason === 'watching');
            view.pauseButton.classList.toggle('button-submit', primaryPause);
            view.pauseButton.classList.toggle('raised-outline', !primaryPause);
        }
        updateRoomBackdrop(page, room, view, media);
    }

    function clearRoomStateExpiry(page, roomId) {
        if (!page._wtStateExpiryTimers) return;
        clearTimeout(page._wtStateExpiryTimers[roomId]);
        delete page._wtStateExpiryTimers[roomId];
    }

    function renewRoomStateExpiry(page, room) {
        var roomId = room.RoomId;
        var generation = page._wtGeneration;
        clearRoomStateExpiry(page, roomId);
        var timer = setTimeout(function () {
            if (!pageActionAllowed(page) || page._wtGeneration !== generation || page._wtStateExpiryTimers[roomId] !== timer) return;
            delete page._wtStateExpiryTimers[roomId];
            if (!(page._wtRooms || []).some(function (currentRoom) { return currentRoom.RoomId === roomId; })) return;
            page._wtRoomStates[roomId] = { failed: true, expired: true };
            delete page._wtMediaTargets[roomId];
            delete page._wtMedia[roomId];
            updateRoomPresentation(page, room);
        }, 10000);
        page._wtStateExpiryTimers[roomId] = timer;
    }

    function loadRoomMedia(page, room) {
        if (!pageActionAllowed(page) || page._wtVisibleRoute !== 'rooms' || page._wtMediaFlights[room.RoomId]) return;
        var roomId = room.RoomId;
        var sequence = (page._wtRoomSequences[roomId] || 0) + 1;
        page._wtRoomSequences[roomId] = sequence;
        var flights = page._wtMediaFlights;
        flights[roomId] = true;
        var requestedAt = Date.now();
        function current() {
            return pageActionAllowed(page) && page._wtRoomSequences[roomId] === sequence &&
                (page._wtRooms || []).some(function (value) { return value.RoomId === roomId; });
        }
        return apiGet(page, 'WatchTogether/Rooms/' + encodeURIComponent(roomId) + '/State').finally(function () {
            delete flights[roomId];
        }).then(function (state) {
            if (!current()) return;
            if (Date.now() - requestedAt >= 10000) throw new Error('stale_state');
            if (!state || state.RoomId !== roomId) throw new Error('invalid_state');
            page._wtRoomStates[roomId] = { state: state };
            renewRoomStateExpiry(page, room);
            var itemId = mediaItemId(state);
            var target = page._wtMediaTargets[roomId];
            if (!itemId || !target || idKey(target.itemId) !== idKey(itemId)) {
                delete page._wtMedia[roomId];
                target = itemId ? { itemId: itemId, sequence: sequence } : null;
                page._wtMediaTargets[roomId] = target;
            }
            // This read is fresher than Rooms, but the endpoint is not an atomic snapshot.
            renderRooms(page, page._wtRooms || []);
            if (!itemId) return;
            if (page._wtMetadataFlights[roomId]) return;
            var identity = page._wtIdentity;
            if (!identity.user || typeof ApiClient.getItem !== 'function') {
                page._wtMedia[roomId] = { unavailable: true };
                updateRoomPresentation(page, room);
                return;
            }
            var metadataFlights = page._wtMetadataFlights;
            metadataFlights[roomId] = true;
            function currentMedia() {
                return pageActionAllowed(page) && page._wtMediaTargets[roomId] === target &&
                    page._wtRoomStates[roomId] && idKey(mediaItemId(page._wtRoomStates[roomId].state)) === idKey(itemId) &&
                    (page._wtRooms || []).some(function (value) { return value.RoomId === roomId; });
            }
            return pageRequest(page, 'item:' + roomId + ':' + sequence, function () {
                return ApiClient.getItem(identity.user, itemId);
            }).then(function (item) {
                if (!currentMedia()) return;
                var title = item && idKey(item.Id) === idKey(itemId) ? mediaTitle(item) : '';
                var heading = title && item.Type === 'Episode' && typeof item.SeriesName === 'string' && item.SeriesName.trim()
                    ? item.SeriesName.trim() : title && typeof item.Name === 'string' ? item.Name.trim() : '';
                page._wtMedia[roomId] = title ? {
                    title: title,
                    heading: heading || title,
                    detail: heading ? title.slice(heading.length).replace(/^ · /, '') : '',
                    itemId: itemId,
                    backdropUrl: roomBackdropUrl(page, item)
                } : { unavailable: true };
                updateRoomPresentation(page, room);
            }).catch(function () {
                if (!currentMedia()) return;
                page._wtMedia[roomId] = { unavailable: true };
                updateRoomPresentation(page, room);
            }).finally(function () { delete metadataFlights[roomId]; });
        }).catch(function () {
            if (!current()) return;
            clearRoomStateExpiry(page, roomId);
            page._wtRoomStates[roomId] = { failed: true };
            delete page._wtMediaTargets[roomId];
            delete page._wtMedia[roomId];
            updateRoomPresentation(page, room);
        });
    }

    function renderRooms(page, rooms) {
        var container = page.querySelector('#wtRooms');
        if (!container) {
            return;
        }

        var renderedRooms = (rooms || []).map(function (room) {
            var entry = page._wtRoomStates[room.RoomId];
            return entry && entry.state ? Object.assign({}, room, entry.state) : room;
        });
        var signature = JSON.stringify([renderedRooms.map(function (room) {
            return [room.RoomId, room.Name, room.State, room.StatusReason, room.ParticipantUserIds, room.PrimaryUserId,
                room.JoinedParticipantUserIds, room.Participants, room.IsAdmin, room.CanEnd, room.CurrentUserJoined];
        }), page._wtRoomBusy, page._wtRoomFeedback, page._wtIsAdmin]);
        if (page._wtRoomsSignature === signature) {
            container.setAttribute('aria-busy', 'false');
            (rooms || []).forEach(function (room) { updateRoomPresentation(page, room); });
            return;
        }
        page._wtRoomsSignature = signature;
        var focused = page.ownerDocument.activeElement;
        var focusedCard = focused && focused.closest ? focused.closest('[data-room-id]') : null;
        var focusRoom = focusedCard && focusedCard.dataset.roomId;
        var focusAction = focused && focused.dataset && focused.dataset.action;
        var focusSummary = focused && focused.tagName === 'SUMMARY';
        rememberDiagnosticPanelState(page);
        Object.keys(page._wtRoomViews || {}).forEach(function (roomId) {
            clearRoomBackdrop(page._wtRoomViews[roomId]);
            clearRoomAvatars(page._wtRoomViews[roomId]);
        });
        clearChildren(container);
        page._wtDiagnosticPanels = {};
        page._wtDiagnosticBusy = page._wtDiagnosticBusy || {};
        page._wtDiagnosticRequests = page._wtDiagnosticRequests || {};
        page._wtDiagnosticOpen = page._wtDiagnosticOpen || {};
        page._wtRoomViews = {};
        container.setAttribute('aria-busy', 'false');
        if (page._wtIsAdmin !== undefined) {
            setAdminVisibility(page, page._wtIsAdmin);
        }
        if (!rooms || rooms.length === 0) {
            var empty = document.createElement('div');
            empty.className = 'wt-emptyState';
            var emptyTitle = document.createElement('strong');
            emptyTitle.textContent = page._wtIsAdmin === true ? '还没有房间' : '暂无参与的房间';
            var emptyText = document.createElement('p');
            emptyText.className = 'fieldDescription';
            emptyText.textContent = page._wtIsAdmin === true
                ? '到“创建与邀请”创建房间或邀请码，再让两位参与者打开同一视频。'
                : '到“邀请”接受邀请码，或请管理员创建房间。';
            empty.appendChild(emptyTitle);
            empty.appendChild(emptyText);
            container.appendChild(empty);
            return;
        }

        renderedRooms.forEach(function (room) {
            var card = document.createElement('article');
            card.className = 'wt-roomCard';
            card.dataset.roomId = room.RoomId;
            var header = document.createElement('div');
            header.className = 'wt-roomHeader';
            var name = document.createElement('h3');
            name.className = 'wt-roomName';
            name.textContent = room.Name || '未命名房间';
            var state = document.createElement('span');
            header.appendChild(name);
            header.appendChild(state);
            card.appendChild(header);

            var layout = document.createElement('div');
            layout.className = 'wt-roomLayout';
            var main = document.createElement('div');
            main.className = 'wt-roomMain';
            var presentation = document.createElement('div');
            presentation.className = 'wt-roomPresentation';
            var backdrop = document.createElement('img');
            backdrop.className = 'wt-roomBackdrop';
            backdrop.alt = '';
            backdrop.setAttribute('aria-hidden', 'true');
            backdrop.decoding = 'async';
            backdrop.hidden = true;
            presentation.appendChild(backdrop);
            var waiting = document.createElement('div');
            waiting.className = 'wt-waitingArtwork';
            waiting.setAttribute('aria-hidden', 'true');
            waiting.appendChild(waitingDecoration());
            presentation.appendChild(waiting);
            var waitingTitle = document.createElement('h4');
            waitingTitle.className = 'wt-waitingTitle';
            presentation.appendChild(waitingTitle);
            var stateDescription = document.createElement('p');
            stateDescription.className = 'fieldDescription wt-roomStateDescription';
            presentation.appendChild(stateDescription);
            var media = document.createElement('p');
            media.className = 'wt-roomMedia';
            var mediaLabel = document.createElement('span');
            mediaLabel.className = 'wt-roomMediaLabel';
            var mediaTitle = document.createElement('strong');
            var mediaDetail = document.createElement('span');
            mediaDetail.className = 'wt-roomMediaDetail';
            media.appendChild(mediaLabel);
            media.appendChild(mediaTitle);
            media.appendChild(mediaDetail);
            presentation.appendChild(media);
            main.appendChild(presentation);

            var meta = document.createElement('div');
            meta.className = 'wt-participants';
            var participantIds = room.ParticipantUserIds || [];
            var avatars = [];
            participantIds.forEach(function (id) {
                var participant = document.createElement('div');
                participant.className = 'wt-participant';
                var displayName = roomUserName(page, room, id);
                var avatar = document.createElement('span');
                avatar.className = 'wt-participantAvatar';
                avatar.setAttribute('aria-hidden', 'true');
                var avatarFallback = document.createElement('span');
                avatarFallback.className = 'wt-participantAvatarFallback';
                avatarFallback.textContent = Array.from(String(displayName).trim())[0] || '?';
                var avatarImage = document.createElement('img');
                avatarImage.className = 'wt-participantAvatarImage';
                avatarImage.alt = '';
                avatarImage.setAttribute('aria-hidden', 'true');
                avatarImage.decoding = 'async';
                avatarImage.hidden = true;
                avatar.appendChild(avatarFallback);
                avatar.appendChild(avatarImage);
                avatars.push({ userId: id, element: avatar, image: avatarImage, fallback: avatarFallback, url: null });
                participant.appendChild(avatar);
                var text = document.createElement('div');
                text.className = 'wt-participantText';
                var participantName = document.createElement('div');
                participantName.className = 'wt-participantName';
                participantName.textContent = displayName;
                if (idKey(id) === idKey(room.PrimaryUserId)) {
                    var primary = document.createElement('small');
                    primary.className = 'wt-primaryLabel';
                    primary.textContent = '主用户';
                    participantName.appendChild(primary);
                }
                var joined = document.createElement('div');
                joined.className = 'wt-participantState';
                var isJoined = (room.JoinedParticipantUserIds || []).map(idKey).indexOf(idKey(id)) >= 0;
                joined.textContent = isJoined ? '已加入' : '已暂离';
                joined.classList.toggle('wt-participantState--joined', isJoined);
                text.appendChild(participantName);
                text.appendChild(joined);
                participant.appendChild(text);
                meta.appendChild(participant);
            });
            main.appendChild(meta);
            layout.appendChild(main);

            var controls = document.createElement('aside');
            controls.className = 'wt-roomControls';
            var controlsTitle = document.createElement('h3');
            controlsTitle.textContent = '房间控制';
            controls.appendChild(controlsTitle);
            function groupHeading(group, title) {
                var heading = document.createElement('h4');
                heading.className = 'wt-controlHeading';
                heading.textContent = title;
                group.appendChild(heading);
            }
            var playback = document.createElement('div');
            playback.className = 'wt-roomActions wt-roomActions--playback';
            playback.setAttribute('role', 'group');
            playback.setAttribute('aria-label', '播放控制');
            groupHeading(playback, '播放控制');
            if (room.CurrentUserJoined && !room.IsAdmin) {
                playback.appendChild(createActionButton(page, room, 'participantResync'));
            }
            var pauseButton = null;
            if (room.IsAdmin) {
                ['pause', 'resume', 'resync'].forEach(function (action) {
                    var button = createActionButton(page, room, action);
                    if (action === 'pause') pauseButton = button;
                    playback.appendChild(button);
                });
            }
            if (playback.querySelector('button')) controls.appendChild(playback);
            var actions = document.createElement('div');
            actions.className = 'wt-roomActions wt-roomActions--members';
            actions.setAttribute('role', 'group');
            actions.setAttribute('aria-label', '成员与诊断');
            groupHeading(actions, '房间成员');
            var isMember = participantIds.map(idKey).indexOf(idKey(page._wtIdentity.user)) >= 0;
            if (isMember) actions.appendChild(createActionButton(page, room, room.CurrentUserJoined ? 'leave' : 'join'));
            if (room.IsAdmin) {
                actions.appendChild(createActionButton(page, room, 'delete'));
            } else if (room.CanEnd) {
                actions.appendChild(createActionButton(page, room, 'delete'));
            }
            if (actions.querySelector('button')) controls.appendChild(actions);
            var membershipLine = document.createElement('p');
            membershipLine.className = 'fieldDescription wt-roomMembership';
            membershipLine.hidden = !isMember;
            membershipLine.textContent = '暂离保留成员关系；结束房间会解除双方成员关系。';
            controls.appendChild(membershipLine);
            var diagnosticsAction = document.createElement('div');
            diagnosticsAction.className = 'wt-diagnosticAction';
            diagnosticsAction.appendChild(createActionButton(page, room, 'diagnostics'));
            var diagnosticHelp = document.createElement('p');
            diagnosticHelp.className = 'fieldDescription';
            diagnosticHelp.textContent = '查看同步状态与排查信息';
            diagnosticsAction.appendChild(diagnosticHelp);
            controls.appendChild(diagnosticsAction);
            layout.appendChild(controls);
            card.appendChild(layout);

            var feedback = page._wtRoomFeedback && page._wtRoomFeedback[room.RoomId];
            var feedbackEl = document.createElement('div');
            feedbackEl.className = 'wt-roomFeedback' + (feedback && feedback.isError ? ' error' : '');
            feedbackEl.setAttribute('role', feedback && feedback.isError ? 'alert' : 'status');
            feedbackEl.setAttribute('aria-live', 'polite');
            feedbackEl.textContent = feedback ? feedback.text : '';
            feedbackEl.hidden = !feedback;
            card.appendChild(feedbackEl);
            card.appendChild(createDiagnosticPanel(page, room));
            page._wtRoomViews[room.RoomId] = {
                state: state, description: stateDescription, media: media, mediaLabel: mediaLabel,
                mediaTitle: mediaTitle, mediaDetail: mediaDetail, presentation: presentation, backdrop: backdrop,
                waiting: waiting, waitingTitle: waitingTitle, pauseButton: pauseButton, avatars: avatars
            };
            updateRoomPresentation(page, room);
            container.appendChild(card);
            startRoomAvatars(page, room, page._wtRoomViews[room.RoomId]);
            if (focusRoom === room.RoomId) {
                var target = focusAction ? card.querySelector('[data-action="' + focusAction + '"]') : focusSummary ? card.querySelector('summary') : null;
                if (target) target.focus({ preventScroll: true });
            }
        });
    }

    function loadRooms(page, announce) {
        if (!pageActionAllowed(page)) return Promise.resolve();
        if (page._wtRoomsLoading) {
            return Promise.resolve();
        }

        var container = page.querySelector('#wtRooms');
        page._wtRoomsLoading = true;
        if (container) {
            container.setAttribute('aria-busy', 'true');
        }
        if (announce) {
            setStatus(page, '正在刷新房间…');
        }

        return apiGet(page, 'WatchTogether/Rooms').then(function (rooms) {
            var list = Array.isArray(rooms) ? rooms : [];
            page._wtRooms = list;
            Object.keys(page._wtRoomStates).forEach(function (roomId) {
                if (!list.some(function (room) { return room.RoomId === roomId; })) {
                    page._wtRoomSequences[roomId] = (page._wtRoomSequences[roomId] || 0) + 1;
                    clearRoomStateExpiry(page, roomId);
                    delete page._wtRoomStates[roomId];
                    delete page._wtMediaTargets[roomId];
                    delete page._wtMedia[roomId];
                }
            });
            page._wtDiagnostics = page._wtDiagnostics || {};
            Object.keys(page._wtDiagnostics).forEach(function (roomId) {
                if (!list.some(function (room) { return room && room.RoomId === roomId; })) {
                    delete page._wtDiagnostics[roomId];
                }
            });
            Object.keys(page._wtDiagnosticOpen || {}).forEach(function (roomId) {
                if (!list.some(function (room) { return room && room.RoomId === roomId; })) {
                    delete page._wtDiagnosticOpen[roomId];
                }
            });
            renderRooms(page, list);
            if (page._wtVisibleRoute === 'rooms') list.forEach(function (room) { loadRoomMedia(page, room); });
            syncForm(page);
            if (announce) {
                setStatus(page, list.length > 0
                    ? '已更新 ' + list.length + ' 个房间'
                    : page._wtIsAdmin === true ? '暂无房间，可以创建一个。' : '暂无参与的房间。');
            }
            return list;
        }).catch(function (err) {
            (page._wtRooms || []).forEach(function (room) {
                clearRoomStateExpiry(page, room.RoomId);
                page._wtRoomSequences[room.RoomId] = (page._wtRoomSequences[room.RoomId] || 0) + 1;
                page._wtRoomStates[room.RoomId] = { failed: true };
                delete page._wtMediaTargets[room.RoomId];
                delete page._wtMedia[room.RoomId];
                updateRoomPresentation(page, room);
            });
            page._wtRooms = null;
            syncForm(page);
            setStatus(page, '房间加载失败：' + errorMessage(err) + '。', true);
            return [];
        }).then(function (result) {
            page._wtRoomsLoading = false;
            return result;
        });
    }

    function setButtonBusy(button, isBusy, busyLabel) {
        if (!button) {
            return;
        }

        if (isBusy) {
            button._wtOriginalLabel = button.textContent;
            button.disabled = true;
            button.setAttribute('aria-busy', 'true');
            button.textContent = busyLabel;
        } else {
            button.disabled = false;
            button.removeAttribute('aria-busy');
            if (button._wtOriginalLabel) {
                button.textContent = button._wtOriginalLabel;
            }
        }
    }

    function control(page, roomId, action, button) {
        if (!pageActionAllowed(page)) return;
        var label = actionLabels[action] || '操作';
        if (action === 'resync' && !window.confirm('重新同步会暂时暂停双方并重新对齐，确认继续吗？')) {
            return;
        }
        setRoomBusy(page, roomId, true);
        clearRoomFeedback(page, roomId);
        roomFeedback(page, roomId, label + '处理中…', false, true);
        renderRooms(page, page._wtRooms || []);
        apiSend(page, 'WatchTogether/Rooms/' + encodeURIComponent(roomId) + '/Action', 'POST', { Action: action })
            .then(function (result) {
                if (result && result.Error) {
                    roomFeedback(page, roomId, label + '已发起，但部分客户端未完成，请检查客户端。', true, true);
                } else if (action === 'resync') {
                    roomFeedback(page, roomId, '重新同步已开始，播放可能暂时暂停，请等待同步完成。', false, false);
                } else if (result && result.Users && result.Users.length > 0) {
                    roomFeedback(page, roomId, label + '已向 ' + result.Users.length + ' 个当前会话发送。', false, false);
                } else if (result && result.Users) {
                    roomFeedback(page, roomId, label + '没有可控制的当前会话。', true, true);
                } else {
                    roomFeedback(page, roomId, label + '已开始，等待完成。', false, false);
                }
                return loadRooms(page, false);
            })
            .catch(function (err) {
                roomFeedback(page, roomId, label + '失败：' + errorMessage(err), true, true);
            })
            .then(function () {
                setRoomBusy(page, roomId, false);
                renderRooms(page, page._wtRooms || []);
            });
    }

    function participantResync(page, roomId, button) {
        if (!pageActionAllowed(page)) return;
        setRoomBusy(page, roomId, true);
        clearRoomFeedback(page, roomId);
        roomFeedback(page, roomId, '重新同步请求处理中…', false, true);
        renderRooms(page, page._wtRooms || []);
        apiSend(page, 'WatchTogether/Rooms/' + encodeURIComponent(roomId) + '/Resync', 'POST')
            .then(function (result) {
                var status = result && result.Status;
                if (status === 'accepted') {
                    roomFeedback(page, roomId, '重新同步请求已受理，双方将暂时暂停并重新对齐。', false, false);
                } else if (status === 'busy') {
                    roomFeedback(page, roomId, result.Reason === 'resync_cooldown'
                        ? '刚刚已请求重新同步，请稍后再试。'
                        : '同步正在进行，请等待当前同步完成。', true, true);
                } else if (status === 'unavailable') {
                    roomFeedback(page, roomId, result.Reason === 'snapshot_unavailable'
                        ? '当前播放会话暂时不可读，请稍后再试。'
                        : '当前房间暂时不可用，请稍后再试。', true, true);
                } else {
                    roomFeedback(page, roomId, '重新同步请求未完成，请稍后再试。', true, true);
                }
                return loadRooms(page, false);
            })
            .catch(function (err) {
                roomFeedback(page, roomId, '重新同步请求失败：' + errorMessage(err), true, true);
            })
            .then(function () {
                setRoomBusy(page, roomId, false);
                renderRooms(page, page._wtRooms || []);
            });
    }

    function deleteRoom(page, room, button) {
        if (!pageActionAllowed(page)) return;
        var roomName = room.Name || '未命名房间';
        var ending = room.CanEnd === true;
        if (!window.confirm((ending ? '确认结束“' : '确认删除“') + roomName + '”吗？' +
            (ending ? '结束房间会解除双方成员关系，只删除同步关系，不删除媒体。' : '这只删除同步关系，不删除媒体。'))) {
            return;
        }

        setRoomBusy(page, room.RoomId, true);
        roomFeedback(page, room.RoomId, '删除中…', false, true);
        renderRooms(page, page._wtRooms || []);
        apiSend(page, 'WatchTogether/Rooms/' + encodeURIComponent(room.RoomId), 'DELETE')
            .then(function (result) {
                if (result && result.Deleted === false) {
                    throw new Error('房间不存在或已删除');
                }
                clearRoomFeedback(page, room.RoomId);
                setTransientStatus(page, ending
                    ? '房间“' + roomName + '”已结束；双方成员关系已解除，媒体未删除。'
                    : '房间“' + roomName + '”已删除；只移除同步关系，媒体未删除。', false);
                return loadRooms(page, false);
            })
            .catch(function (err) {
                roomFeedback(page, room.RoomId, '删除失败：' + errorMessage(err), true, true);
            })
            .then(function () {
                setRoomBusy(page, room.RoomId, false);
                renderRooms(page, page._wtRooms || []);
            });
    }

    function membership(page, room, action, button) {
        if (!pageActionAllowed(page)) return;
        if (action === 'leave' && !window.confirm('暂离“' + (room.Name || '未命名房间') + '”吗？暂离不会解除成员关系，并将尝试暂停仍在房间的一方。')) {
            return;
        }
        setRoomBusy(page, room.RoomId, true);
        clearRoomFeedback(page, room.RoomId);
        roomFeedback(page, room.RoomId, action === 'join' ? '加入中…' : '暂离中…', false, true);
        renderRooms(page, page._wtRooms || []);
        apiSend(page, 'WatchTogether/Rooms/' + encodeURIComponent(room.RoomId) + '/' + (action === 'join' ? 'Join' : 'Leave'), 'POST')
            .then(function (result) {
                var text = action === 'join'
                    ? '已加入房间，请与另一位参与者打开同一视频。'
                    : getLeaveFeedback(result);
                roomFeedback(page, room.RoomId, text,
                    action === 'leave' && Number(result && result.PauseFailed) > 0, false);
                return loadRooms(page, false);
            })
            .catch(function (err) {
                roomFeedback(page, room.RoomId, (action === 'join' ? '加入' : '暂离') + '失败：' + errorMessage(err), true, true);
            })
            .then(function () {
                setRoomBusy(page, room.RoomId, false);
                renderRooms(page, page._wtRooms || []);
            });
    }

    function getLeaveFeedback(result) {
        var attempted = Number(result && result.PauseAttempted) || 0;
        var succeeded = Number(result && result.PauseSucceeded) || 0;
        var failed = Number(result && result.PauseFailed) || 0;
        if (failed > 0) {
            return '已暂离房间，但仍在房间的一方暂停失败，请检查客户端；成员关系仍保留。';
        }
        if (attempted > 0 && succeeded === attempted) {
            return '已暂离房间，仍在房间的一方已暂停；成员关系仍保留。';
        }
        return '已暂离房间，自动同步已停止；成员关系仍保留。';
    }

    function createRoom(page) {
        if (!pageActionAllowed(page)) return;
        if (page._wtIsAdmin !== true) return;
        var nameInput = page.querySelector('#wtRoomName');
        var participantA = page.querySelector('#wtUserA');
        var participantB = page.querySelector('#wtUserB');
        var primary = page.querySelector('#wtPrimary');
        var createButton = page.querySelector('#wtCreate');
        var name = nameInput ? nameInput.value.trim() : '';
        var a = participantA ? participantA.value : '';
        var b = participantB ? participantB.value : '';

        if (!name) {
            setStatus(page, '请先填写房间名称。', true);
            if (nameInput) {
                nameInput.focus();
            }
            return;
        }
        if (page._wtUsers.length < 2 || !a || !b) {
            setStatus(page, '至少需要选择两名参与者。', true);
            return;
        }
        if (a === b) {
            setStatus(page, '请选择两名不同的参与者。', true);
            return;
        }
        var conflict = findRoomConflict(page, [a, b]);
        if (conflict) {
            var message = conflictMessage(page, conflict);
            setStatus(page, message, true);
            setFormHint(page, message, true);
            syncForm(page);
            return;
        }
        if (!primary || !primary.value || (primary.value !== a && primary.value !== b)) {
            setStatus(page, '请选择参与者中的一人为主用户。', true);
            return;
        }

        setButtonBusy(createButton, true, '创建中…');
        setCreateStatus(page, '正在创建房间…', false);
        setStatus(page, '正在创建房间…');
        apiSend(page, 'WatchTogether/Rooms', 'POST', {
            Name: name,
            ParticipantUserIds: [a, b],
            PrimaryUserId: primary.value
        }).then(function (result) {
            var created = result || {};
            // A successful POST is a completed fact, independent of list refresh.
            setCreateStatus(page, '房间“' + name + '”已创建，请让双方打开同一视频。', false);
            page._wtPendingCreatedFeedback = {
                name: name,
                roomId: created.RoomId
            };
            nameInput.value = '';
            syncForm(page);
            return loadRooms(page, true);
        }).catch(function (err) {
            setCreateStatus(page, '创建失败：' + errorMessage(err), true);
            setStatus(page, '创建失败：' + errorMessage(err), true);
        }).then(function () {
            setButtonBusy(createButton, false);
            syncForm(page);
            if (page._wtPendingCreatedFeedback && page._wtPendingCreatedFeedback.roomId) {
                roomFeedback(page, page._wtPendingCreatedFeedback.roomId,
                    '房间“' + page._wtPendingCreatedFeedback.name + '”已创建，请让双方打开同一视频。', false, false);
                page._wtPendingCreatedFeedback = null;
                renderRooms(page, page._wtRooms || []);
            }
        });
    }

    function bindPageEvents(page) {
        if (page._wtEventsBound) {
            return;
        }

        dom.addEventListener(page.querySelector('#wtSaveConfig'), 'click', function () {
            savePluginConfiguration(page);
        });
        dom.addEventListener(page.querySelector('#wtSaveGitHubToken'), 'click', function () {
            saveGitHubToken(page);
        });
        dom.addEventListener(page.querySelector('#wtClearGitHubToken'), 'click', function () {
            clearGitHubToken(page);
        });
        dom.addEventListener(page.querySelector('#wtCreate'), 'click', function () {
            createRoom(page);
        });
        dom.addEventListener(page.querySelector('#wtCreateInvitation'), 'click', function () {
            createInvitation(page);
        });
        dom.addEventListener(page.querySelector('#wtAcceptInvitation'), 'click', function () {
            acceptInvitation(page);
        });
        dom.addEventListener(page.querySelector('#wtCopyInvitationCode'), 'click', function (event) {
            copyInvitationCode(page, event.currentTarget);
        });
        dom.addEventListener(page.querySelector('#wtRefresh'), 'click', function () {
            if (!pageActionAllowed(page)) return;
            clearAllRoomFeedback(page);
            loadRooms(page, true);
        });
        dom.addEventListener(page.querySelector('#wtRoomName'), 'input', function () {
            syncForm(page);
        });
        dom.addEventListener(page.querySelector('#wtUserA'), 'change', function () {
            syncForm(page);
        });
        dom.addEventListener(page.querySelector('#wtUserB'), 'change', function () {
            syncForm(page);
        });
        dom.addEventListener(page.querySelector('#wtPrimary'), 'change', function () {
            syncForm(page);
        });
        page._wtEventsBound = true;
    }

    function View() {
        BaseView.apply(this, arguments);
    }

    Object.assign(View.prototype, BaseView.prototype);

    View.prototype.onResume = function () {
        BaseView.prototype.onResume.apply(this, arguments);
        var page = this.view;
        window.removeEventListener('focus', page._wtRefreshVisible);
        page._wtActive = false;
        page._wtGeneration = (page._wtGeneration || 0) + 1;
        page._wtRoute = currentRoute(page);
        clearPageData(page);
        page._wtIdentity = pageIdentity();
        page._wtActive = true;
        if (!pageActionAllowed(page)) return;
        applyRoute(page);
        bindPageEvents(page);
        syncForm(page);
        loading.show();

        loadUsers(page).then(function () {
            return loadRooms(page, true);
        }).then(function () {
            if (page._wtVisibleRoute === 'invite') return loadInvitations(page);
        }).then(function () {
            if (page._wtVisibleRoute === 'settings') {
                return loadPluginConfiguration(page).catch(function () { return null; }).then(function () { return loadPluginInfo(page); });
            }
        }).then(function () {
            loading.hide();
        }, function () {
            loading.hide();
        });

        clearInterval(page._wtTimer);
        page._wtRefreshVisible = function () {
            if (!pageActionAllowed(page)) return;
            if (page._wtVisibleRoute === 'rooms') loadRooms(page, false);
            else if (page._wtVisibleRoute === 'invite') {
                loadRooms(page, false);
                loadInvitations(page);
            }
        };
        page._wtTimer = setInterval(page._wtRefreshVisible, 5000);
        window.addEventListener('focus', page._wtRefreshVisible);
    };

    View.prototype.onPause = function () {
        var page = this.view;
        page._wtActive = false;
        page._wtGeneration = (page._wtGeneration || 0) + 1;
        window.removeEventListener('focus', page._wtRefreshVisible);
        clearPageData(page);
        loading.hide();
        BaseView.prototype.onPause.apply(this, arguments);
    };

    return View;
});

define(['baseView', 'dom', 'loading', 'globalize', 'emby-input', 'emby-select', 'emby-button'], function (BaseView, dom, loading) {
    'use strict';

    var users = [];
    var pluginId = '0f8d1c2e-3b4a-4c5d-8e6f-7a8b9c0d1e2f';
    var stateLabels = {
        Waiting: '等待参与者',
        Barrier: '正在对齐',
        Watching: '同步中',
        Unavailable: '暂不可用'
    };
    var stateDescriptions = {
        Waiting: '等待两位参与者打开同一视频',
        Barrier: '正在对齐两位参与者的播放位置',
        Watching: '两位参与者已连接，播放会自动同步',
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

    function apiGet(path) {
        return ApiClient.getJSON(apiUrl(path));
    }

    function apiSend(path, method, body) {
        return ApiClient.fetch({
            url: apiUrl(path),
            type: method,
            data: body === undefined ? null : JSON.stringify(body),
            contentType: 'application/json',
            dataType: 'json'
        });
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
        command_failed: '播放控制未完成',
        aligning: '正在对齐播放位置',
        watching: '两位参与者已连接',
        remote_control_unavailable: '当前客户端不支持远程控制',
        unsupported_playback_rate: '播放速度不是 1 倍',
        waiting_for_playback: '等待双方打开同一视频并开始播放'
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
        eligibility_changed: '同步条件变化',
        manual_action: '手动操作',
        resync: '重新同步',
        observed: '观察事件'
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
        var recovery = raw.RecoveryWindow && typeof raw.RecoveryWindow === 'object' ? {
            active: raw.RecoveryWindow.Active === true,
            ageSeconds: diagnosticFiniteNumber(raw.RecoveryWindow.AgeSeconds),
            affectedAliases: (Array.isArray(raw.RecoveryWindow.AffectedAliases) ? raw.RecoveryWindow.AffectedAliases : [])
                .slice(0, 2).map(diagnosticAlias)
        } : { active: false, ageSeconds: null, affectedAliases: [] };
        return {
            schemaVersion: typeof raw.SchemaVersion === 'string' ? raw.SchemaVersion : null,
            pluginVersion: typeof raw.PluginVersion === 'string' ? raw.PluginVersion : null,
            roomState: diagnosticStateLabels[raw.RoomState] ? raw.RoomState : 'Unavailable',
            statusReason: diagnosticReasonLabels[raw.StatusReason] ? raw.StatusReason : null,
            snapshotHealth: diagnosticHealthLabels[raw.SnapshotHealth] ? raw.SnapshotHealth : 'unknown',
            participants: participants,
            sessions: sessions,
            pending: pending,
            barrier: barrier,
            recovery: recovery,
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
        var exportButton = document.createElement('button');
        exportButton.type = 'button';
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
            page._wtDiagnosticOpen[room.RoomId] = details.open;
        });
        page._wtDiagnosticPanels[room.RoomId] = { details: details, body: body };
        var diagnostic = page._wtDiagnostics && page._wtDiagnostics[room.RoomId];
        if (diagnostic) {
            renderDiagnosticDetails(page, room.RoomId, diagnostic, body);
        }
        details.open = page._wtDiagnosticOpen[room.RoomId] === true;
        return details;
    }

    function loadRoomDiagnostics(page, roomId, button) {
        var panel = page._wtDiagnosticPanels && page._wtDiagnosticPanels[roomId];
        if (!panel || (page._wtDiagnosticBusy && page._wtDiagnosticBusy[roomId])) return;
        page._wtDiagnosticBusy = page._wtDiagnosticBusy || {};
        page._wtDiagnosticRequests = page._wtDiagnosticRequests || {};
        var requestToken = String(Date.now()) + ':' + String(Math.random());
        page._wtDiagnosticRequests[roomId] = requestToken;
        page._wtDiagnosticBusy[roomId] = true;
        panel.details.open = true;
        clearChildren(panel.body);
        var loadingText = document.createElement('p');
        loadingText.className = 'fieldDescription';
        loadingText.textContent = '正在读取诊断…';
        panel.body.appendChild(loadingText);
        setButtonBusy(button, true, '读取中…');
        return apiGet('WatchTogether/Rooms/' + encodeURIComponent(roomId) + '/Diagnostics').then(function (raw) {
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
            currentPanel.details.open = true;
        }).finally(function () {
            if (page._wtDiagnosticRequests[roomId] === requestToken) {
                delete page._wtDiagnosticBusy[roomId];
                setButtonBusy(button, false);
            }
        });
    }

    function exportDiagnostic(page, roomId) {
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
            revoke.className = 'button-flat wt-action wt-action--danger';
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
        return apiGet('WatchTogether/Invitations').then(function (list) {
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
        var input = page.querySelector('#wtInvitationName');
        var button = page.querySelector('#wtCreateInvitation');
        var name = input ? input.value.trim() : '';
        setButtonBusy(button, true, '创建中…');
        setInvitationStatus(page, '正在创建邀请码…', false);
        apiSend('WatchTogether/Invitations', 'POST', { Name: name || null }).then(function (result) {
            result = result || {};
            if (!result.Code) {
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
            setInvitationStatus(page, '邀请码创建失败：' + errorMessage(error), true);
        }).then(function () {
            setButtonBusy(button, false);
        });
    }

    function copyInvitationCode(page, button) {
        var value = page._wtInvitationCode && page._wtInvitationCode.code;
        if (!value) {
            setInvitationStatus(page, '当前没有可复制的邀请码。', true);
            return;
        }
        var copied = window.navigator && window.navigator.clipboard && window.navigator.clipboard.writeText
            ? window.navigator.clipboard.writeText(value)
            : Promise.reject(new Error('clipboard_unavailable'));
        copied.then(function () {
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
        if (!invitation || !invitation.InvitationId) {
            return;
        }
        setButtonBusy(button, true, '撤销中…');
        apiSend('WatchTogether/Invitations/' + encodeURIComponent(invitation.InvitationId), 'DELETE').then(function () {
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
        apiSend('WatchTogether/Invitations/' + encodeURIComponent(code) + '/Accept', 'POST').then(function (result) {
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

    function isPermissionError(error) {
        return !!(error && (error.status === 401 || error.status === 403 ||
            error.statusCode === 401 || error.statusCode === 403));
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
            roomsHeading.textContent = isAdmin ? '2. 房间' : '我的房间';
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
        return ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            applyPluginConfiguration(page, config);
            setAdminVisibility(page, true);
            setConfigStatus(page, '配置已读取');
            return config;
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
        var pauseCheckbox = page.querySelector('#wtPauseOtherOnPlaybackStop');
        var notifyCheckbox = page.querySelector('#wtNotifyOtherOnPlaybackStop');
        var syncNotifyCheckbox = page.querySelector('#wtNotifyOnSyncActions');
        var updateChannel = page.querySelector('#wtUpdateChannel');
        if (!pauseCheckbox || !notifyCheckbox || !syncNotifyCheckbox || !updateChannel || !page._wtConfigReady) {
            return Promise.resolve();
        }

        setConfigBusy(page, true);
        setConfigStatus(page, '正在保存配置…');
        return ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            config = config || {};
            config.PauseOtherOnPlaybackStop = pauseCheckbox.checked;
            config.NotifyOtherOnPlaybackStop = notifyCheckbox.checked;
            config.NotifyOnSyncActions = syncNotifyCheckbox.checked;
            config.UpdateChannel = updateChannel.value === 'beta' ? 'beta' : 'stable';
            return ApiClient.updatePluginConfiguration(pluginId, config);
        }).then(function () {
            return ApiClient.getPluginConfiguration(pluginId);
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
        return apiGet('WatchTogether/Info').then(function (info) {
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

    function findUser(id) {
        return users.filter(function (user) {
            return user.Id === id;
        })[0] || null;
    }

    function userName(id) {
        var user = findUser(id);
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

    function roomUserName(room, id) {
        var participant = roomParticipant(room, id);
        if (participant && participant.Name) {
            return participant.Name;
        }
        var user = findUser(id);
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

    function conflictMessage(conflict) {
        return '用户“' + userName(conflict.userId) + '”已在房间“' + roomName(conflict.room) +
            '”中，请先退出或删除原房间，或者选择其他用户。';
    }

    function fillSelect(select, options, selected) {
        if (!select) {
            return;
        }

        clearChildren(select);
        options.forEach(function (user) {
            var opt = document.createElement('option');
            opt.value = user.Id;
            opt.textContent = user.Name;
            select.appendChild(opt);
        });

        var selectedExists = options.some(function (user) {
            return user.Id === selected;
        });
        if (selectedExists) {
            select.value = selected;
        } else if (options.length > 0) {
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

        fillSelect(primary, ids.map(findUser).filter(function (user) {
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
        if (users.length < 2) {
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
                hint = conflictMessage(conflict);
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
        return apiGet('WatchTogether/Users').then(function (list) {
            page._wtIsAdmin = true;
            setAdminVisibility(page, true);
            users = Array.isArray(list) ? list : [];
            page._wtRooms = null;
            fillSelect(page.querySelector('#wtUserA'), users, users.length > 0 ? users[0].Id : null);
            fillSelect(page.querySelector('#wtUserB'), users, users.length > 1 ? users[1].Id : null);
            syncForm(page);
        }).catch(function (err) {
            page._wtIsAdmin = isPermissionError(err) ? false : undefined;
            page._wtRooms = null;
            users = [];
            fillSelect(page.querySelector('#wtUserA'), [], null);
            fillSelect(page.querySelector('#wtUserB'), [], null);
            syncForm(page);
            if (isPermissionError(err)) {
                setAdminVisibility(page, false);
                setFormHint(page, '只有管理员可以创建房间。', true);
                setStatus(page, '当前账号没有管理员权限，已隐藏管理设置。', true);
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
        unsupported_playback_rate: '播放速度不是 1 倍，请恢复正常速度后重试。',
        waiting_for_playback: '等待双方打开同一视频并开始播放。'
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
        var toneClass = action === 'join'
            ? 'wt-action--primary'
            : action === 'leave' || action === 'delete'
                ? 'wt-action--danger'
                : action === 'resync' || action === 'participantResync'
                    ? 'wt-action--accent'
                    : '';
        button.className = 'button-flat wt-action' + (toneClass ? ' ' + toneClass : '');
        button.dataset.act = action;
        button.dataset.action = action;
        button.textContent = action === 'delete' ? (room.IsSelfService && room.CanEnd ? '结束房间' : '删除房间') : action === 'leave' ? '退出房间' : action === 'join' ? '加入房间' : action === 'diagnostics' ? '查看诊断' : actionLabels[action];
        button.title = action === 'delete'
            ? (room.IsSelfService && room.CanEnd ? '结束这个自助房间；只删除同步关系，不删除媒体' : '删除这个房间；只删除同步关系，不删除媒体')
            : action === 'leave'
                ? '退出后将尝试暂停仍在房间的一方'
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

    function renderRooms(page, rooms) {
        var container = page.querySelector('#wtRooms');
        if (!container) {
            return;
        }

        rememberDiagnosticPanelState(page);
        clearChildren(container);
        page._wtDiagnosticPanels = {};
        page._wtDiagnosticBusy = page._wtDiagnosticBusy || {};
        page._wtDiagnosticRequests = page._wtDiagnosticRequests || {};
        page._wtDiagnosticOpen = page._wtDiagnosticOpen || {};
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
                ? '先在上方创建一个房间，再让两位参与者打开同一视频。'
                : '请让管理员把你的账号加入房间。';
            empty.appendChild(emptyTitle);
            empty.appendChild(emptyText);
            container.appendChild(empty);
            return;
        }

        rooms.forEach(function (room) {
            var card = document.createElement('article');
            card.className = 'wt-roomCard';

            var header = document.createElement('div');
            header.className = 'wt-roomHeader';
            var identity = document.createElement('div');
            var name = document.createElement('h3');
            name.className = 'wt-roomName';
            name.textContent = room.Name || '未命名房间';
            identity.appendChild(name);

            var info = getStateInfo(room.State);
            info.description = statusReasonMessage(room.StatusReason);
            if (room.StatusReason === 'different_video') {
                info.label = '视频不一致';
            }
            var stateDescription = document.createElement('p');
            stateDescription.className = 'fieldDescription wt-roomStateDescription';
            stateDescription.textContent = info.description;
            identity.appendChild(stateDescription);

            var state = document.createElement('span');
            state.className = 'wt-roomState wt-roomState-' + info.className;
            state.textContent = info.label;
            state.title = info.description;

            header.appendChild(identity);
            header.appendChild(state);
            card.appendChild(header);

            var meta = document.createElement('div');
            meta.className = 'fieldDescription wt-roomMeta';
            var participantLine = document.createElement('div');
            var participantIds = room.ParticipantUserIds || [];
            participantLine.textContent = '参与者：' + participantIds.map(function (id) {
                return roomUserName(room, id);
            }).join('、');
            var primaryLine = document.createElement('div');
            primaryLine.textContent = '主用户：' + roomUserName(room, room.PrimaryUserId);
            meta.appendChild(participantLine);
            meta.appendChild(primaryLine);
            card.appendChild(meta);

            var membershipLine = document.createElement('div');
            membershipLine.className = 'fieldDescription wt-roomMeta wt-roomMembership';
            membershipLine.textContent = '你的状态：' + (room.CurrentUserJoined ? '已加入' : '已退出');
            card.appendChild(membershipLine);

            var feedback = page._wtRoomFeedback && page._wtRoomFeedback[room.RoomId];
            var feedbackEl = document.createElement('div');
            feedbackEl.className = 'wt-roomFeedback' + (feedback && feedback.isError ? ' error' : '');
            feedbackEl.setAttribute('role', feedback && feedback.isError ? 'alert' : 'status');
            feedbackEl.setAttribute('aria-live', 'polite');
            feedbackEl.textContent = feedback ? feedback.text : '';
            feedbackEl.hidden = !feedback;
            card.appendChild(feedbackEl);

            var actions = document.createElement('div');
            actions.className = 'wt-roomActions';
            actions.appendChild(createActionButton(page, room, room.CurrentUserJoined ? 'leave' : 'join'));
            actions.appendChild(createActionButton(page, room, 'diagnostics'));
            if (room.CurrentUserJoined && !room.IsAdmin) {
                actions.appendChild(createActionButton(page, room, 'participantResync'));
            }
            if (room.IsAdmin) {
                ['pause', 'resume', 'resync', 'delete'].forEach(function (action) {
                    actions.appendChild(createActionButton(page, room, action));
                });
            } else if (room.IsSelfService && room.CanEnd) {
                actions.appendChild(createActionButton(page, room, 'delete'));
            }
            card.appendChild(actions);
            card.appendChild(createDiagnosticPanel(page, room));
            container.appendChild(card);
        });
    }

    function loadRooms(page, announce) {
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

        return apiGet('WatchTogether/Rooms').then(function (rooms) {
            var list = Array.isArray(rooms) ? rooms : [];
            page._wtRooms = list;
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
            syncForm(page);
            if (announce) {
                setStatus(page, list.length > 0
                    ? '已更新 ' + list.length + ' 个房间'
                    : page._wtIsAdmin === true ? '暂无房间，可以创建一个。' : '暂无参与的房间。');
            }
            return list;
        }).catch(function (err) {
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
        var label = actionLabels[action] || '操作';
        if (action === 'resync' && !window.confirm('重新同步会暂时暂停双方并重新对齐，确认继续吗？')) {
            return;
        }
        setRoomBusy(page, roomId, true);
        clearRoomFeedback(page, roomId);
        roomFeedback(page, roomId, label + '处理中…', false, true);
        renderRooms(page, page._wtRooms || []);
        apiSend('WatchTogether/Rooms/' + encodeURIComponent(roomId) + '/Action', 'POST', { Action: action })
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
        setRoomBusy(page, roomId, true);
        clearRoomFeedback(page, roomId);
        roomFeedback(page, roomId, '重新同步请求处理中…', false, true);
        renderRooms(page, page._wtRooms || []);
        apiSend('WatchTogether/Rooms/' + encodeURIComponent(roomId) + '/Resync', 'POST')
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
        var roomName = room.Name || '未命名房间';
        if (!window.confirm('确认删除“' + roomName + '”吗？这只删除同步关系，不删除媒体。')) {
            return;
        }

        setRoomBusy(page, room.RoomId, true);
        roomFeedback(page, room.RoomId, '删除中…', false, true);
        renderRooms(page, page._wtRooms || []);
        apiSend('WatchTogether/Rooms/' + encodeURIComponent(room.RoomId), 'DELETE')
            .then(function (result) {
                if (result && result.Deleted === false) {
                    throw new Error('房间不存在或已删除');
                }
                clearRoomFeedback(page, room.RoomId);
                setTransientStatus(page, '房间“' + roomName + '”已删除；只移除同步关系，媒体未删除。', false);
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
        if (action === 'leave' && !window.confirm('退出“' + (room.Name || '未命名房间') + '”吗？将尝试暂停仍在房间的一方。')) {
            return;
        }
        setRoomBusy(page, room.RoomId, true);
        clearRoomFeedback(page, room.RoomId);
        roomFeedback(page, room.RoomId, action === 'join' ? '加入中…' : '退出中…', false, true);
        renderRooms(page, page._wtRooms || []);
        apiSend('WatchTogether/Rooms/' + encodeURIComponent(room.RoomId) + '/' + (action === 'join' ? 'Join' : 'Leave'), 'POST')
            .then(function (result) {
                var text = action === 'join'
                    ? '已加入房间，请与另一位参与者打开同一视频。'
                    : getLeaveFeedback(result);
                roomFeedback(page, room.RoomId, text,
                    action === 'leave' && Number(result && result.PauseFailed) > 0, false);
                return loadRooms(page, false);
            })
            .catch(function (err) {
                roomFeedback(page, room.RoomId, (action === 'join' ? '加入' : '退出') + '失败：' + errorMessage(err), true, true);
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
            return '已退出房间，但仍在房间的一方暂停失败，请检查客户端。';
        }
        if (attempted > 0 && succeeded === attempted) {
            return '已退出房间，仍在房间的一方已暂停。';
        }
        return '已退出房间，自动同步已停止。';
    }

    function createRoom(page) {
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
        if (users.length < 2 || !a || !b) {
            setStatus(page, '至少需要选择两名参与者。', true);
            return;
        }
        if (a === b) {
            setStatus(page, '请选择两名不同的参与者。', true);
            return;
        }
        var conflict = findRoomConflict(page, [a, b]);
        if (conflict) {
            var message = conflictMessage(conflict);
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
        setStatus(page, '正在创建房间…');
        apiSend('WatchTogether/Rooms', 'POST', {
            Name: name,
            ParticipantUserIds: [a, b],
            PrimaryUserId: primary.value
        }).then(function (result) {
            var created = result || {};
            page._wtPendingCreatedFeedback = {
                name: name,
                roomId: created.RoomId
            };
            nameInput.value = '';
            syncForm(page);
            return loadRooms(page, true);
        }).catch(function (err) {
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
        bindPageEvents(page);
        syncForm(page);
        loading.show();

        loadPluginConfiguration(page).catch(function () {
            return null;
        }).then(function () {
            return loadUsers(page);
        }).then(function () {
            return loadRooms(page, true);
        }).then(function () {
            return loadInvitations(page);
        }).then(function () {
            return loadPluginInfo(page);
        }).then(function () {
            loading.hide();
        }, function () {
            loading.hide();
        });

        clearInterval(page._wtTimer);
        page._wtTimer = setInterval(function () {
            loadRooms(page, false);
            loadInvitations(page);
        }, 5000);
    };

    View.prototype.onPause = function () {
        var page = this.view;
        clearInterval(page._wtTimer);
        clearAllRoomFeedback(page);
        if (page._wtStatusTimer) {
            clearTimeout(page._wtStatusTimer);
            page._wtStatusTimer = null;
        }
        BaseView.prototype.onPause.apply(this, arguments);
    };

    return View;
});

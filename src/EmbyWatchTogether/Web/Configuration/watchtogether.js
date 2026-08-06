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
        resync: '重新同步'
    };

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
            contentType: 'application/json'
        });
    }

    function errorMessage(error) {
        if (error && error.message) {
            return error.message;
        }
        return '未知错误';
    }

    function setStatus(page, text, isError) {
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

    function setConfigBusy(page, isBusy) {
        var pauseCheckbox = page.querySelector('#wtPauseOtherOnPlaybackStop');
        var notifyCheckbox = page.querySelector('#wtNotifyOtherOnPlaybackStop');
        var saveButton = page.querySelector('#wtSaveConfig');
        if (pauseCheckbox) {
            pauseCheckbox.disabled = isBusy;
        }
        if (notifyCheckbox) {
            notifyCheckbox.disabled = isBusy;
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
            setConfigStatus(page, '配置已读取');
            return config;
        }).catch(function (error) {
            page._wtConfigReady = false;
            var pauseCheckbox = page.querySelector('#wtPauseOtherOnPlaybackStop');
            var notifyCheckbox = page.querySelector('#wtNotifyOtherOnPlaybackStop');
            var saveButton = page.querySelector('#wtSaveConfig');
            if (pauseCheckbox) {
                pauseCheckbox.disabled = true;
            }
            if (notifyCheckbox) {
                notifyCheckbox.disabled = true;
            }
            if (saveButton) {
                saveButton.disabled = true;
            }
            setConfigStatus(page,
                isPermissionError(error) ? '只有管理员可以查看和修改此设置。' : '配置读取失败：' + errorMessage(error),
                true);
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
        if (!pauseCheckbox || !notifyCheckbox || !page._wtConfigReady) {
            return Promise.resolve();
        }

        setConfigBusy(page, true);
        setConfigStatus(page, '正在保存配置…');
        return ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            config = config || {};
            config.PauseOtherOnPlaybackStop = pauseCheckbox.checked;
            config.NotifyOtherOnPlaybackStop = notifyCheckbox.checked;
            return ApiClient.updatePluginConfiguration(pluginId, config);
        }).then(function () {
            return ApiClient.getPluginConfiguration(pluginId);
        }).then(function (config) {
            applyPluginConfiguration(page, config);
            setConfigStatus(page, '配置已保存并重新读取');
        }).catch(function (error) {
            setConfigStatus(page,
                isPermissionError(error) ? '保存被拒绝：只有管理员可以修改此设置。' : '配置保存失败：' + errorMessage(error),
                true);
        }).then(function () {
            setConfigBusy(page, false);
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

        select.innerHTML = '';
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
            users = Array.isArray(list) ? list : [];
            page._wtRooms = null;
            fillSelect(page.querySelector('#wtUserA'), users, users.length > 0 ? users[0].Id : null);
            fillSelect(page.querySelector('#wtUserB'), users, users.length > 1 ? users[1].Id : null);
            syncForm(page);
        }).catch(function (err) {
            page._wtIsAdmin = false;
            page._wtRooms = null;
            users = [];
            fillSelect(page.querySelector('#wtUserA'), [], null);
            fillSelect(page.querySelector('#wtUserB'), [], null);
            syncForm(page);
            setFormHint(page, '无法加载用户列表，请确认当前账号有管理员权限。', true);
            setStatus(page, '用户列表加载失败：' + errorMessage(err), true);
            var adminSection = page.querySelector('#wtAdminSection');
            if (adminSection) {
                adminSection.style.display = 'none';
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

    function createActionButton(page, room, action) {
        var button = document.createElement('button', { is: 'emby-button' });
        button.type = 'button';
        var toneClass = action === 'join'
            ? 'wt-action--primary'
            : action === 'leave' || action === 'delete'
                ? 'wt-action--danger'
                : action === 'resync'
                    ? 'wt-action--accent'
                    : '';
        button.className = 'button-flat wt-action' + (toneClass ? ' ' + toneClass : '');
        button.dataset.act = action;
        button.dataset.action = action;
        button.textContent = action === 'delete' ? '删除房间' : action === 'leave' ? '退出房间' : action === 'join' ? '加入房间' : actionLabels[action];
        button.title = action === 'delete'
            ? '删除这个房间'
            : action === 'leave'
                ? '退出后将停止同步，主用户会暂停'
                : action === 'join'
                    ? '加入后需要与另一位参与者打开同一视频'
            : actionLabels[action] + '：' + (getStateInfo(room.State).description || '');
        button.addEventListener('click', function () {
            if (action === 'delete') {
                deleteRoom(page, room, button);
            } else if (action === 'join' || action === 'leave') {
                membership(page, room, action, button);
            } else {
                control(page, room.RoomId, action, button);
            }
        });
        return button;
    }

    function renderRooms(page, rooms) {
        var container = page.querySelector('#wtRooms');
        if (!container) {
            return;
        }

        container.innerHTML = '';
        container.setAttribute('aria-busy', 'false');
        var adminSection = page.querySelector('#wtAdminSection');
        if (adminSection && page._wtIsAdmin !== undefined) {
            adminSection.style.display = page._wtIsAdmin ? '' : 'none';
        }
        if (!rooms || rooms.length === 0) {
            var empty = document.createElement('div');
            empty.className = 'wt-emptyState';
            var emptyTitle = document.createElement('strong');
            emptyTitle.textContent = '还没有房间';
            var emptyText = document.createElement('p');
            emptyText.className = 'fieldDescription';
            emptyText.textContent = '先在上方创建一个房间，再让两位参与者打开同一视频。';
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
            if (room.Error && room.Error.indexOf('不同视频') !== -1) {
                info = { label: '视频不一致', description: room.Error, className: 'waiting' };
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
            participantLine.textContent = '参与者：' + participantIds.map(userName).join('、');
            var primaryLine = document.createElement('div');
            primaryLine.textContent = '主用户：' + userName(room.PrimaryUserId);
            meta.appendChild(participantLine);
            meta.appendChild(primaryLine);
            card.appendChild(meta);

            var membershipLine = document.createElement('div');
            membershipLine.className = 'fieldDescription wt-roomMeta wt-roomMembership';
            membershipLine.textContent = '你的状态：' + (room.CurrentUserJoined ? '已加入' : '已退出');
            card.appendChild(membershipLine);

            if (room.Error) {
                var error = document.createElement('div');
                error.className = 'error wt-roomError';
                error.textContent = '需要处理：' + room.Error;
                card.appendChild(error);
            }

            var actions = document.createElement('div');
            actions.className = 'wt-roomActions';
            actions.appendChild(createActionButton(page, room, room.CurrentUserJoined ? 'leave' : 'join'));
            if (room.IsAdmin) {
                ['pause', 'resume', 'resync', 'delete'].forEach(function (action) {
                    actions.appendChild(createActionButton(page, room, action));
                });
            }
            card.appendChild(actions);
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
            renderRooms(page, list);
            syncForm(page);
            if (announce) {
                setStatus(page, list.length > 0 ? '已更新 ' + list.length + ' 个房间' : '暂无房间，可以创建一个。');
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
        setButtonBusy(button, true, '处理中…');
        setStatus(page, label + '…');
        apiSend('WatchTogether/Rooms/' + encodeURIComponent(roomId) + '/Action', 'POST', { Action: action })
            .then(function (result) {
                if (result && result.Error) {
                    throw new Error(result.Error);
                }
                setStatus(page, label + '指令已发送');
                return loadRooms(page, false);
            })
            .catch(function (err) {
                setStatus(page, label + '失败：' + errorMessage(err), true);
            })
            .then(function () {
                setButtonBusy(button, false);
            });
    }

    function deleteRoom(page, room, button) {
        var roomName = room.Name || '未命名房间';
        if (!window.confirm('确认删除“' + roomName + '”吗？删除后两位参与者将不再自动同步。')) {
            return;
        }

        setButtonBusy(button, true, '删除中…');
        setStatus(page, '正在删除房间…');
        apiSend('WatchTogether/Rooms/' + encodeURIComponent(room.RoomId), 'DELETE')
            .then(function (result) {
                if (result && result.Deleted === false) {
                    throw new Error('房间不存在或已删除');
                }
                setStatus(page, '房间已删除');
                return loadRooms(page, false);
            })
            .catch(function (err) {
                setStatus(page, '删除失败：' + errorMessage(err), true);
            })
            .then(function () {
                setButtonBusy(button, false);
            });
    }

    function membership(page, room, action, button) {
        if (action === 'leave' && !window.confirm('退出“' + (room.Name || '未命名房间') + '”吗？退出后主用户会暂停。')) {
            return;
        }
        setButtonBusy(button, true, action === 'join' ? '加入中…' : '退出中…');
        setStatus(page, action === 'join' ? '正在加入房间…' : '正在退出房间…');
        apiSend('WatchTogether/Rooms/' + encodeURIComponent(room.RoomId) + '/' + (action === 'join' ? 'Join' : 'Leave'), 'POST')
            .then(function () {
                setStatus(page, action === 'join' ? '已加入房间' : '已退出房间');
                return loadRooms(page, false);
            })
            .catch(function (err) {
                setStatus(page, (action === 'join' ? '加入' : '退出') + '失败：' + errorMessage(err), true);
            })
            .then(function () {
                setButtonBusy(button, false);
            });
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
        }).then(function () {
            setStatus(page, '房间已创建');
            nameInput.value = '';
            syncForm(page);
            return loadRooms(page, true);
        }).catch(function (err) {
            setStatus(page, '创建失败：' + errorMessage(err), true);
        }).then(function () {
            setButtonBusy(createButton, false);
            syncForm(page);
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
        dom.addEventListener(page.querySelector('#wtRefresh'), 'click', function () {
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
            loading.hide();
        }, function () {
            loading.hide();
        });

        clearInterval(page._wtTimer);
        page._wtTimer = setInterval(function () {
            loadRooms(page, false);
        }, 5000);
    };

    View.prototype.onPause = function () {
        clearInterval(this.view._wtTimer);
        BaseView.prototype.onPause.apply(this, arguments);
    };

    return View;
});

define(['baseView', 'dom', 'loading', 'globalize', 'emby-input', 'emby-select', 'emby-button'], function (BaseView, dom, loading, globalize) {
    'use strict';

    var users = [];

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

    function escapeHtml(value) {
        return String(value == null ? '' : value).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }

    function setStatus(page, text, isError) {
        var el = page.querySelector('#wtStatus');
        if (el) {
            el.textContent = text;
            el.classList.toggle('error', !!isError);
        }
    }

    function fillSelect(select, selected) {
        select.innerHTML = '';
        users.forEach(function (u) {
            var opt = document.createElement('option');
            opt.value = u.Id;
            opt.textContent = u.Name;
            if (u.Id === selected) {
                opt.selected = true;
            }
            select.appendChild(opt);
        });
        if (!selected && users.length > 0) {
            select.selectedIndex = 0;
        }
    }

    function loadUsers(page) {
        return apiGet('WatchTogether/Users').then(function (list) {
            users = Array.isArray(list) ? list : [];
            fillSelect(page.querySelector('#wtUserA'));
            fillSelect(page.querySelector('#wtUserB'), users.length > 1 ? users[1].Id : null);
            fillSelect(page.querySelector('#wtPrimary'));
        }).catch(function (err) {
            setStatus(page, '无法加载用户列表（需要管理员权限）：' + (err && err.message ? err.message : err), true);
            users = [];
        });
    }

    function renderRooms(page, rooms) {
        var container = page.querySelector('#wtRooms');
        container.innerHTML = '';
        if (!rooms || rooms.length === 0) {
            container.textContent = '暂无房间';
            return;
        }
        var table = document.createElement('table');
        table.className = 'table table-hover';
        table.innerHTML =
            '<thead><tr><th>名称</th><th>状态</th><th>参与者</th><th style="width:320px;">操作</th></tr></thead>';
        var tbody = document.createElement('tbody');
        rooms.forEach(function (room) {
            var tr = document.createElement('tr');
            var names = (room.ParticipantUserIds || []).map(function (id) {
                var u = users.filter(function (x) { return x.Id === id; })[0];
                return u ? u.Name : id;
            }).join(' / ');
            tr.innerHTML =
                '<td>' + escapeHtml(room.Name || '(未命名)') + '</td>' +
                '<td>' + escapeHtml(room.State || '') +
                (room.Error ? '<div class="error">' + escapeHtml(room.Error) + '</div>' : '') + '</td>' +
                '<td>' + escapeHtml(names) + '</td>' +
                '<td>' +
                '<button is="emby-button" type="button" class="raised" data-act="pause">暂停</button> ' +
                '<button is="emby-button" type="button" class="raised" data-act="resume">继续</button> ' +
                '<button is="emby-button" type="button" class="raised" data-act="resync">重新同步</button> ' +
                '<button is="emby-button" type="button" class="raised" data-act="delete">删除</button>' +
                '</td>';
            tr.querySelector('[data-act="pause"]').addEventListener('click', function () {
                control(room.RoomId, 'pause');
            });
            tr.querySelector('[data-act="resume"]').addEventListener('click', function () {
                control(room.RoomId, 'resume');
            });
            tr.querySelector('[data-act="resync"]').addEventListener('click', function () {
                control(room.RoomId, 'resync');
            });
            tr.querySelector('[data-act="delete"]').addEventListener('click', function () {
                apiSend('WatchTogether/Rooms/' + encodeURIComponent(room.RoomId), 'DELETE').then(function () {
                    loadRooms(page);
                }).catch(function (err) {
                    setStatus(page, '删除失败：' + (err && err.message ? err.message : err), true);
                });
            });
            tbody.appendChild(tr);
        });
        table.appendChild(tbody);
        container.appendChild(table);
    }

    function loadRooms(page) {
        return apiGet('WatchTogether/Rooms').then(function (rooms) {
            renderRooms(page, rooms);
            setStatus(page, '已刷新 ' + (rooms ? rooms.length : 0) + ' 个房间');
        }).catch(function (err) {
            setStatus(page, '房间加载失败：' + (err && err.message ? err.message : err), true);
        });
    }

    function control(roomId, action) {
        apiSend('WatchTogether/Rooms/' + encodeURIComponent(roomId) + '/Action', 'POST', { Action: action })
            .then(function () {
                return loadRooms(document.querySelector('.view[data-controller]') || document);
            })
            .catch(function (err) {
                setStatus(document.querySelector('.view[data-controller]') || document,
                    '操作失败：' + (err && err.message ? err.message : err), true);
            });
    }

    function createRoom(page) {
        var name = page.querySelector('#wtRoomName').value.trim();
        var a = page.querySelector('#wtUserA').value;
        var b = page.querySelector('#wtUserB').value;
        var primary = page.querySelector('#wtPrimary').value;
        if (!name || !a || !b || a === b) {
            setStatus(page, '请填写房间名称并选择两名不同参与者', true);
            return;
        }
        apiSend('WatchTogether/Rooms', 'POST', {
            Name: name,
            ParticipantUserIds: [a, b],
            PrimaryUserId: primary
        }).then(function () {
            setStatus(page, '房间已创建');
            page.querySelector('#wtRoomName').value = '';
            return loadRooms(page);
        }).catch(function (err) {
            setStatus(page, '创建失败：' + (err && err.message ? err.message : err), true);
        });
    }

    function View() {
        BaseView.apply(this, arguments);
    }

    Object.assign(View.prototype, BaseView.prototype);

    View.prototype.onResume = function () {
        BaseView.prototype.onResume.apply(this, arguments);
        var page = this.view;
        loading.show();
        Promise.all([loadUsers(page), loadRooms(page)]).then(function () {
            loading.hide();
        }).catch(function () {
            loading.hide();
        });

        clearInterval(page._wtTimer);
        page._wtTimer = setInterval(function () {
            loadRooms(page);
        }, 5000);

        dom.addEventListener(page.querySelector('#wtCreate'), 'click', function () {
            createRoom(page);
        }, { once: true });
    };

    View.prototype.onPause = function () {
        clearInterval(this.view._wtTimer);
        BaseView.prototype.onPause.apply(this, arguments);
    };

    return View;
});

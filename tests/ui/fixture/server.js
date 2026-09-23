const http = require('http');
const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '..', '..', '..');
const htmlPath = path.join(root, 'src', 'EmbyWatchTogether', 'Web', 'Configuration', 'watchtogether.html');
const jsPath = path.join(root, 'src', 'EmbyWatchTogether', 'Web', 'Configuration', 'watchtogether.js');
const html = fs.readFileSync(htmlPath, 'utf8');
const javascript = fs.readFileSync(jsPath, 'utf8');

const users = [
  { Id: 'user-a', Name: 'hope' },
  { Id: 'user-b', Name: 'qsm' },
  { Id: 'user-c', Name: 'very-long-user-name-for-overflow-check' }
];

function room(overrides) {
  return Object.assign({
    RoomId: 'room-1', Name: '周末一起看', State: 'Watching',
    StatusReason: 'watching', ParticipantUserIds: ['user-a', 'user-b'], PrimaryUserId: 'user-a',
    Participants: [
      { Id: 'user-a', Name: 'hope', Joined: true, IsPrimary: true },
      { Id: 'user-b', Name: 'qsm', Joined: true, IsPrimary: false }
    ],
    CurrentUserIsParticipant: true, CurrentUserJoined: true, CanEnd: true, IsAdmin: false,
    _wtState: { Sessions: [
      { UserId: 'user-a', Online: true, IsPaused: false },
      { UserId: 'user-b', Online: true, IsPaused: false }
    ] }
  }, overrides || {});
}

function diagnostic() {
  return {
    RoomState: 'Watching', StatusReason: 'watching', SnapshotHealth: 'fresh', GeneratedAtUtc: '2026-09-22T11:00:00Z',
    Sessions: [
      { Alias: 'userA', Online: true, Paused: false, PositionSeconds: 42, ReportedSupportsRemoteControl: true, EffectiveSupportsRemoteControl: true, AckLatencySeconds: .15, PlaybackRate: 1, SupportedCommands: ['Pause', 'Seek'] },
      { Alias: 'userB', Online: true, Paused: false, PositionSeconds: 41, ReportedSupportsRemoteControl: true, EffectiveSupportsRemoteControl: true, AckLatencySeconds: .21, PlaybackRate: 1, SupportedCommands: ['Pause'] }
    ],
    Pending: [], Barrier: null, Handoff: null, RecoveryWindow: null,
    LastAction: { Type: 'entered_watching', Alias: 'userA', Result: 'success' }, LastError: null,
    Events: [{ AtUtc: '2026-09-22T10:59:00Z', Type: 'entered_watching', Result: 'success' }]
  };
}

function stateForScenario(scenario) {
  if (scenario === 'empty') return [];
  if (scenario === 'admin-manage') return [room({ RoomId: 'managed-1', Name: '管理员长列表房间', CurrentUserIsParticipant: false, CurrentUserJoined: false, CanEnd: false, IsAdmin: true })];
  if (scenario === 'admin-member') return [room({ RoomId: 'admin-member-1', Name: '管理员成员房间', IsAdmin: true })];
  if (scenario === 'long') return [room({ Name: '这是一个很长很长很长的房间名称用于窄屏测试', Participants: [
    { Id: 'user-a', Name: '这是一个非常长的用户名称 A', Joined: true, IsPrimary: true },
    { Id: 'user-c', Name: '这是一个非常长的用户名称 B', Joined: true, IsPrimary: false }
  ], ParticipantUserIds: ['user-a', 'user-c'] })];
  return [room()];
}

function json(res, value, status = 200) {
  res.writeHead(status, { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' });
  res.end(JSON.stringify(value));
}

function page() {
  return `<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>
    html,body { margin: 0; min-height: 100%; font-family: Arial, sans-serif; }
    body { background: var(--theme-background-color, #f7f9fb); }
    .fixture-host { min-height: 100vh; box-sizing: border-box; }
    .fixture-host.dark { --theme-text-color: #edf2f7; --theme-secondary-text-color: #a7b3bf; --theme-primary-color: #4bd47a; --theme-background-color: #151a1f; --card-background-hue: 210; --card-background-saturation: 22%; --card-background-lightness: 15%; --button-background-hue: 210; --button-background-saturation: 22%; --button-background-lightness: 22%; --line-background: rgba(220,230,240,.25); }
    .fixture-host.light { --theme-text-color: #1e2933; --theme-secondary-text-color: #667789; --theme-primary-color: #20a956; --theme-background-color: #f7f9fb; --card-background-hue: 210; --card-background-saturation: 25%; --card-background-lightness: 99%; --button-background-hue: 210; --button-background-saturation: 20%; --button-background-lightness: 95%; --line-background: rgba(38,49,58,.2); }
    .fixture-host.hsl { --theme-text-color-hue: 210; --theme-text-color-saturation: 25%; --theme-text-color-lightness: 22%; --theme-text-color-alpha: 1; --theme-secondary-text-color-alpha: 1; --theme-primary-color-hue: 140; --theme-primary-color-saturation: 56%; --theme-primary-color-lightness: 39%; --theme-primary-color-alpha: 1; --card-background-hue: 210; --card-background-saturation: 25%; --card-background-lightness: 99%; --card-background-alpha: 1; --button-background-hue: 210; --button-background-saturation: 20%; --button-background-lightness: 95%; --button-background-alpha: 1; --line-background: rgba(38,49,58,.2); }
    .skinHeader { position: relative; z-index: 2; display: grid; grid-template-columns: minmax(160px, 1fr) minmax(0, auto) minmax(160px, 1fr); align-items: center; min-height: 56px; padding: 0 16px; box-sizing: border-box; border-bottom: 1px solid var(--line-background, rgba(38,49,58,.2)); color: var(--theme-text-color, #1e2933); background: var(--theme-background-color, #f7f9fb); }
    .headerLeft, .headerRight { min-width: 0; display: flex; align-items: center; gap: 8px; }
    .headerRight { justify-content: flex-end; color: var(--theme-secondary-text-color, #667789); font-size: 12px; }
    .headerLeft { font-size: 20px; font-weight: 600; }
    .headerLeft::before { content: '◆'; margin-right: 4px; color: var(--theme-primary-color, #20a956); font-size: 16px; }
    .headerMiddle { min-width: 0; display: flex; align-items: center; justify-content: flex-end; }
    .headerMiddle.headerMiddle-withSectionTabs { justify-content: center; }
    .skinHeader.headerTop-withSectionTabs { grid-template-columns: minmax(160px, 1fr) minmax(0, auto) minmax(160px, 1fr); }
    @media (max-width: 40em) {
      .skinHeader, .skinHeader.headerTop-withSectionTabs { grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); grid-template-rows: 44px auto; padding: 0 8px 6px; }
      .headerLeft { grid-column: 1; grid-row: 1; font-size: 0; }
      .headerRight { grid-column: 2; grid-row: 1; }
      .headerMiddle { grid-column: 1 / -1; grid-row: 2; width: 100%; justify-content: stretch; }
    }
    .fixture-main { min-width: 0; padding: 10px 16px 16px; box-sizing: border-box; }
    .fixture-label { margin: 0 0 12px; padding: 8px 12px; border: 1px solid #d7dde3; border-radius: 6px; color: #536273; background: #eef2f5; font-size: 13px; }
    .fixture-host.dark .fixture-label { border-color: rgba(220,230,240,.25); color: #a7b3bf; background: rgba(220,230,240,.08); }
    button,input,select { box-sizing: border-box; font: inherit; }
    button { min-height: 44px; padding: 0 16px; border: 1px solid var(--line-background, #c8d0d8); border-radius: 6px; color: var(--theme-text-color, #1e2933); background: var(--wt-button, #eef2f5); cursor: pointer; }
    button.raised.button-submit { border-color: var(--theme-primary-color, #20a956); color: #fff; background: var(--theme-primary-color, #20a956); }
    button:disabled { cursor: default; opacity: .55; }
    input:not([type="checkbox"]),select { width: min(100%, 32rem); min-height: 44px; padding: 0 12px; border: 1px solid var(--line-background, #c8d0d8); border-radius: 5px; color: var(--theme-text-color, #1e2933); background: var(--wt-card, #fff); }
    .fieldDescription,.sectionDescription { line-height: 1.45; }
  </style></head><body><main class="fixture-host"></main><script>
  (function () {
    var params = new URLSearchParams(location.search); var scenario = params.get('scenario') || 'active';
    var theme = params.get('theme') || 'light'; var host = document.querySelector('.fixture-host'); host.className += ' ' + theme;
    var hasHostHeader = params.get('host') !== 'missing';
    var sourceHtml = ${JSON.stringify(html)}; var sourceJs = ${JSON.stringify(javascript)};
    var diagnosticData = ${JSON.stringify(diagnostic())};
    var apiCalls = []; var config = { PauseOtherOnPlaybackStop: true, NotifyOtherOnPlaybackStop: true, NotifyOnSyncActions: true, UpdateChannel: 'stable' };
    var rooms = (scenario === 'empty' || scenario === 'accept-fail' || scenario === 'accept-network' ? [] : scenario === 'admin-manage' ? ${JSON.stringify(stateForScenario('admin-manage'))} : scenario === 'admin-member' ? ${JSON.stringify(stateForScenario('admin-member'))} : scenario === 'long' ? ${JSON.stringify(stateForScenario('long'))} : ${JSON.stringify(stateForScenario('active'))});
    var invitations = [];
    function delay(value) { return new Promise(function (resolve) { setTimeout(function () { resolve(value); }, scenario === 'late' ? 900 : 0); }); }
    function fail(status) { var e = new Error('mock'); e.status = status; return Promise.reject(e); }
    function get(path) {
      apiCalls.push({ method: 'GET', path: path });
      if (path === 'WatchTogether/Users') return (scenario === 'admin-manage' || scenario === 'admin-member' || scenario === 'save-fail') ? delay(${JSON.stringify(users)}) : fail(403);
      if (path === 'WatchTogether/Rooms') return delay(rooms);
      if (path.indexOf('/State') >= 0) {
        var stateRoom = rooms.filter(function (candidate) { return path.indexOf(candidate.RoomId) >= 0; })[0];
        return delay(stateRoom && stateRoom._wtState ? stateRoom._wtState : { Sessions: [] });
      }
      if (path === 'WatchTogether/Invitations') return delay(invitations.slice());
      if (path === 'WatchTogether/Info') return delay({ CurrentVersion: '0.0.0-ui-mock', RepositoryUrl: 'https://example.invalid/mock' });
      if (path.indexOf('/Diagnostics') >= 0) return delay(diagnosticData);
      return delay({});
    }
    function send(path, method, body) {
      apiCalls.push({ method: method, path: path, body: body ? JSON.parse(body) : null });
      if (scenario === 'accept-fail' && path.indexOf('/Accept') >= 0) return delay({ Status: 'invalid_or_expired', Accepted: false });
      if (scenario === 'accept-network' && path.indexOf('/Accept') >= 0) return fail(503);
      if (scenario === 'save-fail' && method === 'POST' && path.indexOf('configuration') >= 0) return fail(500);
      if (path === 'WatchTogether/Invitations' && method === 'POST') {
        var expiresAt = new Date(Date.now() + 900000).toISOString();
        invitations = [{ InvitationId: 'inv-1', Name: body && JSON.parse(body).Name || '周末邀请', ExpiresAtUtc: expiresAt }];
        return delay({ Status: 'created', Created: true, InvitationId: 'inv-1', Code: 'ABCD12EFghI3J4KLMNopQR', ExpiresAtUtc: expiresAt });
      }
      if (path.indexOf('/Accept') >= 0) return delay({ Status: 'accepted', Accepted: true });
      if (path.indexOf('/Action') >= 0) return delay({ Users: ['user-a'] });
      if (path.indexOf('/Resync') >= 0) return delay({ Status: 'accepted' });
      if (path.indexOf('/Leave') >= 0) return delay({ PauseAttempted: 1, PauseSucceeded: 1, PauseFailed: 0 });
      return delay({});
    }
    window.__wtFixture = { apiCalls: apiCalls, errors: [], getRooms: function () { return rooms; }, config: config };
    var nativeCatch = Promise.prototype.catch;
    Promise.prototype.catch = function (handler) {
      return nativeCatch.call(this, function (error) {
        window.__wtFixture.errors.push(String(error && error.stack || error));
        return handler(error);
      });
    };
    window.ApiClient = { getUrl: function (path) { return path; }, getJSON: get, fetch: function (o) { return send(o.url, o.type, o.data); }, getPluginConfiguration: function () { apiCalls.push({ method: 'GET', path: 'configuration' }); return (scenario === 'admin-manage' || scenario === 'admin-member' || scenario === 'save-fail') ? delay(JSON.parse(JSON.stringify(config))) : fail(403); }, updatePluginConfiguration: function (id, value) { apiCalls.push({ method: 'POST', path: 'configuration', body: value }); if (scenario === 'save-fail') return fail(500); config = JSON.parse(JSON.stringify(value)); return delay(JSON.parse(JSON.stringify(config))); } };
    window.define = function (deps, factory) { var BaseView = function () { this.view = arguments[0] || null; }; BaseView.prototype.onResume = function () {}; BaseView.prototype.onPause = function () {}; window.__wtViewCtor = factory(BaseView, { addEventListener: function (el, name, fn) { if (el) el.addEventListener(name, fn); } }, { show: function () {}, hide: function () {} }); };
    var hostHeader = hasHostHeader ? '<header class="skinHeader" data-mock-host="true"><div class="headerLeft">emby</div><div class="headerMiddle headerSection sectionTabs"></div><div class="headerRight">mock host</div></header>' : '';
    document.querySelector('.fixture-host').innerHTML = hostHeader + '<div class="fixture-main"><div class="fixture-label" data-mock="true">模拟宿主 / mock Emby host + API · 不连接真实 Emby</div>' + sourceHtml + '</div>';
    var page = document.querySelector('.wt-page');
    try { (new Function(sourceJs))(); var view = new window.__wtViewCtor(page); view.view = page; view.onResume(); window.__wtFixture.view = view; } catch (error) { window.__wtFixture.error = String(error && error.stack || error); }
  }());
  </script></body></html>`;
}

const server = http.createServer((req, res) => {
  if (req.url.split('?')[0] === '/health') return json(res, { ok: true });
  if (req.url.split('?')[0] === '/') { res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' }); return res.end(page()); }
  res.writeHead(404); res.end('not found');
});
server.listen(4179, '127.0.0.1');

const http = require('http');
const fs = require('fs');
const path = require('path');

const root = process.env.WT_UI_SOURCE_ROOT ? path.resolve(process.env.WT_UI_SOURCE_ROOT) : path.resolve(__dirname, '..', '..', '..');
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
    JoinedParticipantUserIds: ['user-a', 'user-b'],
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

function diagnostic(scenario) {
  const recovering = scenario === 'recovering';
  const positiveDrift = scenario === 'drift-positive';
  return {
    RoomState: recovering ? 'Recovering' : 'Watching',
    StatusReason: recovering ? 'transient_recovery' : 'watching',
    SnapshotHealth: 'fresh', GeneratedAtUtc: '2026-09-22T11:00:00Z',
    RoomHash: 'sensitive-room-id', ServerHash: 'sensitive-server-id', SecretToken: 'should-not-export',
    Sessions: [
      { Alias: 'userA', SessionHash: 'sensitive-session-a', ItemHash: 'sensitive-item-a', Online: true, Paused: false, PositionSeconds: 42, ReportedSupportsRemoteControl: true, EffectiveSupportsRemoteControl: true, AckLatencySeconds: .15, PlaybackRate: 1, SupportedCommands: ['Pause', 'Seek'] },
      { Alias: 'userB', SessionHash: 'sensitive-session-b', ItemHash: 'sensitive-item-b', Online: !recovering, Paused: false, PositionSeconds: 41, ReportedSupportsRemoteControl: true, EffectiveSupportsRemoteControl: true, AckLatencySeconds: .21, PlaybackRate: 1, SupportedCommands: ['Pause'] }
    ],
    Pending: [], Barrier: null, Handoff: null, RecoveryWindow: null,
    TransientRecovery: recovering ? {
      Active: true, AgeSeconds: 4.2, RemainingSeconds: 5.8, MissingAliases: ['userB'],
      Participants: [
        { Alias: 'userA', Missing: false, ExpectedSessionHash: 'hash-session-a', ExpectedItemHash: 'hash-item-a', LastKnownPositionSeconds: 42, LastKnownPaused: false },
        { Alias: 'userB', Missing: true, ExpectedSessionHash: 'hash-session-b', ExpectedItemHash: 'hash-item-b', LastKnownPositionSeconds: 41, LastKnownPaused: false }
      ]
    } : null,
    Drift: { CurrentDriftSeconds: recovering ? null : (positiveDrift ? 1.25 : -1.25), MaxObservedAbsoluteDriftSeconds: 4.4, HoldAgeSeconds: null, LastAutoRepairAtUtc: '2026-09-22T10:59:00Z', AutoRepairCount: 2, LastAutoRepairDriftSeconds: positiveDrift ? 3.2 : -3.2, CooldownRemainingSeconds: 60 },
    LastAction: { Type: recovering ? 'recovery_started' : 'drift_auto_repair_started', Alias: 'userB', Result: recovering ? 'entered' : 'started', DriftSeconds: positiveDrift ? 3.2 : -3.2, UserId: 'sensitive-user-id' }, LastError: null,
    Events: [
      { AtUtc: '2026-09-22T10:59:00Z', Type: recovering ? 'recovery_started' : 'drift_auto_repair_started', Result: recovering ? 'entered' : 'started', DriftSeconds: -2.1, SessionHash: 'sensitive-event-session' },
      { AtUtc: '2026-09-22T10:59:30Z', Type: 'drift_threshold_cleared', Result: 'cleared', DriftSeconds: positiveDrift ? .25 : -.25, UnknownSecret: 'should-not-export' }
    ]
  };
}

function stateForScenario(scenario) {
  if (scenario === 'empty') return [];
  if (scenario === 'admin-manage') return [room({ RoomId: 'managed-1', Name: '管理员长列表房间', CurrentUserIsParticipant: false, CurrentUserJoined: false, CanEnd: false, IsAdmin: true })];
  if (scenario === 'admin-member') return [room({ RoomId: 'admin-member-1', Name: '管理员成员房间', IsAdmin: true })];
  if (scenario === 'recovering') return [room({ RoomId: 'recovering-1', Name: '临时掉线恢复房间', State: 'Recovering', StatusReason: 'transient_recovery', _wtState: { Sessions: [
    { UserId: 'user-a', Online: true, IsPaused: false }, { UserId: 'user-b', Online: false, IsPaused: false }
  ] } })];
  if (scenario === 'primary-missing') return [room({ RoomId: 'primary-missing-1', Name: '主用户掉线房间', State: 'Waiting', StatusReason: 'playback_stopped', _wtState: { Sessions: [
    { UserId: 'user-b', Online: true, IsPaused: false }
  ] } })];
  if (scenario === 'waiting') return [room({ RoomId: 'waiting-1', Name: '等待播放房间', State: 'Waiting', StatusReason: 'waiting_for_playback', _wtState: { Sessions: [] } })];
  if (scenario === 'state-fail') return [room({ RoomId: 'state-fail-1', Name: '状态读取失败房间' })];
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
    .fixture-host { min-height: 100vh; box-sizing: border-box; background: var(--theme-background-color, #f7f9fb); color: var(--theme-text-color, #1e2933); }
    .fixture-host.dark { --theme-text-color: #edf2f7; --theme-text-color-hue: 210; --theme-text-color-saturation: 25%; --theme-text-color-lightness: 95%; --theme-text-color-alpha: 1; --theme-secondary-text-color: #a7b3bf; --theme-primary-color: #4bd47a; --theme-background-color: #151a1f; --card-background-hue: 210; --card-background-saturation: 22%; --card-background-lightness: 15%; --button-background-hue: 210; --button-background-saturation: 22%; --button-background-lightness: 22%; --button-background-alpha: 1; --line-background: rgba(220,230,240,.25); }
    .fixture-host.light { --theme-text-color: #1e2933; --theme-text-color-hue: 210; --theme-text-color-saturation: 25%; --theme-text-color-lightness: 22%; --theme-text-color-alpha: 1; --theme-secondary-text-color: #667789; --theme-primary-color: #20a956; --theme-background-color: #f7f9fb; --card-background-hue: 210; --card-background-saturation: 25%; --card-background-lightness: 99%; --button-background-hue: 210; --button-background-saturation: 20%; --button-background-lightness: 95%; --button-background-alpha: 1; --line-background: rgba(38,49,58,.2); }
    .fixture-host.hsl { --theme-text-color-hue: 210; --theme-text-color-saturation: 25%; --theme-text-color-lightness: 22%; --theme-text-color-alpha: 1; --theme-secondary-text-color-alpha: 1; --theme-primary-color-hue: 140; --theme-primary-color-saturation: 56%; --theme-primary-color-lightness: 39%; --theme-primary-color-alpha: 1; --card-background-hue: 210; --card-background-saturation: 25%; --card-background-lightness: 99%; --card-background-alpha: 1; --button-background-hue: 210; --button-background-saturation: 20%; --button-background-lightness: 95%; --button-background-alpha: 1; --line-background: rgba(38,49,58,.2); }
    .fixture-host.darkgradient { --fixture-gradient-sample: 48,52,61; --theme-text-color: hsla(0,0%,100%,.87); --theme-text-color-hue: 0; --theme-text-color-saturation: 0%; --theme-text-color-lightness: 100%; --theme-text-color-alpha: .87; --theme-secondary-text-color: hsla(0,0%,100%,.6); --theme-primary-color: hsl(116,41.7%,50.2%); --theme-background-color: #30343d; --card-background-hue: 0; --card-background-saturation: 0%; --card-background-lightness: 100%; --card-background-alpha: .16; --button-background-hue: 0; --button-background-saturation: 0%; --button-background-lightness: 100%; --button-background-alpha: .2; --line-background: rgba(255,255,255,.25); background: linear-gradient(135deg, #252a32 0%, #3b4653 100%); }
    .skinHeader { position: relative; z-index: 2; display: grid; grid-template-columns: minmax(160px, 1fr) minmax(0, auto) minmax(160px, 1fr); align-items: center; min-height: 56px; padding: 0 16px; box-sizing: border-box; border-bottom: 1px solid var(--line-background, rgba(38,49,58,.2)); color: var(--theme-text-color, #1e2933); background: var(--theme-background-color, #f7f9fb); }
    .headerLeft, .headerRight { min-width: 0; display: flex; align-items: center; gap: 8px; }
    .headerRight { justify-content: flex-end; color: var(--theme-secondary-text-color, #667789); font-size: 12px; }
    .headerLeft { font-size: 20px; font-weight: 600; }
    .headerLeft::before { content: '◆'; margin-right: 4px; color: var(--theme-primary-color, #20a956); font-size: 16px; }
    .headerMiddle { min-width: 0; display: flex; align-items: center; justify-content: flex-end; }
    .headerMiddle.headerMiddle-withSectionTabs { justify-content: center; }
    .skinHeader.headerTop-withSectionTabs { grid-template-columns: minmax(160px, 1fr) minmax(0, auto) minmax(160px, 1fr); }
    .tabs-viewmenubar { display: flex; max-width: min(100%, 42em); padding: 4px; border: 1px solid var(--line-background, rgba(38,49,58,.2)); border-radius: 999px; background: var(--button-background-color, rgba(128,128,128,.12)); overflow-x: auto; }
    .tabs-viewmenubar-slider { display: flex; gap: 2px; }
    .emby-tab-button { min-height: 44px; padding: 4px 19px; border: 0; border-radius: 999px; color: var(--theme-secondary-text-color, #667789); background: transparent; font: inherit; font-size: .92em; font-weight: 600; white-space: nowrap; }
    .emby-tab-button-active { color: var(--theme-background-color, #fff); background: var(--theme-text-color, #1e2933); }
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
    button { min-height: 44px; padding: 0 16px; color: inherit; cursor: pointer; }
    .emby-button, button[is="emby-button"] { display: inline-flex; align-items: center; box-sizing: border-box; margin: 0 .29em; padding: .66em 2ch; font: inherit; font-weight: 600; color: inherit; border: 0; }
    .readOnlyContent { max-width: 80ch; }
    .auto-center { margin-inline: auto; }
    .page-withDockedDrawer .auto-center { margin-inline: 0; }
    button.raised { border: 0; border-radius: 100em; background: hsla(var(--button-background-hue, 0), var(--button-background-saturation, 0%), var(--button-background-lightness, 95%), var(--button-background-alpha, 1)); color: hsla(var(--theme-text-color-hue, 204), var(--theme-text-color-saturation, 20%), var(--theme-text-color-lightness, 20%), var(--theme-text-color-alpha, 1)); }
    button.raised-outline { background: transparent; border: .08em solid var(--line-background); backdrop-filter: none; }
    button.raised.button-submit { border-color: var(--theme-primary-color, #20a956); color: #fff; background: var(--theme-primary-color, #20a956); }
    button:disabled { cursor: default; opacity: .55; }
    .tabs-viewmenubar .emby-tab-button { justify-content: center; color: var(--theme-secondary-text-color, #667789); text-decoration: none; }
    .tabs-viewmenubar .emby-tab-button-active { color: var(--theme-background-color, #fff); }
    input:not([type="checkbox"]),select { width: min(100%, 32rem); min-height: 44px; padding: 0 12px; border: 1px solid var(--line-background, #c8d0d8); border-radius: 5px; color: var(--theme-text-color, #1e2933); background: var(--wt-card, #fff); }
    /* Geometry-only host contracts; menus still need real-host acceptance. */
    [data-select-host] .selectContainer { position: relative; }
    [data-select-host] select { width: 100%; }
    [data-select-host] .selectLabelText { display: flex; align-items: center; margin-bottom: .1em; }
    [data-select-host] .selectArrow { font-size: 1.7em; line-height: 1; }
    [data-select-host] .selectArrowContainer { position: absolute; right: .3em; pointer-events: none; }
    [data-select-host="legacy"] .selectArrowContainer { top: .1em; }
    [data-select-host="legacy"] .selectArrow { display: inline-block; margin-top: .35em; }
    [data-select-host="modern"] .emby-select-wrapper { position: relative; height: 2.4em; }
    [data-select-host="modern"] select { width: 100%; height: 100%; min-height: 0; }
    [data-select-host="modern"] .selectArrowContainer { display: flex; top: .15em; bottom: .15em; right: .15em; align-items: center; }
    .emby-toggle-label { display: flex; align-items: center; gap: 10px; min-height: 44px; color: var(--theme-text-color, #1e2933); cursor: pointer; }
    .emby-toggle-label input { position: absolute; opacity: 0; pointer-events: none; }
    .toggleLabel { flex: 1 1 auto; }
    .toggleSwitch { flex: 0 0 auto; width: 38px; height: 22px; border-radius: 999px; background: var(--button-background-color, #d7dde3); pointer-events: none; }
    .emby-toggle-label input:checked ~ .toggleSwitch { background: var(--theme-primary-color, #20a956); }
    .fieldDescription,.sectionDescription { line-height: 1.45; }
  </style></head><body><main class="fixture-host"></main><script>
  (function () {
    var params = new URLSearchParams(location.search); var scenario = params.get('scenario') || 'active';
    var theme = params.get('theme') || 'light'; var host = document.querySelector('.fixture-host'); host.className += ' ' + theme;
    var hasHostHeader = params.get('host') !== 'missing';
    var sourceHtml = ${JSON.stringify(html)}; var sourceJs = ${JSON.stringify(javascript)};
    var diagnosticData = ${JSON.stringify(diagnostic('recovering'))};
    if (scenario !== 'recovering') diagnosticData = ${JSON.stringify(diagnostic('active'))};
    if (scenario === 'drift-positive') diagnosticData = ${JSON.stringify(diagnostic('drift-positive'))};
    var apiCalls = []; var accountId = scenario === 'admin-manage' ? 'admin-other' : 'user-a'; var tokenConfigured = false; var serverAddress = location.origin;
    var stateOverride = null; var stateFailure = false; var roomsFailure = false;
    var mediaOverrides = {}; var mediaFailure = 0; var mediaDelay = 0; var stateDelay = 0;
    var imageUrlOverride; var imageDelay = 0; var imageCalls = [];
    var avatarUrlOverride; var avatarCalls = []; var avatarStatuses = {};
    var avatarDelay = Number(params.get('avatar_delay')) || 0;
    if (params.get('avatar_missing')) avatarStatuses[params.get('avatar_missing')] = Number(params.get('avatar_status')) || 404;
    var config = { PauseOtherOnPlaybackStop: true, NotifyOtherOnPlaybackStop: true, NotifyOnSyncActions: true, UpdateChannel: 'stable' };
    var nativeCreateElement = document.createElement.bind(document);
    document.createElement = function (tagName, options) {
      var element = nativeCreateElement(tagName);
      if (String(tagName).toLowerCase() === 'button' && options && options.is === 'emby-button') {
        element.classList.add('emby-button', 'emby-button-focusscale');
      }
      return element;
    };
    var rooms = (scenario === 'empty' || scenario === 'accept-fail' || scenario === 'accept-network' ? [] : scenario === 'admin-manage' ? ${JSON.stringify(stateForScenario('admin-manage'))} : scenario === 'admin-member' ? ${JSON.stringify(stateForScenario('admin-member'))} : scenario === 'recovering' ? ${JSON.stringify(stateForScenario('recovering'))} : scenario === 'primary-missing' ? ${JSON.stringify(stateForScenario('primary-missing'))} : scenario === 'waiting' ? ${JSON.stringify(stateForScenario('waiting'))} : scenario === 'state-fail' ? ${JSON.stringify(stateForScenario('state-fail'))} : scenario === 'long' ? ${JSON.stringify(stateForScenario('long'))} : ${JSON.stringify(stateForScenario('active'))});
    var invitations = [];
    function isAdminScenario() { return ['admin-manage', 'admin-member', 'save-fail', 'token-read-fail', 'token-save-fail', 'token-clear-fail', 'token-slow'].indexOf(scenario) >= 0; }
    function delay(value) { return new Promise(function (resolve) { setTimeout(function () { resolve(value); }, scenario === 'late' || scenario === 'token-slow' ? 900 : 0); }); }
    function fail(status) { var e = new Error('mock'); e.status = status; return Promise.reject(e); }
    function roomState(room) {
      var sessions = room._wtState && room._wtState.Sessions || [];
      return Object.assign({ RoomId: room.RoomId, State: room.State, StatusReason: room.StatusReason,
        Eligible: room.State === 'Watching' || room.State === 'Barrier', SyncItemId: 'item-a',
        PrimaryUserId: room.PrimaryUserId, ParticipantUserIds: room.ParticipantUserIds,
        JoinedParticipantUserIds: room.JoinedParticipantUserIds,
        Sessions: sessions.map(function (s) { return Object.assign({ SessionId: 'session-' + s.UserId, ItemId: 'item-a' }, s); })
      }, stateOverride || {});
    }
    function item(userId, itemId) {
      apiCalls.push({ method: 'GET', path: 'Users/' + userId + '/Items/' + itemId });
      var status = mediaFailure;
      var value = Object.assign({ Id: itemId, Type: 'Episode', Name: '第三集', SeriesName: '山海之间', ParentIndexNumber: 1, IndexNumber: 3, BackdropImageTags: ['own-tag'] }, mediaOverrides[itemId] || {});
      return new Promise(function (resolve, reject) { setTimeout(function () { if (status) { var e = new Error('media'); e.status = status; reject(e); } else resolve(value); }, mediaDelay); });
    }
    function imageUrl(itemId, options) {
      imageCalls.push({ itemId: itemId, options: Object.assign({}, options) });
      if (imageUrlOverride !== undefined) return imageUrlOverride;
      return new URL('/fixture-backdrop.svg?item=' + encodeURIComponent(itemId) + '&tag=' + encodeURIComponent(options.tag) + '&delay=' + imageDelay, serverAddress).href;
    }
    function userImageUrl(userId, options) {
      avatarCalls.push({ userId: userId, options: Object.assign({}, options) });
      if (avatarUrlOverride !== undefined) return avatarUrlOverride;
      return new URL('/fixture-avatar.svg?user=' + encodeURIComponent(userId) + '&status=' + (avatarStatuses[userId] || 200) + '&delay=' + avatarDelay, serverAddress).href;
    }
    function get(path) {
      apiCalls.push({ method: 'GET', path: path });
      if (path === 'WatchTogether/Users') return isAdminScenario() ? delay(${JSON.stringify(users)}) : scenario === 'users-fail' ? fail(500) : fail(403);
      if (path === 'WatchTogether/Rooms') return roomsFailure ? fail(503) : delay(rooms.slice());
      if (path.indexOf('/State') >= 0) {
        if (scenario === 'state-fail' || stateFailure) return fail(503);
        var stateRoom = rooms.filter(function (candidate) { return path.indexOf(candidate.RoomId) >= 0; })[0];
        var stateValue = stateRoom ? roomState(stateRoom) : { Sessions: [] };
        return new Promise(function (resolve) { setTimeout(function () { resolve(stateValue); }, stateDelay); });
      }
      if (path === 'WatchTogether/Invitations') return delay(invitations.slice());
      if (path === 'WatchTogether/Info') return delay({ CurrentVersion: '0.0.0-ui-mock', RepositoryUrl: 'https://example.invalid/mock' });
      if (path === 'WatchTogether/GitHubToken') {
        if (!isAdminScenario()) return fail(403);
        if (scenario === 'token-read-fail') return fail(500);
        return delay({ Configured: tokenConfigured });
      }
      if (path.indexOf('/Diagnostics') >= 0) return delay(diagnosticData);
      return delay({});
    }
    function send(path, method, body) {
      apiCalls.push({ method: method, path: path, body: body ? JSON.parse(body) : null });
      if (scenario === 'accept-fail' && path.indexOf('/Accept') >= 0) return delay({ Status: 'invalid_or_expired', Accepted: false });
      if (scenario === 'accept-network' && path.indexOf('/Accept') >= 0) return fail(503);
      if (scenario === 'save-fail' && method === 'POST' && path.indexOf('configuration') >= 0) return fail(500);
      if (path === 'WatchTogether/GitHubToken') {
        if (!isAdminScenario()) return fail(403);
        if (scenario === 'token-save-fail' && method === 'POST') return fail(500);
        if (scenario === 'token-clear-fail' && method === 'DELETE') return fail(500);
        if (method === 'POST') { tokenConfigured = true; return delay({ Configured: true }); }
        if (method === 'DELETE') { tokenConfigured = false; return delay({ Configured: false }); }
      }
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
    window.__wtFixture = { apiCalls: apiCalls, errors: [], getRooms: function () { return rooms; }, config: config,
      switchAccount: function (id) { accountId = id; }, switchServer: function (value) { serverAddress = value; },
      setRooms: function (value) { rooms = value; }, setState: function (value) { stateOverride = value; },
      setInvitations: function (value) { invitations = value; },
      setStateFailure: function (value) { stateFailure = value; }, setRoomsFailure: function (value) { roomsFailure = value; },
      setMedia: function (id, value) { mediaOverrides[id] = value; }, setMediaFailure: function (value) { mediaFailure = value; },
      setMediaDelay: function (value) { mediaDelay = value; }, setStateDelay: function (value) { stateDelay = value; },
      imageCalls: imageCalls, setImageUrl: function (value) { imageUrlOverride = value; }, setImageDelay: function (value) { imageDelay = value; },
      avatarCalls: avatarCalls, setAvatarUrl: function (value) { avatarUrlOverride = value; }, setAvatarDelay: function (value) { avatarDelay = value; },
      refresh: function () { window.__wtFixture.view.view._wtRefreshVisible(); }
    };
    var nativeCatch = Promise.prototype.catch;
    Promise.prototype.catch = function (handler) {
      return nativeCatch.call(this, function (error) {
        window.__wtFixture.errors.push(String(error && error.stack || error));
        return handler(error);
      });
    };
    window.ApiClient = { getUrl: function (path) { return path; }, serverAddress: function () { return serverAddress; }, serverId: function () { return serverAddress; }, getItem: item, getImageUrl: imageUrl, getUserImageUrl: userImageUrl, getCurrentUserId: function () { return accountId; }, getJSON: get, fetch: function (o) { return send(o.url, o.type, o.data); }, getPluginConfiguration: function () { apiCalls.push({ method: 'GET', path: 'configuration' }); return isAdminScenario() ? delay(JSON.parse(JSON.stringify(config))) : fail(403); }, updatePluginConfiguration: function (id, value) { apiCalls.push({ method: 'POST', path: 'configuration', body: value }); if (scenario === 'save-fail') return fail(500); config = JSON.parse(JSON.stringify(value)); return delay(JSON.parse(JSON.stringify(config))); } };
    function routeView(href) {
      var match = String(href || '').match(/name=([^&]+)/);
      return match ? decodeURIComponent(match[1]) : '';
    }
    function navigate(href) {
      // emby-linkbutton routes the anchor pathname and query, not its hash.
      var target = new URL(href, location.href);
      var route = routeView(target.search);
      history.replaceState({}, '', location.pathname + location.search + '#!' + target.pathname + target.search);
      if (window.__wtFixture.view) {
        window.__wtFixture.view.onPause();
        window.__wtFixture.view.onResume();
      }
      window.__wtFixture.route = route;
    }
    var tabOwner = null;
    var mainTabsManager = {
      getTabsElement: function () { return document.querySelector('.tabs-viewmenubar'); },
      selectedTabIndex: function (selected) { document.querySelectorAll('.emby-tab-button').forEach(function (link, i) { link.classList.toggle('emby-tab-button-active', i === selected); link.setAttribute('aria-current', i === selected ? 'page' : 'false'); }); },
      setTabs: function (page, selectedIndex, getTabs) {
        var middle = document.querySelector('.skinHeader .headerMiddle');
        if (!middle) throw new Error('missing host');
        if (!page) { middle.innerHTML = ''; tabOwner = null; return { tabsContainer: middle, replaced: false }; }
        if (tabOwner === page) return { tabsContainer: middle, replaced: false };
        tabOwner = page;
        middle.innerHTML = '';
        var menubar = document.createElement('div');
        menubar.className = 'tabs-viewmenubar tabs-viewmenubar-backgroundcontainer emby-tabs';
        var slider = document.createElement('div');
        slider.className = 'tabs-viewmenubar-slider emby-tabs-slider';
        (getTabs() || []).forEach(function (tab, index) {
          var link = document.createElement('a');
          var viewName = routeView(tab.href);
          link.id = viewName.indexOf('Invite') >= 0 ? 'wtTabInvite' : viewName.indexOf('Settings') >= 0 ? 'wtTabSettings' : 'wtTabRooms';
          link.setAttribute('is', 'emby-linkbutton');
          link.className = 'emby-button emby-tab-button main-tab-button' + (index === selectedIndex ? ' emby-tab-button-active' : '');
          link.href = tab.href;
          link.textContent = tab.name;
          link.setAttribute('aria-current', index === selectedIndex ? 'page' : 'false');
          link.addEventListener('click', function (event) {
            event.preventDefault();
            navigate(tab.href);
          });
          slider.appendChild(link);
        });
        menubar.appendChild(slider);
        middle.appendChild(menubar);
        return { tabsContainer: middle, replaced: true };
      }
    };
    window.define = function (deps, factory) { var BaseView = function () { this.view = arguments[0] || null; }; BaseView.prototype.onResume = function () {}; BaseView.prototype.onPause = function () {}; window.__wtViewCtor = factory(BaseView, { addEventListener: function (el, name, fn) { if (el) el.addEventListener(name, fn); } }, { show: function () {}, hide: function () {} }, {}, {}, {}, {}, mainTabsManager); };
    var hostHeader = hasHostHeader ? '<header class="skinHeader" data-mock-host="true"><div class="headerLeft">emby</div><div class="headerMiddle headerSection sectionTabs"></div><div class="headerRight">mock host</div></header>' : '';
    document.querySelector('.fixture-host').innerHTML = hostHeader + '<div class="fixture-main"><div class="fixture-label" data-mock="true">模拟宿主 / mock Emby host + API · 不连接真实 Emby</div>' + sourceHtml + '</div>';
    var page = document.querySelector('.wt-page');
    var selectHost = params.get('select_host');
    if (selectHost === 'legacy' || selectHost === 'modern') {
      host.setAttribute('data-select-host', selectHost);
      page.querySelectorAll('select[is="emby-select"]').forEach(function (select) {
        select.classList.add('emby-select');
        var label = document.createElement('label');
        label.className = 'selectLabelText';
        label.textContent = select.getAttribute('label');
        label.htmlFor = select.id;
        var arrow = document.createElement('div');
        arrow.className = 'selectArrowContainer';
        arrow.innerHTML = '<i class="selectArrow md-icon">&#xe313;</i>';
        if (selectHost === 'legacy') {
          select.before(label);
          arrow.insertAdjacentHTML('afterbegin', '<div style="visibility:hidden">0</div>');
          select.parentElement.appendChild(arrow);
        } else {
          var wrapper = document.createElement('div');
          wrapper.className = 'emby-select-wrapper';
          var outerLabel = document.createElement('label');
          outerLabel.className = 'selectLabel';
          select.replaceWith(outerLabel);
          outerLabel.append(label, wrapper);
          wrapper.append(select, arrow);
        }
      });
    }
    try { (new Function(sourceJs))(); var view = new window.__wtViewCtor(page); view.view = page; view.onResume(); window.__wtFixture.view = view; } catch (error) { window.__wtFixture.error = String(error && error.stack || error); }
  }());
  </script></body></html>`;
}

const server = http.createServer((req, res) => {
  const parsed = new URL(req.url, 'http://127.0.0.1:4179');
  if (parsed.pathname === '/fixture-avatar.svg') {
    const status = Number(parsed.searchParams.get('status')) || 200;
    return setTimeout(() => {
      if (res.destroyed) return;
      if (status !== 200) { res.writeHead(status); res.end('fixture avatar unavailable'); return; }
      const color = parsed.searchParams.get('user') === 'user-a' ? '#406452' : '#405c82';
      res.writeHead(200, { 'content-type': 'image/svg+xml', 'cache-control': 'no-store' });
      res.end('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 96 96"><path fill="#dfe8e4" d="M0 0h96v96H0z"/><circle cx="48" cy="35" r="17" fill="' + color + '"/><ellipse cx="48" cy="91" rx="34" ry="31" fill="' + color + '"/></svg>');
    }, Math.min(2000, Math.max(0, Number(parsed.searchParams.get('delay')) || 0)));
  }
  if (parsed.pathname === '/fixture-backdrop.svg') {
    const tag = parsed.searchParams.get('tag');
    const delayMs = Math.max(0, Math.min(3000, Number(parsed.searchParams.get('delay')) || 0));
    return setTimeout(() => {
      if (res.destroyed) return;
      if (tag === 'missing') { res.writeHead(404); res.end('fixture missing'); return; }
      res.writeHead(200, { 'content-type': 'image/svg+xml', 'cache-control': 'no-store' });
      res.end('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1280 720"><defs><linearGradient id="sky" x2="0" y2="1"><stop stop-color="#31546b"/><stop offset="1" stop-color="#779eab"/></linearGradient></defs><path fill="url(#sky)" d="M0 0h1280v720H0z"/><path fill="#324e5a" d="M0 430L220 170l200 290 180-170 180 230 210-260 290 230v230H0z"/><path fill="#183945" d="M0 560l270-110 220 110 280-90 230 100 280-140v290H0z"/><path fill="#12333f" d="M0 635q320-60 640 0t640 0v85H0z"/></svg>');
    }, delayMs);
  }
  if (req.url.split('?')[0] === '/health') return json(res, { ok: true });
  if (req.url.split('?')[0] === '/') { res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' }); return res.end(page()); }
  res.writeHead(404); res.end('not found');
});
server.listen(4179, '127.0.0.1');

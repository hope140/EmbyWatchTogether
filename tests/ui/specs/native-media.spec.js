const { test, expect } = require('@playwright/test');
const fs = require('fs');
const path = require('path');
const artifacts = path.join(__dirname, '..', 'test-results', 'native-pages');
fs.mkdirSync(artifacts, { recursive: true });

async function open(page, scenario = 'active', theme = 'light', width = 1100) {
  await page.setViewportSize({ width, height: 1000 });
  await page.goto(`/?scenario=${scenario}&theme=${theme}`);
  await expect(page.locator('.wt-roomState')).not.toHaveText('正在读取状态');
  await expect(page.locator('.wt-roomCard')).toHaveCount(1);
}
async function mediaCalls(page) {
  return page.evaluate(() => window.__wtFixture.apiCalls.filter(x => x.path.startsWith('Users/')));
}
async function refresh(page) {
  const before = await page.evaluate(() => window.__wtFixture.apiCalls.filter(x => x.path.endsWith('/State')).length);
  await page.locator('#wtRefresh').click();
  await expect.poll(() => page.evaluate(() => window.__wtFixture.apiCalls.filter(x => x.path.endsWith('/State')).length)).toBeGreaterThan(before);
}

test('当前用户权限查询剧集，纯文本共享标题且暂停仍可同步', async ({ page }) => {
  await open(page);
  await expect(page.locator('.wt-roomMediaLabel')).toHaveText('正在一起观看');
  await expect(page.locator('.wt-roomMedia strong')).toHaveText('山海之间');
  await expect(page.locator('.wt-roomMediaDetail')).toHaveText('第 1 季 · 第 3 集');
  expect((await mediaCalls(page))[0].path).toBe('Users/user-a/Items/item-a');
  await expect(page.locator('.wt-roomMedia svg, .wt-roomMedia button, .wt-roomMedia img')).toHaveCount(0);
  await page.evaluate(() => window.__wtFixture.setState({ Sessions: [
    { UserId: 'user-a', ItemId: 'item-a', Online: true, IsPaused: true },
    { UserId: 'user-b', ItemId: 'item-a', Online: true, IsPaused: true }
  ] }));
  await refresh(page);
  await expect(page.locator('.wt-roomState')).toHaveText('同步中');
  await expect(page.locator('.wt-roomMedia')).toContainText('山海之间');
  await page.screenshot({ path: path.join(artifacts, 'rooms-media-light.png'), fullPage: true });
});

const unsafeStates = [
  ['waiting残留item', { State: 'Waiting', StatusReason: 'waiting_for_playback' }, '等待播放'],
  ['视频不同', { State: 'Waiting', StatusReason: 'different_video' }, '视频不一致'],
  ['恢复', { State: 'Recovering', StatusReason: 'transient_recovery' }, '等待连接恢复'],
  ['换集', { State: 'Handoff', StatusReason: 'media_handoff' }, '正在同步视频'],
  ['暂离', { State: 'Waiting', StatusReason: 'member_left', JoinedParticipantUserIds: ['user-a'] }, '等待参与者'],
  ['快照不可用', { StatusReason: 'snapshot_unavailable' }, '状态暂不可用'],
  ['资格失败', { Eligible: false }, '同步中'],
  ['无同步item', { SyncItemId: '' }, '同步中'],
  ['错误成员', { ParticipantUserIds: ['user-a', 'other'] }, '同步中'],
  ['重复会话用户', { Sessions: [{ UserId: 'user-a', Online: true, ItemId: 'item-a' }, { UserId: 'user-a', Online: true, ItemId: 'item-a' }] }, '同步中'],
  ['错误房间', { RoomId: 'another-room' }, '状态暂不可用']
];
for (const [name, state, label] of unsafeStates) {
  test(`媒体gate拒绝${name}并清除旧片名`, async ({ page }) => {
    await open(page);
    await expect(page.locator('.wt-roomMedia')).toContainText('山海之间');
    const before = (await mediaCalls(page)).length;
    await page.evaluate(value => window.__wtFixture.setState(value), state);
    await refresh(page);
    await expect(page.locator('.wt-roomMedia')).toBeHidden();
    await expect(page.locator('.wt-roomState')).toHaveText(label);
    expect((await mediaCalls(page)).length).toBe(before);
  });
}

test('Barrier显示正在同步并保留正确剧集', async ({ page }) => {
  await open(page);
  await page.evaluate(() => window.__wtFixture.setState({ State: 'Barrier', StatusReason: 'aligning' }));
  await refresh(page);
  await expect(page.locator('.wt-roomMediaLabel')).toHaveText('正在同步');
  await expect(page.locator('.wt-roomMedia strong')).toHaveText('山海之间');
  await expect(page.locator('.wt-roomMediaDetail')).toHaveText('第 1 季 · 第 3 集');
});

test('媒体权限撤回或错误Id立即清标题，后续可重新读取', async ({ page }) => {
  await open(page);
  await expect(page.locator('.wt-roomMedia')).toContainText('山海之间');
  for (const status of [403, 404, 500]) {
    await page.evaluate(value => window.__wtFixture.setMediaFailure(value), status);
    await refresh(page);
    await expect(page.locator('.wt-roomMedia')).toHaveText('媒体信息暂不可用');
  }
  await page.evaluate(() => { window.__wtFixture.setMediaFailure(0); window.__wtFixture.setMedia('item-a', { Id: 'wrong' }); });
  await refresh(page);
  await expect(page.locator('.wt-roomMedia')).toHaveText('媒体信息暂不可用');
  await page.evaluate(() => window.__wtFixture.setMedia('item-a', { Id: 'item-a' }));
  await refresh(page);
  await expect(page.locator('.wt-roomMedia')).toContainText('山海之间');
});

test('State及Rooms读取失败清除当前媒体，保留可重试反馈', async ({ page }) => {
  await open(page);
  await expect(page.locator('.wt-roomMedia')).toContainText('山海之间');
  await page.evaluate(() => window.__wtFixture.setStateFailure(true));
  await refresh(page);
  await expect(page.locator('.wt-roomState')).toHaveText('状态暂不可用');
  await expect(page.locator('.wt-roomMedia')).toBeHidden();
  await page.evaluate(() => { window.__wtFixture.setStateFailure(false); });
  await refresh(page);
  await expect(page.locator('.wt-roomMedia')).toContainText('山海之间');
  await page.evaluate(() => window.__wtFixture.setRoomsFailure(true));
  await page.locator('#wtRefresh').click();
  await expect(page.locator('#wtStatus')).toContainText('房间加载失败');
  await expect(page.locator('.wt-roomMedia')).toBeHidden();
});

test('慢媒体响应不阻塞下一次State，Waiting后旧片名不能回显', async ({ page }) => {
  await open(page);
  await expect(page.locator('.wt-roomMedia')).toContainText('山海之间');
  await page.evaluate(() => { window.__wtFixture.setMediaDelay(800); window.__wtFixture.setMedia('item-a', { SeriesName: '迟到片名' }); });
  const before = (await mediaCalls(page)).length;
  await refresh(page);
  await expect.poll(async () => (await mediaCalls(page)).length).toBeGreaterThan(before);
  await page.evaluate(() => window.__wtFixture.setState({ State: 'Waiting', StatusReason: 'waiting_for_playback' }));
  await refresh(page);
  await expect(page.locator('.wt-roomState')).toHaveText('等待播放');
  await page.waitForTimeout(900);
  await expect(page.locator('.wt-roomMedia')).toBeHidden();
  await expect(page.locator('.wt-roomCard')).not.toContainText('迟到片名');
});

test('切换Item与房间移除均拒绝旧媒体', async ({ page }) => {
  await open(page);
  await expect(page.locator('.wt-roomMedia')).toContainText('山海之间');
  await page.evaluate(() => { window.__wtFixture.setMediaDelay(600); window.__wtFixture.setMedia('item-a', { SeriesName: '旧目标A' }); });
  const count = (await mediaCalls(page)).length;
  await refresh(page);
  await expect.poll(async () => (await mediaCalls(page)).length).toBeGreaterThan(count);
  await page.evaluate(() => window.__wtFixture.setState({ SyncItemId: 'item-b', Sessions: [
    { UserId: 'user-a', ItemId: 'item-b', Online: true }, { UserId: 'user-b', ItemId: 'item-b', Online: true }
  ] }));
  await refresh(page);
  await page.waitForTimeout(700);
  await expect(page.locator('.wt-roomCard')).not.toContainText('旧目标A');
  await page.evaluate(() => { window.__wtFixture.setMediaDelay(0); window.__wtFixture.setMedia('item-b', { SeriesName: '新目标B' }); });
  await refresh(page);
  await expect(page.locator('.wt-roomMedia')).toContainText('新目标B');
  await page.evaluate(() => window.__wtFixture.setRooms([]));
  await page.locator('#wtRefresh').click();
  await expect(page.locator('.wt-roomCard')).toHaveCount(0);
});

for (const identity of ['account', 'server', 'pause']) {
  test(`迟到媒体在${identity}变化后不回写且旧操作不发送`, async ({ page }) => {
    await open(page);
    await expect(page.locator('.wt-roomMedia')).toContainText('山海之间');
    await page.evaluate(() => { window.__wtFixture.setMediaDelay(500); window.__wtFixture.setMedia('item-a', { SeriesName: '旧账号标题' }); });
    const before = (await mediaCalls(page)).length;
    await refresh(page);
    await expect.poll(async () => (await mediaCalls(page)).length).toBeGreaterThan(before);
    await page.evaluate(kind => {
      const f = window.__wtFixture;
      const button = document.querySelector('[data-act="participantResync"]');
      if (kind === 'account') f.switchAccount('other-user');
      if (kind === 'server') f.switchServer('http://other.invalid');
      if (kind === 'pause') f.view.onPause();
      button.click();
    }, identity);
    await page.waitForTimeout(650);
    await expect(page.locator('.wt-roomCard')).toHaveCount(0);
    expect(await page.evaluate(() => window.__wtFixture.apiCalls.filter(x => x.method !== 'GET').length)).toBe(0);
    await page.evaluate(() => { window.__wtFixture.setMediaDelay(0); window.__wtFixture.setMedia('item-a', { SeriesName: '恢复后标题' }); window.__wtFixture.view.onResume(); });
    await expect(page.locator('.wt-roomMedia')).toContainText('恢复后标题');
    if (identity === 'account') expect((await mediaCalls(page)).at(-1).path).toBe('Users/other-user/Items/item-a');
  });
}

test('电影、特别篇、缺失字段及恶意长标题安全渲染', async ({ page }) => {
  await open(page, 'active', 'dark', 390);
  for (const [item, expected] of [
    [{ Type: 'Movie', Name: '示例电影' }, '示例电影'],
    [{ Type: 'Episode', ParentIndexNumber: 0, IndexNumber: 2 }, '特别篇 · 第 2 集'],
    [{ Type: 'Episode', SeriesName: null, Name: '独立一集', ParentIndexNumber: null, IndexNumber: null }, '独立一集'],
    [{ Type: 'Movie', Name: '<img src=x onerror=alert(1)>' + '很长的标题'.repeat(25) }, '<img src=x onerror=alert(1)>']
  ]) {
    await page.evaluate(value => window.__wtFixture.setMedia('item-a', value), item);
    await refresh(page);
    await expect(page.locator('.wt-roomMedia')).toContainText(expected);
    await expect(page.locator('.wt-roomMedia img')).toHaveCount(0);
    const width = await page.evaluate(() => [document.documentElement.scrollWidth, document.documentElement.clientWidth]);
    expect(width[0]).toBeLessThanOrEqual(width[1] + 1);
  }
});

test('切换三页保持单导航，状态未变时焦点与busy稳定', async ({ page }) => {
  await open(page, 'admin-member');
  for (let i = 0; i < 3; i++) {
    for (const name of ['Invite', 'Settings', 'Rooms']) {
      await page.locator('.tabs-viewmenubar a[href*="WatchTogether' + name + 'V18"]').click();
      await expect(page.locator('.tabs-viewmenubar a')).toHaveCount(3);
    }
  }
  await expect(page.locator('.wt-roomMedia')).toContainText('山海之间');
  await page.locator('[data-act="pause"]').focus();
  await page.evaluate(() => window.__wtFixture.refresh());
  await expect(page.locator('#wtRooms')).toHaveAttribute('aria-busy', 'false');
  await expect(page.locator('[data-act="pause"]')).toBeFocused();
  await page.locator('[data-act="diagnostics"]').click();
  await expect(page.locator('.wt-diagnosticSummary').first()).toBeVisible();
});

test('State挂起超过新鲜度窗口不继续展示旧片名，成功后恢复', async ({ page }) => {
  await page.clock.install();
  await open(page);
  await expect(page.locator('.wt-roomMedia')).toContainText('山海之间');
  // Isolate the delayed read from periodic retries; the freshness timer still runs.
  await page.evaluate(() => clearInterval(window.__wtFixture.view.view._wtTimer));
  await page.evaluate(() => window.__wtFixture.setStateDelay(20000));
  await refresh(page);
  await page.clock.fastForward(11000);
  await expect(page.locator('.wt-roomMedia')).toBeHidden();
  await expect(page.locator('.wt-roomState')).toHaveText('状态暂不可用');
  await page.clock.fastForward(10000);
  await expect(page.locator('.wt-roomMedia')).toBeHidden();
  await page.evaluate(() => window.__wtFixture.setStateDelay(0));
  await refresh(page);
  await expect(page.locator('.wt-roomMedia')).toContainText('山海之间');
});

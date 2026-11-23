const { test, expect } = require('@playwright/test');

async function installClock(page) {
  await page.clock.install();
  await page.addInitScript(() => {
    window.__wtHidden = false;
    Object.defineProperty(document, 'hidden', { configurable: true, get: () => window.__wtHidden });
  });
}

const counts = page => page.evaluate(() => {
  const calls = window.__wtFixture.apiCalls;
  return {
    list: calls.filter(c => c.path === 'WatchTogether/Rooms').length,
    state: calls.filter(c => c.path.endsWith('/State')).length,
    media: calls.filter(c => c.path.includes('/Items/')).length,
    commands: calls.filter(c => c.method !== 'GET' && c.path.startsWith('WatchTogether/Rooms')).length,
  };
});

async function visibility(page, hidden) {
  await page.evaluate(value => {
    window.__wtHidden = value;
    document.dispatchEvent(new Event('visibilitychange'));
  }, hidden);
}

test('我的房间每秒读本人状态，同一影片限频，隐藏和离页时暂停', async ({page}) => {
  await installClock(page);
  await page.goto('/?scenario=active');
  await expect(page.locator('.wt-roomMedia')).toContainText('山海之间');
  await expect(page.locator('#wtRefresh')).toHaveCount(0);
  const before = await counts(page);
  await page.clock.runFor(3100);
  const foreground = await counts(page);
  expect(foreground.list - before.list).toBeGreaterThanOrEqual(3);
  expect(foreground.state - before.state).toBeGreaterThanOrEqual(3);
  expect(foreground.media).toBe(before.media);

  await visibility(page, true);
  await page.clock.runFor(7300);
  expect((await counts(page)).list).toBe(foreground.list);
  expect((await counts(page)).state).toBe(foreground.state);
  await visibility(page, false);
  await expect.poll(async () => (await counts(page)).state).toBeGreaterThan(foreground.state);
  await expect.poll(async () => (await counts(page)).media).toBeGreaterThan(foreground.media);

  await page.evaluate(() => window.__wtFixture.view.onPause());
  const paused = await counts(page);
  await page.clock.runFor(3300);
  expect(await counts(page)).toEqual(paused);
  expect(paused.commands).toBe(0);
});

test('后台多房间每轮只取一份列表摘要，不逐房读取状态或影片', async ({page}) => {
  await installClock(page);
  await page.goto('/?scenario=admin-manage#!configurationpage?name=WatchTogetherAdminV39');
  await expect(page.locator('.wt-adminRoom')).toHaveCount(1);
  await page.evaluate(() => {
    const fixture = window.__wtFixture;
    const room = fixture.getRooms()[0];
    fixture.setRooms(Array.from({length: 24}, (_, index) => ({...room, RoomId: 'managed-' + index,
      Playback: {ItemId:'shared-item', PrimaryPaused:false, OtherPaused:false, Percent:50, ValidForMs:10000}})));
  });
  await expect(page.locator('.wt-adminRoom')).toHaveCount(24);
  await expect(page.locator('.wt-cardWithImage').first()).toBeVisible();
  await expect(page.locator('#wtRefresh')).toHaveCount(0);
  const before = await counts(page);
  await page.clock.runFor(3200);
  const after = await counts(page);
  expect(after.list - before.list).toBeGreaterThanOrEqual(3);
  expect(after.state).toBe(0);
  expect(after.media).toBe(1);
  expect(after.commands).toBe(0);
  const requests = await page.evaluate(() => window.__wtFixture.apiCalls.filter(c => c.path === 'WatchTogether/Rooms'));
  expect(requests.every(c => c.url.endsWith('?Scope=all&IncludePlayback=true'))).toBe(true);
  await visibility(page, true);
  await page.clock.runFor(2400);
  expect(await counts(page)).toEqual(after);
});

for (const slowResource of ['list', 'state']) test('慢请求不重叠且完成后继续轮询 ' + slowResource, async ({page}) => {
  await installClock(page);
  await page.goto('/?scenario=active');
  await expect(page.locator('.wt-roomMedia')).toContainText('山海之间');
  await page.evaluate(kind => {
    const get = ApiClient.getJSON;
    window.__wtHeldCalls = 0;
    ApiClient.getJSON = path => {
      if (kind === 'list' ? path.startsWith('WatchTogether/Rooms?') : path.endsWith('/State')) {
        window.__wtHeldCalls++;
        return new Promise(resolve => { window.__wtRelease = () => resolve(get(path)); });
      }
      return get(path);
    };
    window.__wtRestoreGet = () => { ApiClient.getJSON = get; };
  }, slowResource);
  await page.clock.runFor(3300);
  expect(await page.evaluate(() => window.__wtHeldCalls)).toBe(1);
  await page.evaluate(() => {
    window.__wtRestoreGet();
    window.__wtRelease();
  });
  const before = await counts(page);
  await page.clock.runFor(3000);
  const after = await counts(page);
  expect(after[slowResource]).toBeGreaterThan(before[slowResource]);
  expect(after.commands).toBe(0);
});

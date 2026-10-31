const { test, expect } = require('@playwright/test');

async function prepareCreate(page, mode = 'success') {
  await page.goto('/?scenario=admin-member&select_host=legacy');
  await expect(page.locator('.wt-roomCard')).toHaveCount(1);
  await page.evaluate(mode => {
    const fixture = window.__wtFixture;
    const template = fixture.getRooms()[0];
    const originalFetch = ApiClient.fetch;
    const originalGet = ApiClient.getJSON;
    fixture.setRooms([]);
    fixture.creationCompleted = false;
    ApiClient.getJSON = path => {
      if (mode === 'refresh-fails' && fixture.creationCompleted && path === 'WatchTogether/Rooms') {
        return Promise.reject({ status: 503 });
      }
      return originalGet(path);
    };
    ApiClient.fetch = options => {
      if (options.type !== 'POST' || options.url !== 'WatchTogether/Rooms') return originalFetch(options);
      const input = JSON.parse(options.data);
      return originalFetch(options).then(() => {
        if (mode === 'post-fails') throw { status: 503 };
        const complete = () => {
          fixture.creationCompleted = true;
          fixture.setRooms([Object.assign({}, template, input, { RoomId: 'created-room' })]);
          return { RoomId: 'created-room' };
        };
        if (mode === 'delayed') return new Promise(resolve => { fixture.finishCreate = () => resolve(complete()); });
        return complete();
      });
    };
  }, mode);
  await navigate(page, 'Invite');
  await page.locator('#wtRoomName').fill('周末一起看');
  await page.locator('#wtUserA').selectOption('user-a');
  await page.locator('#wtUserB').selectOption('user-b');
  await page.locator('#wtPrimary').selectOption('user-a');
}

async function navigate(page, route) {
  await page.locator(`.tabs-viewmenubar a[href*="WatchTogether${route}V"]`).click();
  await expect(page.locator(route === 'Invite' ? '#wtAdminSection' : '#wtRoomsSection')).toBeVisible();
  if (route === 'Invite') await expect(page.locator('#wtUserA option')).toHaveCount(3);
}

const success = '房间“周末一起看”已创建，请让双方打开同一视频。';

test('创建成功反馈在当前创建页可见，刷新及名称校验不会覆盖', async ({ page }) => {
  await prepareCreate(page);
  await page.locator('#wtCreate').click();
  const result = page.locator('#wtAdminSection').getByText(success, { exact: true });
  await expect(result).toBeVisible();
  await expect(page.locator('#wtRoomName')).toHaveValue('');
  await expect(page.locator('#wtCreate')).not.toHaveAttribute('aria-busy', 'true');
  const roomReads = await page.evaluate(() => window.__wtFixture.apiCalls.filter(x => x.method === 'GET' && x.path === 'WatchTogether/Rooms').length);
  await page.evaluate(() => window.__wtFixture.view.view._wtRefreshVisible());
  await expect.poll(() => page.evaluate(() => window.__wtFixture.apiCalls.filter(x => x.method === 'GET' && x.path === 'WatchTogether/Rooms').length)).toBeGreaterThan(roomReads);
  await expect.poll(() => page.evaluate(() => window.__wtFixture.view.view._wtRoomsLoading)).toBe(false);
  await expect(result).toBeVisible();
  expect(await page.evaluate(() => window.__wtFixture.apiCalls.filter(x => x.method === 'POST' && x.path === 'WatchTogether/Rooms').length)).toBe(1);
  await page.screenshot({ path: test.info().outputPath('create-feedback.png'), fullPage: true });
});

test('创建POST成功后列表刷新失败仍保留创建成功事实', async ({ page }) => {
  await prepareCreate(page, 'refresh-fails');
  await page.locator('#wtCreate').click();
  await expect(page.locator('#wtAdminSection').getByText(success, { exact: true })).toBeVisible();
  await expect(page.locator('#wtStatus')).toContainText('房间加载失败');
  await expect(page.locator('#wtStatus')).not.toContainText('创建失败');
  expect(await page.evaluate(() => window.__wtFixture.apiCalls.filter(x => x.method === 'POST' && x.path === 'WatchTogether/Rooms').length)).toBe(1);
});

test('创建POST失败在创建表单显示错误并保留输入供重试', async ({ page }) => {
  await prepareCreate(page, 'post-fails');
  await page.locator('#wtCreate').click();
  await expect(page.locator('#wtAdminSection').getByText(/^创建失败：/)).toBeVisible();
  await expect(page.locator('#wtAdminSection').getByText(success, { exact: true })).toHaveCount(0);
  await expect(page.locator('#wtRoomName')).toHaveValue('周末一起看');
  await expect(page.locator('#wtCreate')).toBeEnabled();
});

test('离页后旧创建完成不能向新页面写入成功反馈', async ({ page }) => {
  await prepareCreate(page, 'delayed');
  await page.locator('#wtCreate').click();
  await expect.poll(() => page.evaluate(() => typeof window.__wtFixture.finishCreate)).toBe('function');
  await navigate(page, 'Rooms');
  await navigate(page, 'Invite');
  await page.evaluate(() => window.__wtFixture.finishCreate());
  await expect(page.locator('#wtAdminSection').getByText(success, { exact: true })).toHaveCount(0);
  await expect(page.locator('#wtRoomName')).toHaveValue('');
  await expect(page.locator('#wtCreate')).not.toHaveAttribute('aria-busy', 'true');
});

test('离开创建页后已显示的成功反馈被清除', async ({ page }) => {
  await prepareCreate(page);
  await page.locator('#wtCreate').click();
  await expect(page.locator('#wtAdminSection').getByText(success, { exact: true })).toBeVisible();
  await navigate(page, 'Rooms');
  await navigate(page, 'Invite');
  await expect(page.locator('#wtAdminSection').getByText(success, { exact: true })).toHaveCount(0);
});

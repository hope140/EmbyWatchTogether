const { test, expect } = require('@playwright/test');
async function calls(page) { return page.evaluate(() => window.__wtFixture.apiCalls.filter(x => x.path === 'WatchTogether/Users').length); }
async function overrideRole(page, mode) {
  await page.addInitScript(mode => {
    let api;
    Object.defineProperty(window, 'ApiClient', { configurable: true, get: () => api, set: value => {
      api = value;
      api.getCurrentUser = () => {
        if (mode === 'failure') return Promise.reject({ status: 500 });
        if (mode === 'missing') return Promise.resolve({ Id: api.getCurrentUserId() });
        if (mode === 'mismatch') return Promise.resolve({ Id: 'another-user', Policy: { IsAdministrator: true } });
        return new Promise(resolve => { window.resolveRole = resolve; });
      };
    }});
  }, mode);
}
for (const scenario of ['active', 'empty']) test('普通用户不请求管理员用户列表 '+scenario, async ({ page }) => {
  await page.goto('/?scenario='+scenario);
  await expect(page.locator('#wtRooms')).toHaveAttribute('aria-busy', 'false');
  expect(await calls(page)).toBe(0);
  await expect(page.locator('#wtStatus')).not.toContainText('加载失败');
  expect(await page.evaluate(() => window.__wtFixture.view.view._wtIsAdmin)).toBe(false);
  await page.locator('.tabs-viewmenubar a[href*="Invite"]').click();
  await expect(page.locator('#wtCreateInvitation')).toBeVisible();
  await expect(page.locator('#wtAdminSection')).toBeHidden();
});
test('管理员保留用户列表，真正500仍可见', async ({page}) => {
  await page.goto('/?scenario=admin-member');
  await expect.poll(() => calls(page)).toBe(1);
  await expect(page.locator('#wtUserA option')).toHaveCount(3);
  await page.goto('/?scenario=users-fail');
  await expect(page.locator('#wtStatus')).toContainText('用户列表加载失败');
  expect(await calls(page)).toBe(1);
});
for (const mode of ['failure','missing','mismatch']) test('角色不可确认不授予管理权限 '+mode, async({page}) => {
  await overrideRole(page,mode); await page.goto('/?scenario=admin-member');
  await expect(page.locator('#wtStatus')).toContainText('当前用户权限读取失败');
  expect(await calls(page)).toBe(0);
  await expect(page.locator('#wtAdminSection')).toBeHidden();
  expect(await page.evaluate(() => window.__wtFixture.view.view._wtIsAdmin)).not.toBe(true);
});
for (const change of ['account','server','page']) test('迟到角色不能授权旧身份 '+change, async({page}) => {
  await overrideRole(page,'pending'); await page.goto('/?scenario=admin-member');
  await expect.poll(() => page.evaluate(() => typeof window.resolveRole)).toBe('function');
  await page.evaluate(change => {
    if(change==='account') window.__wtFixture.switchAccount('user-b');
    else if(change==='server') window.__wtFixture.switchServer('https://other.invalid');
    else window.__wtFixture.view.onPause();
    window.resolveRole({Id:'user-a',Policy:{IsAdministrator:true}});
  },change);
  await page.waitForTimeout(100);
  expect(await calls(page)).toBe(0);
  expect(await page.evaluate(() => window.__wtFixture.view.view._wtIsAdmin)).not.toBe(true);
});

const { test, expect } = require('@playwright/test');
const fs = require('fs');
const path = require('path');
const artifacts = path.join(__dirname, '..', 'test-results', 'cinema-ui');
fs.mkdirSync(artifacts, { recursive: true });

async function open(page, scenario = 'admin-member', width = 1440, theme = 'light') {
  await page.setViewportSize({ width, height: 1050 });
  await page.goto(`/?scenario=${scenario}&theme=${theme}`);
  await expect(page.locator('.wt-roomState')).not.toHaveText('正在读取状态');
  await expect(page.locator('.wt-roomCard')).toHaveCount(1);
}
async function refresh(page) {
  const count = await page.evaluate(() => window.__wtFixture.apiCalls.filter(x => x.path.endsWith('/State')).length);
  await page.locator('#wtRefresh').click();
  await expect.poll(() => page.evaluate(() => window.__wtFixture.apiCalls.filter(x => x.path.endsWith('/State')).length)).toBeGreaterThan(count);
}
async function hasBackdrop(page) {
  await expect(page.locator('.wt-roomPresentation')).toHaveClass(/wt-roomPresentation--image/);
  await expect(page.locator('.wt-roomBackdrop')).toBeVisible();
}
async function noBackdrop(page) {
  await expect(page.locator('.wt-roomPresentation')).not.toHaveClass(/wt-roomPresentation--image/);
  await expect(page.locator('.wt-roomBackdrop')).not.toHaveAttribute('src', /.+/);
}
async function selectView(page, suffix) {
  await page.locator('.headerMiddle a[href*="WatchTogether' + suffix + 'V16"]').click();
  await expect(page.locator('.tabs-viewmenubar')).toHaveCount(1);
}

test('本人媒体背景优先于父背景且全部原生操作可访问', async ({ page }) => {
  await open(page);
  await hasBackdrop(page);
  await page.evaluate(() => window.__wtFixture.setMedia('item-a', { ParentBackdropItemId: 'series-secret-id', ParentBackdropImageTags: ['parent-secret-tag'] }));
  await refresh(page);
  await hasBackdrop(page);
  const imageCall = await page.evaluate(() => window.__wtFixture.imageCalls.at(-1));
  expect(imageCall.itemId).toBe('item-a');
  expect(imageCall.options).toMatchObject({ type: 'Backdrop', index: 0, tag: 'own-tag' });
  for (const action of ['pause', 'resume', 'resync', 'leave', 'delete', 'diagnostics']) {
    await expect(page.locator(`[data-act="${action}"]`)).toBeVisible();
    await expect(page.locator(`[data-act="${action}"]`)).toHaveClass(/emby-button-focusscale/);
  }
  await page.screenshot({ path: path.join(artifacts, 'room-playing-light.png'), fullPage: true });
});

test('父背景fallback，无图及404保留正确片名与可用控件', async ({ page }) => {
  await open(page);
  await page.evaluate(() => window.__wtFixture.setMedia('item-a', { BackdropImageTags: [], ParentBackdropItemId: 'series-a', ParentBackdropImageTags: ['parent-tag'] }));
  await refresh(page);
  await hasBackdrop(page);
  expect(await page.evaluate(() => window.__wtFixture.imageCalls.at(-1).itemId)).toBe('series-a');
  await page.evaluate(() => window.__wtFixture.setMedia('item-a', { BackdropImageTags: [], ParentBackdropImageTags: [], ParentBackdropItemId: null }));
  await refresh(page);
  await noBackdrop(page);
  await expect(page.locator('.wt-roomMedia')).toContainText('山海之间');
  await page.evaluate(() => window.__wtFixture.setMedia('item-a', { BackdropImageTags: ['missing'] }));
  await refresh(page);
  await noBackdrop(page);
  await expect(page.locator('.wt-roomMedia')).toContainText('山海之间');
  await expect(page.locator('[data-act="pause"]')).toBeEnabled();
});

test('背景URL在请求前拒绝外站、凭据和非HTTP协议', async ({ page }) => {
  await open(page);
  await hasBackdrop(page);
  const attempted = [];
  page.on('request', request => { if (request.resourceType() === 'image') attempted.push(request.url()); });
  for (const url of [
    null, '', 0,
    'https://foreign.invalid/backdrop.jpg',
    'http://127.0.0.1:4179@foreign.invalid/backdrop.jpg',
    'http://user:fixture-password@127.0.0.1:4179/fixture-backdrop.svg',
    'data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg"/>',
    'javascript:alert(1)', 'blob:http://127.0.0.1:4179/opaque'
  ]) {
    await page.evaluate(value => window.__wtFixture.setImageUrl(value), url);
    await refresh(page);
    await noBackdrop(page);
    await expect(page.locator('.wt-roomMedia')).toContainText('山海之间');
  }
  expect(attempted).toEqual([]);
});

test('加载中的旧背景在Waiting后不回显', async ({ page }) => {
  await open(page);
  await hasBackdrop(page);
  await page.evaluate(() => { window.__wtFixture.setImageDelay(700); window.__wtFixture.setMedia('item-a', { BackdropImageTags: ['delayed-a'] }); });
  await refresh(page);
  await expect(page.locator('.wt-roomBackdrop')).toHaveAttribute('src', /delayed-a/);
  await page.evaluate(() => window.__wtFixture.setState({ State: 'Waiting', StatusReason: 'waiting_for_playback' }));
  await refresh(page);
  await noBackdrop(page);
  await page.waitForTimeout(850);
  await noBackdrop(page);
  await expect(page.locator('.wt-roomStateDescription')).toBeVisible();
  await expect(page.locator('.wt-roomStateDescription')).toContainText('等待');
  await page.screenshot({ path: path.join(artifacts, 'room-waiting-light.png'), fullPage: true });
});

test('旧图片事件不能覆盖新背景，离页与拒绝读取会清图', async ({ page }) => {
  await open(page);
  await hasBackdrop(page);
  await page.evaluate(() => { window.__wtFixture.setImageDelay(800); window.__wtFixture.setMedia('item-a', { BackdropImageTags: ['delayed-old'] }); });
  await refresh(page);
  await expect(page.locator('.wt-roomBackdrop')).toHaveAttribute('src', /delayed-old/);
  await page.evaluate(() => { window.__wtFixture.setImageDelay(0); window.__wtFixture.setMedia('item-a', { BackdropImageTags: ['new-current'] }); });
  await refresh(page);
  await hasBackdrop(page);
  await expect(page.locator('.wt-roomBackdrop')).toHaveAttribute('src', /new-current/);
  await page.waitForTimeout(950);
  await expect(page.locator('.wt-roomBackdrop')).toHaveAttribute('src', /new-current/);
  await hasBackdrop(page);
  await page.evaluate(() => window.__wtFixture.setMediaFailure(403));
  await refresh(page);
  await noBackdrop(page);
  await page.evaluate(() => window.__wtFixture.view.onPause());
  await expect(page.locator('.wt-roomBackdrop[src]')).toHaveCount(0);
});

test('诊断导出不含图片标识与URL', async ({ page }) => {
  await open(page);
  await page.evaluate(() => window.__wtFixture.setMedia('item-a', { BackdropImageTags: [], ParentBackdropItemId: 'private-parent-marker', ParentBackdropImageTags: ['private-image-marker'] }));
  await refresh(page);
  await hasBackdrop(page);
  await page.locator('[data-act="diagnostics"]').click();
  await expect(page.locator('.wt-diagnosticToolbar')).toBeVisible();
  const downloadPromise = page.waitForEvent('download');
  await page.locator('.wt-diagnosticToolbar button').click();
  const download = await downloadPromise;
  const text = fs.readFileSync(await download.path(), 'utf8');
  expect(text).not.toContain('private-parent-marker');
  expect(text).not.toContain('private-image-marker');
  expect(text).not.toContain('fixture-backdrop');
  expect(text).not.toContain('Backdrop');
});

for (const theme of ['light', 'dark']) {
  test(`三页新布局在${theme}主题下展开，窄屏单列并保留功能`, async ({ page }) => {
    await open(page, 'admin-member', 1440, theme);
    await selectView(page, 'Invite');
    await expect(page.locator('#wtAdminSection')).toBeVisible();
    await expect(page.locator('#wtInvitationSection')).toBeVisible();
    await expect(page.locator('#wtInvitationAcceptCode')).toBeVisible();
    await page.screenshot({ path: path.join(artifacts, `invite-${theme}.png`), fullPage: true });
    await selectView(page, 'Settings');
    await expect(page.locator('#wtConfigSection')).toBeVisible();
    await expect(page.locator('#wtGitHubTokenSection')).toBeVisible();
    await expect(page.locator('#wtAboutSection')).toBeVisible();
    await expect(page.locator('#wtGitHubToken')).toHaveValue('');
    await page.screenshot({ path: path.join(artifacts, `settings-${theme}.png`), fullPage: true });
    await page.setViewportSize({ width: 390, height: 1000 });
    for (const suffix of ['Rooms', 'Invite', 'Settings']) {
      await selectView(page, suffix);
      const widths = await page.evaluate(() => [document.documentElement.scrollWidth, document.documentElement.clientWidth]);
      expect(widths[0]).toBeLessThanOrEqual(widths[1] + 1);
      await page.screenshot({ path: path.join(artifacts, `${suffix}-mobile-${theme}.png`), fullPage: true });
    }
  });
}

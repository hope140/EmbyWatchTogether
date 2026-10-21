const { test, expect } = require('@playwright/test');
const fs = require('fs');

async function open(page, query = '') {
  await page.setViewportSize({ width: 1280, height: 1000 });
  await page.goto('/?scenario=active&theme=light' + query);
  await expect(page.locator('.wt-participant')).toHaveCount(2);
  await expect(page.locator('.wt-roomState')).not.toHaveText('正在读取状态');
}
async function avatarsLoaded(page) {
  for (const image of await page.locator('.wt-participantAvatarImage').all()) {
    await expect(image).toBeVisible();
    await expect.poll(() => image.evaluate(e => e.complete && e.naturalWidth > 0)).toBe(true);
  }
}
async function refresh(page) {
  const before = await page.evaluate(() => window.__wtFixture.apiCalls.filter(x => x.path.endsWith('/State')).length);
  await page.locator('#wtRefresh').click();
  await expect.poll(() => page.evaluate(() => window.__wtFixture.apiCalls.filter(x => x.path.endsWith('/State')).length)).toBeGreaterThan(before);
}

test('仅读取当前两名参与者头像，等待状态仍展示且不请求用户档案', async ({ page }) => {
  await open(page);
  await avatarsLoaded(page);
  await expect(page.locator('.wt-participantAvatarFallback').first()).toBeHidden();
  const calls = await page.evaluate(() => window.__wtFixture.avatarCalls);
  expect([...new Set(calls.map(x => x.userId))].sort()).toEqual(['user-a', 'user-b']);
  expect(calls.every(x => x.options.type === 'Primary' && x.options.maxWidth === 96)).toBe(true);
  expect(await page.evaluate(() => window.__wtFixture.apiCalls.filter(x => /^Users\/[^/]+$/.test(x.path)).length)).toBe(0);
  await page.evaluate(() => window.__wtFixture.setState({ State: 'Waiting', StatusReason: 'waiting_for_playback' }));
  await refresh(page);
  await expect(page.locator('.wt-roomState')).toHaveText('等待播放');
  await avatarsLoaded(page);
});

test('头像403或404回退首字且本代次重绘不会重复请求失败图片', async ({ page }) => {
  for (const status of [403, 404]) {
    const failedResponse = page.waitForResponse(r => r.url().includes('/fixture-avatar.svg?user=user-b'));
    await open(page, '&avatar_missing=user-b&avatar_status=' + status);
    expect((await failedResponse).status()).toBe(status);
    const missing = page.locator('.wt-participant').nth(1);
    await expect(missing.locator('.wt-participantAvatarImage')).not.toHaveAttribute('src', /.+/);
    await expect(missing.locator('.wt-participantAvatarFallback')).toBeVisible();
    const retried = [];
    const listener = request => { if (request.url().includes('/fixture-avatar.svg?user=user-b')) retried.push(request.url()); };
    page.on('request', listener);
    await page.evaluate(() => window.__wtFixture.setState({ State: 'Barrier', StatusReason: 'aligning' }));
    await refresh(page);
    await expect(page.locator('.wt-roomState')).toHaveText('正在对齐');
    await expect(missing.locator('.wt-participantAvatarFallback')).toBeVisible();
    expect(retried).toEqual([]);
    page.off('request', listener);
  }
});

test('头像SDK不可用或不安全URL不发请求并保留姓名首字', async ({ page }) => {
  await open(page);
  await avatarsLoaded(page);
  const attempted = [];
  page.on('request', r => { if (r.resourceType() === 'image' && !r.url().includes('fixture-backdrop')) attempted.push(r.url()); });
  for (const value of [null, '', 'https://foreign.invalid/avatar.jpg', 'data:image/png;base64,AA==', 'http://user:fixture-password@127.0.0.1:4179/fixture-avatar.svg']) {
    await page.evaluate(url => { const f = window.__wtFixture; f.view.onPause(); f.setAvatarUrl(url); f.view.onResume(); }, value);
    await expect(page.locator('.wt-participant')).toHaveCount(2);
    await expect(page.locator('.wt-roomState')).toHaveText('同步中');
    await expect(page.locator('.wt-participantAvatarImage[src]')).toHaveCount(0);
    await expect(page.locator('.wt-participantAvatarFallback').first()).toBeVisible();
  }
  await page.evaluate(() => { const f = window.__wtFixture; f.view.onPause(); delete window.ApiClient.getUserImageUrl; f.view.onResume(); });
  await expect(page.locator('.wt-roomState')).toHaveText('同步中');
  await expect(page.locator('.wt-participantAvatarImage[src]')).toHaveCount(0);
  expect(attempted).toEqual([]);
});

test('成员缺失受限摘要时不读取该头像', async ({ page }) => {
  await open(page);
  await avatarsLoaded(page);
  const before = await page.evaluate(() => window.__wtFixture.avatarCalls.length);
  await page.evaluate(() => window.__wtFixture.setState({ ParticipantUserIds: ['user-a', 'outside-user'], Participants: [{ Id: 'user-a', Name: 'A', Joined: true }] }));
  await refresh(page);
  await expect(page.locator('.wt-participant')).toHaveCount(2);
  const next = await page.evaluate(start => window.__wtFixture.avatarCalls.slice(start), before);
  expect(next.some(x => x.userId === 'outside-user')).toBe(false);
  await expect(page.locator('.wt-participant').nth(1).locator('.wt-participantAvatarImage')).not.toHaveAttribute('src', /.+/);
});

test('旧头像load和error不能改变恢复后或已移除的房间', async ({ page }) => {
  await open(page, '&avatar_delay=700');
  await expect(page.locator('.wt-participantAvatarImage').first()).toHaveAttribute('src', /fixture-avatar/);
  await page.evaluate(() => {
    const image = document.querySelector('.wt-participantAvatarImage');
    window.__staleAvatarLoad = image.onload; window.__staleAvatarError = image.onerror;
    const f = window.__wtFixture; f.view.onPause(); f.setAvatarDelay(0); f.view.onResume();
  });
  await avatarsLoaded(page);
  await page.evaluate(() => { window.__staleAvatarLoad(); window.__staleAvatarError(); });
  await avatarsLoaded(page);
  await page.evaluate(() => window.__wtFixture.setRooms([]));
  await page.locator('#wtRefresh').click();
  await expect(page.locator('.wt-roomCard')).toHaveCount(0);
  await page.waitForTimeout(800);
  await expect(page.locator('.wt-participantAvatarImage[src]')).toHaveCount(0);
});

test('账号或服务器变化使待加载头像失效', async ({ page }) => {
  for (const kind of ['account', 'server']) {
    await open(page, '&avatar_delay=600');
    await expect(page.locator('.wt-participantAvatarImage').first()).toHaveAttribute('src', /fixture-avatar/);
    await page.evaluate(value => {
      const f = window.__wtFixture;
      if (value === 'account') f.switchAccount('new-user'); else f.switchServer('http://other.invalid');
      f.refresh();
    }, kind);
    await expect(page.locator('.wt-roomCard')).toHaveCount(0);
    await page.waitForTimeout(700);
    await expect(page.locator('.wt-participantAvatarImage[src]')).toHaveCount(0);
  }
});

test('诊断导出不包含头像地址', async ({ page }) => {
  await open(page);
  await avatarsLoaded(page);
  await page.locator('[data-act="diagnostics"]').click();
  await expect(page.locator('.wt-diagnosticToolbar')).toBeVisible();
  const downloadPromise = page.waitForEvent('download');
  await page.locator('.wt-diagnosticToolbar button').click();
  const content = fs.readFileSync(await (await downloadPromise).path(), 'utf8');
  expect(content).not.toContain('fixture-avatar');
  expect(content).not.toContain('Avatar');
});

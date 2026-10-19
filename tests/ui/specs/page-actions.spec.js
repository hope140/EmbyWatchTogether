const { test, expect } = require('@playwright/test');

const actions = [
  { name: '创建邀请码', button: '#wtCreateInvitation', path: 'WatchTogether/Invitations', input: '#wtInvitationName' },
  { name: '接受邀请', button: '#wtAcceptInvitation', path: 'WatchTogether/Invitations/FRESH-CODE/Accept', input: '#wtInvitationAcceptCode' },
  { name: '管理员创建房间', button: '#wtCreate', path: 'WatchTogether/Rooms', input: '#wtRoomName' }
];

async function navigate(page, route) {
  await page.locator('.tabs-viewmenubar a[href*="WatchTogether' + route + 'V12"]').click();
  await expect(page.locator('#wtRooms')).toHaveAttribute('aria-busy', 'false');
}

async function fillAction(page, action) {
  await page.locator(action.input).fill('FRESH-CODE');
  if (action.button === '#wtCreate') {
    await page.locator('#wtUserA').selectOption('user-a');
    await page.locator('#wtUserB').selectOption('user-b');
    await page.locator('#wtPrimary').selectOption('user-a');
  }
}

for (const action of actions) {
  test(action.name + '在请求期间离页再返回可重试，旧响应不能解锁新请求', async ({ page }) => {
    await page.goto('/?scenario=admin-member&theme=light');
    await expect(page.locator('#wtRooms')).toHaveAttribute('aria-busy', 'false');
    await page.evaluate(() => window.__wtFixture.setRooms([]));
    await navigate(page, 'Invite');
    await page.evaluate(path => {
      const fixture = window.__wtFixture;
      const original = window.ApiClient.fetch;
      fixture.pendingPosts = [];
      window.ApiClient.fetch = options => {
        if (options.type === 'POST' && options.url === path) {
          return new Promise(resolve => fixture.pendingPosts.push(resolve));
        }
        return original(options);
      };
    }, action.path);
    await fillAction(page, action);
    const button = page.locator(action.button);
    const label = await button.textContent();
    await button.click();
    await expect(button).toHaveAttribute('aria-busy', 'true');
    await expect.poll(() => page.evaluate(() => window.__wtFixture.pendingPosts.length)).toBe(1);

    await navigate(page, 'Rooms');
    await navigate(page, 'Invite');
    await expect(button).toHaveText(label);
    await expect(button).not.toHaveAttribute('aria-busy', 'true');
    await fillAction(page, action);
    await expect(button).toBeEnabled();
    await button.click();
    await expect.poll(() => page.evaluate(() => window.__wtFixture.pendingPosts.length)).toBe(2);
    await page.evaluate(async () => {
      window.__wtFixture.pendingPosts[0]({ Status: 'created', Created: true, Code: 'OLD-CODE', RoomId: 'old-room' });
      await new Promise(resolve => setTimeout(resolve, 50));
    });
    await expect(button).toBeDisabled();
    await expect(button).toHaveAttribute('aria-busy', 'true');
    await expect(page.locator(action.input)).toHaveValue('FRESH-CODE');
    await page.evaluate(buttonId => {
      window.__wtFixture.pendingPosts[1]({
        Status: buttonId === '#wtAcceptInvitation' ? 'accepted' : 'created',
        Accepted: true, Created: true, Code: 'NEW-CODE', RoomId: 'new-room',
        ExpiresAtUtc: new Date(Date.now() + 900000).toISOString()
      });
    }, action.button);
    await expect(button).not.toHaveAttribute('aria-busy', 'true');
    await expect(button).toHaveText(label);
    if (action.button === '#wtCreateInvitation') {
      await expect(page.locator('#wtInvitationCode')).toHaveText('NEW-CODE');
    }
    expect(await page.evaluate(() => window.__wtFixture.pendingPosts.length)).toBe(2);
  });
}

test('旧诊断面板迟到的展开事件不能重新读取或展开新页面诊断', async ({ page }) => {
  await page.goto('/?scenario=active&theme=light');
  await expect(page.locator('.wt-roomCard')).toHaveCount(1);
  await page.evaluate(() => {
    window.__wtFixture.oldDetails = document.querySelector('.wt-diagnostics');
  });
  await navigate(page, 'Invite');
  await navigate(page, 'Rooms');
  await expect(page.locator('.wt-diagnostics')).toBeHidden();
  const before = await page.evaluate(() => window.__wtFixture.apiCalls.filter(x => x.path.endsWith('/Diagnostics')).length);
  await page.evaluate(async () => {
    const oldDetails = window.__wtFixture.oldDetails;
    oldDetails.open = true;
    oldDetails.dispatchEvent(new Event('toggle'));
    await new Promise(resolve => setTimeout(resolve, 50));
  });
  expect(await page.evaluate(() => window.__wtFixture.apiCalls.filter(x => x.path.endsWith('/Diagnostics')).length)).toBe(before);
  expect(await page.evaluate(() => window.__wtFixture.view.view._wtDiagnosticOpen['room-1'] === true)).toBe(false);
  await expect(page.locator('.wt-diagnostics')).toBeHidden();
  await page.locator('button[data-act="diagnostics"]').click();
  await expect(page.locator('.wt-diagnostics')).toBeVisible();
  await expect.poll(() => page.evaluate(() => window.__wtFixture.apiCalls.filter(x => x.path.endsWith('/Diagnostics')).length)).toBe(before + 1);
});

const { test, expect } = require('@playwright/test');

async function open(page, query = '') {
  await page.goto('/?scenario=admin-member&' + query);
  await expect(page.locator('[data-act=leave]')).toBeVisible();
}
async function mutations(page) {
  return page.evaluate(() => window.__wtFixture.apiCalls.filter(x => x.method !== 'GET'));
}
const dialog = page => page.getByRole('alertdialog');

for (const mode of ['function', 'default']) {
  test(`宿主 ${mode} 导出确认暂离仅请求一次，无原生弹窗`, async ({ page }) => {
    await open(page, 'confirm_mode=' + mode);
    await page.locator('[data-act=leave]').click();
    await expect(dialog(page)).toBeVisible();
    expect(await mutations(page)).toEqual([]);
    expect(await page.evaluate(() => window.__wtFixture.confirmations[0].primary)).toBe('cancel');
    await page.locator('[data-confirm=ok]').click();
    await expect.poll(() => mutations(page)).toHaveLength(1);
    expect((await mutations(page))[0].path).toMatch(/\/Leave$/);
    await expect(page.locator('[data-act=leave]')).toBeEnabled();
  });
}

for (const action of ['leave', 'delete', 'resync']) {
  test(`${action} 取消无请求，确认才发送`, async ({ page }) => {
    await open(page);
    await page.locator('[data-act=' + action + ']').click();
    await page.locator('[data-confirm=cancel]').click();
    expect(await mutations(page)).toEqual([]);
    await page.locator('[data-act=' + action + ']').click();
    await page.locator('[data-confirm=ok]').click();
    await expect.poll(() => mutations(page)).toHaveLength(1);
    const call = (await mutations(page))[0];
    if (action === 'delete') {
      expect(call.method).toBe('DELETE');
      expect(await page.evaluate(() => window.__wtFixture.confirmations[1].text)).toContain('结束房间会解除双方成员关系，只删除同步关系，不删除媒体。');
      await expect.poll(() => page.evaluate(() => window.__wtFixture.toasts)).toEqual(['房间已结束']);
    }
    if (action === 'resync') expect(call.body.Action).toBe('resync');
  });
}

test('加载等待与重复点击只显示一个确认', async ({ page }) => {
  await open(page, 'confirm_mode=slow');
  await page.evaluate(() => {
    const button = document.querySelector('[data-act=leave]');
    button.click(); button.dispatchEvent(new MouseEvent('click'));
  });
  await expect(dialog(page)).toHaveCount(1);
  expect(await page.evaluate(() => window.__wtFixture.confirmations.length)).toBe(1);
  await page.locator('[data-confirm=ok]').click();
  await expect.poll(() => mutations(page)).toHaveLength(1);
});

for (const mode of ['missing', 'throw']) {
  test(`确认组件 ${mode} 明确报错且不发送`, async ({ page }) => {
    await open(page, 'confirm_mode=' + mode);
    await page.evaluate(() => clearInterval(window.__wtFixture.view.view._wtTimer));
    await page.locator('[data-act=leave]').click();
    await expect(page.locator('.wt-page')).toContainText('无法打开确认窗口', { timeout: 1000 });
    expect(await mutations(page)).toEqual([]);
    await expect(page.locator('[data-act=leave]')).toBeEnabled();
  });
}

for (const change of ['account', 'server', 'pause', 'resume', 'room-deleted', 'membership', 'permission']) {
  test(`确认等待期间 ${change} 变化不执行旧操作`, async ({ page }) => {
    await open(page);
    await page.locator('[data-act=delete]').click();
    await expect(dialog(page)).toBeVisible();
    await page.evaluate(change => {
      const fixture = window.__wtFixture;
      const view = fixture.view;
      if (change === 'account') fixture.switchAccount('another-user');
      if (change === 'server') fixture.switchServer('https://changed.invalid');
      if (change === 'pause') view.onPause();
      if (change === 'resume') { view.onPause(); view.onResume(); }
      if (change === 'room-deleted') view.view._wtRooms = [];
      if (change === 'membership') view.view._wtRooms[0].CurrentUserJoined = false;
      if (change === 'permission') view.view._wtIsAdmin = false;
      fixture.answerConfirmation(true);
    }, change);
    await expect(dialog(page)).toHaveCount(0);
    // Flush the confirmation's promise continuation before checking mutations.
    await page.evaluate(() => new Promise(resolve => setTimeout(resolve, 50)));
    expect(await mutations(page)).toEqual([]);
  });
}

test('加载组件期间页面离开，不迟到打开确认', async ({ page }) => {
  await open(page, 'confirm_mode=slow');
  await page.locator('[data-act=leave]').click();
  await page.evaluate(() => window.__wtFixture.view.onPause());
  await page.waitForTimeout(450);
  await expect(dialog(page)).toHaveCount(0);
  expect(await mutations(page)).toEqual([]);
});

test('普通播放控制和诊断不新增确认', async ({ page }) => {
  await open(page);
  await page.locator('[data-act=pause]').click();
  await expect.poll(() => mutations(page)).toHaveLength(1);
  await page.locator('[data-act=diagnostics]').click();
  expect(await page.evaluate(() => window.__wtFixture.confirmations.length)).toBe(0);
});

test('Token 确认等待期间切换账号，不清除新账号 Token', async ({ page }) => {
  await open(page);
  await page.locator('#wtTabSettings').click();
  await expect(page.locator('#wtClearGitHubToken')).toBeEnabled();
  await page.locator('#wtClearGitHubToken').click();
  await expect(dialog(page)).toBeVisible();
  await page.evaluate(() => {
    window.__wtFixture.switchAccount('another-user');
    window.__wtFixture.answerConfirmation(true);
  });
  await expect(page.locator('#wtStatus')).toContainText('账号或服务器已变化');
  expect(await mutations(page)).toEqual([]);
});

test('房间成员顺序变化不是成员变化，不误拒绝确认', async ({ page }) => {
  await open(page);
  await page.locator('[data-act=leave]').click();
  await expect(dialog(page)).toBeVisible();
  await page.evaluate(() => {
    const room = window.__wtFixture.view.view._wtRooms[0];
    room.ParticipantUserIds.reverse(); room.JoinedParticipantUserIds.reverse();
    window.__wtFixture.answerConfirmation(true);
  });
  await expect.poll(() => mutations(page)).toHaveLength(1);
});

for (const change of ['deleted', 'membership']) {
  test(`关闭轮询后 ${change} 过期确认立即显示反馈`, async ({ page }) => {
    await open(page);
    await page.evaluate(() => clearInterval(window.__wtFixture.view.view._wtTimer));
    await page.locator('[data-act=delete]').click();
    await expect(dialog(page)).toBeVisible();
    await page.evaluate(change => {
      const fixture = window.__wtFixture;
      if (change === 'deleted') fixture.view.view._wtRooms = [];
      else fixture.view.view._wtRooms[0].CurrentUserJoined = false;
      fixture.answerConfirmation(true);
    }, change);
    await expect(page.locator('.wt-page')).toContainText('房间或权限已变化', { timeout: 1000 });
    expect(await mutations(page)).toEqual([]);
  });
}

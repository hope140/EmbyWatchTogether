const { test, expect } = require('@playwright/test');

async function settled(page) {
  await expect.poll(() => page.evaluate(() => window.__wtFixture.view.view._wtRoomsLoading)).toBe(false);
}

test('首次列表失败就近提示，自动重试成功不显示数量', async ({ page }) => {
  await page.goto('/?scenario=admin-member&rooms_failure=true');
  await expect(page.locator('#wtRoomsStatus')).toHaveText('房间列表加载失败，将自动重试。');
  await expect(page.locator('#wtRoomsSection #wtRoomsStatus')).toBeVisible();
  await expect(page.locator('#wtRooms')).toHaveAttribute('aria-busy', 'false');
  await expect(page.locator('#wtRefresh')).toHaveCount(0);
  await page.screenshot({ path: test.info().outputPath('first-failure.png'), fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: test.info().outputPath('first-failure-mobile.png'), fullPage: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.evaluate(() => window.__wtFixture.setRoomsFailure(false));
  await expect(page.locator('.wt-roomCard')).toHaveCount(1);
  await expect(page.locator('#wtRoomsStatus')).toBeHidden();
  await expect(page.locator('#wtStatus')).toBeEmpty();
  expect(await page.locator('#wtRoomsStatus').evaluate(el => el.getBoundingClientRect().height)).toBe(0);
  await page.screenshot({ path: test.info().outputPath('recovered-mobile.png'), fullPage: true });
});

test('旧列表失败保留卡片和失效保护，重复轮询不重复播报，恢复只清列表提示', async ({ page }) => {
  await page.goto('/?scenario=admin-member');
  await expect(page.locator('.wt-roomCard')).toHaveCount(1);
  await settled(page);
  await page.evaluate(() => window.__wtFixture.setRoomsFailure(true));
  await expect(page.locator('#wtRoomsStatus')).toContainText('当前列表可能不是最新');
  await expect(page.locator('.wt-roomCard')).toHaveCount(1);
  expect(await page.evaluate(() => window.__wtFixture.view.view._wtRooms)).toBeNull();
  await expect(page.locator('.wt-roomMedia')).toBeHidden();
  await page.evaluate(() => {
    window.__roomStatusChanges = 0;
    new MutationObserver(() => window.__roomStatusChanges++).observe(document.querySelector('#wtRoomsStatus'), { childList: true, characterData: true, subtree: true });
    window.__wtFixture.view.view._wtRefreshVisible();
  });
  await settled(page);
  // Repeated failed polls must leave the unchanged error message alone.
  await page.waitForTimeout(5200);
  expect(await page.evaluate(() => window.__roomStatusChanges)).toBe(0);
  await page.screenshot({ path: test.info().outputPath('stale-list.png'), fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: test.info().outputPath('stale-list-mobile.png'), fullPage: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.evaluate(() => { window.__wtFixture.setRoomsFailure(false); window.__wtFixture.view.view._wtRefreshVisible(); });
  await expect(page.locator('#wtRoomsStatus')).toBeHidden();
  await expect(page.locator('#wtStatus')).toBeEmpty();
});

for (const mode of ['failure', 'partial']) {
  test(`列表失败、自动重试及恢复不清除操作 ${mode} 消息`, async ({ page }) => {
    await page.goto('/?scenario=admin-member');
    await expect(page.locator('.wt-roomCard')).toHaveCount(1);
    await page.evaluate(mode => {
      const original = ApiClient.fetch;
      ApiClient.fetch = options => options.url.endsWith('/Action')
        ? mode === 'failure' ? Promise.reject({ status: 503 }) : Promise.resolve({ Error: 'partial' })
        : original(options);
      window.__wtFixture.setRoomsFailure(true);
    }, mode);
    await page.locator('[data-act=pause]').click();
    const feedback = page.locator('.wt-roomFeedback');
    await expect(feedback).toContainText(mode === 'failure' ? '失败' : '部分客户端未完成');
    const text = await feedback.textContent();
      await expect(page.locator('#wtRoomsStatus')).toContainText('当前列表可能不是最新');
    await expect(feedback).toHaveText(text);
    await page.evaluate(() => window.__wtFixture.setRoomsFailure(false));
      await expect(page.locator('#wtRoomsStatus')).toBeHidden();
    await expect(feedback).toHaveText(text);
  });
}

test('用户权限数据加载失败时停止房间读取，刷新不覆盖错误，离页清理', async ({ page }) => {
  await page.goto('/?scenario=users-fail&rooms_failure=true');
  await expect(page.locator('#wtStatus')).toContainText('用户列表加载失败');
  const text = await page.locator('#wtStatus').textContent();
  await expect(page.locator('#wtRoomsStatus')).toBeHidden();
  await page.waitForTimeout(1150);
  await expect(page.locator('#wtStatus')).toHaveText(text);
  expect(await page.evaluate(() => window.__wtFixture.apiCalls.filter(x => x.path === 'WatchTogether/Rooms'))).toEqual([]);
  await page.evaluate(() => window.__wtFixture.view.onPause());
  await expect(page.locator('#wtRoomsStatus')).toBeHidden();
});

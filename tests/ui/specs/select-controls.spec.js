const { test, expect } = require('@playwright/test');

test('四个下拉框沿用 Madby 的宿主默认单选模式', async ({ page }) => {
  await page.goto('/?scenario=admin-member');
  const selects = page.locator('select[is="emby-select"]');
  await expect(selects).toHaveCount(4);
  expect(await selects.evaluateAll(nodes => nodes.map(node => ({ menu: node.getAttribute('data-menu'), multiple: node.multiple })))).toEqual(
    Array.from({ length: 4 }, () => ({ menu: null, multiple: false }))
  );
});

for (const host of ['legacy', 'modern']) {
  for (const width of [390, 1280]) {
    test(`${host} 宿主 ${width}px 的箭头跟随控件中线，标签换行不改变定位`, async ({ page }) => {
      await page.setViewportSize({ width, height: 1000 });
      await page.goto(`/?scenario=admin-member&select_host=${host}`);
      await expect(page.locator('#wtRooms')).toHaveAttribute('aria-busy', 'false');
      for (const route of ['Invite', 'Settings']) {
        await page.locator(`.tabs-viewmenubar a[href*="WatchTogether${route}V17"]`).click();
        // Rooms can retain aria-busy=false while the target route reloads permissions.
        const panel = page.locator(route === 'Invite' ? '#wtAdminSection' : '#wtSettingsSection');
        await expect(panel).toBeVisible();
        const visibleSelects = panel.locator('.selectContainer select:visible');
        await expect(visibleSelects).toHaveCount(route === 'Invite' ? 3 : 1);
        await page.locator('.selectLabelText').evaluateAll(nodes => nodes.forEach(node => {
          node.textContent = '用于验证标签换行后箭头仍然与下拉框垂直居中的较长说明 '.repeat(3);
        }));
        const positions = await visibleSelects.evaluateAll(selects => selects.map(select => {
          const arrow = select.parentElement.querySelector('.selectArrow');
          const box = select.getBoundingClientRect();
          const icon = arrow.getBoundingClientRect();
          return { id: select.id, vertical: Math.abs(icon.y + icon.height / 2 - box.y - box.height / 2), right: box.right - icon.right };
        }));
        expect(positions).toHaveLength(route === 'Invite' ? 3 : 1);
        for (const position of positions) {
          expect(position.vertical, position.id).toBeLessThan(1);
          expect(position.right, position.id).toBeGreaterThanOrEqual(0);
          expect(position.right, position.id).toBeLessThan(12);
        }
      }
    });
  }
}

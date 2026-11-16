const { test, expect } = require('@playwright/test');

for (const width of [320, 390, 1440]) {
  for (const theme of ['light', 'dark']) {
    test(`page headings match at ${width}px ${theme}, refresh stays aligned`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await page.goto(`/?scenario=admin-member&theme=${theme}`);
      await expect(page.locator('#wtRoomsHeading')).toBeVisible();
      const style = selector => page.locator(selector).evaluate(el => {
        const s = getComputedStyle(el);
        return { size: s.fontSize, weight: s.fontWeight, color: s.color };
      });
      const rooms = await style('#wtRoomsHeading');
      const title = await page.locator('#wtRoomsHeading').boundingBox();
      const refresh = await page.locator('#wtRefresh').boundingBox();
      expect(title.x + title.width + 8).toBeLessThanOrEqual(refresh.x);
      expect(Math.abs(title.y + title.height / 2 - refresh.y - refresh.height / 2)).toBeLessThan(1);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await page.screenshot({ path: test.info().outputPath(`rooms-${width}-${theme}.png`), fullPage: true });
      await page.locator('#wtTabInvite').click();
      await expect(page.locator('#wtInviteHeading')).toBeVisible();
      expect(await style('#wtInviteHeading')).toEqual(rooms);
      await page.locator('#wtTabSettings').click();
      await expect(page.locator('#wtSettingsSection')).toBeVisible();
      expect(await style('#wtSettingsSection > .wt-sectionHeading h2')).toEqual(rooms);
    });
  }
}

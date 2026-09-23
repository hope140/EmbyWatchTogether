const { test, expect } = require('@playwright/test');
const fs = require('fs');
const path = require('path');

const artifactDir = path.join(__dirname, '..', 'test-results', 'ui-vnext');
fs.mkdirSync(artifactDir, { recursive: true });

async function open(page, scenario = 'active', theme = 'light', width = 1100, height = 900, host = 'present') {
  await page.setViewportSize({ width, height });
  await page.goto(`/?scenario=${scenario}&theme=${theme}&host=${host}`);
  await expect(page.locator('.wt-page')).toBeVisible();
  await expect.poll(() => page.evaluate(() => window.__wtFixture && window.__wtFixture.apiCalls.some(x => x.path === 'WatchTogether/Rooms'))).toBe(true);
  await expect(page.locator('#wtRooms')).toHaveAttribute('aria-busy', 'false');
  const error = await page.evaluate(() => window.__wtFixture && window.__wtFixture.error);
  expect(error || '').toBe('');
}

async function calls(page) {
  return page.evaluate(() => window.__wtFixture.apiCalls.slice());
}

function relativeLuminance(rgb) {
  const match = rgb.match(/rgba?\(\s*([\d.]+)[, ]+\s*([\d.]+)[, ]+\s*([\d.]+)/i);
  if (!match) throw new Error(`Unexpected computed color: ${rgb}`);
  return match.slice(1, 4).map(Number).map(value => value / 255).map(value => value <= 0.03928 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4).reduce((sum, value, index) => sum + value * [0.2126, 0.7152, 0.0722][index], 0);
}

function contrastRatio(foreground, background) {
  const light = Math.max(relativeLuminance(foreground), relativeLuminance(background));
  const dark = Math.min(relativeLuminance(foreground), relativeLuminance(background));
  return (light + 0.05) / (dark + 0.05);
}

test('Emby 宿主顶栏承载胶囊分页并保持原页面槽位为空', async ({ page }) => {
  await open(page, 'active', 'light', 1440, 1000);
  const host = page.locator('.skinHeader');
  const middle = page.locator('.skinHeader .headerMiddle');
  await expect(host).toHaveClass(/headerTop-withSectionTabs/);
  await expect(middle).toHaveClass(/headerMiddle-withSectionTabs/);
  await expect(middle.locator('#wtPluginNav')).toHaveCount(1);
  await expect(page.locator('#wtPluginNavSlot #wtPluginNav')).toHaveCount(0);
  await expect(page.locator('#wtPluginNav')).toHaveClass(/wt-pluginNav--host/);
  await expect(page.locator('#wtTabMine')).toHaveAttribute('aria-current', 'page');
  const navStyle = await page.locator('#wtPluginNav').evaluate(el => {
    const style = getComputedStyle(el);
    const active = getComputedStyle(el.querySelector('[aria-current="page"]'));
    return { background: style.backgroundColor, activeBackground: active.backgroundColor, radius: style.borderRadius };
  });
  expect(navStyle.background).not.toBe('rgba(0, 0, 0, 0)');
  expect(navStyle.activeBackground).not.toBe('rgba(0, 0, 0, 0)');
  expect(navStyle.radius).toBe('999px');
});

test('浅色、深色和仅 HSL 顶栏当前项均满足 4.5:1 对比度，并保留管理员三项分页截图', async ({ page }) => {
  for (const theme of ['light', 'dark', 'hsl']) {
    await open(page, 'admin-member', theme, 1440, 1000);
    await expect(page.locator('#wtTabMine')).toBeVisible();
    await expect(page.locator('#wtTabManage')).toBeVisible();
    await expect(page.locator('#wtTabSettings')).toBeVisible();
    const colors = await page.locator('#wtTabMine').evaluate(el => {
      const style = getComputedStyle(el);
      return { background: style.backgroundColor, color: style.color };
    });
    expect(contrastRatio(colors.color, colors.background)).toBeGreaterThanOrEqual(4.5);
    if (theme === 'light') {
      await page.screenshot({ path: path.join(artifactDir, 'desktop-light-admin-member.png'), fullPage: true });
    }
  }
});

test('管理员可在顶栏切换 mine、manage、settings，普通用户隐藏管理入口', async ({ page }) => {
  await open(page, 'admin-manage', 'dark');
  for (const id of ['wtTabManage', 'wtTabSettings', 'wtTabMine']) {
    await page.locator(`#${id}`).click();
    await expect(page.locator(`#${id}`)).toHaveAttribute('aria-current', 'page');
  }
  await open(page, 'active', 'light');
  await expect(page.locator('#wtTabManage')).toBeHidden();
  await expect(page.locator('#wtTabSettings')).toBeHidden();
});

test('反复进入离开页面只保留一份分页并清理宿主状态，缺少宿主时回退到页面槽位', async ({ page }) => {
  await open(page, 'active', 'light', 1024, 900);
  for (let index = 0; index < 10; index += 1) {
    await page.evaluate(() => window.__wtFixture.view.onPause());
    await page.evaluate(() => window.__wtFixture.view.onResume());
  }
  await expect.poll(() => page.locator('#wtPluginNav').count()).toBe(1);
  await expect(page.locator('.headerMiddle #wtPluginNav')).toHaveCount(1);
  await page.evaluate(() => window.__wtFixture.view.onPause());
  await expect(page.locator('#wtPluginNavSlot #wtPluginNav')).toHaveCount(1);
  await expect(page.locator('.headerMiddle #wtPluginNav')).toHaveCount(0);
  await expect(page.locator('.skinHeader')).not.toHaveClass(/headerTop-withSectionTabs/);
  await expect(page.locator('.headerMiddle')).not.toHaveClass(/headerMiddle-withSectionTabs/);

  await open(page, 'active', 'light', 1024, 900, 'missing');
  await expect(page.locator('#wtPluginNavSlot #wtPluginNav')).toHaveCount(1);
  await expect(page.locator('#wtPluginNav')).not.toHaveClass(/wt-pluginNav--host/);
});

test('房间桌面端媒体在左、成员和按钮在右，手机端按媒体成员按钮排列', async ({ page }) => {
  await open(page, 'active', 'light', 1440, 1000);
  const desktop = await page.locator('.wt-roomCard').evaluate(card => {
    const media = card.querySelector('.wt-roomMediaPlaceholder').getBoundingClientRect();
    const side = card.querySelector('.wt-roomSide').getBoundingClientRect();
    const participants = card.querySelector('.wt-roomParticipants').getBoundingClientRect();
    const actions = card.querySelector('.wt-roomActions').getBoundingClientRect();
    const rows = Array.from(card.querySelectorAll('.wt-roomParticipant')).map(row => ({
      top: row.getBoundingClientRect().top,
      nameOverflow: row.querySelector('.wt-roomParticipantName').scrollWidth > row.querySelector('.wt-roomParticipantName').clientWidth,
      statusOverflow: row.querySelector('.wt-roomParticipantStatus').scrollWidth > row.querySelector('.wt-roomParticipantStatus').clientWidth
    }));
    return { media, side, participants, actions, rows };
  });
  expect(desktop.media.left).toBeLessThan(desktop.side.left);
  expect(desktop.participants.left).toBeGreaterThanOrEqual(desktop.side.left);
  expect(desktop.actions.left).toBeGreaterThanOrEqual(desktop.side.left);
  expect(desktop.rows).toHaveLength(2);
  expect(desktop.rows[1].top).toBeGreaterThan(desktop.rows[0].top);
  expect(desktop.rows.every(row => !row.nameOverflow && !row.statusOverflow)).toBe(true);

  await open(page, 'active', 'light', 1024, 1000);
  const desktopNarrowRows = await page.locator('.wt-roomCard').evaluate(card => Array.from(card.querySelectorAll('.wt-roomParticipant')).map(row => ({
    nameOverflow: row.querySelector('.wt-roomParticipantName').scrollWidth > row.querySelector('.wt-roomParticipantName').clientWidth,
    statusOverflow: row.querySelector('.wt-roomParticipantStatus').scrollWidth > row.querySelector('.wt-roomParticipantStatus').clientWidth
  })));
  expect(desktopNarrowRows.every(row => !row.nameOverflow && !row.statusOverflow)).toBe(true);

  await open(page, 'active', 'light', 390, 1000);
  const mobile = await page.locator('.wt-roomCard').evaluate(card => {
    const rect = selector => card.querySelector(selector).getBoundingClientRect();
    return { media: rect('.wt-roomMediaPlaceholder'), participants: rect('.wt-roomParticipants'), actions: rect('.wt-roomActions') };
  });
  expect(mobile.media.top).toBeLessThan(mobile.participants.top);
  expect(mobile.participants.top).toBeLessThan(mobile.actions.top);
});

test('管理员成员保留个人主动作，管理动作收纳在更多；管理员非成员没有个人按钮', async ({ page }) => {
  await open(page, 'admin-member', 'light', 1024, 900);
  const participantResync = page.locator('.wt-roomCard .wt-action[data-act="participantResync"]');
  const leave = page.locator('.wt-roomCard .wt-action[data-act="leave"]');
  await expect(participantResync).toBeVisible();
  await expect(participantResync).toHaveText('请求重新对齐');
  await expect(participantResync).not.toHaveClass(/wt-action--danger/);
  await expect(leave).toBeVisible();
  await expect(leave).not.toHaveClass(/wt-action--danger/);
  await participantResync.click();
  await expect(page.locator('#wtConfirmTitle')).toHaveText('请求重新对齐');
  await page.locator('#wtConfirmCancel').click();
  await page.locator('.wt-roomCard .wt-action[data-act="more"]').click();
  await expect(page.locator('.wt-roomCard .wt-moreMenu')).toBeVisible();
  await expect(page.locator('.wt-roomCard .wt-moreMenu [data-act="delete"]')).toBeVisible();
  await expect(page.locator('.wt-roomCard .wt-moreMenu [data-act="delete"]')).toHaveClass(/wt-action--danger/);

  await open(page, 'admin-manage', 'light', 1024, 900);
  await expect(page.locator('.wt-roomCard .wt-action[data-act="participantResync"]')).toHaveCount(0);
  await expect(page.locator('.wt-roomCard .wt-action[data-act="leave"]')).toHaveCount(0);
});

test('已有房间收起帮助，无房间保留邀请和帮助说明', async ({ page }) => {
  await open(page, 'active', 'light');
  await expect(page.locator('.wt-help')).toBeHidden();
  await open(page, 'empty', 'light', 390, 900);
  await expect(page.locator('#wtInvitationSection')).toBeVisible();
  await expect(page.locator('.wt-help')).toBeVisible();
});

test('普通成员当前房间显示真实结构与媒体占位', async ({ page }) => {
  await open(page);
  await expect(page.locator('#wtTabMine')).toHaveAttribute('aria-current', 'page');
  await expect(page.locator('#wtTabManage')).toBeHidden();
  await expect(page.locator('.wt-roomCard')).toContainText('周末一起看');
  await expect(page.locator('.wt-roomState')).toContainText('同步中');
  await expect(page.locator('.wt-roomGroup[data-wt-room-group="current"] .wt-roomGroupHeading')).toHaveCount(0);
  await expect(page.locator('.wt-roomMediaPlaceholder')).toContainText('尚未检测到播放');
  await expect(page.locator('.wt-roomMediaPlaceholder')).toContainText('媒体信息待权限验证');
  await expect(page.locator('.wt-roomMediaPlaceholder button')).toHaveCount(0);
  await expect(page.locator('#wtInvitationSection')).toBeHidden();
  await page.screenshot({ path: path.join(artifactDir, 'desktop-light-active-room.png'), fullPage: true });
});

test('管理员可查看非成员管理列表，并可在 mine/manage/settings 导航', async ({ page }) => {
  await open(page, 'admin-manage', 'dark');
  await expect(page.locator('#wtTabManage')).toBeVisible();
  await page.locator('#wtTabManage').click();
  await expect(page.locator('#wtTabManage')).toHaveAttribute('aria-current', 'page');
  await expect(page.locator('#wtRooms')).toContainText('管理员长列表房间');
  await page.locator('#wtTabSettings').click();
  await expect(page.locator('#wtSettingsSection')).toBeVisible();
  await expect(page.locator('#wtSaveConfig')).toBeDisabled();
  await page.locator('#wtTabMine').click();
  await expect(page.locator('#wtTabMine')).toHaveAttribute('aria-current', 'page');
  await page.screenshot({ path: path.join(artifactDir, 'desktop-dark-same-structure.png'), fullPage: true });
});

test('无房间、邀请创建等待与接受失败都有可见状态', async ({ page }) => {
  await open(page, 'empty', 'light', 390, 900);
  await expect(page.locator('#wtRooms')).toContainText('暂无参与的房间');
  await page.locator('#wtInvitationName').fill('周末邀请');
  await page.locator('#wtCreateInvitation').click();
  await expect(page.locator('#wtInvitationCodePanel')).toBeVisible();
  await expect(page.locator('#wtInvitationCode')).toHaveText('ABCD12EFghI3J4KLMNopQR');
  await page.screenshot({ path: path.join(artifactDir, 'mobile-invitation-390.png'), fullPage: true });

  await open(page, 'accept-fail', 'light', 390, 900);
  await page.locator('#wtInvitationAcceptCode').fill('BAD-CODE');
  await page.locator('#wtAcceptInvitation').click();
  await expect(page.locator('#wtInvitationStatus')).toContainText('邀请码无效或已过期');
});

test('接受邀请网络失败只读回房间且不重发 POST', async ({ page }) => {
  await open(page, 'accept-network');
  await page.locator('#wtInvitationAcceptCode').fill('NETWORK-CODE');
  await page.locator('#wtAcceptInvitation').click();
  await expect(page.locator('#wtInvitationStatus')).toContainText('房间状态尚未确认');
  const requestLog = await calls(page);
  expect(requestLog.filter(x => x.method === 'POST' && x.path.includes('/Accept'))).toHaveLength(1);
  expect(requestLog.filter(x => x.method === 'GET' && x.path === 'WatchTogether/Rooms').length).toBeGreaterThanOrEqual(2);
});

test('确认取消不发送请求，Tab 在对话框内循环并返回触发按钮', async ({ page }) => {
  await open(page);
  const leave = page.locator('.wt-roomCard button[data-act="leave"]');
  await leave.click();
  await expect(page.locator('#wtConfirmOverlay')).toBeVisible();
  const before = await calls(page);
  await page.locator('#wtConfirmCancel').click();
  await expect(page.locator('#wtConfirmOverlay')).toBeHidden();
  expect((await calls(page)).length).toBe(before.length);
  await leave.click();
  await page.locator('#wtConfirmCancel').focus();
  await page.keyboard.press('Tab');
  await expect(page.locator('#wtConfirmSubmit')).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(page.locator('#wtConfirmCancel')).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(leave).toBeFocused();
});

test('5 秒轮询保持邀请输入', async ({ page }) => {
  await open(page, 'empty');
  const accept = page.locator('#wtInvitationAcceptCode');
  await accept.fill('keep-me');
  await page.waitForTimeout(5200);
  await expect(accept).toHaveValue('keep-me');
});

test('5 秒轮询保持更多菜单和诊断展开状态', async ({ page }) => {
  await open(page);
  const more = page.locator('.wt-roomCard button[data-act="more"]');
  await more.click();
  await expect(page.locator('.wt-moreMenu')).toBeVisible();
  await page.waitForTimeout(5200);
  await expect(page.locator('.wt-moreMenu')).toBeVisible();
  await page.locator('.wt-moreMenu button[data-act="diagnostics"]').click();
  await expect(page.locator('.wt-diagnosticToolbar')).toBeVisible();
  await page.waitForTimeout(5200);
  await expect(page.locator('.wt-roomCard details.wt-diagnostics')).toHaveAttribute('open', '');
});

test('迟到响应在暂停后不能写入页面', async ({ page }) => {
  await page.goto('/?scenario=late&theme=light');
  await page.evaluate(() => window.__wtFixture.view.onPause());
  await page.waitForTimeout(1200);
  await expect(page.locator('.wt-roomCard')).toHaveCount(0);
});

test('设置 dirty 保存失败后保留修改，放弃导航恢复原值', async ({ page }) => {
  await open(page, 'save-fail', 'light', 390, 1000);
  await page.locator('#wtTabSettings').click();
  const checkbox = page.locator('#wtPauseOtherOnPlaybackStop');
  await checkbox.uncheck();
  await expect(page.locator('#wtSaveConfig')).toBeEnabled();
  await page.locator('#wtSaveConfig').click();
  await expect(page.locator('#wtConfigStatus')).toContainText('配置保存失败');
  await expect(checkbox).not.toBeChecked();
  await page.locator('#wtTabMine').click();
  await expect(page.locator('#wtConfirmOverlay')).toBeVisible();
  await page.locator('#wtConfirmSubmit').click();
  await expect(page.locator('#wtTabMine')).toHaveAttribute('aria-current', 'page');
  await page.locator('#wtTabSettings').click();
  await expect(checkbox).toBeChecked();
  await page.screenshot({ path: path.join(artifactDir, 'mobile-settings-390.png'), fullPage: true });
});

test('诊断内容经过脱敏，导出前不出现原始标识', async ({ page }) => {
  await open(page, 'active', 'light', 390, 1000);
  await page.locator('.wt-roomCard button[data-act="more"]').click();
  await page.locator('.wt-moreMenu button[data-act="diagnostics"]').click();
  await expect(page.locator('.wt-diagnosticSummary').first()).toContainText('新鲜');
  const text = await page.locator('.wt-roomCard details.wt-diagnostics').innerText();
  expect(text).not.toContain('RoomHash');
  expect(text).not.toContain('SessionHash');
  expect(text).not.toContain('Token');
  await page.screenshot({ path: path.join(artifactDir, 'mobile-diagnostics-390.png'), fullPage: true });
});

for (const width of [320, 360, 390, 430, 768, 1024, 1440, 1920]) {
  test(`${width}px 窄屏无横向溢出且控件满足触控尺寸`, async ({ page }) => {
    await open(page, 'long', 'light', width, 1000);
    const metrics = await page.evaluate(() => ({
      width: document.documentElement.scrollWidth,
      client: document.documentElement.clientWidth,
      controls: Array.from(document.querySelectorAll('button, input:not([type="checkbox"]), select, a.wt-pluginNavLink, label.checkboxContainer')).map(x => Math.min(x.getBoundingClientRect().width, x.getBoundingClientRect().height)).filter(Boolean),
      headerBottom: document.querySelector('.skinHeader').getBoundingClientRect().bottom,
      pageTop: document.querySelector('.wt-page').getBoundingClientRect().top
    }));
    expect(metrics.width).toBeLessThanOrEqual(metrics.client + 1);
    expect(Math.min(...metrics.controls)).toBeGreaterThanOrEqual(44);
    expect(metrics.headerBottom).toBeLessThanOrEqual(metrics.pageTop + 1);
    if (width === 390) await page.screenshot({ path: path.join(artifactDir, 'mobile-active-room-390.png'), fullPage: true });
    if (width === 320) await page.screenshot({ path: path.join(artifactDir, 'mobile-long-name-code-320.png'), fullPage: true });
  });
}

test('浅色、深色和仅 HSL 主题均保持可读且无外部字体', async ({ page }) => {
  for (const theme of ['light', 'dark', 'hsl']) {
    await open(page, 'active', theme, 900, 800);
    const values = await page.locator('.wt-roomCard').evaluate(el => {
      const card = getComputedStyle(el); const text = getComputedStyle(el.querySelector('.wt-roomName'));
      return { background: card.backgroundColor, color: text.color };
    });
    expect(values.background).not.toBe('rgba(0, 0, 0, 0)');
    expect(values.color).not.toBe('');
    expect(await page.locator('link[rel="stylesheet"][href*="http"]').count()).toBe(0);
    if (theme === 'dark') await page.screenshot({ path: path.join(artifactDir, 'desktop-dark-active-room.png'), fullPage: true });
  }
});

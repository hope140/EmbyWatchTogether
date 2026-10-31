const { test, expect } = require('@playwright/test');
const fs = require('fs');
const path = require('path');

const artifactDir = path.join(__dirname, '..', 'test-results', 'native-controls');
fs.mkdirSync(artifactDir, { recursive: true });

async function open(page, scenario = 'active', theme = 'light', width = 1100, height = 900, host = 'present') {
  await page.setViewportSize({ width, height });
  await page.goto(`/?scenario=${scenario}&theme=${theme}&host=${host}`);
  await expect(page.locator('.wt-page')).toBeVisible();
  await expect.poll(() => page.evaluate(() => window.__wtFixture && window.__wtFixture.apiCalls.some(x => x.path === 'WatchTogether/Rooms'))).toBe(true);
  await expect(page.locator('#wtRooms')).toHaveAttribute('aria-busy', 'false');
  const error = await page.evaluate(() => window.__wtFixture && window.__wtFixture.error);
  expect(error || '').toBe('');
  if (scenario === 'save-fail' || scenario.startsWith('token-')) await selectView(page, 'settings');
}

async function selectView(page, view) {
  const name = { rooms: 'Rooms', invite: 'Invite', settings: 'Settings' }[view];
  await page.locator('.tabs-viewmenubar a[href*="WatchTogether' + name + 'V19"]').click();
  const panel = { rooms: '#wtRoomsSection', invite: '#wtInvitationSection', settings: '#wtSettingsSection' }[view];
  await expect(page.locator(panel)).toBeVisible();
  await expect(page.locator('#wtRooms')).toHaveAttribute('aria-busy', 'false');
  if (view === 'settings') await expect(page.locator('#wtGitHubTokenStatus')).not.toContainText('正在读取');
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

async function buttonPaint(page, selector) {
  return page.locator(selector).evaluate(button => {
    function parse(value) {
      const match = value.match(/rgba?\(\s*([\d.]+)[, ]+\s*([\d.]+)[, ]+\s*([\d.]+)(?:[, ]+\s*([\d.]+))?\s*\)/i);
      if (!match) throw new Error(`Unexpected color: ${value}`);
      return [Number(match[1]), Number(match[2]), Number(match[3]), match[4] === undefined ? 1 : Number(match[4])];
    }
    function over(foreground, background) {
      const alpha = foreground[3] + background[3] * (1 - foreground[3]);
      return [0, 1, 2].map(i => (foreground[i] * foreground[3] + background[i] * background[3] * (1 - foreground[3])) / alpha).concat(alpha);
    }
    function luminance(color) {
      return color.slice(0, 3).map(v => v / 255).map(v => v <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4).reduce((sum, v, i) => sum + v * [.2126, .7152, .0722][i], 0);
    }
    function contrast(a, b) {
      const values = [luminance(a), luminance(b)].sort((x, y) => y - x);
      return (values[0] + .05) / (values[1] + .05);
    }
    const ancestors = [];
    for (let node = button.parentElement; node; node = node.parentElement) ancestors.unshift(node);
    let surface = [255, 255, 255, 1];
    for (const node of ancestors) {
      const style = getComputedStyle(node);
      surface = over(parse(style.backgroundColor), surface);
      if (style.backgroundImage !== 'none') {
        const sample = style.getPropertyValue('--fixture-gradient-sample').trim();
        if (!sample) throw new Error('Gradient needs an explicit representative sample');
        surface = sample.split(',').map(Number).concat(1);
      }
    }
    const style = getComputedStyle(button);
    const background = over(parse(style.backgroundColor), surface);
    return {
      classes: button.className,
      background: style.backgroundColor,
      borderStyle: style.borderTopStyle,
      borderWidth: parseFloat(style.borderTopWidth),
      textContrast: contrast(over(parse(style.color), background), background),
      borderContrast: contrast(over(parse(style.borderTopColor), background), surface)
    };
  });
}

async function expectButtonTextCentered(page) {
  const buttons = await page.locator('button[is="emby-button"]').evaluateAll(nodes => nodes.map(button => {
    const rect = button.getBoundingClientRect();
    if (!rect.width || !rect.height) return null;
    const range = document.createRange();
    range.selectNodeContents(button);
    const text = range.getBoundingClientRect();
    return { id: button.id || button.getAttribute('data-act'), x: text.left + text.width / 2 - rect.left - rect.width / 2, y: text.top + text.height / 2 - rect.top - rect.height / 2 };
  }).filter(Boolean));
  expect(buttons.length).toBeGreaterThan(0);
  for (const button of buttons) {
    expect(Math.abs(button.x), `${button.id} horizontal text offset`).toBeLessThanOrEqual(1);
    expect(Math.abs(button.y), `${button.id} vertical text offset`).toBeLessThanOrEqual(2);
  }
}

test('原生三页导航只显示当前区域并保留原生内容限宽', async ({ page }) => {
  await open(page, 'admin-member', 'light', 1440, 1000);
  await expect(page.locator('.tabs-viewmenubar')).toHaveCount(1);
  await expect(page.locator('.tabs-viewmenubar a')).toHaveText(['房间', '创建与邀请', '设置']);
  await expect(page.locator('#wtAdminSection')).toBeHidden();
  await expect(page.locator('#wtSettingsSection')).toBeHidden();
  await selectView(page, 'invite');
  await expect(page.locator('#wtAdminSection')).toBeVisible();
  await expect(page.locator('#wtSettingsSection')).toBeHidden();
  await selectView(page, 'settings');
  await expect(page.locator('#wtSettingsSection')).toBeVisible();
  await expect(page.locator('input[is="emby-input"]')).toHaveCount(4);
  await expect(page.locator('select[is="emby-select"]')).toHaveCount(4);
  await expect(page.locator('input[is="emby-checkbox"]')).toHaveCount(3);
  await expect(page.locator('button[is="emby-button"]')).not.toHaveCount(0);
  const contentLayout = await page.locator('.wt-nativeContent').evaluate(el => ({ maxWidth: getComputedStyle(el).maxWidth, maxWidthPx: parseFloat(getComputedStyle(el).maxWidth), width: el.getBoundingClientRect().width, left: el.getBoundingClientRect().left, viewport: document.documentElement.clientWidth }));
  expect(contentLayout.maxWidth).not.toBe('none');
  expect(contentLayout.width).toBeLessThanOrEqual(contentLayout.maxWidthPx + 1);
  expect(contentLayout.width).toBeLessThanOrEqual(contentLayout.viewport + 1);
  expect(Math.abs(contentLayout.left - (contentLayout.viewport - contentLayout.width) / 2)).toBeLessThanOrEqual(2);
  const sections = await page.locator('.wt-section').evaluateAll(nodes => nodes.map(node => node.id || node.className));
  expect(sections).toEqual(['wtAdminSection', 'wtInvitationSection', 'wtRoomsSection', 'wtHelpSection', 'wtSettingsSection']);
});

test('展开原生侧栏时内容按宿主规则靠左并保持限宽', async ({ page }) => {
  await open(page, 'admin-member', 'light', 1440, 1000);
  await page.locator('.wt-page').evaluate(el => el.classList.add('page-withDockedDrawer'));
  const layout = await page.locator('.wt-nativeContent').evaluate(el => {
    const rect = el.getBoundingClientRect();
    const parent = el.parentElement;
    return { left: rect.left, width: rect.width, maxWidth: parseFloat(getComputedStyle(el).maxWidth), expectedLeft: parent.getBoundingClientRect().left + parseFloat(getComputedStyle(parent).paddingLeft) };
  });
  expect(Math.abs(layout.left - layout.expectedLeft)).toBeLessThanOrEqual(1);
  expect(layout.width).toBeLessThanOrEqual(layout.maxWidth + 1);
});

test('浅色、深色和仅 HSL 单页主体均满足 4.5:1 对比度并保留管理员截图', async ({ page }) => {
  for (const theme of ['light', 'dark', 'hsl']) {
    await open(page, 'admin-member', theme, 1440, 1000);
    await selectView(page, 'settings');
    const colors = await page.locator('#wtSettingsSection h2').evaluate(el => {
      const style = getComputedStyle(el);
      return { background: getComputedStyle(document.querySelector('.fixture-host')).backgroundColor, color: style.color };
    });
    expect(contrastRatio(colors.color, colors.background)).toBeGreaterThanOrEqual(4.5);
    if (theme === 'light') {
      await page.screenshot({ path: path.join(artifactDir, 'desktop-light-admin-member.png'), fullPage: true });
    }
  }
});

test('深色渐变原生描边按钮的文字和边界满足代表点对比度', async ({ page }) => {
  await open(page, 'admin-member', 'darkgradient', 1440, 1000);
  await page.screenshot({ path: path.join(artifactDir, 'desktop-darkgradient-native-controls.png'), fullPage: true });
  const paint = await buttonPaint(page, '.wt-roomCard button[data-act="resume"]');
  expect(paint.classes).toContain('raised-outline');
  expect(paint.textContrast).toBeGreaterThanOrEqual(4.5);
  expect(paint.borderContrast).toBeGreaterThanOrEqual(3);
  test.info().annotations.push({ type: 'contrast', description: `代表渐变点合成文字 ${paint.textContrast.toFixed(3)}:1、边界 ${paint.borderContrast.toFixed(3)}:1` });
});

test('深色渐变中的旧 button-flat 会复现浅底白字，页面控件全部使用 raised', async ({ page }) => {
  await open(page, 'active', 'darkgradient', 1024, 900);
  const legacy = await page.evaluate(() => {
    function parse(value) {
      const match = value.match(/rgba?\(\s*([\d.]+)[, ]+\s*([\d.]+)[, ]+\s*([\d.]+)(?:[, ]+\s*([\d.]+))?\s*\)/i);
      if (!match) throw new Error(`Unexpected color: ${value}`);
      return [Number(match[1]), Number(match[2]), Number(match[3]), match[4] === undefined ? 1 : Number(match[4])];
    }
    function over(foreground, background) {
      const alpha = foreground[3] + background[3] * (1 - foreground[3]);
      return [0, 1, 2].map(index => (foreground[index] * foreground[3] + background[index] * background[3] * (1 - foreground[3])) / alpha).concat(alpha);
    }
    function luminance(color) {
      return color.slice(0, 3).map(value => value / 255).map(value => value <= .03928 ? value / 12.92 : ((value + .055) / 1.055) ** 2.4).reduce((sum, value, index) => sum + value * [0.2126, 0.7152, 0.0722][index], 0);
    }
    function contrast(foreground, background) {
      const light = Math.max(luminance(foreground), luminance(background));
      const dark = Math.min(luminance(foreground), luminance(background));
      return (light + .05) / (dark + .05);
    }
    const probe = document.createElement('button', { is: 'emby-button' });
    probe.setAttribute('is', 'emby-button');
    probe.classList.add('button-flat');
    probe.textContent = 'legacy';
    document.querySelector('.wt-roomActions').appendChild(probe);
    const raised = document.querySelector('.wt-roomActions button.raised');
    const host = document.querySelector('.fixture-host');
    const sample = getComputedStyle(host).getPropertyValue('--fixture-gradient-sample').split(',').map(Number).concat(1);
    const section = parse(getComputedStyle(probe.closest('.wt-section')).backgroundColor);
    const card = parse(getComputedStyle(probe.closest('.wt-roomCard')).backgroundColor);
    const surface = over(card, over(section, sample));
    const legacyBackground = over(parse(getComputedStyle(probe).backgroundColor), surface);
    const legacyForeground = over(parse(getComputedStyle(probe).color), legacyBackground);
    const raisedBackground = over(parse(getComputedStyle(raised).backgroundColor), surface);
    const raisedForeground = over(parse(getComputedStyle(raised).color), raisedBackground);
    const result = { native: probe.className, background: getComputedStyle(probe).backgroundColor, color: getComputedStyle(probe).color, legacyRatio: contrast(legacyForeground, legacyBackground), raisedRatio: contrast(raisedForeground, raisedBackground) };
    probe.remove();
    return result;
  });
  expect(legacy.native).toContain('emby-button-focusscale');
  expect(legacy.color).toContain('255');
  expect(legacy.legacyRatio).toBeLessThan(4.5);
  expect(legacy.raisedRatio).toBeGreaterThan(legacy.legacyRatio);
  await expect(page.locator('.wt-page button.button-flat')).toHaveCount(0);
  await expect(page.locator('.wt-page button.raised')).not.toHaveCount(0);
});

test('浅色、深色和 HSL 原生描边按钮文字和边界可辨', async ({ page }) => {
  for (const theme of ['light', 'dark', 'hsl']) {
    await open(page, 'admin-member', theme, 1024, 900);
    const paint = await buttonPaint(page, '#wtRefresh');
    expect(paint.classes).toContain('raised-outline');
    expect(paint.borderStyle).toBe('solid');
    expect(paint.borderWidth).toBeGreaterThan(0);
    expect(paint.textContrast).toBeGreaterThanOrEqual(4.5);
    expect(paint.borderContrast).toBeGreaterThanOrEqual(3);
  }
});

test('原生按钮文字在浅深主题中居中，主操作保留填充', async ({ page }) => {
  for (const theme of ['light', 'dark', 'hsl', 'darkgradient']) {
    await open(page, 'admin-member', theme, 1024, 1000);
    await expectButtonTextCentered(page);
    await selectView(page, 'invite');
    await expectButtonTextCentered(page);
    await selectView(page, 'settings');
    await expectButtonTextCentered(page);
    await expect(page.locator('.wt-page button.button-submit.raised-outline')).toHaveCount(0);
    await expect(page.locator('.wt-page button.raised:not(.button-submit):not(.raised-outline)')).toHaveCount(0);
    if (theme === 'light' || theme === 'darkgradient') {
      await selectView(page, 'rooms');
      await page.locator('.wt-roomCard').screenshot({ path: path.join(artifactDir, `room-buttons-${theme}.png`) });
    }
  }
});

test('管理员单页显示管理和设置，普通用户隐藏这两个区域', async ({ page }) => {
  await open(page, 'admin-manage', 'dark');
  await selectView(page, 'invite');
  await expect(page.locator('#wtAdminSection')).toBeVisible();
  await selectView(page, 'settings');
  await expect(page.locator('#wtSettingsSection')).toBeVisible();
  await page.screenshot({ path: path.join(artifactDir, 'desktop-dark-settings.png'), fullPage: true });
  await open(page, 'active', 'light');
  await expect(page.locator('#wtAdminSection')).toBeHidden();
  await expect(page.locator('#wtSettingsSection')).toBeHidden();
});

test('普通用户或权限错误时隐藏管理员内容', async ({ page }) => {
  await page.goto('/?scenario=users-fail&theme=light');
  await expect(page.locator('.wt-page')).toBeVisible();
  await expect(page.locator('#wtRooms')).toHaveAttribute('aria-busy', 'false');
  await expect(page.locator('#wtAdminSection')).toBeHidden();
  await expect(page.locator('#wtSettingsSection')).toBeHidden();
});

test('反复进入离开单页保持结构稳定，缺少宿主容器不影响业务区', async ({ page }) => {
  await open(page, 'active', 'light', 1024, 900);
  for (let index = 0; index < 10; index += 1) {
    await page.evaluate(() => window.__wtFixture.view.onPause());
    await page.evaluate(() => window.__wtFixture.view.onResume());
  }
  await expect(page.locator('#wtRooms')).toHaveAttribute('aria-busy', 'false');
  await page.evaluate(() => window.__wtFixture.view.onPause());
  await expect(page.locator('.tabs-viewmenubar')).toHaveCount(0);

  await open(page, 'active', 'light', 1024, 900, 'missing');
  await expect(page.locator('.tabs-viewmenubar')).toHaveCount(0);
});

test('房间卡保留原单页结构、成员信息和原生动态按钮', async ({ page }) => {
  await open(page, 'active', 'light', 1440, 1000);
  const card = page.locator('.wt-roomCard');
  await expect(card).toContainText('周末一起看');
  await expect(card.locator('.wt-roomHeader')).toBeVisible();
  await expect(card.locator('.wt-participant')).toHaveCount(2);
  await expect(card.locator('.wt-roomActions').first()).toBeVisible();
  await expect(card.locator('.wt-participant')).toHaveCount(2);
  await expect(card.locator('.wt-diagnostics')).toBeHidden();
  await card.locator('[data-act="diagnostics"]').click();
  await expect(card.locator('.wt-diagnostics')).toBeVisible();
  await expect(card.locator('button[is="emby-button"]')).not.toHaveCount(0);
  await open(page, 'active', 'light', 390, 1000);
  const mobile = await page.locator('.wt-roomCard').evaluate(card => ({
    width: card.getBoundingClientRect().width,
    scrollWidth: card.scrollWidth,
    clientWidth: card.clientWidth
  }));
  expect(mobile.scrollWidth).toBeLessThanOrEqual(mobile.clientWidth + 1);
});

test('管理员成员保留原有房间动作，动态按钮使用 Emby 原生控件', async ({ page }) => {
  await open(page, 'admin-member', 'light', 1024, 900);
  await expect(page.locator('.wt-roomCard .wt-action[data-act="pause"]')).toHaveAttribute('is', 'emby-button');
  await expect(page.locator('.wt-roomCard .wt-action[data-act="pause"]')).toHaveClass(/emby-button-focusscale/);
  await expect(page.locator('.wt-roomCard .wt-action[data-act="delete"]')).toHaveAttribute('is', 'emby-button');
  await expect(page.locator('.wt-roomCard .wt-action[data-act="delete"]')).toHaveClass(/emby-button-focusscale/);

  await open(page, 'admin-manage', 'light', 1024, 900);
  await expect(page.locator('.wt-roomCard .wt-action[data-act="participantResync"]')).toHaveCount(0);
  await expect(page.locator('.wt-roomCard .wt-action[data-act="join"]')).toHaveCount(0);
  await expect(page.locator('.wt-roomCard .wt-action[data-act="leave"]')).toHaveCount(0);
});

test('已有房间和无房间均保留原单页帮助与邀请区域', async ({ page }) => {
  await open(page, 'active', 'light');
  await expect(page.locator('.wt-help')).toBeVisible();
  await open(page, 'empty', 'light', 390, 900);
  await expect(page.locator('#wtInvitationSection')).toBeHidden();
  await expect(page.locator('.wt-help')).toBeVisible();
  await selectView(page, 'invite');
  await expect(page.locator('#wtInvitationSection')).toBeVisible();
});

test('普通成员当前房间保留原房间卡结构和状态信息', async ({ page }) => {
  await open(page);
  await expect(page.locator('.wt-roomCard')).toContainText('周末一起看');
  await expect(page.locator('.wt-roomState')).toContainText('同步中');
  await expect(page.locator('.wt-roomCard .wt-participants')).toContainText('hope');
  await expect(page.locator('.wt-roomCard .wt-participants')).toContainText('主用户');
  await expect(page.locator('.wt-roomControls button[data-act="diagnostics"]')).toHaveAttribute('is', 'emby-button');
  await page.screenshot({ path: path.join(artifactDir, 'desktop-light-active-room.png'), fullPage: true });
});

test('Waiting 与 State 失败状态反馈保持原页面语义', async ({ page }) => {
  await open(page, 'waiting', 'light', 1024, 900);
  await expect(page.locator('.wt-roomState')).toContainText('等待播放');
  await expect(page.locator('.wt-roomStateDescription')).toContainText('等待');

  await open(page, 'state-fail', 'dark', 1024, 900);
  await expect(page.locator('.wt-roomCard')).toContainText('状态');
});

test('管理员可查看非成员管理列表并保留设置区域', async ({ page }) => {
  await open(page, 'admin-manage', 'dark');
  await expect(page.locator('#wtRooms')).toContainText('管理员长列表房间');
  await selectView(page, 'invite');
  await expect(page.locator('#wtAdminSection')).toBeVisible();
  await selectView(page, 'settings');
  await expect(page.locator('#wtSettingsSection')).toBeVisible();
  await expect(page.locator('#wtSaveConfig')).toBeEnabled();
  await page.screenshot({ path: path.join(artifactDir, 'desktop-dark-same-structure.png'), fullPage: true });
});

test('无房间、邀请创建等待与接受失败都有可见状态', async ({ page }) => {
  await open(page, 'empty', 'light', 390, 900);
  await expect(page.locator('#wtRooms')).toContainText('暂无参与的房间');
  await selectView(page, 'invite');
  await page.locator('#wtInvitationName').fill('周末邀请');
  await page.locator('#wtCreateInvitation').click();
  await expect(page.locator('#wtInvitationCodePanel')).toBeVisible();
  await expect(page.locator('#wtInvitationCode')).toHaveText('ABCD12EFghI3J4KLMNopQR');
  await expect(page.locator('#wtInvitations button[is="emby-button"]')).toHaveClass(/emby-button-focusscale/);
  await page.screenshot({ path: path.join(artifactDir, 'mobile-invitation-390.png'), fullPage: true });

  await open(page, 'accept-fail', 'light', 390, 900);
  await selectView(page, 'invite');
  await page.locator('#wtInvitationAcceptCode').fill('BAD-CODE');
  await page.locator('#wtAcceptInvitation').click();
  await expect(page.locator('#wtInvitationStatus')).toContainText('邀请码无效或已过期');
});

test('接受邀请网络失败只读回房间且不重发 POST', async ({ page }) => {
  await open(page, 'accept-network');
  await selectView(page, 'invite');
  await page.locator('#wtInvitationAcceptCode').fill('NETWORK-CODE');
  await page.locator('#wtAcceptInvitation').click();
  await expect(page.locator('#wtInvitationStatus')).toContainText('邀请接受失败');
  const requestLog = await calls(page);
  expect(requestLog.filter(x => x.method === 'POST' && x.path.includes('/Accept'))).toHaveLength(1);
  expect(requestLog.filter(x => x.method === 'GET' && x.path === 'WatchTogether/Rooms').length).toBeGreaterThanOrEqual(1);
});

test('原有 window.confirm 取消不发送请求', async ({ page }) => {
  await open(page);
  const leave = page.locator('.wt-roomCard button[data-act="leave"]');
  page.once('dialog', dialog => dialog.dismiss());
  const before = await calls(page);
  await leave.click();
  expect((await calls(page)).filter(x => x.method !== 'GET')).toEqual(before.filter(x => x.method !== 'GET'));
});

test('5 秒轮询保持邀请输入', async ({ page }) => {
  await open(page, 'empty');
  await selectView(page, 'invite');
  const accept = page.locator('#wtInvitationAcceptCode');
  await accept.fill('keep-me');
  await page.waitForTimeout(5200);
  await expect(accept).toHaveValue('keep-me');
});

test('5 秒轮询保持诊断展开状态', async ({ page }) => {
  await open(page);
  const diagnostics = page.locator('.wt-roomCard button[data-act="diagnostics"]');
  await diagnostics.click();
  await page.waitForTimeout(5200);
  await expect(page.locator('.wt-diagnosticToolbar')).toBeVisible();
  await page.waitForTimeout(5200);
  await expect(page.locator('.wt-roomCard details.wt-diagnostics')).toHaveAttribute('open', '');
});

test('页面暂停后拒绝迟到回写且恢复重新读取', async ({ page }) => {
  await page.goto('/?scenario=late&theme=light');
  await page.evaluate(() => window.__wtFixture.view.onPause());
  await page.waitForTimeout(1200);
  await expect(page.locator('.wt-roomCard')).toHaveCount(0);
  await page.evaluate(() => window.__wtFixture.view.onResume());
  await expect(page.locator('.wt-roomCard')).toHaveCount(1);
});

test('设置保存失败后保留修改', async ({ page }) => {
  await open(page, 'save-fail', 'light', 390, 1000);
  const checkbox = page.locator('#wtPauseOtherOnPlaybackStop');
  await expect(checkbox).toBeEnabled();
  await checkbox.click();
  await expect(checkbox).not.toBeChecked();
  await expect(page.locator('#wtSaveConfig')).toBeEnabled();
  await page.locator('#wtSaveConfig').click();
  await expect(page.locator('#wtConfigStatus')).toContainText('配置保存失败');
  await expect(checkbox).not.toBeChecked();
  await expect(checkbox).not.toBeChecked();
  await page.screenshot({ path: path.join(artifactDir, 'mobile-settings-390.png'), fullPage: true });
});

test('主体复用 Emby 原生表单、checkbox 和动态按钮语义', async ({ page }) => {
  await open(page, 'admin-member', 'light', 1024, 900);
  await selectView(page, 'settings');
  await expect(page.locator('input[is="emby-checkbox"]')).toHaveCount(3);
  await expect(page.locator('#wtUpdateChannel[is="emby-select"]')).toHaveCount(1);
  await expect(page.locator('#wtSaveConfig[is="emby-button"]')).toHaveClass(/raised/);
  await selectView(page, 'rooms');
  await expect(page.locator('.wt-roomCard')).toHaveCount(1);
  await expect(page.locator('.wt-roomCard button[is="emby-button"]')).not.toHaveCount(0);
  await expect(page.locator('#wtSaveConfig')).not.toHaveClass(/wt-settingsButton/);
});

test('GitHub Token 设置使用原生控件并保留保存、清除和取消确认语义', async ({ page }) => {
  await open(page, 'admin-member', 'light', 1024, 900);
  await selectView(page, 'settings');
  await expect(page.locator('#wtGitHubToken')).toHaveAttribute('type', 'password');
  await expect(page.locator('#wtSaveGitHubToken')).toHaveClass(/raised/);
  await expect(page.locator('#wtClearGitHubToken')).toHaveClass(/raised/);
  await expect(page.locator('#wtSaveGitHubToken')).not.toHaveClass(/wt-settingsButton/);
  await expect(page.locator('#wtClearGitHubToken')).not.toHaveClass(/wt-settingsButton/);
  await expect(page.locator('#wtGitHubTokenStatus')).toContainText('尚未配置');

  await page.locator('#wtGitHubToken').fill('ghp_fixture_secret');
  await page.locator('#wtSaveGitHubToken').click();
  await expect(page.locator('#wtGitHubTokenStatus')).toContainText('Token 已保存');
  await expect(page.locator('#wtGitHubToken')).toHaveValue('');
  const saved = await calls(page);
  expect(saved.filter(x => x.method === 'POST' && x.path === 'WatchTogether/GitHubToken').at(-1).body.Token).toBe('ghp_fixture_secret');

  await page.once('dialog', dialog => dialog.dismiss());
  const beforeCancel = await calls(page);
  await page.locator('#wtClearGitHubToken').click();
  expect((await calls(page)).filter(x => x.method === 'DELETE' && x.path === 'WatchTogether/GitHubToken')).toHaveLength(0);
  expect((await calls(page)).filter(x => x.method !== 'GET')).toEqual(beforeCancel.filter(x => x.method !== 'GET'));
  await page.once('dialog', dialog => dialog.accept());
  await page.locator('#wtClearGitHubToken').click();
  await expect(page.locator('#wtGitHubTokenStatus')).toContainText('Token 已清除');
});

test('设置页浅深主题主体背景与文字保持可读，并保留移动截图', async ({ page }) => {
  for (const theme of ['light', 'dark']) {
    await open(page, 'admin-member', theme, 1024, 1000);
    await selectView(page, 'settings');
    const colors = await page.locator('#wtSettingsSection h2').first().evaluate(el => {
      const pageStyle = getComputedStyle(el.closest('.wt-page'));
      const textStyle = getComputedStyle(el);
      return { background: getComputedStyle(document.querySelector('.fixture-host')).backgroundColor, pageColor: pageStyle.color, text: textStyle.color };
    });
    expect(contrastRatio(colors.text, colors.background)).toBeGreaterThanOrEqual(4.5);
    expect(contrastRatio(colors.pageColor, colors.background)).toBeGreaterThanOrEqual(4.5);
    await page.screenshot({ path: path.join(artifactDir, `desktop-${theme}-settings.png`), fullPage: true });
  }
  await open(page, 'admin-member', 'dark', 390, 1100);
  await selectView(page, 'settings');
  await expect(page.locator('#wtSettingsSection')).toBeVisible();
  await page.screenshot({ path: path.join(artifactDir, 'mobile-settings-dark-390.png'), fullPage: true });
});

test('Token 状态读取失败仍允许清除，保存失败清空草稿并保留错误', async ({ page }) => {
  await open(page, 'token-read-fail', 'light', 1024, 900);
  await expect(page.locator('#wtGitHubTokenStatus')).toContainText('可尝试清除');
  await expect(page.locator('#wtClearGitHubToken')).toBeEnabled();
  await page.once('dialog', dialog => dialog.accept());
  await page.locator('#wtClearGitHubToken').click();
  await expect(page.locator('#wtGitHubTokenStatus')).toContainText('Token 已清除');

  await open(page, 'token-save-fail', 'light', 1024, 900);
  await page.locator('#wtGitHubToken').fill('ghp_fixture_secret');
  await page.locator('#wtSaveGitHubToken').click();
  await expect(page.locator('#wtGitHubTokenStatus')).toContainText('Token 保存失败');
  await expect(page.locator('#wtGitHubToken')).toHaveValue('');
});

test('Token pause 清空明文并在恢复后重新读取', async ({ page }) => {
  await open(page, 'token-slow', 'light', 1024, 900);
  await page.locator('#wtGitHubToken').fill('ghp_pause_secret');
  await page.locator('#wtSaveGitHubToken').click();
  await page.evaluate(() => window.__wtFixture.view.onPause());
  await page.waitForTimeout(1200);
  await expect(page.locator('#wtGitHubToken')).toHaveValue('');
  await expect(page.locator('#wtGitHubTokenStatus')).not.toContainText('Token 已保存');

  await page.evaluate(() => window.__wtFixture.view.onResume());
  await expect(page.locator('#wtGitHubTokenStatus')).toContainText('已配置');
  await expect(page.locator('#wtGitHubToken')).toHaveValue('');
});

test('诊断内容经过脱敏，导出前不出现原始标识', async ({ page }) => {
  await open(page, 'active', 'light', 390, 1000);
  await page.locator('.wt-roomCard button[data-act="diagnostics"]').click();
  await expect(page.locator('.wt-diagnosticSummary').first()).toContainText('新鲜');
  const text = await page.locator('.wt-roomCard details.wt-diagnostics').innerText();
  expect(text).not.toContain('RoomHash');
  expect(text).not.toContain('SessionHash');
  expect(text).not.toContain('Token');
  await expect(page.locator('.wt-diagnosticToolbar button')).toHaveAttribute('is', 'emby-button');
  await expect(page.locator('.wt-diagnosticToolbar button')).toHaveClass(/emby-button-focusscale/);
  await page.screenshot({ path: path.join(artifactDir, 'mobile-diagnostics-390.png'), fullPage: true });
});

test('Recovering 状态只针对临时掉线参与者，主用户缺失仍显示 Waiting', async ({ page }) => {
  await open(page, 'recovering', 'dark', 390, 1400);
  await expect(page.locator('#wtRefresh')).toHaveText('刷新');
  const refreshMetrics = await page.locator('#wtRefresh').evaluate(button => ({ width: button.getBoundingClientRect().width, textWidth: button.scrollWidth }));
  expect(refreshMetrics.textWidth).toBeLessThanOrEqual(refreshMetrics.width);
  await expect(page.locator('.wt-roomState')).toHaveText('等待连接恢复');
  await expect(page.locator('.wt-roomState')).toHaveClass(/wt-roomState-recovering/);
  await expect(page.locator('.wt-roomStateDescription')).toContainText('临时掉线');
  await page.screenshot({ path: path.join(artifactDir, 'mobile-recovering-room-390.png'), fullPage: true });

  await open(page, 'primary-missing', 'light', 390, 1000);
  await expect(page.locator('.wt-roomState')).toHaveText('等待播放');
  await expect(page.locator('.wt-roomState')).not.toHaveClass(/wt-roomState-recovering/);
  const narrowMetrics = await page.evaluate(() => ({ width: document.documentElement.scrollWidth, client: document.documentElement.clientWidth }));
  expect(narrowMetrics.width).toBeLessThanOrEqual(narrowMetrics.client + 1);
});

test('过期快照诊断保留精确标签和安全导出代码，未知值仍被清洗', async ({ page }) => {
  await open(page);
  await page.evaluate(() => {
    const original = window.ApiClient.getJSON;
    window.ApiClient.getJSON = async path => {
      const value = await original(path);
      if (!path.endsWith('/Diagnostics')) return value;
      return Object.assign({}, value, {
        Events: [
          { Type: 'observed', Result: 'observed' },
          { Type: 'snapshot_ignored private-event-token', Result: 'older_activity private-result-token' },
          { Type: 'snapshot_ignored', Result: 'older_activity', Alias: 'userB', AtUtc: '2026-10-31T00:00:00Z' }
        ]
      });
    };
  });
  await page.locator('.wt-roomCard button[data-act="diagnostics"]').click();
  const events = page.locator('.wt-diagnosticList li');
  await expect(events).toHaveCount(3);
  await expect(events.nth(0)).toContainText('忽略过期快照');
  await expect(events.nth(0)).toContainText('活动时间较旧');
  await expect(events.nth(1)).toContainText('观察事件');
  await expect(events.nth(2)).toContainText('观察事件，已记录');

  const downloadPromise = page.waitForEvent('download');
  await page.locator('.wt-diagnosticToolbar button').click();
  const stream = await (await downloadPromise).createReadStream();
  let exported = '';
  for await (const chunk of stream) exported += chunk.toString();
  const diagnostic = JSON.parse(exported);
  expect(diagnostic.events[0]).toMatchObject({ type: 'snapshot_ignored', result: 'older_activity', alias: 'userB' });
  expect(diagnostic.events[1]).toMatchObject({ type: 'observed', result: null });
  expect(diagnostic.events[2]).toMatchObject({ type: 'observed', result: 'observed' });
  expect(await page.locator('.wt-diagnostics').innerText()).not.toContain('private-');
  expect(exported).not.toContain('private-');
});

test('TransientRecovery 和 Drift 诊断清洗后展示并导出，390px 无溢出', async ({ page }) => {
  await open(page, 'recovering', 'dark', 390, 1800);
  await page.locator('.wt-roomCard button[data-act="diagnostics"]').click();
  await expect(page.locator('.wt-roomCard details.wt-diagnostics')).toContainText('临时掉线恢复');
  await expect(page.locator('.wt-roomCard details.wt-diagnostics')).toContainText('缺失参与者');
  await expect(page.locator('.wt-roomCard details.wt-diagnostics')).toContainText('播放漂移');
  const recoveringText = await page.locator('.wt-roomCard details.wt-diagnostics').innerText();
  expect(recoveringText).not.toContain('sensitive-room-id');
  expect(recoveringText).not.toContain('sensitive-session');
  expect(recoveringText).not.toContain('should-not-export');
  const recoveringMetrics = await page.evaluate(() => ({ width: document.documentElement.scrollWidth, client: document.documentElement.clientWidth }));
  expect(recoveringMetrics.width).toBeLessThanOrEqual(recoveringMetrics.client + 1);
  await page.screenshot({ path: path.join(artifactDir, 'mobile-recovering-diagnostics-390.png'), fullPage: true });

  const downloadPromise = page.waitForEvent('download');
  await page.locator('.wt-diagnosticToolbar button').click();
  const download = await downloadPromise;
  const stream = await download.createReadStream();
  let exported = '';
  for await (const chunk of stream) exported += chunk.toString();
  const exportedJson = JSON.parse(exported);
  expect(exportedJson.transientRecovery.missingAliases).toEqual(['userB']);
  expect(exportedJson.transientRecovery.participants[1].expectedSessionHash).toBe('hash-session-b');
  expect(exportedJson.RoomHash).toBeUndefined();
  expect(exportedJson.SecretToken).toBeUndefined();
  const exportedText = JSON.stringify(exportedJson);
  expect(exportedText).not.toContain('sensitive-room-id');
  expect(exportedText).not.toContain('sensitive-server-id');
  expect(exportedText).not.toContain('sensitive-session');
  expect(exportedText).not.toContain('sensitive-item');
  expect(exportedText).not.toContain('sensitive-user-id');
  expect(exportedText).not.toContain('should-not-export');

  await open(page, 'drift-positive', 'light', 390, 1400);
  await page.locator('.wt-roomCard button[data-act="diagnostics"]').click();
  await expect(page.locator('.wt-roomCard details.wt-diagnostics')).toContainText('1.3 秒');
  await expect(page.locator('.wt-roomCard details.wt-diagnostics')).toContainText('自动纠偏次数');
});

for (const width of [320, 360, 390, 430, 768, 1024, 1440, 1920]) {
  test(`${width}px 窄屏无横向溢出且控件满足触控尺寸`, async ({ page }) => {
    await open(page, 'long', 'light', width, 1000);
    const metrics = await page.evaluate(() => ({
      width: document.documentElement.scrollWidth,
      client: document.documentElement.clientWidth,
      controls: Array.from(document.querySelectorAll('button, input:not([type="checkbox"]), select, .emby-tab-button, .emby-toggle-label')).map(x => Math.min(x.getBoundingClientRect().width, x.getBoundingClientRect().height)).filter(Boolean),
      headerBottom: document.querySelector('.skinHeader').getBoundingClientRect().bottom,
      pageTop: document.querySelector('.wt-page').getBoundingClientRect().top
    }));
    expect(metrics.width).toBeLessThanOrEqual(metrics.client + 1);
    expect(Math.min(...metrics.controls)).toBeGreaterThanOrEqual(44);
    expect(metrics.headerBottom).toBeLessThanOrEqual(metrics.pageTop + 1);
    await expectButtonTextCentered(page);
    if (width === 390) await page.screenshot({ path: path.join(artifactDir, 'mobile-active-room-390.png'), fullPage: true });
    if (width === 320) await page.screenshot({ path: path.join(artifactDir, 'mobile-long-name-code-320.png'), fullPage: true });
    await selectView(page, 'invite');
    const invitationMetrics = await page.evaluate(() => ({
      width: document.documentElement.scrollWidth, client: document.documentElement.clientWidth,
      controls: Array.from(document.querySelectorAll('button, input:not([type="checkbox"]), select, .emby-tab-button')).map(x => Math.min(x.getBoundingClientRect().width, x.getBoundingClientRect().height)).filter(Boolean),
      headerBottom: document.querySelector('.skinHeader').getBoundingClientRect().bottom,
      pageTop: document.querySelector('.wt-page').getBoundingClientRect().top
    }));
    expect(invitationMetrics.width).toBeLessThanOrEqual(invitationMetrics.client + 1);
    expect(Math.min(...invitationMetrics.controls)).toBeGreaterThanOrEqual(44);
    expect(invitationMetrics.headerBottom).toBeLessThanOrEqual(invitationMetrics.pageTop + 1);
    await expectButtonTextCentered(page);
  });
}

test('浅色、深色和仅 HSL 主题均保持可读且无外部字体', async ({ page }) => {
  for (const theme of ['light', 'dark', 'hsl']) {
    await open(page, 'active', theme, 900, 800);
    const values = await page.locator('.wt-roomCard').evaluate(el => {
      const card = getComputedStyle(el); const text = getComputedStyle(el.querySelector('.wt-roomName'));
      return { background: card.backgroundColor, color: text.color };
    });
  expect(values.background).not.toBe('');
    expect(values.color).not.toBe('');
    expect(await page.locator('link[rel="stylesheet"][href*="http"]').count()).toBe(0);
    if (theme === 'dark') await page.screenshot({ path: path.join(artifactDir, 'desktop-dark-active-room.png'), fullPage: true });
  }
});

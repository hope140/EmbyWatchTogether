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

test('Emby 原生 mainTabsManager 生成宿主分页并保持页面无自绘导航', async ({ page }) => {
  await open(page, 'active', 'light', 1440, 1000);
  const middle = page.locator('.skinHeader .headerMiddle');
  await expect(middle.locator('.tabs-viewmenubar')).toHaveCount(1);
  await expect(middle.locator('.emby-tab-button')).toHaveCount(1);
  await expect(page.locator('#wtPluginNav, #wtPluginNavSlot')).toHaveCount(0);
  await expect(page.locator('#wtTabMine')).toHaveAttribute('aria-current', 'page');
  await expect(page.locator('#wtTabMine')).toHaveClass(/emby-tab-button-active/);
  const navStyle = await page.locator('.tabs-viewmenubar').evaluate(el => {
    const style = getComputedStyle(el);
    const active = getComputedStyle(el.querySelector('.emby-tab-button-active'));
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
    await expect(page.locator(`#${id}`)).toHaveClass(/emby-tab-button-active/);
    await expect.poll(() => page.evaluate(() => location.hash)).toContain(id === 'wtTabManage' ? 'WatchTogetherUiManageV5' : id === 'wtTabSettings' ? 'WatchTogetherUiSettingsV5' : 'WatchTogetherUiMineV5');
    if (id === 'wtTabSettings') await page.screenshot({ path: path.join(artifactDir, 'desktop-dark-settings.png'), fullPage: true });
  }
  await open(page, 'active', 'light');
  await expect(page.locator('#wtTabManage')).toBeHidden();
  await expect(page.locator('#wtTabSettings')).toBeHidden();
});

test('普通用户直达管理员 route 仍只获得我的房间', async ({ page }) => {
  await page.goto('/?scenario=active&theme=light#!/configurationpage?name=WatchTogetherUiManageV5');
  await expect(page.locator('.wt-page')).toBeVisible();
  await expect(page.locator('#wtRooms')).toHaveAttribute('aria-busy', 'false');
  await expect(page.locator('#wtAdminSection')).toBeHidden();
  await expect(page.locator('#wtSettingsSection')).toBeHidden();
  await expect(page.locator('.headerMiddle .emby-tab-button')).toHaveCount(1);
  await expect(page.locator('#wtTabMine')).toBeVisible();
});

test('Users 权限请求出现其他错误时安全回退为一个原生 tab并隐藏管理员内容', async ({ page }) => {
  await page.goto('/?scenario=users-fail&theme=light');
  await expect(page.locator('.wt-page')).toBeVisible();
  await expect(page.locator('#wtRooms')).toHaveAttribute('aria-busy', 'false');
  await expect(page.locator('.headerMiddle .emby-tab-button')).toHaveCount(1);
  await expect(page.locator('#wtTabMine')).toBeVisible();
  await expect(page.locator('#wtAdminSection')).toBeHidden();
  await expect(page.locator('#wtSettingsSection')).toBeHidden();
});

test('反复进入离开页面只保留一份原生分页，缺少宿主容器时不创建备用导航', async ({ page }) => {
  await open(page, 'active', 'light', 1024, 900);
  for (let index = 0; index < 10; index += 1) {
    await page.evaluate(() => window.__wtFixture.view.onPause());
    await page.evaluate(() => window.__wtFixture.view.onResume());
  }
  await expect.poll(() => page.evaluate(() => document.querySelector('.wt-page')._wtMoreMenuDismissBound)).toBe(true);
  await expect.poll(() => page.locator('.headerMiddle .tabs-viewmenubar').count()).toBe(1);
  await expect(page.locator('.headerMiddle .emby-tab-button')).toHaveCount(1);
  await page.evaluate(() => window.__wtFixture.view.onPause());
  await expect.poll(() => page.evaluate(() => document.querySelector('.wt-page')._wtMoreMenuDismissBound)).toBe(false);
  await expect(page.locator('#wtPluginNav, #wtPluginNavSlot')).toHaveCount(0);

  await open(page, 'active', 'light', 1024, 900, 'missing');
  await expect(page.locator('.headerMiddle')).toHaveCount(0);
  await expect(page.locator('#wtPluginNav, #wtPluginNavSlot')).toHaveCount(0);
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
  await expect(page.locator('.wt-roomMediaPlaceholder')).toContainText('已检测到播放');
  await expect(page.locator('.wt-roomMediaPlaceholder')).toContainText('媒体信息待权限验证');
  await expect(page.locator('.wt-roomParticipant')).toHaveCount(2);
  await expect(page.locator('.wt-roomParticipant').first()).toContainText('播放中');
  await expect(page.locator('.wt-roomMediaPlaceholder button')).toHaveCount(0);
  await expect(page.locator('#wtInvitationSection')).toBeHidden();
  await page.screenshot({ path: path.join(artifactDir, 'desktop-light-active-room.png'), fullPage: true });
});

test('媒体状态只依据匹配参与者的 State 会话，等待和 State 失败不与成员行矛盾', async ({ page }) => {
  await open(page, 'waiting', 'light', 1024, 900);
  await expect(page.locator('.wt-roomMediaPlaceholder')).toContainText('尚未检测到播放');
  await expect(page.locator('.wt-roomMediaPlaceholder')).toContainText('媒体信息待权限验证');
  await expect(page.locator('.wt-roomParticipant').first()).toContainText('未检测到播放');

  await open(page, 'state-fail', 'dark', 1024, 900);
  await expect(page.locator('.wt-roomMediaPlaceholder')).toContainText('播放状态暂无法确认');
  await expect(page.locator('.wt-roomParticipant').first()).toContainText('播放状态暂无法确认');
  await expect(page.locator('.wt-roomMediaPlaceholder')).not.toContainText('已检测到播放');
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
  await expect(checkbox).toBeEnabled();
  await page.locator('label.emby-toggle-label:has(#wtPauseOtherOnPlaybackStop)').click();
  await expect(checkbox).not.toBeChecked();
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

test('主体复用 Emby 原生表单、开关和 paperList 语义', async ({ page }) => {
  await open(page, 'admin-member', 'light', 1024, 900);
  await page.locator('#wtTabSettings').click();
  await expect(page.locator('input[is="emby-toggle"]')).toHaveCount(3);
  await expect(page.locator('label.emby-toggle-label .toggleSwitch')).toHaveCount(3);
  await expect(page.locator('input[is="emby-checkbox"]')).toHaveCount(0);
  await expect(page.locator('#wtUpdateChannel[is="emby-select"]')).toHaveCount(1);
  await expect(page.locator('#wtSaveConfig[is="emby-button"]')).toHaveClass(/raised/);
  await page.locator('#wtTabMine').click();
  await expect(page.locator('#wtRooms .wt-roomGroup:not([hidden]) .wt-roomList.paperList.visualCardBox.vertical-list')).toHaveCount(1);
  await expect(page.locator('.wt-roomCard.listItem.listItem-border')).toHaveCount(1);
  const surface = await page.locator('.wt-roomCard').evaluate(el => {
    const style = getComputedStyle(el);
    return { background: style.backgroundColor, border: style.borderTopWidth, padding: style.paddingTop };
  });
  expect(surface.background).toBe('rgba(0, 0, 0, 0)');
  expect(surface.border).toBe('1px');
});

test('GitHub Token 设置使用原生控件并保留保存、清除和取消确认语义', async ({ page }) => {
  await open(page, 'admin-member', 'light', 1024, 900);
  await page.locator('#wtTabSettings').click();
  await expect(page.locator('#wtGitHubToken')).toHaveAttribute('type', 'password');
  await expect(page.locator('#wtSaveGitHubToken')).toHaveClass(/raised/);
  await expect(page.locator('#wtClearGitHubToken')).toHaveClass(/button-flat/);
  await expect(page.locator('#wtSaveGitHubToken')).not.toHaveClass(/wt-settingsButton/);
  await expect(page.locator('#wtClearGitHubToken')).not.toHaveClass(/wt-settingsButton/);
  await expect(page.locator('#wtGitHubTokenStatus')).toContainText('尚未配置');

  await page.locator('#wtGitHubToken').fill('ghp_fixture_secret');
  await page.locator('#wtSaveGitHubToken').click();
  await expect(page.locator('#wtGitHubTokenStatus')).toContainText('Token 已保存');
  await expect(page.locator('#wtGitHubToken')).toHaveValue('');
  const saved = await calls(page);
  expect(saved.filter(x => x.method === 'POST' && x.path === 'WatchTogether/GitHubToken').at(-1).body.Token).toBe('ghp_fixture_secret');

  await page.locator('#wtClearGitHubToken').click();
  await expect(page.locator('#wtConfirmOverlay')).toBeVisible();
  const beforeCancel = await calls(page);
  await page.locator('#wtConfirmCancel').click();
  expect((await calls(page)).filter(x => x.method === 'DELETE' && x.path === 'WatchTogether/GitHubToken')).toHaveLength(0);
  expect((await calls(page)).length).toBe(beforeCancel.length);
  await page.locator('#wtClearGitHubToken').click();
  await page.locator('#wtConfirmSubmit').click();
  await expect(page.locator('#wtGitHubTokenStatus')).toContainText('Token 已清除');
});

test('设置页浅深主题主体背景与文字保持可读，并保留移动截图', async ({ page }) => {
  for (const theme of ['light', 'dark']) {
    await open(page, 'admin-member', theme, 1024, 1000);
    await page.locator('#wtTabSettings').click();
    const colors = await page.locator('#wtSettingsSection .sectionTitle').first().evaluate(el => {
      const pageStyle = getComputedStyle(el.closest('.wt-page'));
      const textStyle = getComputedStyle(el);
      return { background: getComputedStyle(document.querySelector('.fixture-host')).backgroundColor, pageColor: pageStyle.color, text: textStyle.color };
    });
    expect(contrastRatio(colors.text, colors.background)).toBeGreaterThanOrEqual(4.5);
    expect(contrastRatio(colors.pageColor, colors.background)).toBeGreaterThanOrEqual(4.5);
    await page.screenshot({ path: path.join(artifactDir, `desktop-${theme}-settings.png`), fullPage: true });
  }
  await open(page, 'admin-member', 'dark', 390, 1100);
  await page.locator('#wtTabSettings').click();
  await expect(page.locator('#wtSettingsSection')).toBeVisible();
  await page.screenshot({ path: path.join(artifactDir, 'mobile-settings-dark-390.png'), fullPage: true });
});

test('Token 状态读取失败仍允许清除，保存失败清空草稿并保留错误', async ({ page }) => {
  await open(page, 'token-read-fail', 'light', 1024, 900);
  await page.locator('#wtTabSettings').click();
  await expect(page.locator('#wtGitHubTokenStatus')).toContainText('可尝试清除');
  await expect(page.locator('#wtClearGitHubToken')).toBeEnabled();
  await page.locator('#wtClearGitHubToken').click();
  await page.locator('#wtConfirmSubmit').click();
  await expect(page.locator('#wtGitHubTokenStatus')).toContainText('Token 已清除');

  await open(page, 'token-save-fail', 'light', 1024, 900);
  await page.locator('#wtTabSettings').click();
  await page.locator('#wtGitHubToken').fill('ghp_fixture_secret');
  await page.locator('#wtSaveGitHubToken').click();
  await expect(page.locator('#wtGitHubTokenStatus')).toContainText('Token 保存失败');
  await expect(page.locator('#wtGitHubToken')).toHaveValue('');
});

test('Token 慢响应在 pause 或账号切换后不能写入新页面或保留明文', async ({ page }) => {
  await open(page, 'token-slow', 'light', 1024, 900);
  await page.locator('#wtTabSettings').click();
  await page.locator('#wtGitHubToken').fill('ghp_pause_secret');
  await page.locator('#wtSaveGitHubToken').click();
  await page.evaluate(() => window.__wtFixture.view.onPause());
  await page.waitForTimeout(1200);
  await expect(page.locator('#wtGitHubToken')).toHaveValue('');
  await expect(page.locator('#wtGitHubTokenStatus')).not.toContainText('Token 已保存');

  await page.evaluate(() => window.__wtFixture.view.onResume());
  await page.locator('#wtTabSettings').click();
  await page.locator('#wtGitHubToken').fill('ghp_account_secret');
  await page.locator('#wtSaveGitHubToken').click();
  const staleRoomAction = await page.locator('.wt-roomCard button[data-act="leave"]').elementHandle();
  const beforeAccountSwitch = await calls(page);
  await page.evaluate(() => window.__wtFixture.switchAccount('mock-other'));
  await page.evaluate(() => {
    document.querySelector('#wtCreateInvitation').click();
    document.querySelector('#wtInvitationAcceptCode').value = 'STALE-CODE';
    document.querySelector('#wtAcceptInvitation').click();
  });
  if (staleRoomAction) await staleRoomAction.evaluate(button => button.click());
  const immediateAfterAccountChange = await calls(page);
  const immediateNewCalls = immediateAfterAccountChange.slice(beforeAccountSwitch.length);
  expect(immediateNewCalls.filter(x => ['POST', 'DELETE'].includes(x.method))).toHaveLength(0);
  await page.waitForTimeout(1200);
  await expect(page.locator('#wtGitHubToken')).toHaveValue('');
  await expect(page.locator('#wtStatus')).toContainText('账号已变化');
  await expect(page.locator('.wt-roomCard')).toHaveCount(0);
  await expect(page.locator('#wtAdminSection')).toBeHidden();
  const afterAccountChange = await calls(page);
  const newCalls = afterAccountChange.slice(beforeAccountSwitch.length);
  expect(newCalls.filter(x => ['POST', 'DELETE'].includes(x.method))).toHaveLength(0);

  await page.evaluate(() => window.__wtFixture.view.onResume());
  await expect.poll(() => page.evaluate(() => document.querySelector('.wt-page').inert)).toBe(false);
  await expect(page.locator('#wtRooms')).toHaveAttribute('aria-busy', 'false');
  await page.locator('#wtTabManage').click();
  await expect(page.locator('#wtAdminSection')).toBeVisible();
  await expect(page.locator('#wtUserA option')).toHaveCount(3);
});

test('确认放弃后再次进入设置，第二次 dirty 离页仍会拦截', async ({ page }) => {
  await open(page, 'save-fail', 'light', 390, 1000);
  await page.locator('#wtTabSettings').click();
  const checkbox = page.locator('#wtPauseOtherOnPlaybackStop');
  await expect(checkbox).toBeEnabled();
  await page.locator('label.emby-toggle-label:has(#wtPauseOtherOnPlaybackStop)').click();
  await expect(checkbox).not.toBeChecked();
  await page.locator('#wtTabMine').click();
  await expect(page.locator('#wtConfirmOverlay')).toBeVisible();
  await page.locator('#wtConfirmSubmit').click();
  await expect(page.locator('#wtConfirmOverlay')).toBeHidden();
  await expect(page.locator('#wtTabMine')).toHaveAttribute('aria-current', 'page');

  await page.locator('#wtTabSettings').click();
  await expect(checkbox).toBeEnabled();
  await page.locator('label.emby-toggle-label:has(#wtPauseOtherOnPlaybackStop)').click();
  await expect(checkbox).not.toBeChecked();
  await page.locator('#wtTabMine').click();
  await expect(page.locator('#wtConfirmOverlay')).toBeVisible();
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

test('Recovering 状态只针对临时掉线参与者，主用户缺失仍显示 Waiting', async ({ page }) => {
  await open(page, 'recovering', 'dark', 390, 1400);
  await expect(page.locator('#wtRefresh')).toHaveText('刷新');
  const refreshMetrics = await page.locator('#wtRefresh').evaluate(button => ({ width: button.getBoundingClientRect().width, textWidth: button.scrollWidth }));
  expect(refreshMetrics.textWidth).toBeLessThanOrEqual(refreshMetrics.width);
  await expect(page.locator('.wt-roomState')).toHaveText('正在等待临时掉线的参与者恢复');
  await expect(page.locator('.wt-roomState')).toHaveClass(/wt-roomState-recovering/);
  await expect(page.locator('.wt-roomStateDescription')).toContainText('临时掉线');
  await page.screenshot({ path: path.join(artifactDir, 'mobile-recovering-room-390.png'), fullPage: true });

  await open(page, 'primary-missing', 'light', 390, 1000);
  await expect(page.locator('.wt-roomState')).toHaveText('等待参与者');
  await expect(page.locator('.wt-roomState')).not.toHaveClass(/wt-roomState-recovering/);
  const narrowMetrics = await page.evaluate(() => ({ width: document.documentElement.scrollWidth, client: document.documentElement.clientWidth }));
  expect(narrowMetrics.width).toBeLessThanOrEqual(narrowMetrics.client + 1);
});

test('TransientRecovery 和 Drift 诊断清洗后展示并导出，390px 无溢出', async ({ page }) => {
  await open(page, 'recovering', 'dark', 390, 1800);
  await page.locator('.wt-roomCard button[data-act="more"]').click();
  await page.locator('.wt-moreMenu button[data-act="diagnostics"]').click();
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
  await page.locator('.wt-roomCard button[data-act="more"]').click();
  await page.locator('.wt-moreMenu button[data-act="diagnostics"]').click();
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
    expect(values.background).toBe('rgba(0, 0, 0, 0)');
    expect(values.color).not.toBe('');
    expect(await page.locator('link[rel="stylesheet"][href*="http"]').count()).toBe(0);
    if (theme === 'dark') await page.screenshot({ path: path.join(artifactDir, 'desktop-dark-active-room.png'), fullPage: true });
  }
});

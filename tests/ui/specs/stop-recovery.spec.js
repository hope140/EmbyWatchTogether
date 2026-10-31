const { test, expect } = require('@playwright/test');

test('停止后大位置差明确请求确认，成员操作说明视频切换，诊断保留安全决策', async ({ page }) => {
  await page.goto('/?scenario=active&theme=light');
  await expect(page.locator('.wt-roomCard')).toHaveCount(1);
  await page.evaluate(() => {
    const fixture = window.__wtFixture;
    const room = fixture.getRooms()[0];
    room.State = 'Waiting';
    room.StatusReason = 'stop_resume_confirmation_required';
    room._wtState.Sessions[0].PositionTicks = 0;
    room._wtState.Sessions[1].PositionTicks = 160000000;
    room._wtState.Sessions[1].IsPaused = true;
    const original = ApiClient.getJSON;
    ApiClient.getJSON = path => original(path).then(value => {
      if (!path.endsWith('/Diagnostics')) return value;
      return Object.assign({}, value, { StatusReason: room.StatusReason, Events: [
        { Type: 'primary_transition_waiting', Result: 'pending' },
        { Type: 'primary_transition_expired', Result: 'expired' },
        { Type: 'stop_resume_confirmation_required', Result: 'pending' },
        { Type: 'handoff_primary_pause_requested', Result: 'pending' },
        { Type: 'handoff_primary_paused', Result: 'success' },
        { Type: 'handoff_primary_hold_interrupted', Result: 'changed' },
        { Type: 'primary_transition_expired private-event', Result: 'expired private-result' }
      ] });
    });
    fixture.setRooms([room]);
    fixture.view.view._wtRefreshVisible();
  });
  await expect(page.locator('.wt-roomCard')).toContainText('停止后的进度差较大');
  await expect(page.locator('.wt-roomCard')).toContainText('必要时会切换视频');
  expect(await page.evaluate(() => window.__wtFixture.apiCalls.filter(x => x.method === 'POST').length)).toBe(0);
  await page.getByRole('button', { name: '请求重新同步', exact: true }).click();
  await expect.poll(() => page.evaluate(() => window.__wtFixture.apiCalls.filter(x => x.method === 'POST' && x.path.endsWith('/Resync')).length)).toBe(1);
  await page.locator('button[data-act="diagnostics"]').click();
  await expect(page.locator('.wt-diagnostics')).toContainText('换片等待远控条件恢复');
  await expect(page.locator('.wt-diagnostics')).toContainText('换片观察窗口已过期');
  await expect(page.locator('.wt-diagnostics')).toContainText('停止后等待确认恢复');
  await expect(page.locator('.wt-diagnostics')).toContainText('请求主用户暂停等待');
  await expect(page.locator('.wt-diagnostics')).toContainText('主用户已暂停等待');
  await expect(page.locator('.wt-diagnostics')).toContainText('主用户操作取消换片等待');
  const download = page.waitForEvent('download');
  await page.locator('.wt-diagnosticToolbar button').click();
  const stream = await (await download).createReadStream();
  let exported = '';
  for await (const chunk of stream) exported += chunk.toString();
  const data = JSON.parse(exported);
  expect(data.events.map(x => x.type)).toEqual(['observed', 'handoff_primary_hold_interrupted', 'handoff_primary_paused', 'handoff_primary_pause_requested', 'stop_resume_confirmation_required', 'primary_transition_expired', 'primary_transition_waiting']);
  expect(exported).not.toContain('private-');
});

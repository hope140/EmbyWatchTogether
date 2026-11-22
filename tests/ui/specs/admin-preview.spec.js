const { test, expect } = require('@playwright/test');
const adminUrl = '/?scenario=admin-manage#!configurationpage?name=WatchTogetherAdminV36';
async function ready(page) {
  await expect.poll(() => page.evaluate(() => window.__wtFixture.view.view._wtRoomsLoading)).toBe(false);
}
async function multi(page, stopPolling = true) {
  await page.evaluate(stop => {
    if (stop) clearInterval(window.__wtFixture.view.view._wtTimer);
    const room = {...window.__wtFixture.getRooms()[0], Playback: {
      ItemId: 'item-a', PrimaryPaused: false, OtherPaused: false, Percent: 57, ValidForMs: 10000
    }};
    window.__wtFixture.setRooms([room, {...room, RoomId: 'managed-2', Name: '第二个房间', PrimaryUserId: 'user-b', State: 'Waiting', StatusReason: 'waiting_for_playback', Playback: null}]);
    window.__wtFixture.refresh();
  }, stopPolling);
  await expect(page.locator('.wt-adminRoom')).toHaveCount(2);
}
for (const [lang,label,primary] of [['zh-CN','后台管理','主用户'],['zh-TW','後台管理','主使用者'],['en-US','Administration','Primary user']]) {
  test('后台轻量多房间、三语言、窄屏 '+lang, async ({ page }) => {
    await page.setViewportSize({width:375,height:812});
    await page.emulateMedia({reducedMotion:'reduce'});
    await page.goto('/?scenario=admin-manage&locale='+lang);
    await expect(page.locator('#wtRoomsHeading')).toContainText(lang==='en-US'?'My rooms':lang==='zh-TW'?'我的房間':'我的房间');
    await expect(page.locator('#wtRooms [data-room-id]')).toHaveCount(0);
    await page.locator('.tabs-viewmenubar a[href*="AdminV36"]').click();
    await expect(page.locator('#wtRoomsHeading')).toHaveText(label);
    await multi(page);
    const owners = page.locator('.wt-cardOwner');
    await expect(owners).toHaveCount(2);
    await expect(owners.first()).toContainText(primary);
    await expect(owners.first()).toContainText('hope');
    await expect(owners.last()).toBeVisible();
    await expect(owners.last()).toContainText('qsm');
    await expect(page.locator('.wt-cardPeople').first()).not.toContainText(primary);
    await expect(page.locator('progress').first()).toHaveAccessibleName(/hope/);
    await expect(page.locator('progress').last()).toBeHidden();
    await expect(page.locator('.wt-cardFilm').first()).toContainText('山海之间');
    await expect(page.locator('.wt-cardWithImage')).toHaveCount(1);
    await expect(page.locator('.wt-cardWaiting').first()).toBeHidden();
    await expect(page.locator('.wt-cardWaiting').last()).toBeVisible();
    await expect(page.locator('.wt-cardWaiting').last()).toHaveAttribute('aria-hidden', 'true');
    await expect(page.locator('progress').first()).toHaveAttribute('value','57');
    expect(await page.evaluate(() => window.__wtFixture.apiCalls.filter(x => /\/State$|\/Diagnostics$/.test(x.path)))).toEqual([]);
    expect(await page.evaluate(() => window.__wtFixture.apiCalls.filter(x => x.path.includes('/Items/')).length)).toBe(1);
    expect(await page.locator('.wt-adminRoom button').count()).toBe(2);
    expect(await page.evaluate(() => window.__wtFixture.avatarCalls.length)).toBe(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({path:test.info().outputPath('admin-'+lang+'.png'),fullPage:true});
    await page.setViewportSize({width:812,height:375});
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}
test('普通用户直访后台回退本人房间，无全站请求',async({page})=>{
  await page.goto('/?scenario=active#!configurationpage?name=WatchTogetherAdminV36');
  await expect(page.locator('.wt-roomCard')).toHaveCount(1);
  await expect(page.locator('.tabs-viewmenubar a[href*="Admin"]')).toHaveCount(0);
  await expect(page.locator('.wt-adminRoom')).toHaveCount(0);
  expect(await page.evaluate(()=>window.__wtFixture.apiCalls.filter(x=>x.scope==='all'))).toEqual([]);
});
test('权限未确认不读取房间或显示后台',async({page})=>{
  await page.addInitScript(()=>{let api;Object.defineProperty(window,'ApiClient',{get:()=>api,set:value=>{api=value;api.getCurrentUser=()=>Promise.reject({status:503});}});});
  await page.goto(adminUrl);
  await expect(page.locator('#wtStatus')).toContainText('当前用户权限读取失败');
  expect(await page.evaluate(()=>window.__wtFixture.apiCalls.filter(x=>x.path==='WatchTogether/Rooms'))).toEqual([]);
  await expect(page.locator('.wt-adminRoom')).toHaveCount(0);
});
test('自动刷新失败保留后台列表，连接恢复后重新显示媒体',async({page})=>{
  await page.goto(adminUrl);await multi(page, false);
  await expect(page.locator('[data-act=diagnostics]')).toHaveCount(0);
  await expect(page.locator('.wt-cardWithImage')).toHaveCount(1);
  await page.evaluate(()=>window.__wtFixture.setRoomsFailure(true));
  await expect(page.locator('.wt-cardWithImage')).toHaveCount(0);
  await expect(page.locator('#wtRoomsStatus')).toContainText('当前列表可能不是最新');
  await expect(page.locator('.wt-adminRoom')).toHaveCount(2);
  await page.locator('.wt-adminRoom').first().locator('[data-act=delete]').click();
  await expect(page.getByRole('alertdialog')).toHaveCount(0);
  await page.evaluate(()=>window.__wtFixture.setRoomsFailure(false));
  await expect(page.locator('#wtRoomsStatus')).toBeHidden();
});
test('结束确认房间和双方，失败可重试，重复结束合理提示',async({page})=>{
  await page.goto(adminUrl);await multi(page);
  await page.evaluate(()=>{const fetch=ApiClient.fetch;let calls=0;ApiClient.fetch=o=>o.type==='DELETE'?(++calls===1?Promise.reject({status:503}):Promise.resolve({Deleted:false})):fetch(o);});
  const button=()=>page.locator('.wt-adminRoom').first().locator('[data-act=delete]');
  await button().click();await expect(page.getByRole('alertdialog')).toContainText('管理员长列表房间');
  await expect(page.getByRole('alertdialog')).toContainText('hope / qsm');
  await page.locator('[data-confirm=cancel]').click();
  await button().click();await page.locator('[data-confirm=ok]').click();
  await expect(page.locator('.wt-roomFeedback').first()).toContainText('结束失败');
  await expect(button()).toBeEnabled();await expect(page.locator('.wt-adminRoom')).toHaveCount(2);
  await button().click();await page.locator('[data-confirm=ok]').click();
  await expect.poll(()=>page.evaluate(()=>window.__wtFixture.toasts)).toContain('房间已结束或已由其他管理员结束');
});
for (const change of ['account','server','pause']) test('全站列表迟到和旧确认隔离 '+change,async({page})=>{
  await page.goto(adminUrl);await multi(page);
  await page.locator('.wt-adminRoom').first().locator('[data-act=delete]').click();
  await expect(page.getByRole('alertdialog')).toBeVisible();
  await page.evaluate(change=>{
    const fixture=window.__wtFixture;
    const get=ApiClient.getJSON;
    ApiClient.getJSON=path=>path.startsWith('WatchTogether/Rooms?')?new Promise(resolve=>{window.finishAdminRead=()=>resolve(fixture.getRooms());}):get(path);
    fixture.refresh();
    window.changeAdminIdentity=()=>{
      if(change==='account')fixture.switchAccount('user-b');
      if(change==='server')fixture.switchServer('https://other.invalid');
      if(change==='pause')fixture.view.onPause();
      fixture.refresh();
    };
  },change);
  await expect.poll(()=>page.evaluate(()=>typeof window.finishAdminRead)).toBe('function');
  await page.evaluate(()=>{window.changeAdminIdentity();window.finishAdminRead();});
  await page.locator('[data-confirm=ok]').click();
  await expect(page.locator('.wt-adminRoom')).toHaveCount(0);
  expect(await page.evaluate(()=>window.__wtFixture.apiCalls.filter(x=>x.method==='DELETE'))).toEqual([]);
  expect(await page.evaluate(()=>window.__wtFixture.view.view._wtRooms)).toBeNull();
});
test('全站读取403清除已有后台数据和权限',async({page})=>{
  await page.goto(adminUrl);await multi(page);
  await page.evaluate(()=>{const get=ApiClient.getJSON;ApiClient.getJSON=path=>path.includes('Scope=all')?Promise.reject({status:403}):get(path);window.__wtFixture.refresh();});
  await expect(page.locator('.wt-adminRoom')).toHaveCount(0);
  await expect(page.locator('.tabs-viewmenubar a[href*="Admin"]')).toHaveCount(0);
});

test('双端状态、未知进度、切集和到期不显示旧媒体', async ({ page }) => {
  await page.goto(adminUrl);
  await multi(page);
  const card = page.locator('.wt-adminRoom').first();
  await expect(card.locator('.wt-cardFilm')).toContainText('山海之间');
  async function change(state, playback) {
    await page.evaluate(({state, playback}) => {
      const fixture = window.__wtFixture;
      const rooms = fixture.getRooms();
      fixture.setRooms([{...rooms[0], State: state, Playback: playback}, rooms[1]]);
      fixture.refresh();
    }, {state, playback});
  }
  const paused = {ItemId:'item-a', PrimaryPaused:true, OtherPaused:false, Percent:null, ValidForMs:10000};
  await change('Watching', paused);
  await expect(card.locator('.wt-cardStatus')).toContainText('主用户已暂停 · 对方播放中');
  await expect(card.locator('progress')).toBeHidden();
  await change('Handoff', null);
  await expect(card.locator('img')).toBeHidden();
  await expect(card.locator('.wt-cardWaiting')).toBeVisible();
  await expect(card.locator('.wt-cardFilm')).not.toContainText('山海之间');
  await change('Watching', {...paused, OtherPaused:true, Percent:0, ValidForMs:900});
  await expect(card.locator('.wt-cardStatus')).toHaveText('已暂停');
  await expect(card.locator('progress')).toHaveAttribute('value','0');
  await expect(card.locator('.wt-cardWaiting')).toBeHidden();
  await expect(card.locator('.wt-cardStatus')).toHaveText('状态暂不可用');
  await expect(card.locator('.wt-cardWaiting')).toBeVisible();
  await expect(card.locator('img')).toBeHidden();
});

test('屏外卡片延迟读取、同片复用、缺图保留片名且切集拒绝迟到数据', async ({page}) => {
  await page.setViewportSize({width: 375, height: 812});
  await page.goto(adminUrl);
  await expect(page.locator('.wt-adminRoom')).toHaveCount(1);
  await page.evaluate(() => {
    const f = window.__wtFixture;
    clearInterval(f.view.view._wtTimer);
    const base = f.getRooms()[0];
    const playback = {ItemId:'shared', PrimaryPaused:false, OtherPaused:false, Percent:57, ValidForMs:10000};
    f.setMedia('shared', {Name:'同一部电影', Type:'Movie', BackdropImageTags:['missing']});
    f.setRooms(Array.from({length:12}, (_, index) => ({...base, RoomId:'r-'+index,
      Playback:{...playback, ItemId:index === 11 ? 'outside' : 'shared'}})));
    f.refresh();
  });
  const first = page.locator('.wt-adminRoom').first();
  await expect(first.locator('h3')).toHaveText('同一部电影');
  await expect(first.locator('img')).toBeHidden();
  await expect(first.locator('.wt-cardWaiting')).toBeHidden();
  const reads = () => page.evaluate(() => window.__wtFixture.apiCalls.filter(x=>x.path.includes('/Items/')).map(x=>x.path));
  expect(await reads()).toEqual(['Users/admin-other/Items/shared']);
  await page.locator('.wt-adminRoom').last().scrollIntoViewIfNeeded();
  await expect.poll(reads).toContain('Users/admin-other/Items/outside');
  await first.scrollIntoViewIfNeeded();
  await page.evaluate(() => {
    const f = window.__wtFixture;
    f.setMediaDelay(500);
    f.setMedia('new-episode',{Name:'迟到的片名',Type:'Movie'});
    const rooms = f.getRooms();
    rooms[0] = {...rooms[0],Playback:{...rooms[0].Playback,ItemId:'new-episode'}};
    f.setRooms(rooms);
    f.refresh();
  });
  await expect.poll(reads).toContain('Users/admin-other/Items/new-episode');
  await page.evaluate(() => {
    const f = window.__wtFixture;
    const rooms = f.getRooms();
    rooms[0] = {...rooms[0],State:'Handoff',Playback:null};
    f.setRooms(rooms);
    f.refresh();
  });
  await expect(first.locator('h3')).not.toHaveText('同一部电影');
  await page.waitForTimeout(600);
  await expect(first.locator('h3')).not.toHaveText('迟到的片名');
  await expect(first.locator('img')).toBeHidden();
});

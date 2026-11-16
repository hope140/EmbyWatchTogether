const { test, expect } = require('@playwright/test');
async function open(page, mode='') {
  await page.goto('/?scenario=admin-member&select_host=legacy&toast_mode='+mode);
  await expect(page.locator('.wt-roomCard')).toHaveCount(1);
}
async function route(page, name) { await page.locator(`.tabs-viewmenubar a[href*="WatchTogether${name}V"]`).click(); }
async function form(page, mode='') {
 await open(page,mode);
 await page.evaluate(()=>window.__wtFixture.setRooms([]));
 await route(page,'Invite');
 await expect(page.locator('#wtCreate')).toBeEnabled();
}
test('空表单安静、点击缺项定位、字段校验与重置',async({page})=>{
 await form(page);
 await expect(page.locator('#wtRoomNameError')).toBeHidden();
 await page.locator('#wtUserB').selectOption('user-a');
 await expect(page.locator('#wtUserBError')).toContainText('必须不同');
 await page.locator('#wtCreate').click();
 await expect(page.locator('#wtUserB')).toBeFocused();
 expect(await page.evaluate(()=>window.__wtFixture.apiCalls.filter(x=>x.method==='POST'&&x.path==='WatchTogether/Rooms').length)).toBe(0);
 await page.locator('#wtUserB').selectOption('user-b');
 await page.locator('#wtCreate').click();
 await expect.poll(()=>page.evaluate(()=>window.__wtFixture.toasts)).toEqual(['房间已创建']);
 await expect(page.locator('#wtRoomName')).toHaveValue('');
 await expect(page.locator('#wtRoomNameError')).toBeHidden();
 await expect(page.locator('#wtUserBError')).toBeHidden();
});
for(const mode of ['default','missing','throw','reject']) test('保存成功隔离 toast '+mode,async({page})=>{
 await open(page,mode); await route(page,'Settings');
 await expect(page.locator('#wtSaveConfig')).toBeEnabled();
 await expect(page.locator('#wtConfigStatus')).toBeEmpty();
 await page.evaluate(()=>{window.__saves=0; const save=ApiClient.updatePluginConfiguration; ApiClient.updatePluginConfiguration=function(){window.__saves++;const args=arguments;return new Promise(resolve=>{window.__finishSave=()=>resolve(save.apply(this,args));});};});
 await page.locator('#wtSaveConfig').click();
 await expect.poll(()=>page.evaluate(()=>typeof window.__finishSave)).toBe('function');
 await expect(page.locator('#wtSaveConfig')).toHaveText('保存中…');
 await expect(page.locator('#wtSaveConfig')).toBeDisabled();
 await expect(page.locator('#wtConfigStatus')).toBeEmpty();
 await page.evaluate(()=>window.__finishSave());
 if(mode==='default') await expect.poll(()=>page.evaluate(()=>window.__wtFixture.toasts)).toEqual(['配置已保存']);
 else await expect(page.locator('#wtSuccessFallback')).toHaveText('配置已保存');
 await expect(page.locator('#wtConfigStatus')).toBeEmpty();
 expect(await page.evaluate(()=>window.__saves)).toBe(1);
});
test('迟到 toast 模块不越过页面代次',async({page})=>{
 await form(page,'slow'); await page.locator('#wtRoomName').fill('新房间'); await page.locator('#wtCreate').click();
 await expect.poll(()=>page.evaluate(()=>typeof window.__wtFixture.finishToast)).toBe('function');
 await route(page,'Rooms'); await page.evaluate(()=>window.__wtFixture.finishToast());
 await expect(page.locator('#wtSuccessFallback')).toBeEmpty();
 expect(await page.evaluate(()=>window.__wtFixture.toasts)).toEqual([]);
});
test('重复提交只发送一次，成功后后备提示不会覆盖其他错误',async({page})=>{
 await form(page,'missing'); await page.locator('#wtRoomName').fill('新房间');
 await page.evaluate(()=>{const fetch=ApiClient.fetch;ApiClient.fetch=o=>o.type==='POST'&&o.url==='WatchTogether/Rooms'?new Promise(resolve=>{window.__finish=()=>resolve(fetch(o));}):fetch(o);});
 await page.evaluate(()=>{document.querySelector('#wtCreate').click();document.querySelector('#wtCreate').dispatchEvent(new Event('click'));});
 await expect(page.locator('#wtCreate')).toBeDisabled();
 await page.evaluate(()=>{document.querySelector('#wtStatus').textContent='独立操作失败';window.__finish();});
 await expect(page.locator('#wtSuccessFallback')).toHaveText('房间已创建');
 await expect(page.locator('#wtStatus')).toHaveText('独立操作失败');
 expect(await page.evaluate(()=>window.__wtFixture.apiCalls.filter(x=>x.method==='POST'&&x.path==='WatchTogether/Rooms').length)).toBe(1);
});
for(const width of [390,1280]) test('字段提示排版 '+width,async({page})=>{
 await page.setViewportSize({width,height:900});await form(page);await page.locator('#wtUserB').selectOption('user-a');await page.locator('#wtCreate').click();
 await expect(page.locator('#wtUserBError')).toBeVisible();
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
 await page.screenshot({path:test.info().outputPath('form-'+width+'.png'),fullPage:true});
});
for (const change of ['account','server','pause']) test('toast 等待期间身份失效 '+change,async({page})=>{
 await form(page,'slow');await page.locator('#wtRoomName').fill('房间');await page.locator('#wtCreate').click();
 await expect.poll(()=>page.evaluate(()=>typeof window.__wtFixture.finishToast)).toBe('function');
 await page.evaluate(change=>{const f=window.__wtFixture;if(change==='account')f.switchAccount('another-user');if(change==='server')f.switchServer('https://changed.invalid');if(change==='pause')f.view.onPause();f.finishToast();},change);
 expect(await page.evaluate(()=>window.__wtFixture.toasts)).toEqual([]);
 await expect(page.locator('#wtSuccessFallback')).toBeEmpty();
});
test('冲突提示不把暂离当成解除关系，禁止请求',async({page})=>{
 await open(page);await route(page,'Invite');await page.locator('#wtRoomName').fill('冲突');await page.locator('#wtCreate').click();
 await expect(page.locator('#wtUserAError')).toContainText('暂离仍会保留成员关系');
 await expect(page.locator('#wtUserA')).toBeFocused();
 expect(await page.evaluate(()=>window.__wtFixture.apiCalls.filter(x=>x.method==='POST'&&x.path==='WatchTogether/Rooms').length)).toBe(0);
});
test('部分暂离失败保持可见且不报成功 toast',async({page})=>{
 await open(page);await page.evaluate(()=>{const fetch=ApiClient.fetch;ApiClient.fetch=o=>o.url.endsWith('/Leave')?fetch(o).then(()=>({PauseAttempted:1,PauseSucceeded:0,PauseFailed:1})):fetch(o);});
 await page.locator('[data-act=leave]').click();await page.locator('[data-confirm=ok]').click();
 await expect(page.locator('.wt-roomCard')).toContainText('暂停失败');
 expect(await page.evaluate(()=>window.__wtFixture.toasts)).toEqual([]);
});

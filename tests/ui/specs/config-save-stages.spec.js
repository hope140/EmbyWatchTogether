const { test, expect } = require('@playwright/test');
async function setup(page, mode) {
 await page.goto('/?scenario=admin-member');
 await page.locator('.tabs-viewmenubar a[href*="WatchTogetherSettingsV"]').click();
 await expect(page.locator('#wtSaveConfig')).toBeEnabled();
 await page.evaluate(mode=>{
  const get=ApiClient.getPluginConfiguration, put=ApiClient.updatePluginConfiguration;
  window.__saveOrder=[];let reads=0;
  ApiClient.getPluginConfiguration=function(){
   window.__saveOrder.push('read');reads++;
   if(mode==='initial-fail'&&reads===1)return Promise.reject({status:503});
   if(mode==='readback-fail'&&reads===2)return Promise.reject({status:503});
   if(mode==='hold-read'&&reads===1 || mode==='hold-readback'&&reads===2)return new Promise((resolve,reject)=>{window.__releaseSave=ok=>ok?resolve(get.apply(this,arguments)):reject({status:503});});
   return get.apply(this,arguments);
  };
  ApiClient.updatePluginConfiguration=function(){
   window.__saveOrder.push('submit');
   if(mode==='submit-fail')return Promise.reject({status:500});
   if(mode==='denied')return Promise.reject({status:403});
   if(mode==='uncertain')return put.apply(this,arguments).then(()=>{throw new TypeError('connection lost after commit');});
   if(mode==='hold-submit')return new Promise(resolve=>{window.__releaseSave=()=>resolve(put.apply(this,arguments));});
   return put.apply(this,arguments);
  };
 },mode);
}
for(const [mode,copy,order] of [
 ['initial-fail','保存前读取配置失败，尚未提交修改',['read']],
 ['submit-fail','配置提交请求失败，结果尚未确认，请重新读取检查。',['read','submit']],
 ['uncertain','配置提交请求失败，结果尚未确认，请重新读取检查。',['read','submit']],
 ['denied','配置提交被拒绝，请确认管理员权限后重新读取检查。',['read','submit']],
 ['readback-fail','配置已提交，但暂时无法确认，请重新读取检查。',['read','submit','read']]
]) test('保存阶段反馈 '+mode,async({page})=>{
 await setup(page,mode);await page.locator('#wtSaveConfig').click();
 await expect(page.locator('#wtConfigStatus')).toContainText(copy);
 await expect(page.locator('#wtSaveConfig')).toBeEnabled();
 expect(await page.evaluate(()=>window.__saveOrder)).toEqual(order);
 expect(await page.evaluate(()=>window.__wtFixture.toasts)).toEqual([]);
 await expect(page.locator('#wtConfigStatus')).not.toContainText('配置保存失败');
});
test('正常成功顺序不变且 busy 防重复',async({page})=>{
 await setup(page,'hold-submit');
 await page.evaluate(()=>{document.querySelector('#wtSaveConfig').click();document.querySelector('#wtSaveConfig').dispatchEvent(new Event('click'));});
 await expect.poll(()=>page.evaluate(()=>typeof window.__releaseSave)).toBe('function');
 await expect(page.locator('#wtSaveConfig')).toHaveText('保存中…');await expect(page.locator('#wtSaveConfig')).toBeDisabled();
 await expect(page.locator('#wtConfigStatus')).toBeEmpty();
 await page.evaluate(()=>window.__releaseSave());
 await expect.poll(()=>page.evaluate(()=>window.__wtFixture.toasts)).toEqual(['配置已保存']);
 expect(await page.evaluate(()=>window.__saveOrder)).toEqual(['read','submit','read']);
 await expect(page.locator('#wtSaveConfig')).toBeEnabled();
});
for(const mode of ['hold-read','hold-readback'])for(const change of ['pause','account','server'])test('保存迟到结果失效 '+mode+' '+change,async({page})=>{
 await setup(page,mode);await page.locator('#wtSaveConfig').click();
 await expect.poll(()=>page.evaluate(()=>typeof window.__releaseSave)).toBe('function');
 await page.evaluate(change=>{const f=window.__wtFixture;if(change==='pause')f.view.onPause();if(change==='account')f.switchAccount('other-user');if(change==='server')f.switchServer('https://other.invalid');window.__releaseSave(false);},change);
 await expect(page.locator('#wtConfigStatus')).not.toContainText('配置已提交');
 await expect(page.locator('#wtConfigStatus')).not.toContainText('保存前读取配置失败');
 expect(await page.evaluate(()=>window.__wtFixture.toasts)).toEqual([]);
 expect(await page.evaluate(()=>window.__saveOrder)).toEqual(mode==='hold-read'?['read']:['read','submit','read']);
});

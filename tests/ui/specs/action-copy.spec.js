const { test, expect } = require('@playwright/test');
for(const [scenario,verb,warning] of [['admin-member','结束','双方将退出房间。'],['admin-manage','删除','双方将退出房间。']])test(verb+'的确认、进行中与失败文字一致',async({page})=>{
 await page.goto('/?scenario='+scenario);await expect(page.locator('[data-act=delete]')).toBeVisible();
 await page.evaluate(()=>{const original=ApiClient.fetch;ApiClient.fetch=o=>o.type==='DELETE'?new Promise((resolve,reject)=>{window.__failDelete=()=>reject({status:503});}):original(o);});
 await page.locator('[data-act=delete]').click();
 await expect(page.getByRole('alertdialog')).toContainText(warning);
 await page.locator('[data-confirm=ok]').click();
 await expect(page.locator('.wt-roomFeedback')).toContainText(verb+'中…');
 await page.evaluate(()=>window.__failDelete());
 await expect(page.locator('.wt-roomFeedback')).toContainText(verb+'失败：');
 expect(await page.evaluate(()=>window.__wtFixture.toasts)).toEqual([]);
});
for(const [result,copy,failure] of [
 [{PauseAttempted:1,PauseSucceeded:1,PauseFailed:0},'已暂离，对方已暂停，可重新加入。',false],
 [{PauseAttempted:0,PauseSucceeded:0,PauseFailed:0},'已暂离，自动同步已停止；对方暂停未确认。',false],
 [{PauseAttempted:1,PauseSucceeded:0,PauseFailed:1},'已暂离，对方暂停失败，请检查客户端。',true]
])test('暂离结果保持区分 '+copy,async({page})=>{
 await page.goto('/?scenario=admin-member');await expect(page.locator('[data-act=leave]')).toBeVisible();
 await page.evaluate(result=>{const original=ApiClient.fetch;ApiClient.fetch=o=>o.url.endsWith('/Leave')?original(o).then(()=>result):original(o);},result);
 await page.locator('[data-act=leave]').click();await expect(page.getByRole('alertdialog')).toContainText('可重新加入，并尝试暂停对方。');await page.locator('[data-confirm=ok]').click();
 if(failure){await expect(page.locator('.wt-roomFeedback')).toHaveText(copy);expect(await page.evaluate(()=>window.__wtFixture.toasts)).toEqual([]);expect(await page.evaluate(()=>Object.keys(window.__wtFixture.view.view._wtRoomFeedbackTimers||{}))).toEqual([]);}
 else await expect.poll(()=>page.evaluate(()=>window.__wtFixture.toasts)).toEqual([copy]);
});
test('管理员删除成功短提示',async({page})=>{
 await page.goto('/?scenario=admin-manage');await page.locator('[data-act=delete]').click();await page.locator('[data-confirm=ok]').click();await expect.poll(()=>page.evaluate(()=>window.__wtFixture.toasts)).toEqual(['房间已删除']);
});

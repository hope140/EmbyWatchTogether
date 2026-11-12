const { test, expect } = require('@playwright/test');
async function invitePage(page, admin = false) {
  await page.goto('/?scenario=' + (admin ? 'admin-member' : 'empty'));
  await expect(page.locator('#wtRooms')).toHaveAttribute('aria-busy', 'false');
  if (admin) await page.evaluate(() => window.__wtFixture.setRooms([]));
  await page.locator('.tabs-viewmenubar a[href*="InviteV22"]').click();
  await expect(page.locator('#wtCreateInvitation')).toBeVisible();
}
for (const name of ['', '   ', '周末电影']) test('手动名称可选且保留已填值 '+JSON.stringify(name), async ({page}) => {
  await invitePage(page, true);
  await page.locator('#wtRoomName').fill(name);
  await page.locator('#wtCreate').click();
  await expect.poll(() => page.evaluate(() => window.__wtFixture.apiCalls.filter(x=>x.method==='POST' && x.path==='WatchTogether/Rooms').length)).toBe(1);
  expect(await page.evaluate(() => window.__wtFixture.apiCalls.find(x=>x.method==='POST' && x.path==='WatchTogether/Rooms').body.Name)).toBe(name.trim());
  await expect(page.locator('#wtRoomNameError')).toBeHidden();
  await expect(page.locator('#wtRoomName')).toHaveValue('');
});
for (const mode of ['missing','throw','reject','success']) test('复制独立于邀请码创建 '+mode, async({page})=>{
  await invitePage(page);
  await page.evaluate(mode=>Object.defineProperty(navigator,'clipboard',{configurable:true,value:mode==='missing'?undefined:{writeText:()=>{if(mode==='throw')throw new Error('denied');return mode==='reject'?Promise.reject(new Error('denied')):Promise.resolve();}}}),mode);
  await page.locator('#wtCreateInvitation').click();
  await expect(page.locator('#wtInvitationCode')).toHaveText('ABCD12EFghI3J4KLMNopQR');
  const before = await page.evaluate(()=>JSON.stringify(window.__wtFixture.view.view._wtInvitationCode));
  await page.locator('#wtCopyInvitationCode').click();
  if(mode==='success') await expect(page.locator('#wtCopyInvitationCode')).toContainText('已复制');
  else await expect(page.locator('#wtInvitationStatus')).toHaveText('自动复制失败，请手动复制邀请码。');
  expect(await page.evaluate(()=>JSON.stringify(window.__wtFixture.view.view._wtInvitationCode))).toBe(before);
  expect(await page.evaluate(()=>window.__wtFixture.apiCalls.filter(x=>x.method==='POST' && x.path==='WatchTogether/Invitations').length)).toBe(1);
  await expect(page.locator('#wtInvitationCode')).toHaveText('ABCD12EFghI3J4KLMNopQR');
});
test('迟到复制失败不污染切页后的提示',async({page})=>{
  await invitePage(page);
  await page.evaluate(()=>Object.defineProperty(navigator,'clipboard',{configurable:true,value:{writeText:()=>new Promise((_,reject)=>window.rejectCopy=reject)}}));
  await page.locator('#wtCreateInvitation').click();await page.locator('#wtCopyInvitationCode').click();
  await expect.poll(()=>page.evaluate(()=>typeof window.rejectCopy)).toBe('function');
  await page.locator('.tabs-viewmenubar a[href*="RoomsV22"]').click();
  await page.evaluate(()=>window.rejectCopy(new Error('late')));
  await expect(page.locator('#wtInvitationStatus')).not.toContainText('自动复制失败');
});

import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { withOfficeUi } from './lib/native-office-ui.mjs';

await withOfficeUi('account-menu-ui', async ({ output, url, invoke, openPage, require }) => {
  const admin = (await invoke('Login', { username:'oa-review', password:'Review-2026-Test' })).accessToken;
  await invoke('createUserAccount', { username:'self-password-ui', fullName:'业务部密码测试负责人', role:'OfficeEmployee',
    companyScope:'DEFAULT', departmentId:'GENERAL', isActive:true, resetPassword:'Original-Pass-2026', permissionGrants:[] }, {}, admin);
  const other = (await invoke('Login', { username:'self-password-ui', password:'Original-Pass-2026' })).accessToken;
  const page = await openPage('self-password-ui', 'Original-Pass-2026');
  const trigger = page.getByRole('button', { name:/^账号菜单：/u });
  const menu = page.getByRole('menu');
  assert.equal(await page.locator('.workspace-logout-button').count(), 0);
  await trigger.focus(); await page.keyboard.press('ArrowDown'); await menu.waitFor();
  await page.getByRole('menuitem', { name:'修改密码', exact:true }).waitFor();
  assert.equal(await page.evaluate(() => document.activeElement.textContent), '修改密码');
  await page.keyboard.press('End'); assert.equal(await page.evaluate(() => document.activeElement.textContent), '退出登录');
  await page.keyboard.press('Escape'); await menu.waitFor({ state:'hidden' });
  assert(await trigger.evaluate(element => element === document.activeElement));
  for (const width of [1440, 1180, 680, 390]) {
    await page.setViewportSize({ width, height:900 }); await trigger.click(); await menu.waitFor();
    const box = await menu.boundingBox(), anchor = await trigger.boundingBox();
    assert(box.x >= 0 && box.x + box.width <= width + 1, `Menu stays inside ${width}px`);
    assert(Math.abs(box.x + box.width - anchor.x - anchor.width) < 2, 'Menu aligns with account right edge');
    assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `No overflow at ${width}px`);
    if (width === 1440 || width === 390) await page.screenshot({ path:path.join(output, `account-menu-${width}.png`) });
    await page.keyboard.press('Escape');
  }
  await page.setViewportSize({ width:1440, height:1000 }); await trigger.click();
  const axe = require('axe-core'); await page.evaluate(axe.source);
  assert.deepEqual((await page.evaluate(() => window.axe.run(document.querySelector('.session-strip'), { runOnly:{ type:'tag', values:['wcag2a','wcag2aa'] } }))).violations, []);
  await page.getByRole('menuitem', { name:'我的权限来源', exact:true }).click();
  const sourceDialog = page.getByRole('dialog', { name:'当前身份与权限来源', exact:true });
  assert(!/受限功能|账号关闭|身份关闭|方案关闭|上级授权关闭|不会与其他单位身份合并/u.test(await sourceDialog.innerText()));
  for (const width of [1440, 680, 390]) {
    await page.setViewportSize({width,height:900});
    assert(await sourceDialog.evaluate(element => element.scrollWidth <= element.clientWidth + 1),`Permission dialog has no overflow at ${width}px`);
    assert.deepEqual((await page.evaluate(() => window.axe.run(document.querySelector('[role="dialog"]'), {runOnly:{type:'tag',values:['wcag2a','wcag2aa']}}))).violations,[]);
    await page.screenshot({path:path.join(output,`permission-source-${width}.png`)});
  }
  await sourceDialog.locator('summary').click();
  await sourceDialog.getByText('当前没有已授权操作。',{exact:true}).waitFor();
  await sourceDialog.getByRole('button', { name:'关闭窗口', exact:true }).click();
  await page.setViewportSize({width:1440,height:1000});
  const requests = []; page.on('request', request => { if (request.url().endsWith('/api/auth/password')) requests.push(request); });
  const openPassword = async () => { await trigger.click(); await page.getByRole('menuitem', { name:'修改密码', exact:true }).click(); };
  await openPassword();
  const dialog = page.getByRole('dialog', { name:'修改密码', exact:true });
  await dialog.getByRole('button', { name:'取消', exact:true }).click(); assert.equal(requests.length, 0);
  await openPassword();
  await dialog.getByLabel('原密码', { exact:true }).fill('Original-Pass-2026');
  await dialog.getByLabel('新密码', { exact:true }).fill('Changed-Pass-2026');
  await dialog.getByLabel('确认新密码', { exact:true }).fill('Different-Pass-2026');
  await dialog.getByRole('button', { name:'修改并重新登录', exact:true }).click();
  await dialog.getByText('两次输入的新密码不一致。', { exact:true }).waitFor(); assert.equal(requests.length, 0);
  await dialog.getByLabel('原密码', { exact:true }).fill('wrong');
  await dialog.getByLabel('确认新密码', { exact:true }).fill('Changed-Pass-2026');
  await dialog.getByRole('button', { name:'修改并重新登录', exact:true }).click();
  await dialog.getByText('原密码不正确。', { exact:true }).waitFor(); assert.equal(requests.length, 1);
  await dialog.getByLabel('原密码', { exact:true }).fill('Original-Pass-2026');
  const response = page.waitForResponse(response => response.url().endsWith('/api/auth/password') && response.status() === 200);
  await dialog.getByRole('button', { name:'修改并重新登录', exact:true }).click(); await response;
  await page.locator('input[autocomplete=username]').waitFor();
  await page.getByText('密码修改成功，请使用新密码重新登录。', { exact:true }).waitFor();
  const get = token => fetch(`${url}/api/auth/me`, { headers:{ authorization:`Bearer ${token}` } });
  assert.equal((await get(other)).status, 401); assert.equal((await get(admin)).status, 200);
  assert.equal((await fetch(`${url}/api/auth/password`, { method:'POST', headers:{ 'content-type':'application/json' }, body:JSON.stringify({ currentPassword:'a',newPassword:'Anonymous-2026' }) })).status, 401);
  const relogged = await openPage('self-password-ui', 'Changed-Pass-2026');
  await relogged.getByRole('button', { name:/^账号菜单：/u }).click();
  await relogged.getByRole('menuitem', { name:'退出登录', exact:true }).click();
  await relogged.locator('input[autocomplete=username]').waitFor();
  await invoke('createUserAccount', {username:'restricted-permission-ui',fullName:'权限范围测试',role:'OfficeEmployee',companyScope:'DEFAULT',departmentId:'GENERAL',
    isActive:true,resetPassword:'Restricted-Pass-2026',disabledModules:['office.purchase'],oaAssignedOnly:true},{},admin);
  const restricted = await openPage('restricted-permission-ui','Restricted-Pass-2026');
  await restricted.getByRole('button',{name:/^账号菜单：/u}).click();
  await restricted.getByRole('menuitem',{name:'我的权限来源',exact:true}).click();
  const restrictedDialog = restricted.getByRole('dialog',{name:'当前身份与权限来源',exact:true});
  await restrictedDialog.getByText('受限功能',{exact:true}).waitFor();
  await restrictedDialog.getByText('账号限制',{exact:true}).waitFor();
  assert.equal(await restrictedDialog.getByText('身份限制',{exact:true}).count(),0);
  await restrictedDialog.getByText('仅本人及明确参与的事项',{exact:true}).waitFor();
  await restrictedDialog.locator('summary').click();
  assert(await restrictedDialog.locator('.permission-source-operations li').count()>0);
  await restricted.screenshot({path:path.join(output,'permission-source-restricted.png')});
  fs.writeFileSync(path.join(output, 'summary.json'), JSON.stringify({ keyboard:true, responsive:true, accessibility:true, selfPassword:true, otherSessionsRevoked:true, unrelatedAccountPreserved:true,
    compactPermissionSource:true, realRestrictionsVisible:true }, null, 2));
});

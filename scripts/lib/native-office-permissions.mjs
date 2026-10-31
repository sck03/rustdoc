import assert from 'node:assert/strict';
import path from 'node:path';
import { delay } from './chromium-cdp.mjs';
import { captureScreenshot } from './web-runtime-browser-session.mjs';
import { verifyDesktopPayment } from './native-payment-workflow.mjs';
import { linkBusinessPerson } from './approval-fixtures.mjs';

export async function verifyDesktopOfficePermissions({ run, request, token, cdp, output, edition }) {
  const identity = { username: 'desktop-employee', fullName: '普通员工桌面验收', role: 'OfficeEmployee',
    permissionTemplateId: null, companyScope: 'DEFAULT', departmentId: 'GENERAL', isActive: true,
    resetPassword: 'Desktop-Employee-2026' };
  const account = await request('createUserAccount', token, identity);
  assert.equal(account.status, 200);
  const person = await linkBusinessPerson(async (id, body, parameters, accessToken) => {
    const result = await request(id, accessToken, body, parameters);
    assert.equal(result.status, 200, JSON.stringify(result.body));
    return result.body;
  }, token, account.body.user);
  const signIn = async () => {
    const deadline = Date.now() + 30000;
    while (Date.now() < deadline && !await run("!!document.querySelector('input[autocomplete=username]')")) await delay(100);
    for (const [field, value] of [['username', identity.username], ['current-password', identity.resetPassword]]) {
      await run(`document.querySelector('input[autocomplete="${field}"]').focus()`);
      await cdp.send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'a', code: 'KeyA', modifiers: 2, windowsVirtualKeyCode: 65 });
      await cdp.send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'a', code: 'KeyA', modifiers: 2, windowsVirtualKeyCode: 65 });
      await cdp.send('Input.insertText', { text: value });
    }
    await run("document.querySelector('button[type=submit]').click(); true");
    while (Date.now() < deadline && await run("!!document.querySelector('input[autocomplete=username]')")) await delay(100);
    assert(!await run("!!document.querySelector('input[autocomplete=username]')"), 'Employee React login succeeded');
    await delay(500);
  };
  await run("document.querySelector('.workspace-logout-button').click(); true");
  await signIn();
  assert.equal(await run('location.hash'), '#/office/approvals', 'Employee opens the office workspace');
  const text = await run('document.body.innerText');
  assert(text.includes('人员与组织') && text.includes('申请与资源'));
  const navigation = await run("document.querySelector('#workspace-primary-navigation').textContent");
  assert(navigation.includes('工作台') && !navigation.includes('付款报销打印') && !navigation.includes('报表模板管理'));
  assert(!text.includes('单证概览') && !text.includes('账号与权限') && !text.includes('人员档案'));
  await captureScreenshot(cdp, path.join(output, `${edition}-employee.png`));
  const permissionGrants = [['document.payments', ['view', 'operate']], ['document.payment-output', ['preview', 'print', 'export-pdf']],
    ['document.report-templates', ['view', 'design', 'clone']], ['document.report-resources', ['upload']]]
    .flatMap(([resourceKey, actions]) => actions.map(action => ({ resourceKey, action, dataScope: 'own' })));
  const updated = await request('updateUserAccount', token,
    { ...identity, resetPassword: '', expectedVersion: person.account.versionNumber, permissionGrants }, { id: account.body.user.id });
  assert.equal(updated.status, 200);
  // A permission change revokes the old session; reload and authenticate afresh.
  await run('location.reload(); true');
  await signIn();
  await run("location.hash='#/reports/templates/manage'; true");
  const templateDeadline = Date.now() + 30000;
  while (Date.now() < templateDeadline && !await run("!!document.querySelector('.template-select-field select')?.value")) await delay(100);
  assert(!await run("!!document.querySelector('option[value=ExportDocument]')"), 'Payment designer excludes export documents');
  const source = await run("document.querySelector('.template-select-field select').value");
  assert(source, 'Explicitly authorized payment template catalog loaded');
  const staff = await request('Login', '', { username: identity.username, password: identity.resetPassword });
  assert.equal(staff.status, 200);
  await verifyDesktopPayment({ run, request, staff: staff.body, cdp, output });
  const cloned = await request('CloneUserReportTemplate', staff.body.accessToken,
    { reportType: 'PaymentVoucher', name: '桌面私人付款模板', sourceTemplatePath: source });
  assert.equal(cloned.status, 201); assert.equal(cloned.body.shareScope, 'Private');
  await run(`location.hash='#/reports/templates?reportType=PaymentVoucher&userTemplateId=${cloned.body.id}'; true`);
  const canvasDeadline = Date.now() + 30000;
  while (Date.now() < canvasDeadline && !await run("!!document.querySelector('[data-v3-element-id]')")) await delay(100);
  assert(await run("!!document.querySelector('[data-v3-element-id]')"), 'Payment designer renders inside WebView2');
  await captureScreenshot(cdp, path.join(output, `${edition}-employee-designer.png`));
}

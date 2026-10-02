import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { CdpClient, delay } from './lib/chromium-cdp.mjs';
import { captureScreenshot, evaluate, getFreePort } from './lib/web-runtime-browser-session.mjs';
import { spawnProcessTree, stopProcessTree } from './lib/child-process-tree.mjs';
import { productEditionCatalog } from './lib/product-editions.mjs';
import { verifyDesktopPayment } from './lib/native-payment-workflow.mjs';

assert.equal(process.platform, 'win32', 'This gate exercises Windows WebView2 packages.');
const repo = path.resolve(import.meta.dirname, '..');
const packages = path.resolve(process.argv[2] || path.join(repo, 'artifacts/native-desktop'));
const output = path.join(repo, 'artifacts/edition-validation', `desktop-${Date.now()}`);
fs.mkdirSync(output, { recursive: true });
const openapi = JSON.parse(fs.readFileSync(path.join(repo, 'crates/export-doc-contracts/src/openapi.json'), 'utf8'));
const operations = new Map(Object.entries(openapi.paths).flatMap(([url, methods]) =>
  Object.entries(methods).filter(([, value]) => value.operationId).map(([method, value]) => [value.operationId, { url, method }])));
const results = [];
const catalog = { ...productEditionCatalog.editions, ...productEditionCatalog.localTestEditions };
const selected = process.argv[3] ? [process.argv[3]] : Object.keys(productEditionCatalog.editions);
assert(selected.every(edition => catalog[edition]), 'Unknown desktop test edition');
const withoutOcr = process.argv.includes('--without-ocr');

for (const edition of selected) {
  const name = `ExportDocManager.Tauri.${edition}`;
  const appRoot = path.join(packages, name);
  const marker = JSON.parse(fs.readFileSync(path.join(appRoot, 'exportdoc-native-package.json'), 'utf8'));
  assert.equal(marker.edition, edition);
  assert.equal(marker.ocr, !withoutOcr && catalog[edition].resourceProfile.ocr);
  if (edition === 'Full') { assert.equal(marker.localTest, true); assert.equal(marker.database, 'SQLite'); }
  assert.equal(fs.existsSync(path.join(appRoot, 'sidecar/ocr/exportdoc-ocr.exe')), marker.ocr);
  assert.equal(fs.existsSync(path.join(appRoot, 'Resources/ExcelTemplates/invoice-import-template.xlsx')), marker.documentResources);
  const port = await getFreePort();
  const child = spawnProcessTree(path.join(appRoot, 'ExportDocManager.exe'), ['--app-root', appRoot, '--data-root', path.join(output, edition)], {
    cwd: appRoot, windowsHide: true, stdio: 'ignore', env: { ...process.env,
      EXPORTDOCMANAGER_DESKTOP_SMOKE: '1', EXPORTDOCMANAGER_DESKTOP_TOKEN: 'isolated-edition-test',
      WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: `--remote-debugging-address=127.0.0.1 --remote-debugging-port=${port}` },
  });
  let cdp, failure;
  child.once('error', error => { failure = error; });
  const run = async expression => (await evaluate(cdp, expression, true)).value;
  try {
    const deadline = Date.now() + 60000;
    while (Date.now() < deadline) {
      if (failure) throw failure;
      assert.equal(child.exitCode, null, `${edition}: host exited before opening a window`);
      try {
        const pages = await (await fetch(`http://127.0.0.1:${port}/json/list`, { signal: AbortSignal.timeout(1000) })).json();
        const page = pages.find(page => page.type === 'page' && /tauri\.localhost/.test(page.url));
        if (page) { cdp = await CdpClient.connect(page.webSocketDebuggerUrl); break; }
      } catch { /* WebView may still be starting. */ }
      await delay(200);
    }
    assert(cdp, `${edition}: packaged WebView did not open`);
    while (Date.now() < deadline && !await run("!!document.querySelector('input[autocomplete=username]')")) await delay(100);
    assert(await run("!!document.querySelector('input[autocomplete=username]')"), `${edition}: login missing`);
    const context = await run("window.__TAURI_INTERNALS__.invoke('get_desktop_runtime_context')");
    assert.equal(context.productEdition, edition);
    const request = async (id, token, body, parameters = {}) => {
      const operation = operations.get(id);
      assert(operation, `Unknown contract operation: ${id}`);
      const route = operation.url.replace(/\{([^}]+)\}/gu, (_, key) => encodeURIComponent(parameters[key]));
      const response = await fetch(context.apiBaseUrl + route, {
        method: operation.method, signal: AbortSignal.timeout(15000),
        headers: { 'Content-Type': 'application/json', 'X-ExportDocManager-Desktop-Token': context.desktopAccessToken,
          ...(token ? { Authorization: `Bearer ${token}` } : {}) },
        ...(body ? { body: JSON.stringify(body) } : {}),
      });
      return { status: response.status, body: await response.json() };
    };
    const login = await request('Login', '', { username: 'admin', password: '' });
    assert.equal(login.status, 200);
    assert.equal(login.body.user.capabilities.productEdition, edition);
    const statuses = {};
    for (const [id, editions] of [
      ['ListInvoices', ['Document', 'Full']], ['GetCrmDashboard', ['Sales', 'Full']],
      ['ListPersonnel', ['Full']], ['ListGeneralRequest', ['Full']], ['ListUsers', ['Full']],
      ['ListAnnouncements', ['Full']], ['ListNotifications', ['Full']],
    ]) {
      statuses[id] = (await request(id, login.body.accessToken)).status;
      assert.equal(statuses[id], editions.includes(edition) ? 200 : 403, `${edition} ${id}`);
    }
    if (edition !== 'Full') {
      const unauthorized = await request('CreatePersonnel', login.body.accessToken, {});
      assert.equal(unauthorized.status, 403);
    }
    assert.equal(login.body.user.capabilities.canManageUsers, edition === 'Full');
    assert.equal(login.body.user.capabilities.usesOfficeRegister, true);
    await run("document.querySelector('input[autocomplete=username]').focus()");
    await cdp.send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'a', code: 'KeyA', modifiers: 2, windowsVirtualKeyCode: 65 });
    await cdp.send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'a', code: 'KeyA', modifiers: 2, windowsVirtualKeyCode: 65 });
    await cdp.send('Input.insertText', { text: 'admin' });
    await run("document.querySelector('button[type=submit]').click()");
    const loginDeadline = Date.now() + 30000;
    while (Date.now() < loginDeadline && await run("!!document.querySelector('input[autocomplete=username]')")) await delay(100);
    assert(!await run("!!document.querySelector('input[autocomplete=username]')"), `${edition}: React login failed`);
    await delay(500);
    const text = await run('document.body.innerText');
    const expectedHome = edition === 'Sales' ? '/crm/dashboard' : '/dashboard';
    assert.equal(await run('location.hash'), `#${expectedHome}`, `${edition}: fixed edition home`);
    assert(!text.includes('当前页面不可用'), `${edition}: first login must not show a permission redirect warning`);
    assert(text.includes(catalog[edition].displayName), `${edition}: wrong product title`);
    await captureScreenshot(cdp, path.join(output, `${edition}.png`));
    if (edition === 'Full') {
      assert(text.includes('人事管理') && text.includes('行政办公') && text.includes('公司公告') && text.includes('站内通知'));
      await run("location.hash='#/office/announcements'; true");
      const pageDeadline = Date.now() + 15000;
      while (Date.now() < pageDeadline && !await run("!!document.querySelector('[aria-label=公司公告]')")) await delay(100);
      assert(await run("!!document.querySelector('[aria-label=公司公告]')"), 'Full SQLite announcement page missing');
      await captureScreenshot(cdp, path.join(output, `${edition}-announcements.png`));
      const account = await request('createUserAccount', login.body.accessToken, {
        username: 'desktop-employee', fullName: '普通员工桌面验收', role: 'OfficeEmployee', permissionTemplateId: null,
        companyScope: 'DEFAULT', departmentId: 'GENERAL', isActive: true, resetPassword: 'Desktop-Employee-2026',
      });
      assert.equal(account.status, 200);
      await run("document.querySelector('.workspace-logout-button').click(); true");
      const staffDeadline = Date.now() + 30000;
      while (Date.now() < staffDeadline && !await run("!!document.querySelector('input[autocomplete=username]')")) await delay(100);
      for (const [field, value] of [['username', 'desktop-employee'], ['current-password', 'Desktop-Employee-2026']]) {
        await run(`document.querySelector('input[autocomplete="${field}"]').focus()`);
        await cdp.send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'a', code: 'KeyA', modifiers: 2, windowsVirtualKeyCode: 65 });
        await cdp.send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'a', code: 'KeyA', modifiers: 2, windowsVirtualKeyCode: 65 });
        await cdp.send('Input.insertText', { text: value });
      }
      await run("document.querySelector('button[type=submit]').click(); true");
      while (Date.now() < staffDeadline && await run("!!document.querySelector('input[autocomplete=username]')")) await delay(100);
      assert.equal(await run('location.hash'), '#/office/approvals', 'Employee opens the office workspace');
      await delay(500);
      const staffText = await run('document.body.innerText');
      assert(staffText.includes('人事管理') && staffText.includes('行政办公'));
      const staffNavigation = await run("document.querySelector('#workspace-primary-navigation').textContent");
      assert(staffNavigation.includes('工作台') && staffNavigation.includes('付款报销打印') && staffNavigation.includes('报表模板管理'));
      assert(!staffText.includes('单证概览') && !staffText.includes('账号与权限') && !staffText.includes('人员档案'));
      await captureScreenshot(cdp, path.join(output, `${edition}-employee.png`));
      await run("location.hash='#/reports/templates/manage'; true");
      const templateDeadline = Date.now() + 30000;
      while (Date.now() < templateDeadline && !await run("!!document.querySelector('.template-select-field select')?.value")) await delay(100);
      assert(!await run("!!document.querySelector('option[value=ExportDocument]')"), 'Employee designer excludes export documents');
      const source = await run("document.querySelector('.template-select-field select').value");
      assert(source, 'Employee payment template catalog loaded');
      const staff = await request('Login', '', { username: 'desktop-employee', password: 'Desktop-Employee-2026' });
      await verifyDesktopPayment({ run, request, staff: staff.body, cdp, output });
      const cloned = await request('CloneUserReportTemplate', staff.body.accessToken,
        { reportType: 'PaymentVoucher', name: '桌面私人付款模板', sourceTemplatePath: source });
      assert.equal(cloned.status, 201); assert.equal(cloned.body.shareScope, 'Private');
      await run(`location.hash='#/reports/templates?reportType=PaymentVoucher&userTemplateId=${cloned.body.id}'; true`);
      const canvasDeadline = Date.now() + 30000;
      while (Date.now() < canvasDeadline && !await run("!!document.querySelector('[data-v3-element-id]')")) await delay(100);
      assert(await run("!!document.querySelector('[data-v3-element-id]')"), 'Employee payment designer renders inside WebView2');
      await captureScreenshot(cdp, path.join(output, `${edition}-employee-designer.png`));
    } else { assert(!text.includes('人事管理') && !text.includes('行政办公') && !text.includes('账号与权限')); }
    results.push({ edition, home: expectedHome, ocr: marker.ocr, documentResources: marker.documentResources, statuses, screenshot: `${edition}.png` });
    await run("setTimeout(() => window.__TAURI_INTERNALS__.invoke('request_app_exit'), 100); true");
    const exitDeadline = Date.now() + 50000;
    while (child.exitCode === null && Date.now() < exitDeadline) await delay(100);
    assert.equal(child.exitCode, 0, `${edition}: graceful exit failed`);
    await assert.rejects(fetch(context.apiBaseUrl + '/health', { signal: AbortSignal.timeout(1000) }));
  } finally {
    cdp?.close();
    await stopProcessTree(child);
  }
}
fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify(results, null, 2));
console.log(`${selected.join(', ')} Windows desktop editions passed: ${output}`);

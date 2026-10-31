import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import { CdpClient, delay } from './lib/chromium-cdp.mjs';
import { captureScreenshot, evaluate, getFreePort } from './lib/web-runtime-browser-session.mjs';
import { spawnProcessTree, stopProcessTree } from './lib/child-process-tree.mjs';
import { productEditionCatalog } from './lib/product-editions.mjs';
import { verifyDesktopOfficePermissions } from './lib/native-office-permissions.mjs';
import { verifyDesktopDetailDesigner } from './lib/native-detail-designer.mjs';
import { verifyDesktopListPagination } from './lib/native-list-pagination.mjs';
import { verifyDesktopConditionalReports } from './lib/native-conditional-reports.mjs';
import { verifyDesktopBlankGrid } from './lib/native-blank-grid-designer.mjs';
import { verifyDesktopBlankDetails } from './lib/native-blank-detail-designer.mjs';
import { verifyDesktopBookingExport } from './lib/native-booking-export.mjs';
import { verifyDesktopBusinessAttachments } from './lib/native-business-attachments.mjs';
import { verifyDesktopOcr } from './lib/native-ocr-ui.mjs';
import { verifyDesktopEntryRows } from './lib/native-entry-print-rows.mjs';
import { verifyDesktopPersonnelDeletion } from './lib/native-personnel-deletion.mjs';
import { verifyDesktopMaintenance } from './lib/native-maintenance-workflow.mjs';

assert.equal(process.platform, 'win32', 'This gate exercises Windows WebView2 packages.');
const repo = path.resolve(import.meta.dirname, '..');
const packages = path.resolve(process.argv[2] || path.join(repo, 'artifacts/native-desktop'));
const output = path.resolve(process.env.EXPORTDOCMANAGER_DESKTOP_VALIDATION_OUTPUT_ROOT || path.join(repo, 'artifacts/edition-validation'), `desktop-${Date.now()}`);
fs.mkdirSync(output, { recursive: true });
const openapi = JSON.parse(fs.readFileSync(path.join(repo, 'crates/export-doc-contracts/src/openapi.json'), 'utf8'));
const operations = new Map(Object.entries(openapi.paths).flatMap(([url, methods]) =>
  Object.entries(methods).filter(([, value]) => value.operationId).map(([method, value]) => [value.operationId, { url, method, request: value.requestBody?.content?.['application/json']?.schema }])));
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
    assert.equal(await run('document.title'), catalog[edition].productName, `${edition}: login title matches the package`);
    await captureScreenshot(cdp, path.join(output, `${edition}-login.png`));
    const context = await run("window.__TAURI_INTERNALS__.invoke('get_desktop_runtime_context')");
    assert.equal(context.productEdition, edition);
    const sealBytes = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==', 'base64');
    const sealPath = path.join(output, `${edition}-seal.png`);
    fs.writeFileSync(sealPath, sealBytes);
    const sealResponse = await run(`(async()=>{const bytes=await window.__TAURI_INTERNALS__.invoke('read_exporter_seal_image_file',{path:${JSON.stringify(sealPath)}});return {binary:bytes instanceof ArrayBuffer,bytes:Array.from(new Uint8Array(bytes))};})()`);
    assert.equal(sealResponse.binary, true, 'seal IPC must return raw bytes');
    assert.deepEqual(sealResponse.bytes, [...sealBytes]);
    const mismatchedSeal = path.join(output, `${edition}-seal.jpg`);
    fs.writeFileSync(mismatchedSeal, sealBytes);
    const sealError = await run(`window.__TAURI_INTERNALS__.invoke('read_exporter_seal_image_file',{path:${JSON.stringify(mismatchedSeal)}}).then(()=>'',error=>String(error))`);
    assert.match(sealError, /扩展名与实际图片格式不一致/);
    const request = async (id, token, body, parameters = {}) => {
      const operation = operations.get(id);
      assert(operation, `Unknown contract operation: ${id}`);
      const schema = operation.request?.$ref ? openapi.components.schemas[operation.request.$ref.split('/').at(-1)] : operation.request;
      if (body && schema?.required?.includes('commandKey') && !body.commandKey) body = { ...body, commandKey: randomUUID() };
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
    assert(!Object.hasOwn(login.body.user.capabilities, "usesOfficeRegister"));
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
    const health = await request('getHealth', login.body.accessToken);
    assert.equal(health.status, 200);
    assert.equal(health.body.productVersion, marker.version, 'the package label must match the running Rust binary');
    await run("location.hash='#/system/about'; true");
    const aboutDeadline = Date.now() + 15000;
    while (Date.now() < aboutDeadline && !await run(`document.querySelector('.about-detail-grid')?.textContent.includes(${JSON.stringify(health.body.informationalVersion)})`)) await delay(100);
    assert(await run(`document.querySelector('.about-detail-grid')?.textContent.includes(${JSON.stringify('v' + marker.version)})`));
    assert(await run(`document.querySelector('.about-detail-grid')?.textContent.includes(${JSON.stringify(health.body.informationalVersion)})`));
    await captureScreenshot(cdp, path.join(output, `${edition}-about.png`));
    if (marker.ocr && !process.argv.includes('--blank-grid-only') && !process.argv.includes('--entry-rows-only')) await verifyDesktopOcr({ run, cdp, repo, output, context, token: login.body.accessToken, operations });
    if (process.argv.includes('--entry-rows-only')) {
      await verifyDesktopEntryRows({ run, request, token: login.body.accessToken, cdp, output });
      if (edition === 'Full') await verifyDesktopPersonnelDeletion({ run, request, token: login.body.accessToken, cdp, output });
    } else if (process.argv.includes('--blank-grid-only')) {
      await verifyDesktopBlankGrid({ run, request, token: login.body.accessToken, businessDate: login.body.user.businessDate, cdp, output });
    } else if (edition === 'Full') {
      assert(text.includes('人员与组织') && text.includes('申请与资源') && text.includes('公司公告') && text.includes('站内通知'));
      await verifyDesktopMaintenance({run,request,token:login.body.accessToken,businessDate:login.body.user.businessDate,cdp,output});
      await verifyDesktopListPagination({ run, request, token: login.body.accessToken, cdp, output });
      await verifyDesktopDetailDesigner({ run, request, token: login.body.accessToken, cdp, output });
      const invoice = await verifyDesktopConditionalReports({ run, request, token: login.body.accessToken, cdp, output });
      await verifyDesktopEntryRows({ run, request, token: login.body.accessToken, cdp, output });
      await verifyDesktopPersonnelDeletion({ run, request, token: login.body.accessToken, cdp, output });
      await verifyDesktopBlankDetails({ run, request, token: login.body.accessToken, invoice, cdp, output });
      await verifyDesktopBookingExport({ run, request, token: login.body.accessToken, invoice, cdp, output });
      await verifyDesktopBusinessAttachments({ run, request, token: login.body.accessToken, invoice, cdp, output });
      await verifyDesktopBlankGrid({ run, request, token: login.body.accessToken, businessDate: login.body.user.businessDate, cdp, output });
      await run("location.hash='#/office/announcements'; true");
      const pageDeadline = Date.now() + 15000;
      while (Date.now() < pageDeadline && !await run("!!document.querySelector('[aria-label=公司公告]')")) await delay(100);
      assert(await run("!!document.querySelector('[aria-label=公司公告]')"), 'Full SQLite announcement page missing');
      await captureScreenshot(cdp, path.join(output, `${edition}-announcements.png`));
      await verifyDesktopOfficePermissions({ run, request, token: login.body.accessToken, cdp, output, edition });
    } else { assert(!text.includes('人员与组织') && !text.includes('申请与资源') && !text.includes('账号与权限')); }
    results.push({ edition, home: expectedHome, version: health.body.productVersion, build: health.body.informationalVersion, ocr: marker.ocr, documentResources: marker.documentResources, statuses, screenshot: `${edition}.png` });
    await run("setTimeout(() => window.__TAURI_INTERNALS__.invoke('request_app_exit'), 100); true");
    const exitDeadline = Date.now() + 50000;
    while (child.exitCode === null && Date.now() < exitDeadline) await delay(100);
    assert.equal(child.exitCode, 0, `${edition}: graceful exit failed`);
    await assert.rejects(fetch(context.apiBaseUrl + '/health', { signal: AbortSignal.timeout(1000) }));
  } catch (error) {
    console.error(error);
    if (cdp) {
      await captureScreenshot(cdp, path.join(output, `${edition}-failure.png`)).catch(diagnostic => console.error('Failure screenshot unavailable:', diagnostic));
      const text = await run('document.body.innerText').catch(() => 'WebView did not respond.');
      fs.writeFileSync(path.join(output, `${edition}-failure.txt`), `${error.stack || error}\n${text}`);
    }
    throw error;
  } finally {
    cdp?.close();
    await stopProcessTree(child);
  }
}
fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify(results, null, 2));
console.log(`${selected.join(', ')} Windows desktop editions passed: ${output}`);

import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { withOfficeUi } from './lib/native-office-ui.mjs';

await withOfficeUi('file-storage-ui', async ({ output, url, invoke, operations, openPage, require }) => {
  const token = (await invoke('Login', { username: 'oa-review', password: 'Review-2026-Test' })).accessToken;
  const route = (name, parameters = {}) => operations.get(name).route.replace(/\{([^}]+)\}/gu, (_, key) => parameters[key]);
  const request = (name, parameters = {}, init = {}) => fetch(url + route(name, parameters), { ...init, headers: { authorization: `Bearer ${token}`, ...init.headers } });
  const initial = await invoke('GetFileStorage', undefined, {}, token);
  assert.equal(initial.healthy, true); assert.equal(initial.kind, 'local');
  const denied = await fetch(url + route('GetFileStorage')); assert.equal(denied.status, 401);
  assert.equal((await fetch(url + route('GetFileStoragePolicy'))).status, 401);
  await invoke('createUserAccount', { username: 'storage-reader', fullName: '容量策略验收', role: 'OfficeEmployee', companyScope: 'DEFAULT', departmentId: 'GENERAL', isActive: true, resetPassword: 'Reader-2026-Test', permissionGrants: [] }, {}, token);
  const reader = (await invoke('Login', { username: 'storage-reader', password: 'Reader-2026-Test' })).accessToken;
  assert.deepEqual(await invoke('GetFileStoragePolicy', undefined, {}, reader), initial.policy);
  const readerHeaders = { authorization: `Bearer ${reader}` };
  assert.equal((await fetch(url + route('GetFileStorage'), { headers: readerHeaders })).status, 403);
  assert.equal((await fetch(url + route('UpdateFileStoragePolicy'), { method: 'PUT', headers: { ...readerHeaders, 'content-type': 'application/json' }, body: JSON.stringify({ expectedVersion: initial.versionNumber, policy: initial.policy }) })).status, 403);
  let person = await invoke('CreatePersonnel', { requestKey: 'storage-ui', employeeNumber: 'STORAGE-UI', departmentId: 'GENERAL', jobTitle: '资料管理', employmentType: 'FullTime', hireDate: '2026-09-01', profile: { fullName: '附件分页验收' } }, {}, token);
  const pdf = index => Buffer.from(`%PDF-1.7\n% attachment ${index}\n1 0 obj<</Type/Catalog>>endobj\n%%EOF\n`);
  for (let index = 0; index < 55; index++) {
    const form = new FormData(); form.set('expectedVersion', String(person.versionNumber));
    form.set('file', new Blob([pdf(index)], { type: 'application/pdf' }), `合同-${index}.pdf`);
    const response = await request('UploadPersonnelAttachment', { id: person.employee.id }, { method: 'POST', body: form });
    assert.equal(response.status, 200, await response.clone().text()); person = await response.json();
  }
  assert.equal(person.attachmentCount, 55); assert.equal(person.attachments.length, 50);
  const first = await invoke('ListPersonnelAttachments', undefined, { id: person.employee.id }, token);
  const second = await invoke('ListPersonnelAttachments', undefined, { id: person.employee.id, after: first.nextCursor }, token);
  assert.equal(second.items.length, 5); assert.equal(new Set([...first.items, ...second.items].map(item => item.id)).size, 55);
  const file = first.items[0], parameters = { id: person.employee.id, attachmentId: file.id };
  const whole = Buffer.from(await (await request('DownloadPersonnelAttachment', parameters)).arrayBuffer());
  for (const [range, from, to] of [['bytes=0-7', 0, 8], ['bytes=-8', whole.length - 8, whole.length]]) {
    const response = await request('DownloadPersonnelAttachment', parameters, { headers: { Range: range } });
    assert.equal(response.status, 206); assert.deepEqual(Buffer.from(await response.arrayBuffer()), whole.subarray(from, to));
    assert.equal(response.headers.get('content-range'), `bytes ${from}-${to - 1}/${whole.length}`);
  }
  assert.equal((await request('DownloadPersonnelAttachment', parameters, { headers: { Range: 'bytes=99999-' } })).status, 416);
  const page = await openPage('oa-review', 'Review-2026-Test');
  await page.goto(`${url}/#/settings`);
  const panel = page.getByRole('region', { name: '文件与存储', exact: true });
  await panel.getByText('正常', { exact: true }).waitFor();
  await panel.locator('summary').filter({ hasText: '容量策略' }).click();
  await panel.getByLabel('单文件上限（MiB）', { exact: true }).fill('80');
  await panel.getByRole('button', { name: '保存容量策略', exact: true }).click();
  await panel.getByText('容量策略已保存。', { exact: true }).waitFor();
  assert.equal((await invoke('GetFileStoragePolicy', undefined, {}, token)).fileBytes, 80 * 1024 * 1024);
  await panel.locator('summary').filter({ hasText: '迁移存储' }).click();
  await panel.getByLabel('目标目录', { exact: true }).fill('AttachmentArchive');
  await panel.getByRole('button', { name: '核验并迁移', exact: true }).click();
  const deadline = Date.now() + 60000;
  let status;
  while (Date.now() < deadline) {
    status = await invoke('GetFileStorage', undefined, {}, token);
    if (status.directory === 'AttachmentArchive' && !status.migrationPending) break;
    await new Promise(resolve => setTimeout(resolve, 200));
  }
  assert.equal(status.directory, 'AttachmentArchive'); assert.equal(status.migrationPending, false);
  assert.deepEqual(Buffer.from(await (await request('DownloadPersonnelAttachment', parameters)).arrayBuffer()), whole);
  const marker = path.join(output, 'Data/AttachmentArchive/.store-id');
  const original = fs.readFileSync(marker);
  try {
    fs.writeFileSync(marker, 'wrong-store');
    assert.equal((await invoke('GetFileStorage', undefined, {}, token)).healthy, false);
    assert.equal((await request('DownloadPersonnelAttachment', parameters)).status, 503);
  } finally { fs.writeFileSync(marker, original); }
  await panel.getByRole('button', { name: '检查状态', exact: true }).click();
  await panel.getByText('正常', { exact: true }).waitFor();
  await page.evaluate(require('axe-core').source);
  for (const width of [1440, 390]) {
    await page.setViewportSize({ width, height: 950 });
    assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
    const violations = await page.evaluate(async () => (await window.axe.run('[aria-label="文件与存储"]')).violations);
    assert.deepEqual(violations.map(item => item.id), []);
    await page.screenshot({ path: path.join(output, `storage-${width}.png`), fullPage: true });
  }
});

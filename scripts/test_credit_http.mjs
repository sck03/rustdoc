import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { spawnProcessTree, stopProcessTree } from './lib/child-process-tree.mjs';
import { officeReviewExecutable, officeReviewOperations, officeReviewInvoke } from './lib/native-office-ui.mjs';
import { creditPdfFixtures } from './lib/credit-pdf-fixtures.mjs';

const repo = path.resolve(import.meta.dirname, '..');
assert(process.argv[2], 'Pass the governed AppRoot containing current OCR resources');
const app = path.resolve(process.argv[2]);
const output = path.join(repo, 'artifacts/credit-http', String(Date.now()));
fs.mkdirSync(output, { recursive: true });
const spec = JSON.parse(fs.readFileSync(path.join(repo, 'crates/export-doc-contracts/src/openapi.json')));
const server = spawnProcessTree(officeReviewExecutable(repo), [path.join(output, 'Data'), app], { cwd: repo, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
try {
  const url = await new Promise((resolve, reject) => {
    let log = '';
    const timer = setTimeout(() => reject(Error(`Fixture startup timed out: ${log}`)), 60000);
    server.once('error', error => { clearTimeout(timer); reject(error); });
    server.once('exit', code => { clearTimeout(timer); reject(Error(`Fixture exited ${code}: ${log}`)); });
    server.stderr.on('data', chunk => { log += chunk; });
    server.stdout.on('data', chunk => { log += chunk; const match = log.match(/\{"url":"([^"]+)"\}/u); if (match) { clearTimeout(timer); resolve(match[1]); } });
  });
  const invoke = officeReviewInvoke(url, spec, officeReviewOperations(spec));
  const admin = (await invoke('Login', { username: 'oa-review', password: 'Review-2026-Test' })).accessToken;
  for (const [name, grants] of [['credit-only', ['view', 'operate']], ['credit-denied', []]]) {
    await invoke('createUserAccount', { username: name, fullName: name, role: 'OfficeEmployee', companyScope: 'DEFAULT', departmentId: 'GENERAL', isActive: true,
      resetPassword: 'Credit-Review-2026', permissionGrants: grants.map(action => ({ resourceKey: 'document.invoices', action, dataScope: 'own' })) }, {}, admin);
  }
  const user = (await invoke('Login', { username: 'credit-only', password: 'Credit-Review-2026' })).accessToken;
  const denied = (await invoke('Login', { username: 'credit-denied', password: 'Credit-Review-2026' })).accessToken;
  const upload = (token, name, bytes, signal) => fetch(`${url}/api/tools/letter-of-credit/import-upload?fileName=${encodeURIComponent(name)}`, {
    method: 'POST', headers: { authorization: `Bearer ${token}`, 'content-type': 'application/octet-stream' }, body: bytes, signal,
  });
  assert.equal((await upload(denied, 'credit.txt', Buffer.from('credit text'))).status, 403);
  assert.equal((await upload(user, 'credit.txt', Buffer.from('credit text'))).status, 200, 'Invoice-only grant must suffice');
  assert.equal((await upload(user, 'credit.gif', Buffer.from('GIF89a'))).status, 400);
  const fixture = creditPdfFixtures()[0][1];
  const started = performance.now();
  let extractionDone = false;
  const extracting = upload(user, 'mixed.pdf', fixture, AbortSignal.timeout(600000)).then(async response => {
    assert.equal(response.status, 200, await response.clone().text());
    const result = await response.json(); extractionDone = true;
    assert(result.extractedText.includes('NATIVE CREDIT COVER') && result.extractedText.includes('INV-2026-001'));
  });
  const logins = [];
  for (let index = 0; index < 3; index++) {
    const start = performance.now();
    await invoke('Login', { username: 'oa-review', password: 'Review-2026-Test' });
    logins.push({ elapsedMs: Math.round(performance.now() - start), duringExtraction: !extractionDone });
  }
  await extracting;
  assert(logins.some(row => row.duringExtraction), 'The workload must overlap login');
  const controller = new AbortController();
  const cancelled = upload(user, 'cancel.pdf', fixture, controller.signal).catch(error => error);
  setTimeout(() => controller.abort(), 100);
  assert.equal((await cancelled).name, 'AbortError');
  const recoveryStarted = performance.now();
  let recovered;
  do {
    recovered = await upload(user, 'after-cancel.pdf', fixture, AbortSignal.timeout(600000));
    if (recovered.status !== 429) break;
    await new Promise(resolve => setTimeout(resolve, 100));
  } while (performance.now() - recoveryStarted < 10000);
  assert.equal(recovered.status, 200, await recovered.text());
  fs.writeFileSync(path.join(output, 'result.json'), JSON.stringify({ passed: true, logins, extractionAndRecoveryMs: Math.round(performance.now() - started) }, null, 2));
  console.log(`Credit HTTP permissions, concurrent logins, cancellation and recovery passed: ${output}`);
} finally { await stopProcessTree(server); }

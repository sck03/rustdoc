import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { officeReviewExecutable, officeReviewInvoke, officeReviewOperations } from './native-office-ui.mjs';
import { spawnProcessTree, stopProcessTree } from './child-process-tree.mjs';

export async function verifyS3RecoveryRestart(repo, expected) {
  const server = spawnProcessTree(officeReviewExecutable(repo), [expected.data], { cwd: repo, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  try {
    const url = await new Promise((resolve, reject) => {
      let log = '';
      const timer = setTimeout(() => reject(Error('Recovery restart timed out')), 60000);
      server.once('error', error => { clearTimeout(timer); reject(error); });
      server.once('exit', code => { clearTimeout(timer); reject(Error('Recovery restart exited ' + code)); });
      server.stdout.on('data', chunk => {
        log = (log + chunk).slice(-4096); const match = log.match(/\{"url":"([^"]+)"\}/u);
        if (match) { clearTimeout(timer); resolve(match[1]); }
      });
      server.stderr.resume();
    });
    const spec = await (await fetch(url + '/openapi/v1.json', { signal: AbortSignal.timeout(10000) })).json();
    const operations = officeReviewOperations(spec), invoke = officeReviewInvoke(url, spec, operations);
    await verifyRecoveredS3({ url, invoke, operations }, expected);
  } finally { await stopProcessTree(server); }
}

export async function verifyRecoveredS3({ url, invoke, operations }, expected) {
  const token = (await invoke('Login', expected.credentials)).accessToken;
  const storage = await invoke('GetFileStorage', undefined, {}, token);
  const remote = expected.reconnectTarget ?? expected.restoreTarget;
  assert.equal(storage.kind, remote ? 's3' : 'local');
  assert.equal(storage.healthy, true);
  if (remote) {
    for (const key of ['endpoint', 'bucket', 'prefix']) assert.equal(storage.s3[key], remote[key]);
  }
  const get = (route, credentials = token) => fetch(url + route, { signal: AbortSignal.timeout(60000), headers: { authorization: 'Bearer ' + credentials } });
  const route = (name, parameters = {}) => operations.get(name).route.replace(/\{([^}]+)\}/gu, (_, key) => parameters[key]);
  assert.equal((await get(route('GetFileStorage'), expected.oldToken)).status, 401);
  for (const file of expected.files) {
    const response = await get(route('DownloadPersonnelAttachment', { id: expected.person, attachmentId: file.id }));
    assert.equal(response.status, 200);
    assert.equal(createHash('sha256').update(Buffer.from(await response.arrayBuffer())).digest('hex'), file.hash);
  }
  assert.equal((await get(route('GetPersonnel', { id: expected.marker }))).status, 404);
  const person = await invoke('GetPersonnel', undefined, { id: expected.person }, token);
  const bytes = Buffer.from('%PDF-1.7\n% written after recovery\n%%EOF\n');
  const form = new FormData(); form.set('expectedVersion', String(person.versionNumber));
  form.set('file', new Blob([bytes], { type: 'application/pdf' }), '恢复后新增.pdf');
  const uploaded = await fetch(url + route('UploadPersonnelAttachment', { id: expected.person }), { method: 'POST', body: form, headers: { authorization: 'Bearer ' + token }, signal: AbortSignal.timeout(60000) });
  assert.equal(uploaded.status, 200, await uploaded.clone().text());
  const attachment = (await uploaded.json()).attachments.find(file => file.fileName === '恢复后新增.pdf');
  const downloaded = await get(route('DownloadPersonnelAttachment', { id: expected.person, attachmentId: attachment.id }));
  assert.equal(downloaded.status, 200); assert.deepEqual(Buffer.from(await downloaded.arrayBuffer()), bytes);
  expected.checks.push('restart restores database and attachment hashes and revokes old sessions');
  expected.checks.push('restored application accepts a new attachment and reads back identical bytes');
}

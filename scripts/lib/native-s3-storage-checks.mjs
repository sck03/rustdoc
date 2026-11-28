import assert from 'node:assert/strict';
import { createHash, randomUUID } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
const execute = promisify(execFile);
const hash = bytes => createHash('sha256').update(bytes).digest('hex');

export async function verifyExternalS3(fixture, target, alternate) {
  const { repo, url, invoke, operations, output } = fixture;
  const checks = [];
  const token = (await invoke('Login', { username: 'oa-review', password: 'Review-2026-Test' })).accessToken;
  const call = (name, body, parameters = {}) => invoke(name, body, parameters, token);
  const route = (name, parameters) => operations.get(name).route.replace(/\{([^}]+)\}/gu, (_, key) => parameters[key]);
  const request = (name, parameters, init = {}) => fetch(url + route(name, parameters), {
    ...init, signal: AbortSignal.timeout(60000), headers: { authorization: 'Bearer ' + token, ...init.headers },
  });
  const wait = async receipt => {
    assert(receipt.jobId, 'Expected a task receipt');
    for (let i = 0; i < 600; i++) {
      const job = await call('GetJob', undefined, { jobId: receipt.jobId });
      if (['Succeeded', 'Failed', 'Canceled'].includes(job.status)) {
        assert.equal(job.status, 'Succeeded', JSON.stringify(job)); return job;
      }
      await new Promise(resolve => setTimeout(resolve, 200));
    }
    throw Error('S3 task exceeded two minutes: ' + receipt.jobId);
  };
  const s3 = (remote, bucket = remote.bucket) => ({
    endpoint: remote.endpoint, region: remote.region ?? 'us-east-1', bucket,
    prefix: 'exportdoc-isolated/' + randomUUID(), accessKeyId: remote.accessKeyId, addressing: 'path',
  });
  // Race independent signed requests against the same new key. A successful PUT alone
  // cannot establish the compare-and-create semantics required by the recovery writer.
  const key = 'exportdoc-isolated/' + randomUUID();
  const contenders = await Promise.all(Array.from({ length: 12 }, async (_, index) => {
    const body = path.join(output, 'conditional-' + index + '.txt');
    fs.writeFileSync(body, String(index));
    const { stdout } = await execute(process.platform === 'win32' ? 'curl.exe' : 'curl', [
      '--silent', '--show-error', '--max-time', '30', '--aws-sigv4', 'aws:amz:' + (target.region ?? 'us-east-1') + ':s3',
      '--user', target.accessKeyId + ':' + target.secret, '--request', 'PUT', '--header', 'If-None-Match: *',
      '--data-binary', '@' + body, '--output', path.join(output, 'conditional-' + index + '.response'),
      '--write-out', '%{http_code}', target.endpoint.replace(/\/$/u, '') + '/' + target.backupBucket + '/' + key,
    ], { windowsHide: true, timeout: 35000 }).catch(() => { throw Error('Signed S3 request failed; check endpoint, credentials and curl SigV4 support.'); });
    return Number(stdout);
  }));
  assert.equal(contenders.filter(status => status === 200).length, 1, JSON.stringify(contenders));
  assert.equal(contenders.filter(status => status === 412).length, 11, JSON.stringify(contenders));
  checks.push('atomic create: one winner among twelve requests');

  let person = await call('CreatePersonnel', { requestKey: randomUUID(), employeeNumber: 'S3-' + randomUUID(),
    departmentId: 'GENERAL', jobTitle: '存储验收', employmentType: 'FullTime', hireDate: '2026-01-01', profile: { fullName: '隔离存储验收' } });
  const samples = [Buffer.from('%PDF-1.7\n% isolated attachment\n%%EOF\n'),
    Buffer.concat([Buffer.from('%PDF-1.7\n'), Buffer.alloc(10 * 1024 * 1024, 65), Buffer.from('\n%%EOF\n')]),
    fs.readFileSync(path.join(repo, 'tests/ReportTemplateFixtures/sample-seal.png'))];
  const files = [];
  const upload = async index => {
    const form = new FormData(); form.set('expectedVersion', String(person.versionNumber));
    const image = index === 2, name = '隔离附件-' + index + (image ? '.png' : '.pdf');
    form.set('file', new Blob([samples[index]], { type: image ? 'image/png' : 'application/pdf' }), name);
    const response = await request('UploadPersonnelAttachment', { id: person.employee.id }, { method: 'POST', body: form });
    assert.equal(response.status, 200, await response.clone().text()); person = await response.json();
    files.push({ id: person.attachments.find(file => file.fileName === name).id, hash: hash(samples[index]) });
  };
  const migrate = async remote => {
    await wait(await call('StartFileStorageMigration', { expectedVersion: (await call('GetFileStorage')).versionNumber,
      kind: 's3', s3: s3(remote), secret: remote.secret, token: '' }));
    const state = await call('GetFileStorage'); assert.equal(state.kind, 's3'); assert.equal(state.healthy, true);
  };
  const verify = async () => {
    for (const [index, file] of files.entries()) {
      const parameters = { id: person.employee.id, attachmentId: file.id };
      const response = await request('DownloadPersonnelAttachment', parameters);
      assert.equal(response.status, 200); assert.equal(hash(Buffer.from(await response.arrayBuffer())), file.hash);
      const range = await request('DownloadPersonnelAttachment', parameters, { headers: { Range: 'bytes=1-31' } });
      assert.equal(range.status, 206); assert.deepEqual(Buffer.from(await range.arrayBuffer()), samples[index].subarray(1, 32));
    }
  };
  await upload(0); await migrate(target); await upload(1); await upload(2); await verify();
  checks.push('local migration, PNG image, 10 MiB multipart upload, SHA256 and range download');
  if (alternate) { await migrate(alternate); await verify(); await migrate(target); await verify(); checks.push('cross-provider migration and return'); }
  const status = await call('GetBackupPlan');
  const password = 'Isolated-Recovery-' + randomUUID();
  await call('SaveBackupPlan', { expectedRevision: status.revision, expectedSettingsRevision: status.settingsRevision,
    plan: { ...status.plan, enabled: false, replicaMode: 'S3', s3: s3(target, target.backupBucket) },
    encryptionPassword: password, webDav: status.webDav, webDavPassword: null, s3SecretAccessKey: target.secret, s3SessionToken: '' });
  await wait(await call('RunBackupPlan'));
  const points = await call('ListRecoveryArchives'); assert(points.items.length > 0);
  const marker = await call('CreatePersonnel', { requestKey: randomUUID(), employeeNumber: 'AFTER-' + randomUUID(),
    departmentId: 'GENERAL', jobTitle: '恢复标记', employmentType: 'FullTime', hireDate: '2026-01-01', profile: { fullName: '恢复点之后' } });
  await wait(await call('StageRecoveryArchiveRestore', { password, adminPassword: 'Review-2026-Test', confirmationText: 'RECOVER', fileMode: 'Local' }, { id: points.items[0].id }));
  checks.push('encrypted S3 recovery point discovered and staged into local storage');
  return { data: path.join(output, 'Data'), person: person.employee.id, marker: marker.employee.id, files, oldToken: token, checks };
}

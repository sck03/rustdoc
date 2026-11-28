import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { officeReviewInvoke } from './native-office-ui.mjs';
import { postgresS3Host } from './native-s3-postgres-host.mjs';
import { isolateOnlineS3Routes } from './native-s3-network-checks.mjs';
import { verifyExternalS3 } from './native-s3-storage-checks.mjs';
import { verifyRecoveredS3 } from './native-s3-recovery-checks.mjs';

export async function verifyPostgresS3(repo, targets, index) {
  const routes = await isolateOnlineS3Routes(targets); let host;
  try {
    const target = routes.targets[index];
    host = await postgresS3Host(repo, target.name, index);
    const relativeTemplate = 'Templates/Export/s3-integrity.dtpl';
    const template = fs.readFileSync(path.join(repo, 'Templates/Export/invoice_template.dtpl'));
    fs.mkdirSync(path.dirname(path.join(host.source, relativeTemplate)), { recursive: true });
    fs.writeFileSync(path.join(host.source, relativeTemplate), template);
    const digest = bytes => createHash('sha256').update(bytes).digest('hex');
    const keyDigest = digest(fs.readFileSync(path.join(host.source, 'Security/native-master-key.bin')));
    const expected = await verifyExternalS3({ ...host, stageRecovery: false }, target, routes.targets.find(other => other !== target));
    await host.stop();
    await routes.close();
    for (const remote of routes.targets) await assert.rejects(fetch(remote.endpoint, { signal: AbortSignal.timeout(2000) }));
    assert(!fs.existsSync(path.join(host.target, 'Security/native-master-key.bin')));
    assert(!fs.existsSync(path.join(host.target, relativeTemplate)));
    const fixture = await host.start('TARGET', true);
    const session = await fetch(fixture.url + fixture.operations.get('OpenMaintenanceSession').route, {
      method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ token: host.bootstrap }), signal: AbortSignal.timeout(10000),
    });
    assert.equal(session.status, 200);
    const cookie = session.headers.get('set-cookie')?.split(';')[0]; assert(cookie);
    const headers = { cookie, 'X-Maintenance-Csrf': (await session.json()).csrfToken };
    const invoke = officeReviewInvoke(fixture.url, host.spec, fixture.operations, headers);
    const settle = async () => {
      const deadline = Date.now() + 180000;
      while (Date.now() < deadline) {
        const state = await invoke('GetMaintenanceStatus');
        if (!state.busy) return state;
        await new Promise(resolve => setTimeout(resolve, 200));
      }
      throw Error('Cold S3 recovery operation timed out');
    };
    const unlock = { repositoryId: '', locationId: '', password: expected.recoveryPassword, oldInstanceIsolated: true,
      sourceKind: 'S3', s3: expected.repository, secretAccessKey: target.secret, sessionToken: '' };
    const wrong = await fetch(fixture.url + fixture.operations.get('UnlockRecoveryRepository').route, {
      method: 'POST', headers: { ...headers, 'content-type': 'application/json' }, body: JSON.stringify({ ...unlock, password: 'Wrong-isolated-password' }), signal: AbortSignal.timeout(60000),
    });
    if (wrong.ok) assert.equal((await settle()).state, 'Failed');
    else assert.equal(wrong.status, 400);
    assert(!fs.existsSync(path.join(host.target, 'Security/native-master-key.bin')));
    await invoke('UnlockRecoveryRepository', unlock);
    const unlocked = await settle(); assert.equal(unlocked.state, 'Unlocked', JSON.stringify(unlocked));
    assert(unlocked.points.length > 0); assert.equal(unlocked.points[0].provider, 'PostgreSQL');
    await invoke('PreflightRecoveryPoint', { pointId: unlocked.points[0].id, fileMode: 'Local', targetLocation: '' });
    const preflight = await settle(); assert.equal(preflight.state, 'Ready', JSON.stringify(preflight));
    await invoke('ApplyRecoveryPoint', { planId: preflight.planId });
    const applied = await settle(); assert.equal(applied.state, 'Completed', JSON.stringify(applied));
    await host.stop();
    const recovered = await host.start('TARGET');
    await verifyRecoveredS3(recovered, expected);
    assert.equal(digest(fs.readFileSync(path.join(host.target, 'Security/native-master-key.bin'))), keyDigest);
    assert.deepEqual(fs.readFileSync(path.join(host.target, relativeTemplate)), template);
    expected.checks.push('PostgreSQL cold restore into an empty database and new DataRoot with online attachment routes disconnected',
      'wrong recovery password leaves target unchanged; master key and template restored from S3');
    const result = { provider: target.name, database: 'PostgreSQL', checks: expected.checks, passed: true };
    fs.writeFileSync(path.join(host.output, 's3-result.json'), JSON.stringify(result, null, 2));
    console.log('PostgreSQL S3 cold recovery passed: ' + host.output);
    return result;
  } finally { await host?.close(); await routes.close(); }
}

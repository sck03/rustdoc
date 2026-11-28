import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createHash, randomUUID } from 'node:crypto';
import { openS3RecoveryUi, verifyBackupIdentityUi, restoreS3ThroughUi } from './native-s3-recovery-ui.mjs';
import { postgresS3Host } from './native-s3-postgres-host.mjs';
import { isolateOnlineS3Routes } from './native-s3-network-checks.mjs';
import { verifyExternalS3 } from './native-s3-storage-checks.mjs';
import { verifyRecoveredS3 } from './native-s3-recovery-checks.mjs';

export async function verifyPostgresS3(repo, targets, index) {
  const routes = await isolateOnlineS3Routes(targets); let host, ui;
  try {
    const target = routes.targets[index];
    host = await postgresS3Host(repo, target.name, index);
    const relativeTemplate = 'Templates/Export/s3-integrity.dtpl';
    const template = fs.readFileSync(path.join(repo, 'Templates/Export/invoice_template.dtpl'));
    fs.mkdirSync(path.dirname(path.join(host.source, relativeTemplate)), { recursive: true });
    fs.writeFileSync(path.join(host.source, relativeTemplate), template);
    const digest = bytes => createHash('sha256').update(bytes).digest('hex');
    const expected = await verifyExternalS3({ ...host, stageRecovery: false }, target, routes.targets.find(other => other !== target));
    const keyDigest = digest(fs.readFileSync(path.join(host.source, 'Security/native-master-key.bin')));
    const sourceStorage = JSON.parse(fs.readFileSync(path.join(host.source, 'Configuration/file-storage.json'), 'utf8'));
    const reconnect = target.restoreMode === 'Reconnect';
    if (reconnect) expected.reconnectTarget = sourceStorage.backend.settings;
    else if (index > 0) expected.restoreTarget = { ...targets[0], prefix: 'exportdoc-isolated/restored-' + randomUUID(), addressing: 'path' };
    ui = await openS3RecoveryUi(repo, host.output);
    await verifyBackupIdentityUi(ui.page, host, expected);
    await host.stop();
    if (!reconnect) {
      await routes.close();
      for (const remote of routes.targets) await assert.rejects(fetch(remote.endpoint, { signal: AbortSignal.timeout(2000) }));
    }
    assert(!fs.existsSync(path.join(host.target, 'Security/native-master-key.bin')));
    assert(!fs.existsSync(path.join(host.target, relativeTemplate)));
    const fixture = await host.start('TARGET', true);
    await restoreS3ThroughUi(ui.page, fixture, expected, { secret: target.secret, token: host.bootstrap,
      afterRejected: () => assert(!fs.existsSync(path.join(host.target, 'Security/native-master-key.bin'))) });
    await host.stop();
    const recovered = await host.start('TARGET');
    await verifyRecoveredS3(recovered, expected);
    assert.equal(digest(fs.readFileSync(path.join(host.target, 'Security/native-master-key.bin'))), keyDigest);
    assert.deepEqual(fs.readFileSync(path.join(host.target, relativeTemplate)), template);
    if (reconnect) {
      const restoredStorage = JSON.parse(fs.readFileSync(path.join(host.target, 'Configuration/file-storage.json'), 'utf8'));
      assert.equal(restoredStorage.identity, sourceStorage.identity, 'Reconnect retains the original file-library identity');
      assert.deepEqual(restoredStorage.backend, sourceStorage.backend, 'Reconnect reuses the original S3 configuration and protected credentials');
    }
    assert.deepEqual(ui.errors, [], 'No unhandled errors in the real recovery UI');
    expected.checks.push(reconnect ? 'replace stopped application host with an empty PostgreSQL database and new DataRoot, reconnecting the intact original S3 library' : 'PostgreSQL cold restore into an empty database and new DataRoot with online attachment routes disconnected',
      reconnect ? 'retain original file-library identity, endpoint, bucket, prefix and protected credentials' : expected.restoreTarget ? 'rebuild attachments in a new S3 namespace on ' + expected.restoreTarget.name : 'rebuild attachments in local storage',
      'real React backup identity display and S3 unlock, preflight and apply',
      'wrong recovery password leaves target unchanged; master key and template restored from S3');
    const result = { provider: target.name, database: 'PostgreSQL', restoreMode: reconnect ? 'Reconnect' : expected.restoreTarget ? 'S3' : 'Local', checks: expected.checks, passed: true };
    fs.writeFileSync(path.join(host.output, 's3-result.json'), JSON.stringify(result, null, 2));
    console.log('PostgreSQL S3 cold recovery passed: ' + host.output);
    return result;
  } catch (error) {
    if (ui) {
      await ui.page.screenshot({ path: path.join(host.output, 'failure.png'), fullPage: true }).catch(() => {});
      fs.writeFileSync(path.join(host.output, 'failure-page.txt'), await ui.page.locator('body').ariaSnapshot().catch(() => 'Page unavailable'));
    }
    throw error;
  } finally { await ui?.browser.close(); await host?.close(); await routes.close(); }
}

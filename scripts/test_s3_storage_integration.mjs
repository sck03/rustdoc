import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { withOfficeUi } from './lib/native-office-ui.mjs';
import { verifyExternalS3 } from './lib/native-s3-storage-checks.mjs';
import { verifyS3RecoveryRestart } from './lib/native-s3-recovery-checks.mjs';
import { verifyPostgresS3 } from './lib/native-s3-postgres-checks.mjs';

assert.equal(process.env.EXPORTDOC_S3_TEST_ISOLATED, '1', 'Use dedicated empty test buckets and set EXPORTDOC_S3_TEST_ISOLATED=1');
const config = JSON.parse(fs.readFileSync(process.env.EXPORTDOC_S3_TEST_CONFIG, 'utf8'));
assert(Array.isArray(config.targets) && config.targets.length > 0, 'Provide targets');
for (const target of config.targets) {
  assert(/^[a-z0-9-]+$/u.test(target.name), 'Use a safe test name');
  const endpoint = new URL(target.endpoint);
  assert(['http:', 'https:'].includes(endpoint.protocol) && !endpoint.username && !endpoint.password && !endpoint.search && !endpoint.hash);
  for (const key of ['bucket', 'backupBucket', 'accessKeyId', 'secret']) assert(target[key], 'Missing ' + key);
  assert.notEqual(target.bucket, target.backupBucket, 'Use independent attachment and backup test buckets');
  assert(target.restoreMode === undefined || target.restoreMode === 'Reconnect', 'Optional restoreMode must be Reconnect');
  if (target.restoreMode) assert.equal(process.env.EXPORTDOC_S3_TEST_DATABASE, 'PostgreSQL', 'Reconnect validation uses the PostgreSQL cold-recovery runner');
}
const results = [];
for (const target of config.targets) {
  if (process.env.EXPORTDOC_S3_TEST_DATABASE === 'PostgreSQL') {
    results.push(await verifyPostgresS3(path.resolve(import.meta.dirname, '..'), config.targets, config.targets.indexOf(target)));
    continue;
  }
  let restored;
  await withOfficeUi('external-s3-' + target.name, async fixture => {
    restored = await verifyExternalS3(fixture, target, config.targets.find(other => other !== target));
  });
  await verifyS3RecoveryRestart(path.resolve(import.meta.dirname, '..'), restored);
  const result = { provider: target.name, checks: restored.checks, passed: true };
  fs.writeFileSync(path.join(path.dirname(restored.data), 's3-result.json'), JSON.stringify(result, null, 2));
  results.push(result);
}
console.log(JSON.stringify(results, null, 2));

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { verifyPostgresRestore, waitForJob } from './lib/native-docker-validation.mjs';

const contract = JSON.parse(readFileSync(new URL('../crates/export-doc-contracts/src/openapi.json', import.meta.url), 'utf8'));
const operations = new Map(Object.values(contract.paths).flatMap(methods =>
  Object.values(methods).filter(value => value.operationId).map(value => [value.operationId, value])));
for (const name of ['CreatePostgreSqlPhysicalBackup', 'RestorePostgreSqlPhysicalBackup']) {
  assert.equal(operations.get(name).responses['202'].content['application/json'].schema.$ref,
    '#/components/schemas/BackgroundJobSnapshot', `${name} must use the current asynchronous contract`);
}

function fixture(restoreStatus = 'Succeeded') {
  const state = { password: 'isolated-validation-password' };
  const calls = [];
  let backups = [];
  let units = [];
  let restorePolls = 0;
  const operation = async (name, { parameters = {}, body } = {}) => {
    calls.push(name);
    switch (name) {
      case 'ListPostgreSqlPhysicalBackups': return Response.json({ backups });
      case 'CreatePostgreSqlPhysicalBackup':
        return Response.json({ jobId: 'backup-job', status: 'Queued' }, { status: 202 });
      case 'CreateUnit':
        assert.equal(body.nameEN, 'Restore-backup-job');
        units = [{ id: 17 }];
        return Response.json(units[0]);
      case 'RestorePostgreSqlPhysicalBackup':
        assert.deepEqual(body, { backupFileName: 'isolated.dump', adminPassword: state.password, confirmationText: 'RESTORE DATABASE' });
        return Response.json({ jobId: 'restore-job', status: 'Queued' }, { status: 202 });
      case 'GetJob':
        if (parameters.jobId === 'backup-job') {
          backups = [{ fileName: 'isolated.dump' }];
          return Response.json({ jobId: 'backup-job', status: 'Succeeded' });
        }
        assert.equal(parameters.jobId, 'restore-job');
        assert.equal(state.afterBackupUnitId, undefined, 'Do not promote validation state before staging succeeds');
        restorePolls++;
        return Response.json({ jobId: 'restore-job', status: restorePolls === 1 ? 'Running' : restoreStatus, errorMessage: 'isolated restore failure' });
      case 'ListUnits': return Response.json(units);
      default: assert.fail(`Unexpected operation ${name}`);
    }
  };
  return { state, calls, operation, get restorePolls() { return restorePolls; }, applyRestore() { units = []; } };
}

const accepted = fixture();
await verifyPostgresRestore(accepted.operation, accepted.state, false);
assert.deepEqual(accepted.calls, [], 'The initial phase must not stage a restore');
await verifyPostgresRestore(accepted.operation, accepted.state, true);
assert.equal(accepted.restorePolls, 2, 'Wait for the accepted restore task to finish');
assert.equal(accepted.state.afterBackupUnitId, 17);
await assert.rejects(verifyPostgresRestore(accepted.operation, accepted.state, false), /Restore must remove data/u);
accepted.applyRestore();
await verifyPostgresRestore(accepted.operation, accepted.state, false);
assert.equal(accepted.calls.filter(name => name === 'RestorePostgreSqlPhysicalBackup').length, 1, 'Resume must verify persistence without staging again');

for (const status of ['Failed', 'Canceled']) {
  const rejected = fixture(status);
  await assert.rejects(verifyPostgresRestore(rejected.operation, rejected.state, true), new RegExp(status));
  assert.equal(rejected.state.afterBackupUnitId, undefined, `${status} restore must not advance validation state`);
}
for (const jobId of [undefined, '', ' ']) {
  await assert.rejects(waitForJob(() => assert.fail('An invalid task identifier must not be polled'), jobId), /task identifier/u);
}
console.log('Docker validation contracts passed: asynchronous restore, pending/failure/cancellation, task identity and persistence after offline restore.');

import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { fileURLToPath } from 'node:url';

export async function waitForJob(operation, jobId) {
  const deadline = Date.now() + 120000;
  while (Date.now() < deadline) {
    const job = await (await operation('GetJob', { parameters: { jobId } })).json();
    if (job.status === 'Succeeded') return job;
    if (['Failed', 'Canceled'].includes(job.status)) throw new Error(`Native file task ${job.status}`);
    await new Promise(resolve => setTimeout(resolve, 250));
  }
  throw new Error('Native file task timed out.');
}

export async function verifyPostgresRestore(operation, state, stageRestore) {
  if (state.afterBackupUnitId) {
    const units = await (await operation('ListUnits')).json();
    assert(!units.some(unit => unit.id === state.afterBackupUnitId), 'Restore must remove data created after the backup');
  }
  if (!stageRestore) return;
  const before = await (await operation('ListPostgreSqlPhysicalBackups')).json();
  const existing = new Set(before.backups.map(backup => backup.fileName));
  const backup = await (await operation('CreatePostgreSqlPhysicalBackup')).json();
  await waitForJob(operation, backup.jobId);
  const after = await (await operation('ListPostgreSqlPhysicalBackups')).json();
  const created = after.backups.filter(item => !existing.has(item.fileName));
  assert.equal(created.length, 1, 'Expected one new isolated PostgreSQL dump');
  const unit = await (await operation('CreateUnit', { body: { id: 0, nameEN: `Restore-${backup.jobId}`,
    nameCN: '恢复验证临时单位', code: 'RV', rowVersion: '' } })).json();
  assert(unit.id, 'Post-backup mutation must be saved');
  state.afterBackupUnitId = unit.id;
  const staged = await (await operation('RestorePostgreSqlPhysicalBackup', { body: {
    backupFileName: created[0].fileName, adminPassword: state.password, confirmationText: 'RESTORE DATABASE',
  } })).json();
  assert(staged.success && staged.restartRequired, 'PostgreSQL restore must be staged');
}

export function verifyComposeModel(model) {
  const { postgres, application, initialize, restore } = model.services;
  assert.equal(model.networks.database.internal, true, 'database network must be private');
  assert.deepEqual(Object.keys(postgres.networks), ['database']);
  assert.equal(postgres.ports?.length ?? 0, 0, 'PostgreSQL must not publish a host port');
  assert.deepEqual(postgres.volumes.filter(v => v.type === 'volume').map(v => [v.source, v.target]),
    [['postgres_data', '/var/lib/postgresql']]);
  assert.equal(application.depends_on.postgres.condition, 'service_healthy');
  assert.equal(application.depends_on.initialize, undefined, 'API startup must not repeat maintenance');
  assert.deepEqual(application.secrets.map(s => s.source).sort(), ['app_connection', 'bootstrap_token']);
  for (const [name, service] of Object.entries({ application, initialize, restore })) {
    assert.equal(service.image, application.image, `${name} must use the same program version`);
    assert.equal(service.read_only, true);
    assert.equal(service.volumes?.some(v => v.source === 'postgres_data') ?? false, false);
    if (name !== 'application') {
      assert.deepEqual(service.profiles, ['maintenance']);
      assert.equal(service.ports?.length ?? 0, 0);
      assert.deepEqual(Object.keys(service.networks), ['database']);
    }
  }
  assert.deepEqual(restore.secrets.map(s => s.source).sort(), ['app_connection', 'maintenance_connection']);
  assert.deepEqual(initialize.secrets.map(s => s.source), ['maintenance_connection']);
  for (const service of [application, restore]) {
    assert.deepEqual(service.volumes.filter(v => v.type === 'volume').map(v => [v.source, v.target]),
      [['app_data', '/var/lib/exportdoc']], 'API and restore must share managed files');
  }
}

export async function verifyDockerTopology(runtimeRoot) {
  const execute = promisify(execFile);
  const run = async args => (await execute('docker', args, {
    timeout: 30000, windowsHide: true, maxBuffer: 4 * 1024 * 1024,
    env: { ...process.env, NATIVE_RUNTIME_ROOT: runtimeRoot },
  })).stdout;
  const compose = ['compose', '--project-name', 'exportdoc-rust-native', '--file',
    fileURLToPath(new URL('../../deploy/rust-native/compose.yml', import.meta.url))];
  verifyComposeModel(JSON.parse(await run([...compose, '--profile', 'maintenance', 'config', '--format', 'json'])));
  const ids = (await run([...compose, 'ps', '--quiet', 'application', 'postgres'])).trim().split(/\s+/u);
  assert.equal(ids.length, 2, 'one API and one independent database container must be running');
  const containers = JSON.parse(await run(['inspect', ...ids]));
  const byService = new Map(containers.map(c => [c.Config.Labels['com.docker.compose.service'], c]));
  const postgres = byService.get('postgres');
  const application = byService.get('application');
  for (const container of [postgres, application]) assert.equal(container.State.Health.Status, 'healthy');
  assert(Object.values(postgres.NetworkSettings.Ports).every(bindings => !bindings?.length));
  const databaseNetwork = Object.keys(postgres.NetworkSettings.Networks);
  assert.equal(databaseNetwork.length, 1);
  const [network] = JSON.parse(await run(['network', 'inspect', databaseNetwork[0]]));
  assert.equal(network.Internal, true);
  assert(application.NetworkSettings.Networks[databaseNetwork[0]]);
  assert(!application.Mounts.some(m => /maintenance|bootstrap_password/u.test(m.Destination)));
  const databaseVolume = postgres.Mounts.find(m => m.Type === 'volume').Name;
  assert(!application.Mounts.some(m => m.Name === databaseVolume));
}

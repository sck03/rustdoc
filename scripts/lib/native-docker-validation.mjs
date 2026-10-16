import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { fileURLToPath } from 'node:url';
import { readFileSync } from 'node:fs';

export async function cleanupCompletedSetup(docker, compose, project) {
  for (const service of ['setup', 'initialize']) {
    const id = (await docker([...compose, 'ps', '--all', '--quiet', service])).trim();
    const [container] = JSON.parse(await docker(['inspect', id]));
    assert.equal(container.Name, `/${project}-${service}`);
    assert.equal(container.State.Status, 'exited');
    assert.equal(container.State.ExitCode, 0);
  }
  await docker([...compose, 'rm', '--force', 'setup', 'initialize']);
  const ids = (await docker([...compose, 'ps', '--all', '--quiet'])).trim().split(/\s+/u);
  const containers = JSON.parse(await docker(['inspect', ...ids]));
  assert.deepEqual(containers.map(item => item.Name).sort(), [`/${project}-application`, `/${project}-postgres`]);
  assert(containers.every(item => item.State.Health.Status === 'healthy'));
}

export async function verifyContainerOcr(request, endpoint) {
  assert(endpoint, 'The OCR upload contract must be present');
  const query = new URLSearchParams({ sourceName: 'skew_7deg.png', sourceMimeType: 'image/png' });
  const response = await request(`${endpoint.route}?${query}`, {
    method: endpoint.method, headers: { 'content-type': 'image/png' },
    body: readFileSync(new URL('../../tests/OcrFixtures/skew_7deg.png', import.meta.url)),
  }, 'Real container OCR inference');
  const result = await response.json();
  assert(result.fullText.includes('COMMERCIAL INVOICE') && result.fullText.includes('客户名称：示例贸易有限公司'));
  assert.equal(result.lines.length, 5);
}

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
  const prepared = Boolean(model.services.setup);
  assert.equal(model.networks.database.internal, true, 'database network must be private');
  assert.deepEqual(Object.keys(postgres.networks), ['database']);
  assert.equal(postgres.ports?.length ?? 0, 0, 'PostgreSQL must not publish a host port');
  assert.deepEqual(postgres.volumes.filter(v => v.type === 'volume' && v.source === 'postgres_data').map(v => [v.source, v.target]),
    [['postgres_data', '/var/lib/postgresql']]);
  if (prepared) {
    assert.equal(application.depends_on.initialize.condition, 'service_completed_successfully');
    assert.equal(initialize.depends_on.postgres.condition, 'service_healthy');
    assert.equal(postgres.depends_on.setup.condition, 'service_completed_successfully');
    assert.equal(model.services.setup.network_mode, 'none');
    assert.equal(application.environment.EXPORTDOCMANAGER_POSTGRES_MAINTENANCE_CONNECTION_FILE, undefined);
    const mounts = service => service.volumes.map(v => [v.type, v.target, Boolean(v.read_only)]).sort((a, b) => a[1].localeCompare(b[1]));
    assert.deepEqual(mounts(application), [['bind', '/run/exportdoc', true], ['bind', '/var/lib/exportdoc', false]]);
    assert.deepEqual(mounts(restore), [['bind', '/run/exportdoc', true], ['bind', '/run/exportdoc-maintenance', true], ['bind', '/var/lib/exportdoc', false]]);
    assert.deepEqual(mounts(initialize), [['bind', '/run/exportdoc-maintenance', true]]);
    for (const service of Object.values(model.services)) assert.equal(service.build, undefined, 'published deployment cannot require source files');
  } else {
    assert.equal(application.depends_on.postgres.condition, 'service_healthy');
    assert.equal(application.depends_on.initialize, undefined, 'script deployment starts API after maintenance');
    assert.deepEqual(application.secrets.map(s => s.source).sort(), ['app_connection', 'bootstrap_token']);
    assert.deepEqual(restore.secrets.map(s => s.source).sort(), ['app_connection', 'maintenance_connection']);
    assert.deepEqual(initialize.secrets.map(s => s.source), ['maintenance_connection']);
  }
  for (const [name, service] of Object.entries({ application, initialize, restore })) {
    assert.equal(service.image, application.image, `${name} must use the same program version`);
    assert.equal(service.read_only, true);
    assert.equal(service.volumes?.some(v => v.source === 'postgres_data') ?? false, false);
    if (name !== 'application') {
      if (name === 'restore' || !prepared) assert.deepEqual(service.profiles, ['maintenance']);
      assert.equal(service.ports?.length ?? 0, 0);
      assert.deepEqual(Object.keys(service.networks), ['database']);
    }
  }
  for (const service of [application, restore]) {
    const data = service.volumes.find(v => v.target === '/var/lib/exportdoc');
    assert(data && !data.read_only, 'API and restore require the same writable managed directory');
    assert.equal(data.type, prepared ? 'bind' : 'volume');
    assert.equal(data.source, application.volumes.find(v => v.target === '/var/lib/exportdoc').source);
  }
}

export async function verifyDockerTopology(runtimeRoot, composeArguments) {
  const execute = promisify(execFile);
  const run = async args => (await execute('docker', args, {
    timeout: 30000, windowsHide: true, maxBuffer: 4 * 1024 * 1024,
    env: { ...process.env, NATIVE_RUNTIME_ROOT: runtimeRoot },
  })).stdout;
  const compose = composeArguments ?? ['compose', '--project-name', 'exportdoc-rust-native', '--file',
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
  const databaseVolume = postgres.Mounts.find(m => m.Type === 'volume' && m.Destination === '/var/lib/postgresql').Name;
  assert(!application.Mounts.some(m => m.Name === databaseVolume));
}

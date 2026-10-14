import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { randomBytes, randomUUID, createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { spawnProcessTree, stopProcessTree } from './lib/child-process-tree.mjs';
import { cargoArtifactDirectory } from './lib/cargo-paths.mjs';
import { locateChromeForTesting } from './lib/chromium-executable.mjs';
import { startChrome } from './lib/web-runtime-browser-session.mjs';
import { waitForJob } from './lib/native-docker-validation.mjs';

// Only the existing PostgreSQL runner may supply this isolated, loopback database.
assert.equal(process.env.EXPORTDOC_MAINTENANCE_ISOLATED, '1', 'Use test-native-postgres.ps1 -Scope Maintenance');
const connection = process.env.EXPORTDOC_TEST_MAINTENANCE_UI_APP;
const maintenance = process.env.EXPORTDOC_TEST_MAINTENANCE_UI_MAINTENANCE;
for (const value of [connection, maintenance]) {
  assert(value && /(?:^|\s)host=127\.0\.0\.1(?:\s|$)/u.test(value)
    && /(?:^|\s)dbname=native_maintenance_ui(?:\s|$)/u.test(value), 'Expected a runner-owned maintenance database');
}
const repo = path.resolve(import.meta.dirname, '..');
const root = path.join(repo, '.codex-runtime/native-maintenance-ui', randomUUID());
const app = path.join(root, 'App');
let data = path.join(root, 'Data');
const configuration = path.join(root, 'Runtime');
for (const directory of [app, data, configuration]) fs.mkdirSync(directory, { recursive: true, mode: 0o700 });
const require = createRequire(path.join(repo, 'apps/export-doc-web/package.json'));
const { chromium } = require('playwright');
const executable = path.join(cargoArtifactDirectory(repo), 'export-doc-server' + (process.platform === 'win32' ? '.exe' : ''));
for (const name of ['Templates', 'Resources/Fonts/OpenSource', 'Resources/ExcelTemplates']) {
  fs.cpSync(path.join(repo, name), path.join(app, name), { recursive: true });
}
const bin = path.join(app, 'Tools/PostgreSQL/bin');
fs.mkdirSync(bin, { recursive: true });
for (const entry of fs.readdirSync(process.env.EXPORTDOC_TEST_PG_BIN, { withFileTypes: true })) {
  if (entry.isFile() && (/^(pg_dump|pg_restore|psql)(\.exe)?$/u.test(entry.name) || entry.name.endsWith('.dll'))) {
    fs.copyFileSync(path.join(process.env.EXPORTDOC_TEST_PG_BIN, entry.name), path.join(bin, entry.name));
  }
}
const bootstrap = randomBytes(32).toString('hex');
let password = 'Maintenance-' + randomBytes(16).toString('hex');
const sourcePassword = password;
for (const [name, value] of [['app.txt', connection], ['maintenance.txt', maintenance], ['bootstrap.txt', bootstrap]]) {
  fs.writeFileSync(path.join(configuration, name), value, { flag: 'wx', mode: 0o600 });
}
const environment = { ...process.env };
for (const key of [
  'EXPORTDOCMANAGER_POSTGRES_CONNECTION', 'EXPORTDOCMANAGER_POSTGRES_CONNECTION_FILE',
  'EXPORTDOCMANAGER_POSTGRES_MAINTENANCE_CONNECTION', 'EXPORTDOCMANAGER_POSTGRES_MAINTENANCE_CONNECTION_FILE',
  'EXPORTDOCMANAGER_BOOTSTRAP_TOKEN', 'EXPORTDOCMANAGER_BOOTSTRAP_TOKEN_FILE', 'EXPORTDOCMANAGER_MASTER_KEY',
]) delete environment[key];
Object.assign(environment, {
  EXPORTDOCMANAGER_POSTGRES_CONNECTION_FILE: path.join(configuration, 'app.txt'),
  EXPORTDOCMANAGER_BOOTSTRAP_TOKEN_FILE: path.join(configuration, 'bootstrap.txt'),
  EXPORTDOCMANAGER_POSTGRES_OWNER: 'native_owner',
  EXPORTDOCMANAGER_BUSINESS_TIME_ZONE: 'Asia/Shanghai',
});
const maintenanceEnvironment = {
  ...environment, EXPORTDOCMANAGER_POSTGRES_MAINTENANCE_CONNECTION_FILE: path.join(configuration, 'maintenance.txt'),
};
const common = () => ['--app-root', app, '--data-root', data];
const apiArguments = () => [...common(), '--web-root', path.join(repo, 'apps/export-doc-web/dist'), '--bind', '127.0.0.1:0', '--endpoint-file', path.join(data, 'endpoint.json')];
const execute = promisify(execFile);
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
let server, log, browser, chrome, page, origin, access, operations, runNumber = 0;
async function cli(arguments_, env = maintenanceEnvironment) {
  return execute(executable, arguments_, { cwd: repo, env, windowsHide: true, timeout: 120000, maxBuffer: 2 * 1024 * 1024 });
}
async function startApi() {
  const endpoint = path.join(data, 'endpoint.json');
  fs.rmSync(endpoint, { force: true });
  log = fs.createWriteStream(path.join(root, 'server-' + (++runNumber) + '.log'));
  server = spawnProcessTree(executable, apiArguments(), {
    cwd: repo, env: environment, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'],
  });
  server.stdout.pipe(log, { end: false });
  server.stderr.pipe(log, { end: false });
  const deadline = Date.now() + 60000;
  while (!fs.existsSync(endpoint) && Date.now() < deadline) {
    assert(server.exitCode === null && server.signalCode === null, 'API exited; inspect the isolated server log');
    await delay(100);
  }
  assert(fs.existsSync(endpoint), 'API startup timed out');
  origin = JSON.parse(fs.readFileSync(endpoint, 'utf8')).apiBaseUrl;
  assert.equal(new URL(origin).hostname, '127.0.0.1');
  const contract = await fetch(origin + '/openapi/v1.json', { signal: AbortSignal.timeout(10000) }).then(r => r.json());
  operations = new Map(Object.entries(contract.paths).flatMap(([route, methods]) =>
    Object.entries(methods).filter(([, operation]) => operation.operationId)
      .map(([method, operation]) => [operation.operationId, { route, method: method.toUpperCase() }])));
}
async function stopApi() {
  if (server) await stopProcessTree(server, 5000);
  server = undefined;
  if (log) await new Promise(resolve => log.end(resolve));
  log = undefined;
}
async function operation(name, { parameters = {}, body, headers = {} } = {}) {
  const endpoint = operations.get(name);
  assert(endpoint, 'Missing generated operation: ' + name);
  const route = endpoint.route.replace(/\{([^}]+)\}/gu, (_, key) => encodeURIComponent(parameters[key] ?? ''));
  const response = await fetch(new URL(route, origin), {
    method: endpoint.method, signal: AbortSignal.timeout(35000),
    headers: { ...(access ? { authorization: 'Bearer ' + access } : {}), 'content-type': 'application/json', ...headers },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!response.ok) throw new Error(name + ': ' + response.status + ' ' + await response.text());
  return response;
}
async function login(first = false) {
  await page.goto(origin);
  await page.locator('input[autocomplete=username]').fill('admin');
  await page.locator('input[autocomplete=current-password]').fill(password);
  if (first) {
    await page.locator('.login-connection-settings summary').click();
    await page.getByLabel('管理员初始化口令', { exact: true }).fill(bootstrap);
  }
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await page.locator('.login-submit-button').waitFor({ state: 'hidden' });
  access = (await (await operation('Login', { body: { username: 'admin', password } })).json()).accessToken;
  assert(access);
  await page.goto(origin + '/#/settings?section=postgresql');
  await page.getByRole('heading', { name: '数据库备份与外部文件恢复', exact: true }).waitFor();
}
async function createUnit(name) {
  return (await operation('CreateUnit', { body: { id: 0, nameEN: name, nameCN: '隔离恢复验证', code: 'RV', rowVersion: '' } })).json();
}
async function assertUnitAbsent(name) {
  const units = await (await operation('ListUnits')).json();
  assert(!units.some(unit => unit.nameEN === name), 'Restored database retained a post-backup mutation');
}
async function restoreOffline() {
  // A live API must prevent an independent maintenance process from restoring.
  await assert.rejects(cli([...common(), '--restore-pending']), error => error.code !== 0);
  await stopApi();
  await assert.rejects(cli(apiArguments(), environment), error => error.code !== 0 && /恢复/u.test(error.stderr));
  await cli([...common(), '--restore-pending']);
  await cli([...common(), '--restore-pending']); // Restarting completed maintenance is harmless.
  await startApi();
  await login();
}
async function downloadPackage(job, destination) {
  await waitForJob(operation, job.jobId);
  const issued = await operation('CreateJobDownloadTicket', { parameters: { jobId: job.jobId } });
  const cookie = issued.headers.get('set-cookie')?.split(';')[0];
  assert(cookie, 'Download must be bound to the authenticated session');
  const ticket = await issued.json();
  const url = new URL(ticket.downloadUrl, origin);
  assert.equal(url.origin, origin);
  const response = await fetch(url, { headers: { cookie }, signal: AbortSignal.timeout(35000) });
  assert(response.ok);
  fs.writeFileSync(destination, Buffer.from(await response.arrayBuffer()));
}
try {
  await cli([...common(), '--initialize-only']);
  await startApi();
  chrome = await startChrome({ browserExecutable: locateChromeForTesting(repo), userDataDir: path.join(root, 'Chrome'), timeoutMs: 30000 });
  browser = await chromium.connectOverCDP(chrome.browserWebSocketUrl);
  page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
  page.setDefaultTimeout(30000);
  await login(true);
  // Exercise the new plan through the real React client and PostgreSQL service.
  await page.getByRole('button', { name: '日常灾备', exact: true }).click();
  const schedule = page.locator('[aria-label="日常灾备"]');
  await schedule.getByLabel('启用日常灾备', { exact: true }).waitFor();
  assert.equal(await schedule.getByLabel('启用日常灾备', { exact: true }).isChecked(), false);
  await schedule.getByLabel('备份包密码', { exact: true }).fill('Scheduled-password-2026');
  await schedule.getByLabel('再次输入新密码', { exact: true }).fill('Scheduled-password-2026');
  await page.getByRole('button', { name: '保存灾备设置', exact: true }).click();
  await page.getByText('灾备设置已保存。', { exact: true }).waitFor();
  const started = page.waitForResponse(response => response.url().endsWith('/api/disaster-recovery/plan/run') && response.request().method() === 'POST');
  await schedule.getByRole('button', { name: '立即备份一次' }).click();
  const scheduledJob = await (await started).json();
  await waitForJob(operation, scheduledJob.jobId);
  const scheduledStatus = await (await operation('GetBackupPlan')).json();
  assert.equal(scheduledStatus.plan.enabled, false);
  assert.equal(scheduledStatus.lastResult, 'Succeeded');
  assert.equal(scheduledStatus.s3Available, true);
  assert.equal(scheduledStatus.archives.length, 1);
  const archive = scheduledStatus.archives[0];
  const issued = await operation('CreateRecoveryArchiveDownloadTicket', { parameters: { id: archive.id } });
  const archiveCookie = issued.headers.get('set-cookie')?.split(';')[0];
  const archiveTicket = await issued.json();
  assert.equal((await fetch(new URL(archiveTicket.downloadUrl, origin))).status, 404);
  const archiveDownload = await fetch(new URL(archiveTicket.downloadUrl, origin), { headers: { cookie: archiveCookie } });
  assert(archiveDownload.ok);
  assert(Buffer.from(await archiveDownload.arrayBuffer()).subarray(0, 22).toString().startsWith('EDM-SERVER-MIGRATION-2'));
  await createUnit('After scheduled archive');
  await page.getByRole('button', { name: '重新加载灾备设置', exact: true }).click();
  await page.locator('[aria-label="完整备份列表"]').getByRole('button', { name: '用于恢复' }).first().click();
  const archiveRestore = page.locator('[aria-label="从本地完整备份恢复"]');
  await archiveRestore.getByLabel('所选备份的密码').fill('Scheduled-password-2026');
  await archiveRestore.getByLabel('管理员当前密码').fill(password);
  await archiveRestore.getByLabel('恢复确认文本').fill('MIGRATE');
  const stagedArchive = page.waitForResponse(response => response.url().endsWith(`/api/disaster-recovery/archives/${archive.id}/restore`) && response.request().method() === 'POST');
  await archiveRestore.getByRole('button', { name: '校验并安排完整恢复' }).click();
  await waitForJob(operation, (await (await stagedArchive).json()).jobId);
  await page.screenshot({ path: path.join(root, 'scheduled-archive-restore.png'), fullPage: true });
  await restoreOffline();
  await assertUnitAbsent('After scheduled archive');
  const backup = page.locator('.maintenance-database-backup-panel');
  const recovery = page.locator('.maintenance-database-recovery-panel');
  await backup.getByRole('button', { name: '创建备份', exact: true }).click();
  await page.getByText('PostgreSQL 数据库备份已创建，请在列表下载并另存。', { exact: true }).waitFor();
  const downloaded = page.waitForEvent('download');
  await backup.getByRole('button', { name: /^下载 /u }).first().click();
  const dump = path.join(root, 'database.dump');
  await (await downloaded).saveAs(dump);
  assert.equal(fs.readFileSync(dump).subarray(0, 5).toString(), 'PGDMP');
  await createUnit('After database backup');
  await page.locator('details[aria-label="恢复数据库"] > summary').click();
  await recovery.locator('input[type=file]').setInputFiles(dump);
  await recovery.getByLabel('管理员当前密码').fill(password);
  await recovery.getByLabel('恢复确认文本', { exact: true }).fill('RESTORE DATABASE');
  await recovery.getByRole('button', { name: '恢复上传文件', exact: true }).click();
  await page.getByText('恢复已暂存。停止服务后运行 --restore-pending 维护命令，再启动服务。', { exact: true }).waitFor();
  assert(await page.locator('details[aria-label="服务器维护步骤"]').evaluate(element => element.open));
  await page.getByText(/docker compose run --rm --no-deps restore/u).waitFor();
  assert(await backup.getByRole('button', { name: '创建备份', exact: true }).isDisabled());
  await page.screenshot({ path: path.join(root, 'database-restore-pending.png'), fullPage: true });
  await restoreOffline();
  await assertUnitAbsent('After database backup');

  let template = path.join(data, 'Templates/Export/maintenance-fixture.dtpl');
  fs.mkdirSync(path.dirname(template), { recursive: true });
  const originalTemplate = fs.readFileSync(path.join(repo, 'Templates/Export/invoice_template.dtpl'));
  fs.writeFileSync(template, originalTemplate);
  const migration = page.locator('.maintenance-full-migration-panel');
  await page.getByRole('button', { name: '整机迁移', exact: true }).click();
  await migration.getByLabel('管理员当前密码').fill(password);
  await migration.getByLabel('新迁移包密码', { exact: true }).fill('Migration-password-2026');
  await migration.getByLabel('再次输入密码', { exact: true }).fill('Migration-password-2026');
  await migration.getByLabel('创建确认文本', { exact: true }).fill('MIGRATE');
  const migrationDownload = page.waitForEvent('download', { timeout: 120000 });
  await migration.getByRole('button', { name: '创建并下载迁移包', exact: true }).click();
  await (await migrationDownload).saveAs(path.join(root, 'created-in-browser.edmmigration'));
  // Existing API-created packages may have valid passwords weaker than the new
  // password suggestion. Restoring them must not apply the creation-only policy.
  const legacyPassword = '12345678';
  const job = await (await operation('CreateServerMigrationPackage', {
    body: { password: legacyPassword, adminPassword: password, confirmationText: 'MIGRATE' },
  })).json();
  const packageFile = path.join(root, 'valid-password.edmmigration');
  await downloadPackage(job, packageFile);
  const key = fs.readFileSync(path.join(data, 'Security/native-master-key.bin'));
  await createUnit('After migration snapshot');
  fs.writeFileSync(template, 'post-backup template mutation');
  // Move the same package to a different database and DataRoot, with a different
  // temporary administrator and master key, just as a new server deployment does.
  await stopApi();
  const targetApp = process.env.EXPORTDOC_TEST_MAINTENANCE_TARGET_APP;
  const targetMaintenance = process.env.EXPORTDOC_TEST_MAINTENANCE_TARGET_MAINTENANCE;
  for (const value of [targetApp, targetMaintenance]) {
    assert(value && /(?:^|\s)host=127\.0\.0\.1(?:\s|$)/u.test(value)
      && /(?:^|\s)dbname=native_maintenance_target(?:\s|$)/u.test(value), 'Expected the isolated migration target');
  }
  fs.writeFileSync(path.join(configuration, 'app.txt'), targetApp);
  fs.writeFileSync(path.join(configuration, 'maintenance.txt'), targetMaintenance);
  data = path.join(root, 'TargetData');
  fs.mkdirSync(data, { mode: 0o700 });
  password = 'Target-' + randomBytes(16).toString('hex');
  await cli([...common(), '--initialize-only']);
  await startApi();
  await login(true);
  await createUnit('Target only');
  template = path.join(data, 'Templates/Export/maintenance-fixture.dtpl');
  fs.mkdirSync(path.dirname(template), { recursive: true });
  fs.writeFileSync(template, 'target template');
  assert.notDeepEqual(fs.readFileSync(path.join(data, 'Security/native-master-key.bin')), key);
  await page.locator('details[aria-label="完整迁移与换机"] > summary').click();
  await migration.locator('input[type=file]').setInputFiles(packageFile);
  for (const candidate of ['wrong-password', legacyPassword]) {
    await migration.getByLabel('管理员当前密码').fill(password);
    await migration.getByLabel('迁移包密码', { exact: true }).fill(candidate);
    await migration.getByLabel('恢复确认文本', { exact: true }).fill('MIGRATE');
    const response = page.waitForResponse(r => new URL(r.url()).pathname === operations.get('StageServerMigrationRestore').route && r.request().method() === 'POST');
    await migration.getByRole('button', { name: '安排完整恢复', exact: true }).click();
    assert.equal((await response).status(), candidate === legacyPassword ? 200 : 400);
    if (candidate !== legacyPassword) {
      await page.getByText(/包密码错误/u).waitFor();
      assert.equal((await (await operation('GetServerMigrationStatus')).json()).pendingRestore, false);
    }
  }
  await page.getByText('恢复已暂存。停止服务后运行 --restore-pending 维护命令，再启动服务。', { exact: true }).waitFor();
  await page.screenshot({ path: path.join(root, 'migration-restore-pending.png'), fullPage: true });
  password = sourcePassword;
  await restoreOffline();
  await assertUnitAbsent('After migration snapshot');
  await assertUnitAbsent('Target only');
  assert.deepEqual(fs.readFileSync(template), originalTemplate);
  assert.deepEqual(fs.readFileSync(path.join(data, 'Security/native-master-key.bin')), key);
  await page.screenshot({ path: path.join(root, 'restored-maintenance.png'), fullPage: true });
  fs.writeFileSync(path.join(root, 'result.json'), JSON.stringify({
    succeeded: true, database: 'PostgreSQL 18', platform: process.platform,
    checks: ['secret-file startup', 'browser bootstrap', 'opt-in scheduled plan', 'real complete archive', 'archive cookie binding', 'server-side archive restore', 'dump create/download/upload', 'both pending states',
      'live-instance rejection', 'fail-closed startup', 'offline restore and idempotency', 'browser migration creation',
      'wrong password rejection', 'valid existing password', 'MIGRATE confirmation', 'cross-database and DataRoot migration',
      'original administrator restored', 'database/key/template recovery'],
    dumpSha256: createHash('sha256').update(fs.readFileSync(dump)).digest('hex'),
  }, null, 2));
  console.log('Real PostgreSQL maintenance UI passed: ' + root);
} catch (error) {
  if (page) {
    fs.writeFileSync(path.join(root, 'failure-page.txt'), await page.locator('body').innerText().catch(() => 'Page unavailable'));
    await page.screenshot({ path: path.join(root, 'failure-page.png'), fullPage: true }).catch(() => {});
  }
  throw error;
} finally {
  if (browser) await browser.close();
  if (chrome) await stopProcessTree(chrome.process, 5000);
  await stopApi();
  for (const name of ['app.txt', 'maintenance.txt', 'bootstrap.txt']) fs.rmSync(path.join(configuration, name), { force: true });
}

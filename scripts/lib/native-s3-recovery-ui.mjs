import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { locateChromeForTesting } from './chromium-executable.mjs';

export async function openS3RecoveryUi(repo, output) {
  const require = createRequire(path.join(repo, 'apps/export-doc-web/package.json'));
  const { chromium } = require('playwright');
  const browser = await chromium.launchPersistentContext(path.join(output, 'Chrome'), { executablePath: locateChromeForTesting(repo), headless: true, viewport: { width: 1440, height: 1000 } });
  const page = await browser.newPage(); page.setDefaultTimeout(30000);
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  page.on('request', request => { if (request.method() === 'POST') fs.appendFileSync(path.join(output, 'maintenance-requests.log'), new Date().toISOString() + ' ' + new URL(request.url()).pathname + '\n'); });
  return { browser, page, errors };
}

export async function verifyBackupIdentityUi(page, fixture, expected) {
  await page.goto(fixture.url);
  await page.locator('input[autocomplete=username]').fill(expected.credentials.username);
  await page.locator('input[autocomplete=current-password]').fill(expected.credentials.password);
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await page.locator('.login-submit-button').waitFor({ state: 'hidden' });
  await page.goto(fixture.url + '/#/system/backup?section=recovery');
  await page.getByRole('tab', { name: '备份计划', exact: true }).click();
  const id = page.getByLabel('恢复仓库编号', { exact: true });
  await id.waitFor(); assert.equal(await id.inputValue(), expected.repositoryId);
  assert(await id.evaluate(input => input.readOnly));
  await page.screenshot({ path: path.join(fixture.output, 'backup-repository-id.png'), fullPage: true });
}

export async function restoreS3ThroughUi(page, fixture, expected, { secret, token, afterRejected }) {
  const fillS3 = async (container, values) => {
    for (const [key, label] of [['endpoint', '服务地址'], ['bucket', '存储桶'], ['region', '区域'], ['prefix', '对象前缀'], ['accessKeyId', 'Access Key ID']]) await container.getByLabel(label, { exact: true }).fill(values[key]);
  };
  await page.goto(fixture.url + '/#/maintenance');
  await page.getByLabel('部署端一次性凭证').fill(token);
  await page.getByRole('button', { name: '进入维护', exact: true }).click();
  await page.getByRole('combobox', { name: /^恢复源/u }).selectOption('S3');
  await page.getByLabel('恢复仓库编号', { exact: true }).fill(expected.repositoryId);
  await fillS3(page, expected.repository);
  await page.getByLabel('旧业务实例已停止并隔离，不能继续写入文件库和备份仓库', { exact: true }).check();
  const run = async (label, operation, state) => {
    const response = page.waitForResponse(r => r.url().endsWith(fixture.operations.get(operation).route) && r.request().method() === 'POST');
    const finished = page.waitForResponse(async response => {
      if (new URL(response.url()).pathname !== '/maintenance-api/status' || response.request().method() !== 'GET') return false;
      assert(response.ok(), 'Maintenance status ' + response.status());
      const status = await response.json();
      if (!status.busy && status.state === 'Failed' && state !== 'Failed') throw Error(JSON.stringify(status));
      return !status.busy && status.state === state;
    }, { timeout: 180000 });
    await page.getByRole('button', { name: label, exact: true }).click();
    const accepted = await response; assert(accepted.ok(), await accepted.text());
    const result = await (await finished).json();
    fs.appendFileSync(path.join(fixture.output, 'maintenance-requests.log'), operation + ' ' + JSON.stringify(result) + '\n');
    return result;
  };
  await page.getByLabel('恢复凭证', { exact: true }).fill('Wrong-isolated-password');
  await page.getByLabel('Secret Access Key', { exact: true }).fill(secret);
  const rejected = await run('解锁仓库', 'UnlockRecoveryRepository', 'Failed');
  assert.equal(rejected.failure.code, 'VALIDATION_FAILED');
  assert.equal(rejected.failure.outcome, 'NotCommitted');
  assert.deepEqual(rejected.points, []);
  afterRejected();
  await page.getByLabel('恢复凭证', { exact: true }).fill(expected.recoveryPassword);
  await page.getByLabel('Secret Access Key', { exact: true }).fill(secret);
  const unlocked = await run('解锁仓库', 'UnlockRecoveryRepository', 'Unlocked');
  assert(unlocked.points.length > 0); assert.equal(unlocked.points[0].provider, 'PostgreSQL');
  await page.getByRole('combobox', { name: /^恢复点/u }).selectOption(unlocked.points[0].id);
  await page.getByRole('combobox', { name: /^附件恢复方式/u }).selectOption(expected.reconnectTarget ? 'Reconnect' : expected.restoreTarget ? 'S3' : 'Local');
  if (expected.restoreTarget) {
    const fields = page.locator('details').filter({ has: page.locator('summary', { hasText: /^新 S3 文件库$/u }) });
    await fillS3(fields, expected.restoreTarget);
    await fields.getByLabel('Secret Access Key', { exact: true }).fill(expected.restoreTarget.secret);
  }
  await run('完整预检', 'PreflightRecoveryPoint', 'Ready');
  await page.screenshot({ path: path.join(fixture.output, 'cold-recovery-preflight.png'), fullPage: true });
  await run('执行恢复与切换', 'ApplyRecoveryPoint', 'Completed');
  await page.screenshot({ path: path.join(fixture.output, 'cold-recovery-completed.png'), fullPage: true });
}

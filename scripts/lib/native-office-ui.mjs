import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { cargoExampleExecutable } from './cargo-paths.mjs';
import { locateChromeForTesting } from './report-regression-common.mjs';
import { spawnProcessTree, stopProcessTree } from './child-process-tree.mjs';

export async function withOfficeUi(name, run) {
  const repo = path.resolve(import.meta.dirname, '../..');
  const output = path.join(repo, 'artifacts', name, String(Date.now()));
  fs.mkdirSync(output, { recursive: true });
  const require = createRequire(path.join(repo, 'apps/export-doc-web/package.json'));
  const { chromium } = require('playwright');
  const spec = JSON.parse(fs.readFileSync(path.join(repo, 'crates/export-doc-contracts/src/openapi.json'), 'utf8'));
  const operations = new Map(Object.entries(spec.paths).flatMap(([route, methods]) => Object.entries(methods).map(([method, op]) => [op.operationId, { route, method }])));
  const server = spawnProcessTree(cargoExampleExecutable(repo, 'office_review'), [path.join(output, 'Data')], { cwd: repo, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  const contexts = [];
  const errors = [];
  try {
    const url = await new Promise((resolve, reject) => {
      let log = '';
      const timer = setTimeout(() => reject(new Error(`Office fixture timeout: ${log}`)), 60000);
      const fail = error => { clearTimeout(timer); reject(error); };
      server.once('error', fail); server.once('exit', code => fail(new Error(`Office fixture exited ${code}: ${log}`)));
      server.stderr.on('data', chunk => { log += chunk; });
      server.stdout.on('data', chunk => { log += chunk; const match = log.match(/\{"url":"([^"]+)"\}/u); if (match) { clearTimeout(timer); resolve(match[1]); } });
    });
    const invoke = async (id, body, parameters = {}, token = '') => {
      const op = operations.get(id); assert(op, `Unknown operation ${id}`);
      const route = op.route.replace(/\{([^}]+)\}/gu, (_, key) => encodeURIComponent(parameters[key]));
      const response = await fetch(url + route, { method: op.method.toUpperCase(), headers: { ...(token ? { authorization: `Bearer ${token}` } : {}), ...(body ? { 'content-type': 'application/json' } : {}) }, ...(body ? { body: JSON.stringify(body) } : {}) });
      assert(response.ok, `${id} (${response.status}): ${await response.clone().text()}`);
      return response.json();
    };
    const openPage = async (username, password) => {
      const context = await chromium.launchPersistentContext(path.join(output, `Chrome-${contexts.length}`), { executablePath: locateChromeForTesting(repo), headless: true, viewport: { width: 1440, height: 1000 }, acceptDownloads: true });
      contexts.push(context);
      const page = await context.newPage(); page.setDefaultTimeout(30000);
      page.on('pageerror', error => errors.push(error.message));
      await page.goto(url);
      await page.locator('input[autocomplete=username]').fill(username);
      await page.locator('input[autocomplete=current-password]').fill(password);
      await page.getByRole('button', { name: '登录', exact: true }).click();
      await page.locator('.login-submit-button').waitFor({ state: 'hidden' });
      return page;
    };
    await run({ repo, output, require, url, operations, invoke, openPage });
    assert.deepEqual(errors, [], 'No unhandled page errors');
    console.log(`Office UI verification passed: ${output}`);
  } catch (error) {
    for (const [i, context] of contexts.entries()) {
      const page = context.pages().at(-1);
      if (page) { await page.screenshot({ path: path.join(output, `failure-${i}.png`), fullPage: true }).catch(() => {}); fs.writeFileSync(path.join(output, `failure-${i}.txt`), `${page.url()}\n${await page.locator('body').innerText().catch(() => '')}`); }
    }
    throw error;
  } finally {
    await Promise.all(contexts.map(context => context.close()));
    await stopProcessTree(server);
  }
}

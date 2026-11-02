import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import { createRequire } from 'node:module';
import { cargoExampleExecutable } from './cargo-paths.mjs';
import { locateChromeForTesting } from './chromium-executable.mjs';
import { spawnProcessTree, stopProcessTree } from './child-process-tree.mjs';

export function officeReviewExecutable(repo) {
  if (!process.env.EXPORTDOC_OFFICE_REVIEW_BINARY) return cargoExampleExecutable(repo, 'office_review');
  const executable = path.resolve(process.env.EXPORTDOC_OFFICE_REVIEW_BINARY);
  const runtime = path.join(repo, '.codex-runtime') + path.sep;
  const comparable = value => process.platform === 'win32' ? value.toLowerCase() : value;
  assert(comparable(executable).startsWith(comparable(runtime)) && path.basename(executable) === `office_review${process.platform === 'win32' ? '.exe' : ''}`, 'Use a repository-owned isolated fixture copy');
  assert(!fs.lstatSync(executable).isSymbolicLink(), 'Fixture executable must be a regular file');
  return executable;
}
export function officeReviewOperations(spec) {
  return new Map(Object.entries(spec.paths).flatMap(([route, methods]) => Object.entries(methods).map(([method, op]) => [op.operationId, { route, method, request: op.requestBody?.content?.['application/json']?.schema }])));
}
export function officeReviewInvoke(url, spec, operations) {
  return async (id, body, parameters = {}, token = '') => {
    const op = operations.get(id); assert(op, `Unknown operation ${id}`);
    const schema = op.request?.$ref ? spec.components.schemas[op.request.$ref.split('/').at(-1)] : op.request;
    if (body && schema?.required?.includes('commandKey') && !body.commandKey) body = { ...body, commandKey: randomUUID() };
    const route = op.route.replace(/\{([^}]+)\}/gu, (_, key) => encodeURIComponent(parameters[key]));
    const query = new URLSearchParams(Object.entries(parameters).filter(([key]) => !op.route.includes(`{${key}}`)).map(([key, value]) => [key, String(value)]));
    const response = await fetch(url + route + (query.size ? '?' + query : ''), { method: op.method.toUpperCase(), signal: AbortSignal.timeout(60000), headers: { ...(token ? { authorization: `Bearer ${token}` } : {}), ...(body ? { 'content-type': 'application/json' } : {}) }, ...(body ? { body: JSON.stringify(body) } : {}) });
    if (!response.ok) throw new Error(`${id} (${response.status}): ${await response.text()}`);
    return response.json();
  };
}

export async function withOfficeUi(name, run) {
  const repo = path.resolve(import.meta.dirname, '../..');
  const output = path.join(repo, 'artifacts', name, String(Date.now()));
  fs.mkdirSync(output, { recursive: true });
  const require = createRequire(path.join(repo, 'apps/export-doc-web/package.json'));
  const { chromium } = require('playwright');
  const spec = JSON.parse(fs.readFileSync(path.join(repo, 'crates/export-doc-contracts/src/openapi.json'), 'utf8'));
  const operations = officeReviewOperations(spec);
  const executable = officeReviewExecutable(repo);
  const server = spawnProcessTree(executable, [path.join(output, 'Data')], { cwd: repo, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
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
    const invoke = officeReviewInvoke(url, spec, operations);
    assert.deepEqual(await fetch(`${url}/openapi/v1.json`).then(response => response.json()), spec, 'The running fixture must expose the current generated contract');
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
      await page.waitForURL(current => current.hash.startsWith('#/') && current.hash.length > 2);
      await page.getByRole('main').getByRole('heading', { level: 1 }).waitFor();
      await page.getByText('正在加载页面', { exact: true }).waitFor({ state: 'hidden' });
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

import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { locateChromeForTesting } from './lib/chromium-executable.mjs';

const repo = path.resolve(import.meta.dirname, '..');
const web = path.join(repo, 'apps/export-doc-web');
const output = path.join(repo, 'artifacts/about-page-ui');
const require = createRequire(path.join(web, 'package.json'));
const source = name => JSON.stringify(path.join(web, 'src', name).replaceAll('\\', '/'));
const temporary = path.join(output, 'Temp');
fs.mkdirSync(temporary, { recursive: true });
Object.assign(process.env, { TEMP: temporary, TMP: temporary, TMPDIR: temporary });
const bundle = path.join(output, 'app.js');
await require('esbuild').build({ stdin: { loader: 'tsx', resolveDir: web, contents: `
  import React, { Profiler, useState } from 'react';
  import { createRoot } from 'react-dom/client';
  import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
  import { AboutPage } from ${source('features/system/AboutPage.tsx')};
  import { getProductEditionPresentation } from ${source('app/productEdition.ts')};
  import { queryKeys } from ${source('api/queryKeys.ts')};
  import ${source('styles.css')}; import ${source('theme.css')}; import ${source('responsiveOverrides.css')};
  window.requests = []; window.commits = 0;
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } } });
  const client = { getHealth: ({ signal }) => new Promise((resolve, reject) => {
    window.requests.push({ signal, resolve, reject });
    signal.addEventListener('abort', () => reject(new DOMException('Canceled', 'AbortError')), { once: true });
  }) };
  window.setHealth = value => queries.setQueryData(queryKeys.health(), value);
  window.refresh = () => void queries.refetchQueries({ queryKey: queryKeys.health() });
  function App() {
    const [open, setOpen] = useState(true);
    return <><button onClick={() => setOpen(false)}>离开页面</button>
      {open ? <Profiler id='about' onRender={() => window.commits++}>
        <AboutPage client={client} product={getProductEditionPresentation('Full')}/>
      </Profiler> : <p>页面已关闭</p>}</>;
  }
  createRoot(document.getElementById('root')).render(<React.StrictMode><QueryClientProvider client={queries}><App/></QueryClientProvider></React.StrictMode>);
` }, outfile: bundle, bundle: true, platform: 'browser', format: 'iife', jsx: 'automatic', logLevel: 'silent' });

const health = { productVersion: '9.8.7', informationalVersion: '9.8.7+git.012345abcdef', checkedAt: '2026-10-12T00:00:00Z' };
const results = [];
let context;
try {
  context = await require('playwright').chromium.launchPersistentContext(path.join(output, 'Chrome'), {
    executablePath: locateChromeForTesting(repo), headless: true,
  });
  const detail = (page, label) => page.getByText(label, { exact: true }).locator('..').locator('strong');
  const settle = page => page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
  async function scenario(name, check) {
    const page = await context.newPage();
    page.setDefaultTimeout(5000);
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    try {
      await page.setViewportSize({ width: 1280, height: 900 });
      await page.setContent('<!doctype html><html lang="zh-CN"><title>产品版本验收</title><div id="root"></div></html>');
      await page.addStyleTag({ path: path.join(output, 'app.css') });
      await page.addScriptTag({ path: bundle });
      await page.waitForFunction(() => window.requests.some(request => !request.signal.aborted));
      await check(page);
      assert.deepEqual(errors, []);
      results.push(name);
    } finally {
      await page.close();
    }
  }
  const resolve = (page, value = health) => page.evaluate(value => window.requests.at(-1).resolve(value), value);
  await scenario('服务端版本、构建来源及诊断刷新不触发无关渲染', async page => {
    assert.equal(await detail(page, '版本').textContent(), '读取中…');
    await resolve(page);
    await page.getByText('v9.8.7', { exact: true }).waitFor();
    assert.equal(await detail(page, '构建标识').textContent(), health.informationalVersion);
    await settle(page);
    const commits = await page.evaluate(() => window.commits);
    await page.evaluate(value => window.setHealth({ ...value, checkedAt: '2026-10-12T01:00:00Z', runtimePaths: [{ key: 'changed' }] }), health);
    await settle(page);
    assert.equal(await page.evaluate(() => window.commits), commits, 'unrelated health data must not rerender the About page');
    await page.evaluate(value => window.setHealth({ ...value, productVersion: '9.8.8', informationalVersion: '9.8.8+git.123456abcdef' }), health);
    await page.getByText('v9.8.8', { exact: true }).waitFor();
    await page.screenshot({ path: path.join(output, 'about-desktop.png'), fullPage: true });
    await page.setViewportSize({ width: 375, height: 812 });
    await settle(page);
    assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'product identity must fit a narrow viewport');
    await page.screenshot({ path: path.join(output, 'about-narrow.png'), fullPage: true });
  });
  await scenario('首次读取失败、显式重试和恢复', async page => {
    await page.evaluate(() => window.requests.at(-1).reject(new Error('Temporary failure')));
    await page.getByRole('alert').waitFor();
    assert.equal(await detail(page, '版本').textContent(), '未获取');
    await page.getByRole('button', { name: '重试', exact: true }).click();
    await detail(page, '版本').filter({ hasText: '读取中…' }).waitFor();
    await resolve(page);
    await page.getByText('v9.8.7', { exact: true }).waitFor();
    assert.equal(await page.getByRole('alert').count(), 0);
  });
  await scenario('刷新失败明确标记旧结果', async page => {
    await resolve(page);
    await page.getByText('v9.8.7', { exact: true }).waitFor();
    const count = await page.evaluate(() => window.requests.length);
    await page.evaluate(() => window.refresh());
    await page.waitForFunction(count => window.requests.length > count, count);
    await page.evaluate(() => window.requests.at(-1).reject(new Error('Refresh failed')));
    await page.getByText('以下版本信息为上次读取结果，请重试以核实当前程序。', { exact: true }).waitFor();
    assert.equal(await detail(page, '版本').textContent(), 'v9.8.7');
    await page.getByRole('button', { name: '重试', exact: true }).click();
    await resolve(page, { ...health, productVersion: '9.8.9' });
    await page.getByText('v9.8.9', { exact: true }).waitFor();
  });
  await scenario('空白版本回退及单个 v 前缀', async page => {
    await resolve(page, { productVersion: '  ', informationalVersion: ' v9.8.7 ' });
    await page.getByText('v9.8.7', { exact: true }).waitFor();
    assert.equal(await detail(page, '构建标识').textContent(), '未提供');
    await page.evaluate(() => window.setHealth({ productVersion: ' v9.8.8 ', informationalVersion: '' }));
    await page.getByText('v9.8.8', { exact: true }).waitFor();
  });
  await scenario('离开页面取消读取', async page => {
    await page.getByRole('button', { name: '离开页面', exact: true }).click();
    await page.getByText('页面已关闭', { exact: true }).waitFor();
    assert(await page.evaluate(() => window.requests.every(request => request.signal.aborted)));
    await resolve(page);
    await settle(page);
    assert.equal(await page.getByRole('region', { name: '关于', exact: true }).count(), 0);
  });
  fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify({ passed: results.length, scenarios: results }, null, 2));
  console.log(`About page UI passed: ${results.length} scenarios. ${output}`);
} finally {
  await context?.close();
}

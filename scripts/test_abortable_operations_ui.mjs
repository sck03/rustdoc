import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { locateChromeForTesting } from './lib/report-regression-common.mjs';

const repo = path.resolve(import.meta.dirname, '..');
const web = path.join(repo, 'apps/export-doc-web');
const output = path.join(repo, 'artifacts/abortable-operations-ui');
const require = createRequire(path.join(web, 'package.json'));
const source = name => JSON.stringify(path.join(web, 'src', name).replaceAll('\\', '/'));
const temporary = path.join(output, 'Temp');
fs.mkdirSync(temporary, { recursive: true });
Object.assign(process.env, { TEMP: temporary, TMP: temporary, TMPDIR: temporary });
const bundle = path.join(output, 'app.js');
await require('esbuild').build({ stdin: { loader: 'tsx', resolveDir: web, contents: `
  import React, { useState } from 'react';
  import { createRoot } from 'react-dom/client';
  import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
  import { useAbortableOperation } from ${source('ui/useAbortableOperation.ts')};
  import { useOfficeOperation } from ${source('features/office/useOfficeData.ts')};
  window.pending = []; window.results = []; window.completed = []; window.refreshes = [];
  const delayed = signal => new Promise((resolve, reject) => window.pending.push({ signal, resolve, reject }));
  function Generic({ scope }) {
    const run = useAbortableOperation(scope);
    return <button onClick={() => void run(delayed).then(value => window.results.push({ value }),
      error => window.results.push({ name: error.name, message: error.message }))}>开始读取</button>;
  }
  function Office() {
    const operation = useOfficeOperation();
    return <><button disabled={operation.busy} onClick={() => void operation.run(delayed,
      value => window.completed.push(value))}>保存</button><output>{operation.error}</output></>;
  }
  function App() {
    const [scope, setScope] = useState(0), [open, setOpen] = useState(true);
    useQuery({ queryKey: ['office', 'test'], initialData: 'before', staleTime: Infinity,
      queryFn: () => new Promise(resolve => window.refreshes.push(resolve)) });
    useQuery({ queryKey: ['worklist', 'test'], initialData: 'before', staleTime: Infinity,
      queryFn: () => new Promise(resolve => window.refreshes.push(resolve)) });
    return <><button onClick={() => setScope(value => value + 1)}>切换资料</button>
      <button onClick={() => setOpen(false)}>离开编辑页</button>
      {open ? window.mode === 'office' ? <Office/> : <Generic scope={scope}/> : <p>编辑页已关闭</p>}</>;
  }
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } } });
  createRoot(document.getElementById('root')).render(<React.StrictMode><QueryClientProvider client={queries}><App/></QueryClientProvider></React.StrictMode>);
` }, outfile: bundle, bundle: true, platform: 'browser', format: 'iife', jsx: 'automatic', logLevel: 'silent' });

let context;
const results = [];
const failures = [];
try {
  context = await require('playwright').chromium.launchPersistentContext(path.join(output, 'Chrome'), {
    executablePath: locateChromeForTesting(repo), headless: true,
  });
  async function scenario(name, mode, check) {
    const page = await context.newPage();
    page.setDefaultTimeout(5000);
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    try {
      await page.setContent('<!doctype html><html lang="zh-CN"><title>异步操作验收</title><div id="root"></div></html>');
      await page.evaluate(value => { window.mode = value; }, mode);
      await page.addScriptTag({ path: bundle });
      await page.getByRole('button', { name: mode === 'office' ? '保存' : '开始读取', exact: true }).click();
      await page.waitForFunction(() => window.pending.length === 1);
      await check(page);
      assert.deepEqual(errors, []);
      results.push(name);
    } catch (error) { failures.push(`${name}: ${error.message}`); }
    finally { await page.close(); }
  }
  for (const boundary of ['none', 'scope', 'unmount']) {
    for (const reject of [false, true]) {
      await scenario(`read-${boundary}-${reject ? 'failure' : 'success'}`, 'generic', async page => {
        if (boundary !== 'none') {
          await page.getByRole('button', { name: boundary === 'scope' ? '切换资料' : '离开编辑页' }).click();
          await page.waitForFunction(() => window.pending[0].signal.aborted);
        }
        await page.evaluate(reject => reject ? window.pending[0].reject(new Error('读取失败')) : window.pending[0].resolve('current'), reject);
        await page.waitForFunction(() => window.results.length === 1);
        const [result] = await page.evaluate(() => window.results);
        if (boundary !== 'none') assert.equal(result.name, 'AbortError');
        else assert.deepEqual(result, reject ? { name: 'Error', message: '读取失败' } : { value: 'current' });
      });
    }
  }
  for (const boundary of ['none', 'save', 'refresh']) {
    await scenario(`save-unmount-${boundary}`, 'office', async page => {
      if (boundary === 'save') {
        await page.getByRole('button', { name: '离开编辑页' }).click();
        await page.waitForFunction(() => window.pending[0].signal.aborted);
        await page.evaluate(() => window.pending[0].resolve('saved'));
        await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
        assert.deepEqual(await page.evaluate(() => window.completed), []);
        return;
      }
      await page.evaluate(() => window.pending[0].resolve('saved'));
      await page.waitForFunction(() => window.refreshes.length === 2);
      assert.deepEqual(await page.evaluate(() => window.completed), ['saved'], 'apply success before refreshing can remove the current record');
      if (boundary === 'refresh') {
        await page.getByRole('button', { name: '离开编辑页' }).click();
        await page.getByText('编辑页已关闭').waitFor();
      }
      await page.evaluate(() => window.refreshes.forEach(resolve => resolve('updated')));
      // Drain query notifications and the operation's final continuation.
      await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
      assert.deepEqual(await page.evaluate(() => window.completed), ['saved'], 'refresh completion cannot repeat the callback');
      if (boundary === 'none') assert.equal(await page.getByRole('button', { name: '保存', exact: true }).isEnabled(), true);
    });
  }
  fs.writeFileSync(path.join(output, 'summary.json'), JSON.stringify({ results, failures }, null, 2));
  assert.deepEqual(failures, []);
  process.stdout.write(`Abortable operation UI passed (${results.length} cases).\n`);
} finally { await context?.close(); }

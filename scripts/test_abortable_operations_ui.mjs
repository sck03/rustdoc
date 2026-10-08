import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { locateChromeForTesting } from './lib/chromium-executable.mjs';

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
  import { MemoryRouter } from 'react-router-dom';
  import { ConfirmationProvider } from ${source('ui/ConfirmationProvider.tsx')};
  import { UnsavedChangesProvider } from ${source('ui/unsavedChangesGuard.tsx')};
  import { PermissionAccessProvider } from ${source('app/PermissionAccessContext.tsx')};
  import { SupplierProductLinksPanel } from ${source('features/suppliers/SupplierProductLinksPanel.tsx')};
  import { SalesOpportunityPage } from ${source('features/opportunities/SalesOpportunityPage.tsx')};
  window.pending = []; window.results = []; window.completed = []; window.refreshes = [];
  const delayed = signal => new Promise((resolve, reject) => window.pending.push({ signal, resolve, reject }));
  const lookup = (input, options, kind = 'primary') => new Promise((resolve, reject) => window.pending.push({ ...input, kind, signal: options.signal, resolve, reject }));
  const client = { searchSupplierProductOptions: lookup, queryCrmCustomers: lookup, listProducts: (input, options) => lookup(input, options, 'product'),
    querySupplierProductLinks: async () => ({ items: [], totalCount: 0, totalPages: 1 }),
    querySalesOpportunities: async () => ({ items: [], totalCount: 0, totalPages: 1 }) };
  function Generic({ scope }) {
    const run = useAbortableOperation(scope, { cancelPrevious: window.mode === 'latest' });
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
      {open ? window.mode === 'supplier' ? <SupplierProductLinksPanel client={client} supplierId={scope + 1} supplierName='供应商' canEdit/>
        : window.mode.startsWith('sales') ? <SalesOpportunityPage client={client} businessTimeZone='Asia/Shanghai'/>
        : window.mode === 'office' ? <Office/> : <Generic scope={scope}/> : <p>编辑页已关闭</p>}</>;
  }
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } } });
  const permissions = ['sales.opportunities', 'sales.quotes'].map(resourceKey => ({ resourceKey, action: 'create', dataScope: 'all' }));
  createRoot(document.getElementById('root')).render(<React.StrictMode><MemoryRouter initialEntries={['/?view=editor']}><QueryClientProvider client={queries}>
    <PermissionAccessProvider permissions={permissions}><ConfirmationProvider><UnsavedChangesProvider><App/></UnsavedChangesProvider></ConfirmationProvider></PermissionAccessProvider>
  </QueryClientProvider></MemoryRouter></React.StrictMode>);
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
      if (['generic', 'office', 'latest'].includes(mode)) {
        await page.getByRole('button', { name: mode === 'office' ? '保存' : '开始读取', exact: true }).click();
        await page.waitForFunction(() => window.pending.length === 1);
      }
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
  for (const mode of ['generic', 'latest']) {
    await scenario(`concurrent-${mode}`, mode, async page => {
      await page.getByRole('button', { name: '开始读取', exact: true }).click();
      await page.waitForFunction(() => window.pending.length === 2);
      assert.equal(await page.evaluate(() => window.pending[0].signal.aborted), mode === 'latest');
      await page.evaluate(() => window.pending[1].resolve('new'));
      await page.waitForFunction(() => window.results.length === 1);
      await page.evaluate(() => window.pending[0].resolve('old'));
      await page.waitForFunction(() => window.results.length === 2);
      const results = await page.evaluate(() => window.results);
      assert.deepEqual(results[0], { value: 'new' });
      if (mode === 'latest') assert.equal(results[1].name, 'AbortError');
      else assert.deepEqual(results[1], { value: 'old' });
    });
  }
  for (const mode of ['supplier', 'sales', 'sales-product']) {
    for (const reject of [false, true]) {
      await scenario(`lookup-${mode}-late-${reject ? 'failure' : 'success'}`, mode, async page => {
        if (mode === 'supplier') await page.getByRole('button', { name: '新增供货关系', exact: true }).click();
        const placeholder = mode === 'supplier' ? '输入产品货号或名称' : mode === 'sales' ? '搜索客户' : '搜索产品货号或名称';
        const input = page.getByPlaceholder(placeholder, { exact: true });
        await input.fill('最新');
        await input.locator('..').getByRole('button', { name: '查找', exact: true }).click();
        await page.waitForFunction(() => window.pending.some(request => request.keyword === '最新'));
        await page.evaluate(() => window.pending.find(request => request.keyword === '最新').resolve({ items: [{ id: 2, name: '最新结果', nameCN: '最新结果', productCode: 'NEW' }], totalCount: 1 }));
        const select = page.locator(mode === 'sales' ? 'select[name="crmCustomerId"]' : 'select[name="productId"]');
        await select.locator('option').filter({ hasText: '最新结果' }).waitFor({ state: 'attached' });
        await page.evaluate(({ reject, mode }) => window.pending.filter(request => request.keyword === '' && request.kind === (mode === 'sales-product' ? 'product' : 'primary')).forEach(request => reject
          ? request.reject(new Error('过时查询失败')) : request.resolve({ items: [{ id: 1, name: '过时结果', nameCN: '过时结果' }], totalCount: 1 })), { reject, mode });
        await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
        assert.match(await select.textContent(), /最新结果/);
        assert.doesNotMatch(await page.locator('body').textContent(), /过时查询失败/);
        await input.fill('先搜索');
        await input.locator('..').getByRole('button', { name: '查找', exact: true }).click();
        await input.fill('离开');
        await input.locator('..').getByRole('button', { name: '查找', exact: true }).click();
        assert.equal(await page.evaluate(() => window.pending.find(request => request.keyword === '先搜索').signal.aborted), true);
        await page.evaluate(() => window.pending.find(request => request.keyword === '先搜索').resolve({ items: [], totalCount: 0 }));
        if (mode === 'supplier') {
          await page.getByRole('button', { name: '切换资料', exact: true }).click();
          assert.equal(await page.evaluate(() => window.pending.find(request => request.keyword === '离开').signal.aborted), true);
        }
        await page.getByRole('button', { name: '离开编辑页', exact: true }).click();
        assert.equal(await page.evaluate(() => window.pending.find(request => request.keyword === '离开').signal.aborted), true);
        await page.evaluate(() => window.pending.find(request => request.keyword === '离开').reject(new Error('离开后失败')));
      });
    }
  }
  fs.writeFileSync(path.join(output, 'summary.json'), JSON.stringify({ results, failures }, null, 2));
  assert.deepEqual(failures, []);
  process.stdout.write(`Abortable operation UI passed (${results.length} cases).\n`);
} finally { await context?.close(); }

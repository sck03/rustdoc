import assert from 'node:assert/strict';
import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';
import { createRequire } from 'node:module';
import { locateChromeForTesting } from './lib/chromium-executable.mjs';

const repo = path.resolve(import.meta.dirname, '..'), web = path.join(repo, 'apps/export-doc-web');
const output = path.join(repo, 'artifacts/input-method-ui');
const require = createRequire(path.join(web, 'package.json'));
const source = name => JSON.stringify(path.join(web, 'src', name).replaceAll('\\', '/'));
fs.mkdirSync(path.join(output, 'Temp'), { recursive: true });
Object.assign(process.env, { TEMP: path.join(output, 'Temp'), TMP: path.join(output, 'Temp'), TMPDIR: path.join(output, 'Temp') });
await require('esbuild').build({ stdin: { loader: 'tsx', resolveDir: web, contents: `
  import React, { useState } from 'react';
  import { createRoot } from 'react-dom/client';
  import { MemoryRouter } from 'react-router-dom';
  import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
  import ${source('styles/index.css')};
  import { PermissionAccessProvider } from ${source('app/PermissionAccessContext.tsx')};
  import { WorkspaceNavigation } from ${source('app/WorkspaceNavigation.tsx')};
  import { ConfirmationProvider } from ${source('ui/ConfirmationProvider.tsx')};
  import { UnsavedChangesProvider } from ${source('ui/unsavedChangesGuard.tsx')};
  import { Dialog } from ${source('ui/primitives/Dialog.tsx')};
  import { Pagination } from ${source('ui/primitives/Pagination.tsx')};
  import { ReportDesignerV3ColorField } from ${source('features/report-designer/ReportDesignerV3ColorField.tsx')};
  import { QueryPage } from ${source('features/query/QueryPage.tsx')};
  import { SingleWindowReferenceCatalogPage } from ${source('features/single-window/SingleWindowReferenceCatalogPage.tsx')};
  window.calls = { search: [], save: 0, page: [], color: [] };
  let catalog = { countries: [{ code: 'CN', chineseName: '中国', englishName: 'China', aliases: ['PRC'] }] };
  const client = {
    getSettings: async () => ({ settings: { system: { defaultExportDirectory: '' } } }),
    listQueriedInvoices: async input => { window.calls.search.push(input); return { items: [], totalCount: 0, pageNumber: 1, pageSize: 20 }; },
    getSingleWindowReferenceCatalog: async () => ({ catalog }),
    updateSingleWindowReferenceCatalog: async ({ body }) => { window.calls.save++; catalog = body.catalog; return { catalog }; },
  };
  function Controls() {
    const [open, setOpen] = useState(true);
    return <><Pagination pageNumber={1} totalCount={60} pageSize={20} isBusy={false} onPageChange={page => window.calls.page.push(page)} />
      <ReportDesignerV3ColorField label="颜色" value="#000000" onCommit={color => window.calls.color.push(color)} />
      <button onClick={() => setOpen(true)}>打开弹窗</button>
      {open && <Dialog title="输入法" onClose={() => setOpen(false)}><input aria-label="弹窗输入" /><input aria-label="自行处理按键" onKeyDown={e => { if (e.key === 'Escape') e.preventDefault(); }} /></Dialog>}</>;
  }
  const mode = new URLSearchParams(location.search).get('mode');
  const permissions = ['document.query', 'document.declaration-dictionary'].flatMap(resourceKey => ['view', 'operate', 'manage'].map(action => ({ resourceKey, action, dataScope: 'all' })));
  createRoot(document.getElementById('root')).render(<MemoryRouter><QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
    <PermissionAccessProvider grants={[]} permissions={permissions} subject={{ id: 1 }} canManageSettings={false}><ConfirmationProvider><UnsavedChangesProvider>
      {mode === 'query' ? <QueryPage client={client} businessDate="2026-11-07" /> : mode === 'catalog' ? <SingleWindowReferenceCatalogPage client={client} /> :
        mode === 'navigation' ? <WorkspaceNavigation groups={[]} pathname="/" collapsed={false} activeGroupKey="" expandedGroups={new Set()} navigationRef={null} onToggleGroup={() => {}} onExpand={() => {}} /> : <Controls />}
    </UnsavedChangesProvider></ConfirmationProvider></PermissionAccessProvider></QueryClientProvider></MemoryRouter>);
` }, outfile: path.join(output, 'app.js'), bundle: true, platform: 'browser', format: 'esm', jsx: 'automatic', logLevel: 'silent' });
const server = http.createServer((request, response) => {
  const name = new URL(request.url, 'http://localhost').pathname.slice(1);
  if (!name) { response.setHeader('Content-Type', 'text/html; charset=utf-8'); response.end('<!doctype html><html lang="zh-CN"><title>输入法按键回归</title><link rel="stylesheet" href="/app.css"><div id="root"></div><script type="module" src="/app.js"></script></html>'); }
  else if (['app.js', 'app.css'].includes(name)) { response.setHeader('Content-Type', name.endsWith('.js') ? 'text/javascript' : 'text/css'); response.end(fs.readFileSync(path.join(output, name))); }
  else response.writeHead(404).end();
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const results = [];
let context;
try {
  context = await require('playwright').chromium.launchPersistentContext(path.join(output, 'Chrome'), { executablePath: locateChromeForTesting(repo), headless: true });
  for (const composition of [{ isComposing: true }, { keyCode: 229 }]) {
    for (const mode of ['query', 'catalog', 'navigation', 'controls']) {
      const page = await context.newPage(), errors = [];
      await page.addInitScript(() => { localStorage.clear(); sessionStorage.clear(); });
      page.setDefaultTimeout(5000);
      page.on('pageerror', error => errors.push(error.message));
      const key = (locator, key, extras = {}) => locator.dispatchEvent('keydown', { key, bubbles: true, cancelable: true, ...composition, ...extras });
      try {
        await page.goto(`http://127.0.0.1:${server.address().port}/?mode=${mode}`);
        if (mode === 'query') {
          await page.waitForFunction(() => window.calls.search.length === 1);
          const input = page.locator('[data-query-filter="keyword"]');
          await input.fill('中国');
          await key(input, 'Enter'); await page.waitForTimeout(80);
          assert.equal(await page.evaluate(() => window.calls.search.length), 1, 'candidate confirmation must not run a query');
          await input.press('Enter');
          await page.waitForFunction(() => window.calls.search.length === 2);
        } else if (mode === 'catalog') {
          const aliases = page.locator('.reference-catalog-table tbody tr').first().locator('textarea').last();
          await aliases.waitFor(); await aliases.focus();
          await key(aliases, 'Enter');
          assert.equal(await page.getByRole('dialog').count(), 0, 'candidate confirmation must not open the alias editor');
          await key(aliases, 's', { ctrlKey: true }); await page.waitForTimeout(80);
          assert.equal(await page.evaluate(() => window.calls.save), 0, 'composition must not save a catalog');
          await aliases.press('Enter');
          const editor = page.getByRole('dialog', { name: '编辑别名' }).locator('textarea');
          await editor.fill('中国别名');
          await key(editor, 'Escape'); await key(editor, 'Enter', { ctrlKey: true });
          assert.equal(await editor.inputValue(), '中国别名', 'candidate cancellation and confirmation retain the alias draft');
          await editor.press('Control+Enter');
          await page.getByRole('dialog').waitFor({ state: 'hidden' });
          assert.equal(await aliases.inputValue(), '中国别名');
        } else if (mode === 'navigation') {
          const input = page.getByRole('searchbox', { name: '查找功能' });
          await input.fill('单证'); await key(input, 'Escape');
          assert.equal(await input.inputValue(), '单证', 'candidate cancellation must not clear navigation search');
          await input.press('Escape'); assert.equal(await input.inputValue(), '');
        } else {
          const input = page.getByLabel('弹窗输入', { exact: true });
          await input.focus(); await key(input, 'Escape');
          assert.equal(await page.getByRole('dialog').count(), 1, 'candidate cancellation must not close a dialog');
          const consumed = page.getByLabel('自行处理按键');
          await consumed.focus(); await consumed.press('Escape');
          assert.equal(await page.getByRole('dialog').count(), 1, 'a handled Escape must not close the containing dialog');
          await input.focus(); await input.press('Escape');
          await page.getByRole('dialog').waitFor({ state: 'hidden' });
          const jump = page.getByLabel('跳转页');
          await jump.fill('2'); await key(jump, 'Enter');
          assert.deepEqual(await page.evaluate(() => window.calls.page), [], 'composition must not change pages');
          await jump.press('Enter'); assert.deepEqual(await page.evaluate(() => window.calls.page), [2]);
          const color = page.getByLabel('颜色高级色值');
          await color.fill('#ffffff'); await key(color, 'Enter');
          assert.deepEqual(await page.evaluate(() => window.calls.color), [], 'composition must not commit color');
          await color.press('Enter'); assert.deepEqual(await page.evaluate(() => window.calls.color), ['#ffffff']);
        }
        assert.deepEqual(errors, [], 'React errors');
        results.push({ mode, composition, passed: true });
      } catch (error) { results.push({ mode, composition, passed: false, error: error.message, errors }); }
      finally { await page.close(); }
    }
  }
  fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify(results, null, 2));
  assert(results.every(result => result.passed), JSON.stringify(results, null, 2));
  console.log('Input methods: query, catalog, navigation, modal, pagination and color preserve composition and ordinary keyboard actions.');
} finally { await context?.close(); await new Promise(resolve => server.close(resolve)); }

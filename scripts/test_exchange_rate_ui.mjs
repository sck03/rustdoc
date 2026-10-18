import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { locateChromeForTesting } from './lib/chromium-executable.mjs';

const repo = path.resolve(import.meta.dirname, '..');
const web = path.join(repo, 'apps/export-doc-web');
const output = path.join(repo, 'artifacts/exchange-rate-ui');
const require = createRequire(path.join(web, 'package.json'));
const source = name => JSON.stringify(path.join(web, 'src', name).replaceAll('\\', '/'));
const defaults = JSON.parse(fs.readFileSync(path.join(repo, 'crates/export-doc-contracts/src/generated_contract.json'), 'utf8')).configuration.defaults;
fs.mkdirSync(path.join(output, 'Temp'), { recursive: true });
Object.assign(process.env, { TEMP: path.join(output, 'Temp'), TMP: path.join(output, 'Temp'), TMPDIR: path.join(output, 'Temp') });
await require('esbuild').build({ stdin: { loader: 'tsx', resolveDir: web, contents: `
  import React, { useState } from 'react';
  import { createRoot } from 'react-dom/client';
  import { MemoryRouter, Routes, Route } from 'react-router-dom';
  import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
  import { ExchangeRatePage } from ${source('features/tools/ExchangeRatePage.tsx')};
  import { SettingsPage } from ${source('features/settings/SettingsPage.tsx')};
  import { ConfirmationProvider } from ${source('ui/ConfirmationProvider.tsx')};
  import { UnsavedChangesProvider } from ${source('ui/unsavedChangesGuard.tsx')};
  import { queryKeys } from ${source('api/queryKeys.ts')};
  import ${source('styles/cascade.css')}; import ${source('styles/foundation.css')};
  import ${source('styles/workspaces.css')}; import ${source('styles/responsive.css')};
  window.requests = []; window.saved = ${JSON.stringify(defaults)};
  window.saved.revision = 0;
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false }, mutations: { retry: false } } });
  const settings = () => ({ settings: structuredClone(window.saved), secrets: {}, storagePolicy: 'fixture' });
  const client = {
    getSettings: async () => { if (window.settingsFailure) throw new Error('settings unavailable'); return settings(); },
    listExchangeRates: (request, init) => new Promise((resolve, reject) => {
      window.requests.push({ request, signal: init.signal, resolve, reject, configuration: structuredClone(window.saved.exchangeRate) });
      init.signal.addEventListener('abort', () => reject(new DOMException('Cancelled', 'AbortError')), { once: true });
    }),
    updateSettings: async ({ body }) => { window.saved = { ...body.settings, revision: window.saved.revision + 1 }; return { success: true, settings: structuredClone(window.saved), secrets: {}, message: '设置已保存', requiresRestart: false }; },
  };
  window.remoteSettings = async patch => {
    window.saved.exchangeRate = { ...window.saved.exchangeRate, ...patch };
    queries.setQueryData(queryKeys.settings(), settings());
  };
  function App() {
    const [open, setOpen] = useState(true);
    return <><button onClick={() => setOpen(false)}>离开测试</button>{open ? <Routes>
      <Route path='/tools/exchange-rates' element={<ExchangeRatePage client={client} canManageSettings={window.manage !== false}/>}/>
      <Route path='/settings' element={<SettingsPage client={client} canManageSettings canManageUsers canUseDocumentWorkspace isDesktopRuntime={false} productName='测试'/>}/>
    </Routes> : <p>测试页面已关闭</p>}</>;
  }
  createRoot(document.getElementById('root')).render(<React.StrictMode><QueryClientProvider client={queries}><MemoryRouter initialEntries={['/tools/exchange-rates']}><ConfirmationProvider><UnsavedChangesProvider><App/></UnsavedChangesProvider></ConfirmationProvider></MemoryRouter></QueryClientProvider></React.StrictMode>);
` }, outfile: path.join(output, 'app.js'), bundle: true, platform: 'browser', format: 'iife', jsx: 'automatic', logLevel: 'silent' });

const quote = (overrides = {}) => ({ rates: [
  { currencyName: '美元', buyingRate: 700.12, sellingRate: 702.35, middleRate: 701, publishTime: '2026-10-17T09:00:00+08:00' },
  { currencyName: '日元', buyingRate: 4.5321, sellingRate: 4.5532, middleRate: null, publishTime: '2026-10-17T09:00:00+08:00' },
], selectedCurrencies: ['美元', '日元'], availableCurrencies: ['美元', '日元', '欧元'], missingCurrencies: [], sourceUrl: defaults.exchangeRate.url,
  sourceFormat: 'BankOfChinaHtml', fetchedAt: '2026-10-17T01:00:00Z', lastAttemptAt: '2026-10-17T01:00:00Z', nextRefreshAt: null,
  autoRefreshEnabled: false, cacheDurationMinutes: 30, statusText: '已读取 2 种常用货币。', storagePolicy: '每 100 外币折算人民币；不自动改写单据汇率。', isStale: false, lastError: '', ...overrides });
const results = [];
const browser = await require('playwright').chromium.launch({ executablePath: locateChromeForTesting(repo), headless: true });
const resolve = (page, result = quote()) => page.evaluate(result => window.requests.at(-1).resolve(result), result);
const waitRequest = page => page.waitForFunction(() => window.requests.some(r => !r.signal.aborted));
async function scenario(name, test, before = '') {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  page.setDefaultTimeout(7000);
  try {
    await page.setContent('<!doctype html><html lang="zh-CN"><title>汇率界面验收</title><div id="root"></div></html>');
    if (before) await page.evaluate(before);
    await page.addStyleTag({ path: path.join(output, 'app.css') });
    await page.addScriptTag({ path: path.join(output, 'app.js') });
    await test(page);
    assert.deepEqual(errors, []);
    results.push(name);
  } catch (error) {
    await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true });
    console.error(name, errors, (await page.locator('body').innerText()).slice(0, 4000));
    throw error;
  } finally { await context.close(); }
}
try {
  await scenario('打开即读取、报价单位、桌面及窄屏', async page => {
    await waitRequest(page); await resolve(page);
    await page.getByRole('cell', { name: '700.1200', exact: true }).waitFor();
    assert.match(await page.locator('[aria-label="今日汇率"]').textContent(), /100 外币/);
    assert.equal(await page.locator('details[open]').count(), 0);
    await page.screenshot({ path: path.join(output, 'exchange-desktop.png'), fullPage: true });
    await page.setViewportSize({ width: 390, height: 844 });
    assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
    assert(await page.locator('.exchange-rate-table-frame').evaluate(e => e.scrollWidth > e.clientWidth));
    await page.screenshot({ path: path.join(output, 'exchange-narrow.png'), fullPage: true });
  });
  await scenario('设置与今日汇率共享配置，保存间隔及币种后立即更新', async page => {
    await waitRequest(page); await resolve(page);
    await page.getByRole('link', { name: '币制与汇率设置', exact: true }).click();
    await page.getByLabel('获取间隔', { exact: true }).selectOption('60');
    await page.getByLabel('启用自动获取', { exact: true }).check();
    await page.getByLabel('常用货币顺序', { exact: true }).selectOption('美元');
    await page.getByRole('button', { name: '移出常用货币', exact: true }).click();
    await page.getByRole('button', { name: '保存全部修改', exact: true }).click();
    await page.waitForFunction(() => window.saved.exchangeRate.autoRefreshEnabled && window.saved.exchangeRate.cacheDurationMinutes === 60 && !window.saved.exchangeRate.selectedCurrencies.includes('美元'));
    await page.waitForFunction(() => window.requests.at(-1).configuration.cacheDurationMinutes === 60);
    await resolve(page, quote({ autoRefreshEnabled: true, cacheDurationMinutes: 60, rates: [quote().rates[1]], selectedCurrencies: ['日元'], nextRefreshAt: '2026-10-17T02:00:00Z' }));
    await page.getByRole('link', { name: '查看今日汇率', exact: true }).click();
    await page.getByText('每 60 分钟', { exact: true }).waitFor();
    assert.equal(await page.getByRole('cell', { name: '美元', exact: true }).count(), 0);
    await page.getByRole('link', { name: '币制与汇率设置', exact: true }).click();
    await page.getByLabel('汇率源网址', { exact: true }).fill('https://example.com/rates?base=CNY');
    assert(await page.getByRole('button', { name: '刷新报价与币种', exact: true }).isDisabled());
    await page.getByLabel('来源格式', { exact: true }).selectOption('Auto');
    await page.screenshot({ path: path.join(output, 'exchange-settings.png'), fullPage: true });
  });
  await scenario('失败保留报价，成功重试清除错误', async page => {
    await waitRequest(page); await resolve(page);
    const button = page.getByRole('button', { name: '刷新汇率', exact: true });
    await button.click();
    await page.evaluate(() => window.requests.at(-1).reject(new Error('network failed')));
    await page.getByText(/network failed/).waitFor();
    assert.equal(await page.getByRole('cell', { name: '美元', exact: true }).count(), 1);
    await button.click(); await resolve(page);
    await page.getByRole('button', { name: '刷新汇率', exact: true }).waitFor();
    await page.getByText(/network failed/).waitFor({ state: 'hidden' });
    await button.click(); await resolve(page, quote({ isStale: true, lastError: 'source unavailable', nextRefreshAt: '2026-10-17T02:00:00Z' }));
    await page.getByText(/source unavailable/).waitFor();
  });
  await scenario('清空选择及缺失币种不伪造成功报价', async page => {
    await waitRequest(page); await resolve(page, quote({ rates: [], selectedCurrencies: [] }));
    await page.getByRole('cell', { name: /尚未选择常用货币/ }).waitFor();
    await page.getByRole('button', { name: '刷新汇率', exact: true }).click();
    await resolve(page, quote({ missingCurrencies: ['欧元'] }));
    await page.getByText(/来源暂未提供：欧元/).waitFor();
  });
  await scenario('自动状态轮询、关闭停止轮询、离开取消在途请求', async page => {
    await waitRequest(page);
    await page.clock.install();
    await page.evaluate(() => window.remoteSettings({ autoRefreshEnabled: true }));
    await page.waitForFunction(() => window.requests.at(-1).configuration.autoRefreshEnabled);
    await page.evaluate(() => window.requests.at(-1).reject(new Error('initial source error')));
    await page.getByText(/initial source error/).waitFor();
    const failed = await page.evaluate(() => window.requests.length);
    await page.clock.fastForward(60_001);
    await page.waitForFunction(n => window.requests.length > n, failed);
    await resolve(page, quote({ autoRefreshEnabled: true }));
    await page.getByRole('cell', { name: '美元', exact: true }).waitFor();
    const count = await page.evaluate(() => window.requests.length);
    await page.clock.fastForward(60_001);
    await page.waitForFunction(n => window.requests.length > n, count);
    await resolve(page, quote({ autoRefreshEnabled: false }));
    await page.getByText('已关闭 · 手动获取', { exact: true }).waitFor();
    const stopped = await page.evaluate(() => window.requests.length);
    await page.clock.fastForward(120_000);
    assert.equal(await page.evaluate(() => window.requests.length), stopped);
    await page.getByRole('button', { name: '刷新汇率', exact: true }).click();
    await page.getByRole('button', { name: '离开测试', exact: true }).click();
    assert(await page.evaluate(() => window.requests.at(-1).signal.aborted));
  });
  await scenario('配置变更取消旧来源请求并拒绝迟到回写', async page => {
    await waitRequest(page); await resolve(page);
    await page.getByRole('button', { name: '刷新汇率', exact: true }).click();
    await page.evaluate(() => { window.oldRequest = window.requests.at(-1); return window.remoteSettings({ url: 'https://example.com/new', sourceFormat: 'Json' }); });
    await page.waitForFunction(() => window.requests.at(-1).configuration.url === 'https://example.com/new');
    assert(await page.evaluate(() => window.oldRequest.signal.aborted));
    await resolve(page, quote({ sourceUrl: 'https://example.com/new', rates: [quote().rates[1]] }));
    await page.evaluate(result => window.oldRequest.resolve(result), quote());
    await page.getByRole('cell', { name: '日元', exact: true }).waitFor();
    assert.equal(await page.getByRole('cell', { name: '美元', exact: true }).count(), 0);
  });
  await scenario('设置读取失败可以重试，普通账号不显示管理入口', async page => {
    await page.getByText(/settings unavailable/).waitFor();
    await page.evaluate(() => { window.settingsFailure = false; });
    await page.getByRole('button', { name: '刷新汇率', exact: true }).click();
    await waitRequest(page); await resolve(page);
    await page.getByRole('cell', { name: '美元', exact: true }).waitFor();
    assert.equal(await page.getByRole('link', { name: '币制与汇率设置', exact: true }).count(), 0);
  }, 'window.settingsFailure = true; window.manage = false;');
  fs.writeFileSync(path.join(output, 'result.json'), JSON.stringify({ passed: results.length, scenarios: results }, null, 2));
  console.log(`Exchange rate UI: ${results.length} scenarios passed.`);
} finally { await browser.close(); }

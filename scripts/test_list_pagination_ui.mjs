import assert from 'node:assert/strict';
import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';
import { createRequire } from 'node:module';
import { locateChromeForTesting } from './lib/report-regression-common.mjs';

const repo = path.resolve(import.meta.dirname, '..');
const web = path.join(repo, 'apps/export-doc-web');
const output = path.join(repo, 'artifacts/list-pagination-ui');
const require = createRequire(path.join(web, 'package.json'));
const source = name => JSON.stringify(path.join(web, 'src', name).replaceAll('\\', '/'));
fs.mkdirSync(output, { recursive: true });
const temporary = path.join(output, 'Temp');
fs.mkdirSync(temporary, { recursive: true });
Object.assign(process.env, { TEMP: temporary, TMP: temporary, TMPDIR: temporary });
await require('esbuild').build({ stdin: { loader: 'tsx', resolveDir: web, contents: `
  import React from 'react';
  import { createRoot } from 'react-dom/client';
  import { QueryClient, QueryClientProvider, onlineManager } from '@tanstack/react-query';
  import { MemoryRouter } from 'react-router-dom';
  import { PermissionAccessProvider } from ${source('app/PermissionAccessContext.tsx')};
  import { ConfirmationProvider } from ${source('ui/ConfirmationProvider.tsx')};
  import { InvoiceListPage } from ${source('features/invoices/InvoiceListPage.tsx')};
  import { PaymentListPage } from ${source('features/payments/PaymentListPage.tsx')};
  import { JobCenterPage } from ${source('features/jobs/JobCenterPage.tsx')};
  import { QueryPage } from ${source('features/query/QueryPage.tsx')};
  import { AuditLogPage } from ${source('features/audit-logs/AuditLogPage.tsx')};
  import { SingleWindowOperationCenterPage } from ${source('features/single-window/SingleWindowOperationCenterPage.tsx')};
  import { MasterDataListPage } from ${source('features/master-data/MasterDataListPage.tsx')};
  import { BusinessAttachmentsPage } from ${source('features/attachments/BusinessAttachmentsPage.tsx')};
  import { WorklistPage } from ${source('features/worklist/WorklistPage.tsx')};
  import { AnnouncementsPage } from ${source('features/communication/AnnouncementsPage.tsx')};
  import { NotificationsPage } from ${source('features/communication/NotificationsPage.tsx')};
  import { ReportImageResourceGallery } from ${source('features/report-designer/ReportImageResourceGallery.tsx')};
  import { useReportTemplateWorkspaceQueries } from ${source('features/reports/useReportTemplateWorkspaceQueries.ts')};
  import { ListPaginationControls } from ${source('ui/ListPaginationControls.tsx')};
  import { getMasterDataConfig } from ${source('features/master-data/masterDataConfigs.ts')};
  import ${source('styles/cascade.css')}; import ${source('styles/foundation.css')};
  import ${source('styles/workspaces.css')}; import ${source('styles/responsive.css')};
  const mode = new URLSearchParams(location.search).get('mode');
  window.__requests = []; window.__totalPages = 3; window.__fail = false;
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false, staleTime: 30_000 } } });
  window.__refresh = () => queries.refetchQueries({ type: 'active' });
  window.__online = value => onlineManager.setOnline(value);
  window.__paused = () => queries.getQueryCache().findAll().some(query => query.isActive() && query.state.fetchStatus === 'paused');
  const list = (input, options) => new Promise((resolve, reject) => {
    const request = { pageNumber: input.pageNumber, aborted: false }; window.__requests.push(request);
    const timer = setTimeout(() => {
      request.completed = true;
      if (window.__fail) return reject(new Error('分页测试读取失败'));
      const totalPages = window.__totalPages, pageNumber = Math.max(1, Math.min(input.pageNumber, totalPages));
      resolve({ items: [], rows: [], pageNumber: ['announcements', 'notifications'].includes(mode) ? input.pageNumber : pageNumber, pageSize: input.pageSize, totalPages,
        totalCount: totalPages * input.pageSize, hasNextPage: pageNumber < totalPages, hasPreviousPage: pageNumber > 1 });
    }, 180);
    options?.signal?.addEventListener('abort', () => {
      request.aborted = true; clearTimeout(timer); reject(new DOMException('Aborted', 'AbortError'));
    }, { once: true });
  });
  const client = { listInvoices: list, listPayments: list, listJobs: list, listQueriedInvoices: list,
    listAuditLogs: list, listSingleWindowOperationCenter: list,
    listBusinessAttachments: async (...args) => ({ page: await list(...args) }),
    listBusinessAttachmentCategories: async () => ({ items: [] }),
    getWorklist: async (...args) => ({ page: await list(...args), sources: [] }),
    listAnnouncements: list, listNotifications: list, getNotificationUnreadCount: async () => ({ unreadCount: 0 }),
    queryReportTemplateV3ImageResources: list, listUserReportTemplates: list, listReportTemplates: async () => [],
    listCustomersPage: mode === 'master' ? list : async () => ({ items: [] }),
    getSettings: async () => ({ settings: {} }) };
  function Templates() {
    const { userTemplateDirectory: d, userTemplatesQuery: q } = useReportTemplateWorkspaceQueries({ client, reportType: 'ExportDocument', enabled: true,
      includeDesignerData: false, includeArchived: false, canPreviewInvoiceSource: false, canPreviewPaymentSource: false, selectedUserTemplateId: 0, selectedTemplatePath: '' });
    return <>{q.isError && <p>{q.error.message}</p>}<ListPaginationControls pageNumber={d.pageNumber} pageSize={50} totalPages={d.totalPages} totalCount={d.totalCount}
      pageSizeOptions={[50]} isBusy={d.loading} onPageChange={d.onPageChange} onPageSizeChange={() => {}} /></>;
  }
  const components = { invoices: InvoiceListPage, payments: PaymentListPage, jobs: JobCenterPage,
    query: QueryPage, audit: AuditLogPage, 'single-window': SingleWindowOperationCenterPage, master: MasterDataListPage,
    attachments: BusinessAttachmentsPage, worklist: WorklistPage, announcements: AnnouncementsPage, notifications: NotificationsPage,
    resources: ReportImageResourceGallery, templates: Templates };
  const Component = components[mode];
  const permissions = ['document.invoices', 'document.payments', 'document.jobs', 'document.query', 'document.single-window', 'document.master-data', 'office.announcements', 'office.notifications']
    .map(resourceKey => ({ resourceKey, action: 'view', dataScope: 'all' }));
  createRoot(document.getElementById('root')).render(<MemoryRouter><QueryClientProvider client={queries}>
    <PermissionAccessProvider grants={[]} permissions={permissions} subject={{ id: 1 }} canManageSettings={false}>
      <ConfirmationProvider><main><h1>列表分页验收</h1><Component client={client} businessDate='2026-10-06'
        user={{ id: 1, capabilities: { permissions }, businessTimeZone: 'Asia/Shanghai' }} editable={false} resources={[]} onChoose={() => {}}
        config={getMasterDataConfig('customers')} canOperate={false} canManage={false} canViewMasterData canViewHsCodes={false}/></main></ConfirmationProvider>
    </PermissionAccessProvider></QueryClientProvider></MemoryRouter>);
` }, outfile: path.join(output, 'app.js'), bundle: true, platform: 'browser', format: 'esm', jsx: 'automatic', logLevel: 'silent' });
const server = http.createServer((request, response) => {
  const name = new URL(request.url, 'http://localhost').pathname.slice(1);
  if (!name) {
    response.setHeader('Content-Type', 'text/html; charset=utf-8');
    response.end('<!doctype html><html lang="zh-CN"><meta name="viewport" content="width=device-width, initial-scale=1"><title>列表分页验收</title><link rel="stylesheet" href="/app.css"><div id="root"></div><script type="module" src="/app.js"></script></html>');
  } else if (['app.js', 'app.css'].includes(name)) {
    response.setHeader('Content-Type', name.endsWith('.js') ? 'text/javascript' : 'text/css');
    response.end(fs.readFileSync(path.join(output, name)));
  } else response.writeHead(404).end();
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
let context;
const results = [];
try {
  context = await require('playwright').chromium.launchPersistentContext(path.join(output, 'Chrome'), {
    executablePath: locateChromeForTesting(repo), headless: true, viewport: { width: 1280, height: 900 },
  });
  for (const mode of ['invoices', 'payments', 'jobs', 'query', 'audit', 'single-window', 'master', 'attachments', 'worklist', 'announcements', 'notifications', 'resources', 'templates']) {
    const page = await context.newPage();
    page.setDefaultTimeout(5000);
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto(`http://127.0.0.1:${server.address().port}/?mode=${mode}`);
    if (mode === 'resources') await page.getByText('图片资源库', { exact: true }).click();
    const pager = page.locator(mode === 'resources' ? '.report-designer-v3-resource-card-actions' : '.pagination-bar');
    const waitPage = async number => {
      if (mode === 'resources') {
        await page.waitForFunction(number => window.__requests.at(-1)?.completed && window.__requests.at(-1).pageNumber === number &&
          (window.__totalPages === 1 || document.querySelector('.report-designer-v3-resource-card-actions span')?.textContent.startsWith(number + ' /')), number);
        return;
      }
      await page.waitForFunction(number => document.querySelector('.pagination-bar')?.textContent.includes('第 ' + number + ' /'), number);
      await pager.getByRole('button', { name: '跳转', exact: true }).waitFor({ state: 'visible' });
      await page.waitForFunction(() => !document.querySelector('.page-jump-control button')?.disabled);
    };
    try {
      await waitPage(1);
      await pager.getByRole('button', { name: '下一页', exact: true }).click();
      await waitPage(2);
      await pager.getByRole('button', { name: mode === 'resources' ? '下一页' : '末页', exact: true }).click();
      await waitPage(3);
      assert.deepEqual(await page.evaluate(() => window.__requests.map(r => r.pageNumber)), [1, 2, 3], mode + ': no rollback requests');
      assert.equal(await page.evaluate(() => window.__requests.some(r => r.aborted)), false, mode + ': requested pages must not be cancelled');
      await page.evaluate(async () => { window.__fail = true; await window.__refresh(); });
      assert(await page.locator('body').innerText().then(text => text.includes('分页测试读取失败')), mode + ': query errors remain visible');
      assert.equal(await page.evaluate(() => window.__requests.at(-1).pageNumber), 3, mode + ': failed refresh retains requested page');
      await page.evaluate(async () => { window.__fail = false; await window.__refresh(); });
      await waitPage(3);
      await page.evaluate(async () => { window.__totalPages = 1; await window.__refresh(); });
      await waitPage(1);
      await page.evaluate(async () => { window.__totalPages = 3; await window.__refresh(); });
      // Refreshing the current page leaves other pages in the production freshness window.
      if (mode === 'resources') { await pager.getByRole('button', { name: '下一页', exact: true }).click(); await waitPage(2); }
      await page.evaluate(() => window.__online(false));
      await pager.getByRole('button', { name: mode === 'resources' ? '下一页' : '末页', exact: true }).click();
      await page.waitForFunction(() => window.__paused());
      await page.evaluate(() => window.__online(true));
      await waitPage(3);
      assert.equal(await page.evaluate(() => window.__requests.at(-1).aborted), false, mode + ': stale corrected-page cache cannot cancel a new request');
      assert.deepEqual(errors, [], mode + ': React errors');
      await page.screenshot({ path: path.join(output, mode + '.png'), fullPage: true });
      results.push({ mode, passed: true });
    } catch (error) {
      results.push({ mode, passed: false, error: error.message, requests: await page.evaluate(() => window.__requests), errors });
      await page.screenshot({ path: path.join(output, mode + '-failed.png'), fullPage: true });
    } finally { await page.close(); }
  }
  fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify(results, null, 2));
  assert(results.every(result => result.passed), JSON.stringify(results, null, 2));
  console.log(`List pagination: ${results.length} React lists passed delayed navigation, failed refresh/retry, server page correction, fresh-cache recovery and offline resume.`);
} finally {
  await context?.close();
  await new Promise(resolve => server.close(resolve));
}

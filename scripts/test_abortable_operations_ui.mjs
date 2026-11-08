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
  import React, { useMemo, useState } from 'react';
  import { createRoot } from 'react-dom/client';
  import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
  import { useAbortableOperation } from ${source('ui/useAbortableOperation.ts')};
  import { useOfficeOperation } from ${source('features/office/useOfficeData.ts')};
  import { AttachmentUploadPanel } from ${source('ui/AttachmentUploadPanel.tsx')};
  import { ExporterSealField } from ${source('features/master-data/ExporterSealField.tsx')};
  import { MemoryRouter } from 'react-router-dom';
  import { ConfirmationProvider } from ${source('ui/ConfirmationProvider.tsx')};
  import { UnsavedChangesProvider } from ${source('ui/unsavedChangesGuard.tsx')};
  import { PermissionAccessProvider } from ${source('app/PermissionAccessContext.tsx')};
  import { SupplierProductLinksPanel } from ${source('features/suppliers/SupplierProductLinksPanel.tsx')};
  import { SupplierAssessmentsPanel } from ${source('features/suppliers/SupplierAssessmentsPanel.tsx')};
  import { SalesOpportunityPage } from ${source('features/opportunities/SalesOpportunityPage.tsx')};
  import { useReportTemplatePreviewMutations } from ${source('features/reports/useReportTemplatePreviewMutations.ts')};
  import { InvoiceReportPreviewPanel } from ${source('features/invoices/InvoiceReportPreviewPanel.tsx')};
  import { useInvoiceDocumentPackageWorkspace } from ${source('features/invoices/useInvoiceDocumentPackageWorkspace.ts')};
  window.pending = []; window.results = []; window.completed = []; window.refreshes = [];
  window.attachmentUploads = [];
  window.attachmentRecord = { versionNumber: 1, attachments: Array.from({ length: window.mode === 'attachments-partial' ? 18 : 20 }, () => ({ sizeBytes: 1 })) };
  const delayed = signal => new Promise((resolve, reject) => window.pending.push({ signal, resolve, reject }));
  if (window.mode === 'seal') {
    window.isTauri = true;
    window.__TAURI_INTERNALS__ = { invoke: command => new Promise((resolve, reject) => window.pending.push({ command, resolve, reject })) };
  }
  const lookup = (input, options, kind = 'primary') => new Promise((resolve, reject) => window.pending.push({ ...input, kind, signal: options.signal, resolve, reject }));
  const client = { searchSupplierProductOptions: lookup, queryCrmCustomers: lookup, listProducts: (input, options) => lookup(input, options, 'product'),
    listReportTemplates: async () => [{templatePath:'builtin:invoice_template.dtpl',displayName:'发票',withSealDefault:true}],
    getSettings: async () => ({}),
    previewInvoiceReportDraftHtml: lookup, previewInvoiceReportHtml: lookup, previewInvoiceDocumentPackageHtml: lookup,
    listSupplierAssessments: (input, options) => lookup(input, options, 'assessment-list'),
    createSupplierAssessment: (input, options) => lookup(input, options, 'assessment-create'),
    updateSupplierAssessment: (input, options) => lookup(input, options, 'assessment-update'),
    querySupplierProductLinks: async () => ({ items: [], totalCount: 0, totalPages: 1 }),
    querySalesOpportunities: async () => ({ items: [], totalCount: 0, totalPages: 1 }) };
  function Generic({ scope }) {
    const run = useAbortableOperation(scope, { cancelPrevious: window.mode === 'latest' });
    return <button onClick={() => void run(delayed).then(value => window.results.push({ value }),
      error => window.results.push({ name: error.name, message: error.message }))}>开始读取</button>;
  }
  function Invoice({ scope }) {
    const draft = useMemo(() => ({id:7,invoiceNo:'draft-'+scope}), [scope]);
    return <InvoiceReportPreviewPanel client={client} invoiceId={7} invoiceDraft={draft}/>;
  }
  function Package({ scope }) {
    const [revision, setRevision] = useState(0);
    const templates = useMemo(() => Array.from({length:revision+1}, (_, index) => ({templatePath:'template-'+index,displayName:'模板'+index,withSealDefault:true})), [revision]);
    const workspace = useInvoiceDocumentPackageWorkspace({client, invoiceId:7,sourceRevision:scope,previewAllowed:true, templates,desktopAvailable:false,defaultExportDirectory:'',feedback:{clear(){},showError(message){window.results.push(message)},showJob(){},showStatus(){}},onPreviewGenerated(){}});
    window.packageState = workspace.templateState;
    return <><button onClick={() => workspace.changeTemplateSelected('template-0',false)}>取消第一份</button>
      <button onClick={() => workspace.changeTemplateSeal('template-0',false)}>取消第一枚章</button>
      <button onClick={() => setRevision(value=>value+1)}>刷新模板目录</button>
      <button onClick={workspace.previewPackage}>组合预览</button><output>{workspace.preview?.items?.[0]?.html ?? ''}</output></>;
  }
  function Preview({ scope, allowed }) {
    const [formatMode, setMode] = useState('sample');
    const [reportType, setReportType] = useState('ExportDocument');
    const previewClient = {
      previewReportTemplateContent: (input, options) => lookup(input, options, 'sample'),
      previewInvoiceReportHtml: (input, options) => lookup(input, options, 'invoice'),
      previewPaymentVoucherHtml: (input, options) => lookup(input, options, 'payment'),
    };
    const { previewMutation, page, setPreviewFormat } = useReportTemplatePreviewMutations({
      client: previewClient, reportType, selectedTemplatePath: 'builtin:template.dtpl', content: 'draft-'+scope,
      sampleProfile: 'sample', mode: formatMode, canDesignTemplates: allowed, withSeal: true,
      previewInvoiceId: 7, previewPaymentId: 9,
      onPreviewed: response => window.completed.push(response), onError: error => window.results.push(error.message),
    });
    return <><button onClick={() => previewMutation.mutate({content:'committed-'+scope})}>生成预览</button>
      <button onClick={() => previewMutation.mutate({pageIndex:1,previewRevision:page.previewRevision})}>预览下一页</button>
      <button onClick={() => setMode('savedSource')}>当前单据</button>
      <button onClick={() => setReportType('PaymentVoucher')}>付款模板</button>
      <button onClick={() => setPreviewFormat('PdfPage')}>实际PDF</button>
      <output>{page.pageIndex+'/'+page.pageCount}</output></>;
  }
  function Office() {
    const operation = useOfficeOperation();
    return <><button disabled={operation.busy} onClick={() => void operation.run(delayed,
      value => window.completed.push(value))}>保存</button><output>{operation.error}</output></>;
  }
  function Attachments() {
    const [record, setRecord] = useState(window.attachmentRecord);
    const operation = useOfficeOperation();
    return <><button onClick={() => setRecord(window.attachmentRecord)}>刷新附件记录</button>
      <AttachmentUploadPanel record={record} operation={operation} upload={async (current, body) => {
        window.attachmentUploads.push(current.versionNumber);
        if (current.versionNumber !== window.attachmentRecord.versionNumber) throw new Error('过时版本');
        if (window.mode === 'attachments-partial' && window.attachmentUploads.length === 2) throw new Error('第二个附件未完成');
        return window.attachmentRecord = { versionNumber: current.versionNumber + 1,
          attachments: [...current.attachments, { sizeBytes: body.get('file').size }] };
      }}/><output>{operation.error}</output></>;
  }
  function App() {
    const [scope, setScope] = useState(0), [open, setOpen] = useState(true), [sealDisabled, setSealDisabled] = useState(false);
    const refresh = () => window.mode.startsWith('attachments') ? Promise.resolve('updated') : new Promise(resolve => window.refreshes.push(resolve));
    useQuery({ queryKey: ['office', 'test'], initialData: 'before', staleTime: Infinity,
      queryFn: refresh });
    useQuery({ queryKey: ['worklist', 'test'], initialData: 'before', staleTime: Infinity,
      queryFn: refresh });
    return <><button onClick={() => setScope(value => value + 1)}>切换资料</button>
      <button onClick={() => setOpen(false)}>离开编辑页</button>
      <button onClick={() => setSealDisabled(true)}>停用印章操作</button>
      {open ? window.mode === 'supplier' ? <SupplierProductLinksPanel client={client} supplierId={scope + 1} supplierName='供应商' canEdit/>
        : window.mode === 'assessment' ? <SupplierAssessmentsPanel client={client} businessDate='2026-10-12' supplierId={scope + 1} supplierName={'供应商'+(scope+1)} canCreate canEdit canApprove canDelete/>
        : window.mode.startsWith('sales') ? <SalesOpportunityPage client={client} businessTimeZone='Asia/Shanghai'/>
        : window.mode === 'seal' ? <ExporterSealField label='单证章路径' scopeKey={String(scope)} value='' actionDisabled={sealDisabled}
          onUploadFile={async file => window.completed.push({name:file.name,type:file.type,bytes:Array.from(new Uint8Array(await file.arrayBuffer()))})}
          onError={error => window.results.push(error.message)}/>
        : window.mode === 'invoice-preview' ? <Invoice scope={scope}/>
        : window.mode === 'package-preview' ? <Package scope={scope}/>
        : window.mode === 'preview' ? <Preview scope={scope} allowed={!sealDisabled}/>
        : window.mode === 'office' ? <Office/> : window.mode.startsWith('attachments') ? <Attachments/> : <Generic scope={scope}/> : <p>编辑页已关闭</p>}</>;
  }
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } } });
  window.queries = queries;
  const permissions = ['sales.opportunities', 'sales.quotes'].map(resourceKey => ({ resourceKey, action: 'create', dataScope: 'all' }));
  permissions.push(...['preview','print'].map(action=>({resourceKey:'document.invoice-output',action,dataScope:'all'})),{resourceKey:'common.report-catalog',action:'view',dataScope:'all'});
  createRoot(document.getElementById('root')).render(<React.StrictMode><MemoryRouter initialEntries={['/?view=editor']}><QueryClientProvider client={queries}>
    <PermissionAccessProvider permissions={permissions} subject={{id:1,companyScope:'DEFAULT',departmentId:'GENERAL'}}><ConfirmationProvider><UnsavedChangesProvider><App/></UnsavedChangesProvider></ConfirmationProvider></PermissionAccessProvider>
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
  for (const reject of [false, true]) {
    await scenario('invoice-preview-discards-old-draft-'+reject, 'invoice-preview', async page => {
      await page.getByRole('button', {name:'预览',exact:true}).click();
      await page.waitForFunction(() => window.pending.length === 1);
      await page.getByRole('button',{name:'切换资料',exact:true}).click();
      await page.waitForFunction(() => window.pending[0].signal.aborted);
      await page.evaluate(reject => reject ? window.pending[0].reject(new Error('obsolete error')) : window.pending[0].resolve({html:'OLD-DRAFT-A'}), reject);
      await page.waitForTimeout(50);
      assert.equal(await page.getByRole('button',{name:'打印',exact:true}).isEnabled(), false);
      assert(!await page.locator('body').innerText().then(text=>text.includes('obsolete error')));
      for (let index=1;index<=4;index++) {
        await page.getByRole('button',{name:'预览',exact:true}).click();
        await page.waitForFunction(count=>window.pending.length===count,index+1);
        await page.evaluate(index=>window.pending[index].resolve({html:'<!doctype html><html><body>current-'+index+'</body></html>'}),index);
        await page.waitForFunction(()=>[...document.querySelectorAll('button')].some(button=>button.textContent.includes('打印')&&!button.disabled));
      }
      assert.equal(await page.evaluate(()=>window.queries.getMutationCache().getAll().filter(m=>m.state.data?.html).length),0);
    });
  }
  await scenario('package-refresh-preserves-selection-and-cancels-preview','package-preview',async page=>{
    await page.getByRole('button',{name:'取消第一份',exact:true}).click();
    await page.getByRole('button',{name:'取消第一枚章',exact:true}).click();
    await page.getByRole('button',{name:'刷新模板目录',exact:true}).click();
    await page.waitForFunction(()=>Object.keys(window.packageState).length===2);
    assert.deepEqual(await page.evaluate(()=>window.packageState['template-0']),{selected:false,withSeal:false});
    assert.deepEqual(await page.evaluate(()=>window.packageState['template-1']),{selected:true,withSeal:true});
    await page.getByRole('button',{name:'组合预览',exact:true}).click();
    await page.waitForFunction(()=>window.pending.length===1);
    await page.getByRole('button',{name:'切换资料',exact:true}).click();
    await page.waitForFunction(()=>window.pending[0].signal.aborted);
    await page.evaluate(()=>window.pending[0].resolve({items:[{html:'obsolete-package'}]}));
    await page.waitForTimeout(50);
    assert.equal(await page.locator('output').innerText(),'');
  });
  for (const kind of ['sample', 'invoice', 'payment']) {
    await scenario('preview-request-and-pagination-'+kind, 'preview', async page => {
      if (kind !== 'sample') await page.getByRole('button', {name:'当前单据',exact:true}).click();
      if (kind === 'payment') await page.getByRole('button', {name:'付款模板',exact:true}).click();
      await page.getByRole('button', {name:'实际PDF',exact:true}).click();
      await page.getByRole('button', {name:'生成预览',exact:true}).click();
      await page.waitForFunction(() => window.pending.length === 1);
      const request = await page.evaluate(() => ({kind:window.pending[0].kind,body:window.pending[0].body}));
      assert.equal(request.kind, kind); assert.equal(request.body.content, 'committed-0');
      assert.equal(request.body.previewFormat, 'PdfPage'); assert.equal(request.body.pageIndex, 0);
      await page.evaluate(() => window.pending[0].resolve({html:'first',pageIndex:0,pageCount:2,previewRevision:'revision'}));
      await page.waitForFunction(() => window.completed.length === 1);
      await page.getByRole('button', {name:'预览下一页',exact:true}).click();
      await page.waitForFunction(() => window.pending.length === 2);
      assert.deepEqual(await page.evaluate(() => [window.pending[1].kind,window.pending[1].body.pageIndex,window.pending[1].body.previewRevision]), [kind,1,'revision']);
    });
  }
  for (const boundary of ['scope','unmount','disabled','mode','format','report']) for (const reject of [false,true]) {
    await scenario('preview-cancel-'+boundary+'-'+reject, 'preview', async page => {
      await page.getByRole('button', {name:'生成预览',exact:true}).click();
      await page.waitForFunction(() => window.pending.length === 1);
      await page.getByRole('button', {name:{scope:'切换资料',unmount:'离开编辑页',disabled:'停用印章操作',mode:'当前单据',format:'实际PDF',report:'付款模板'}[boundary],exact:true}).click();
      await page.waitForFunction(() => window.pending[0].signal.aborted);
      await page.evaluate(reject => reject ? window.pending[0].reject(new Error('迟到错误')) : window.pending[0].resolve({html:'迟到预览',pageCount:99}), reject);
      await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
      assert.deepEqual(await page.evaluate(() => [window.completed,window.results]), [[],[]]);
    });
  }
  for (const phase of ['select', 'read']) for (const boundary of ['none', 'scope', 'unmount', 'disabled']) for (const reject of [false, true]) {
    await scenario(`seal-${phase}-${boundary}-${reject ? 'failure' : 'success'}`, 'seal', async page => {
      await page.getByRole('button', { name: '选择并上传单证章图片', exact: true }).click();
      await page.waitForFunction(() => window.pending.length === 1);
      if (phase === 'read') {
        await page.evaluate(() => window.pending[0].resolve('E:/selected/中文印章.png'));
        await page.waitForFunction(() => window.pending.length === 2);
        assert.equal(await page.evaluate(() => window.pending[1].command), 'read_exporter_seal_image_file');
      }
      if (boundary !== 'none') await page.getByRole('button', { name: {scope:'切换资料',unmount:'离开编辑页',disabled:'停用印章操作'}[boundary], exact: true }).click();
      await page.evaluate(({phase,reject}) => {
        const request = window.pending.at(-1);
        if (reject) request.reject(new Error('印章读取失败'));
        else request.resolve(phase === 'select' ? 'E:/selected/中文印章.png' : new Uint8Array([137,80,78,71]).buffer);
      }, {phase,reject});
      if (boundary === 'none' && !reject && phase === 'select') {
        await page.waitForFunction(() => window.pending.length === 2);
        await page.evaluate(() => window.pending[1].resolve(new Uint8Array([137,80,78,71]).buffer));
      }
      await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
      assert.deepEqual(await page.evaluate(() => window.completed), boundary === 'none' && !reject ? [{name:'中文印章.png',type:'image/png',bytes:[137,80,78,71]}] : []);
      assert.deepEqual(await page.evaluate(() => window.results), boundary === 'none' && reject ? ['印章读取失败'] : []);
      if (phase === 'select' && boundary !== 'none') assert.equal(await page.evaluate(() => window.pending.length), 1);
    });
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
  const attachmentFile = name => ({ name, mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.7\n%%EOF') });
  for (const boundary of ['scope', 'unmount', 'refresh-failure']) {
    await scenario(`supplier-assessment-save-${boundary}`, 'assessment', async page => {
      await page.waitForFunction(() => window.pending.some(item => item.kind === 'assessment-list' && !item.signal.aborted));
      await page.evaluate(() => window.pending.filter(item => item.kind === 'assessment-list').forEach(item => item.resolve([])));
      await page.getByRole('button', { name: '记录新评价', exact: true }).click();
      await page.locator('form').evaluate(form => { form.requestSubmit(); form.requestSubmit(); });
      await page.waitForFunction(() => window.pending.some(item => item.kind === 'assessment-create'));
      assert.equal(await page.evaluate(() => window.pending.filter(item => item.kind === 'assessment-create').length), 1, 'repeated submission starts only one write');
      assert.equal(await page.getByRole('button', { name: '保存评价草稿' }).isDisabled(), true);
      if (boundary !== 'refresh-failure') {
        await page.getByRole('button', { name: boundary === 'scope' ? '切换资料' : '离开编辑页', exact: true }).click();
        await page.waitForFunction(() => window.pending.find(item => item.kind === 'assessment-create').signal.aborted);
      }
      const saved = { id: 10, supplierCompanyId: 1, assessmentDate: '2026-10-12', assessmentKind: '定期评价',
        qualityScore: 4, deliveryScore: 4, serviceScore: 4, priceScore: 4, averageScore: 4,
        conclusion: '合格', status: 'Draft', versionNumber: 1, notes: '已保存的评价' };
      await page.evaluate(saved => window.pending.find(item => item.kind === 'assessment-create').resolve(saved), saved);
      if (boundary === 'refresh-failure') {
        await page.waitForFunction(() => window.pending.filter(item => item.kind === 'assessment-list').length === 3);
        await page.evaluate(() => window.pending.filter(item => item.kind === 'assessment-list').at(-1).reject(new Error('网络中断')));
        await page.getByText('供应商评价已记录。列表刷新失败：网络中断', { exact: true }).waitFor();
        assert.equal(await page.locator('textarea[name=notes]').inputValue(), saved.notes, 'saved result is retained even if the refresh fails');
        await page.getByRole('button', { name: '保存评价草稿' }).click();
        await page.waitForFunction(() => window.pending.some(item => item.kind === 'assessment-update'));
        assert.equal(await page.evaluate(() => window.pending.filter(item => item.kind === 'assessment-create').length), 1, 'retrying a saved result updates it instead of creating a duplicate');
      } else {
        await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
        assert.doesNotMatch(await page.locator('body').textContent(), /已保存的评价|供应商评价已记录/);
        assert.equal(await page.evaluate(() => window.pending.filter(item => item.kind === 'assessment-list' && item.supplierId === 1).length), 2, 'canceled saves do not start obsolete list reloads');
      }
    });
  }
  await scenario('attachment-capacity-follows-refreshed-record', 'attachments', async page => {
    const picker = page.locator('input[type=file]');
    assert.equal(await picker.isDisabled(), true);
    await page.evaluate(() => { window.attachmentRecord = { versionNumber: 2, attachments: window.attachmentRecord.attachments.slice(1) }; });
    await page.getByRole('button', { name: '刷新附件记录' }).click();
    await page.waitForFunction(() => !document.querySelector('input[type=file]').disabled);
    await picker.setInputFiles(attachmentFile('replacement.pdf'));
    await page.getByRole('button', { name: '上传所选附件（1）', exact: true }).click();
    await page.waitForFunction(() => window.attachmentRecord.attachments.length === 20);
    assert.deepEqual(await page.evaluate(() => window.attachmentUploads), [2]);
  });
  await scenario('attachment-partial-upload-retains-newer-capacity-and-version', 'attachments-partial', async page => {
    const picker = page.locator('input[type=file]');
    await picker.setInputFiles(['first.pdf', 'second.pdf'].map(attachmentFile));
    await page.getByRole('button', { name: '上传所选附件（2）', exact: true }).click();
    await page.getByText('第二个附件未完成', { exact: true }).waitFor();
    await picker.setInputFiles(attachmentFile('third.pdf'));
    await page.getByText(/当前最多再选 1 个文档附件/).waitFor();
    assert.equal(await page.locator('.attachment-draft > ul > li').count(), 1);
    await page.getByRole('button', { name: '上传所选附件（1）', exact: true }).click();
    await page.waitForFunction(() => window.attachmentRecord.attachments.length === 20);
    assert.deepEqual(await page.evaluate(() => window.attachmentUploads), [1, 2, 2]);
  });
  fs.writeFileSync(path.join(output, 'summary.json'), JSON.stringify({ results, failures }, null, 2));
  assert.deepEqual(failures, []);
  process.stdout.write(`Abortable operation UI passed (${results.length} cases).\n`);
} finally { await context?.close(); }

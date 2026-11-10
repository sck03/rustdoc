import assert from 'node:assert/strict';
import { verifyDesktopClientDirectories, verifyOfficialClientTransport } from './native-single-window-directories.mjs';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { captureScreenshot } from './web-runtime-browser-session.mjs';
import { waitFor } from './web-runtime-smoke-common.mjs';
import { withNativeFilePicker } from './native-file-picker.mjs';

export async function verifyDesktopSingleWindow({ run, cdp, output, token, invoiceId }) {
  const { createExportDocManagerApiClient } = await import(pathToFileURL(path.join(output, 'entry-print-model.mjs')).href);
  const context = await run("window.__TAURI_INTERNALS__.invoke('get_desktop_runtime_context')");
  const client = createExportDocManagerApiClient({ baseUrl: context.apiBaseUrl, accessToken: token, desktopAccessToken: context.desktopAccessToken });
  const wait = expression => waitFor(() => run(`Boolean(${expression})`), 30000, expression);
  const conflict = operation => assert.rejects(operation, error => error.status === 409);
  const initial = await client.getSingleWindowReferenceCatalog();
  const saved = await client.updateSingleWindowReferenceCatalog({ body: { catalog: initial.catalog, expectedRevision: initial.revision } });
  assert(saved.revision > initial.revision);
  await conflict(client.resetSingleWindowReferenceCatalog({ expectedRevision: initial.revision }));
  await conflict(client.updateSingleWindowReferenceCatalog({ body: { catalog: initial.catalog, expectedRevision: initial.revision } }));
  await conflict(client.importSingleWindowReferenceCatalogJson({ body: new Blob([JSON.stringify(initial.catalog)], { type: 'application/json' }), expectedRevision: initial.revision }));
  const producer = await client.createCustomsCooProducerProfile({ body: { profile: { prdcEtpsName: 'DESKTOP-VERSION-CHECK', ciqRegNo: 'TEST-001' } } });
  const updated = await client.updateCustomsCooProducerProfile({ id: producer.id, body: { profile: { prdcEtpsName: 'DESKTOP-VERSION-CHECK', ciqRegNo: 'TEST-002' }, expectedVersion: producer.profile.versionNumber } });
  await conflict(client.deleteCustomsCooProducerProfile({ id: producer.id, expectedVersion: producer.profile.versionNumber }));
  const page = await client.listCustomsCooProducerProfiles({ keyword: 'DESKTOP-VERSION-CHECK', pageNumber: 1, pageSize: 1 });
  assert.equal(page.totalCount, 1); assert.equal(page.items.length, 1);
  await client.deleteCustomsCooProducerProfile({ id: producer.id, expectedVersion: updated.profile.versionNumber });
  const results = [];
  const agentDraft = await client.getAgentConsignmentDocument({ sourceId: invoiceId });
  await client.saveAgentConsignmentDocument({ sourceId: invoiceId, body: {
    ...agentDraft, expectedDraftRevision: agentDraft.draftRevision, copCusCode: '1234567890',
    gName: '测试货物', codeTS: '6109100000', declTotal: '114142', ieDate: '20261109',
    tradeMode: '0110', oriCountry: '142', tradeCode: '1234567890', agentCode: '1234567890',
  } });
  for (const [kind, label, fieldName, read] of [
    ['acd', '主要货物名称', 'gName', () => client.getAgentConsignmentDocument({ sourceId: invoiceId })],
    ['coo', '原产地证编号', 'certNo', () => client.getCustomsCooDocument({ sourceId: invoiceId })],
  ]) {
    await run(`location.hash=${JSON.stringify(`#/single-window/${kind}/${invoiceId}`)}; true`);
    const field = `[...document.querySelectorAll('label')].find(e=>e.textContent.trim().startsWith(${JSON.stringify(label)}))?.querySelector('input')`;
    const save = "[...document.querySelectorAll('button')].find(e=>e.textContent.trim()==='保存草稿')";
    await wait(`${field} && ${save} && !${save}.disabled`);
    const route = await run('location.hash');
    const sections = await run("[...document.querySelectorAll('.single-window-section-nav-item')].map(button=>button.getAttribute('aria-controls'))");
    assert(sections.length > 0);
    for (const id of sections) {
      await run(`document.querySelector('[aria-controls="'+${JSON.stringify(id)}+'"].single-window-section-nav-item').click(); true`);
      assert.equal(await run('location.hash'), route);
      assert(await run(`document.getElementById(${JSON.stringify(id)}).checkVisibility()`));
    }
    const edit = async text => {
      await run(`${field}.focus(); ${field}.select(); true`);
      await cdp.send('Input.insertText', { text });
      await wait(`${field}.value===${JSON.stringify(text)}`);
    };
    // Pause the real HTTP response after SQLite commits, then continue editing.
    await edit('SUBMITTED');
    await cdp.send('Fetch.enable', { patterns: [{ urlPattern: `*/api/single-window/${kind}/${invoiceId}`, requestStage: 'Response' }] });
    let paused;
    try {
      const response = (async () => {
        let next = cdp.waitForEvent('Fetch.requestPaused');
        for (;;) {
          const event = await next;
          if (event.request.method === 'PUT') return event;
          // WebView2 may perform a CORS preflight before the actual write.
          next = cdp.waitForEvent('Fetch.requestPaused');
          await cdp.send('Fetch.continueRequest', { requestId: event.requestId });
        }
      })();
      await run(`${save}.click(); true`);
      paused = await response;
      assert.equal(paused.responseStatusCode, 200);
      await edit('CONTINUED');
      await cdp.send('Fetch.continueRequest', { requestId: paused.requestId });
      paused = null;
      await wait(`!${save}.disabled`);
      assert.equal(await run(`${field}.value`), 'CONTINUED');
    } finally {
      if (paused) await cdp.send('Fetch.continueRequest', { requestId: paused.requestId });
      await cdp.send('Fetch.disable');
    }
    const before = await read(); assert.equal(before[fieldName], 'SUBMITTED');
    await run(`${save}.click(); true`);
    await waitFor(async () => (await read())[fieldName] === 'CONTINUED', 30000, `${kind} continued draft was not saved`);
    await wait(`!${save}.disabled`);
    await run(`${field}.focus(); ${field}.select(); true`);
    await cdp.send('Input.imeSetComposition', { text: '中文', selectionStart: 2, selectionEnd: 2 });
    await cdp.send('Input.insertText', { text: kind === 'acd' ? '中文货物' : 'COO-中文' });
    await run(`${save}.click(); true`);
    const expected = kind === 'acd' ? '中文货物' : 'COO-中文';
    await waitFor(async () => (await read())[fieldName] === expected, 30000, `${kind} IME roundtrip failed`);
    await wait(`!${save}.disabled`);
    await captureScreenshot(cdp, path.join(output, `Full-single-window-${kind}.png`), { captureBeyondViewport: false });
    results.push({ kind, continuedInput: true, imeRoundtrip: true, draftRevision: (await read()).draftRevision });
  }
  const workbook = path.resolve(import.meta.dirname, '../../crates/export-doc-excel/tests/fixtures/last-row-formulas.xls');
  const preview = await client.previewSingleWindowExcelImport({ fileName: 'customer-format.xls', body: new Blob([fs.readFileSync(workbook)]) });
  assert.equal(preview.success, true, JSON.stringify(preview.errors));
  const { settings } = await client.getSettings();
  const scheme = { ...settings.excelImport, ...preview.analysisReport.itemTable.columns,
    schemeName: 'Desktop-Excel', sheetName: preview.analysisReport.selectedWorksheetName,
    itemsStartRow: preview.analysisReport.itemTable.dataStartRow, itemsEndRow: 0 };
  for (const key of Object.keys(scheme)) if (key.endsWith('Cell')) scheme[key] = '';
  await client.updateSettings({ body: { settings: { ...settings, excelImportSchemes: [scheme] }, updateSecrets: false } });
  await run("location.hash='#/settings?section=excelImport'; location.reload(); true");
  const advanced = "[...document.querySelectorAll('summary')].find(e=>e.textContent==='高级导入方案')";
  await wait(advanced);
  assert.equal(await run(`${advanced}.parentElement.open`), false);
  await run(`${advanced}.click(); true`);
  await wait("[...document.querySelectorAll('label')].some(e=>e.textContent.includes('工作表名称')&&e.querySelector('input'))");
  await captureScreenshot(cdp, path.join(output, 'Full-excel-import-settings.png'), { captureBeyondViewport: false });
  await run("location.hash='#/settings?section=system'; true");
  await wait("document.querySelector('[aria-label=\"常规与目录\"]')");
  assert(!await run("[...document.querySelectorAll('label')].some(e=>e.textContent.includes('软件名称'))"));
  const mode = "[...document.querySelectorAll('label')].find(e=>e.textContent.includes('Excel 识别方式'))?.querySelector('select')";
  const selectMode = value => run(`((select)=>{select.value=${JSON.stringify(value)};select.dispatchEvent(new Event('change',{bubbles:true}));return true;})(${mode})`);
  await run("location.hash='#/invoices'; true");
  const invoiceMode = "[...document.querySelectorAll('summary')].find(e=>e.textContent==='特殊格式 Excel 导入')";
  await wait(invoiceMode);
  await run(`${invoiceMode}.click(); true`);
  await wait(`${mode}?.querySelector('option[value="Desktop-Excel"]')`);
  assert.equal(await run(`${mode}.value`), '');
  await selectMode('Desktop-Excel');
  await captureScreenshot(cdp, path.join(output, 'Full-invoice-excel-mode.png'), { captureBeyondViewport: false });
  await withNativeFilePicker({run,command:'select_excel_file',destination:workbook}, async () => {
    await run("[...document.querySelectorAll('button')].find(e=>e.textContent.trim()==='导入 Excel').click(); true");
    await wait("location.hash.startsWith('#/invoices/new')");
    const itemsTab = "[...document.querySelectorAll('button')].find(e=>e.textContent.trim().startsWith('商品明细'))";
    await wait(itemsTab);
    await run(`${itemsTab}.click(); true`);
    await wait(`[...document.querySelectorAll('input')].some(e=>e.value===${JSON.stringify(preview.invoice.items[0].styleNo)})`);
  });
  await run("location.hash='#/single-window/sources'; true");
  const sourceField = "[...document.querySelectorAll('label')].find(e=>e.textContent.includes('资料编号 / 客户发票号'))?.querySelector('input')";
  await waitFor(() => run(`(()=>{[...document.querySelectorAll('[role="dialog"] button')].find(e=>e.textContent.trim()==='离开当前编辑页')?.click(); return Boolean(${sourceField});})()`), 30000, 'Invoice import draft did not close');
  assert(await run("[...document.querySelectorAll('button')].some(e=>e.textContent==='下载 Excel 模板'&&!e.disabled)"));
  const templatePath = path.join(output, 'single-window-import-template.xlsx');
  await withNativeFilePicker({run,command:'select_save_excel_path',destination:templatePath}, async () => {
    await run("[...document.querySelectorAll('button')].find(e=>e.textContent==='下载 Excel 模板').click(); true");
    await wait("document.body.textContent.includes('导入模板已保存。')");
  });
  assert(fs.readFileSync(templatePath).subarray(0, 2).equals(Buffer.from('PK')));
  await wait(`${mode}?.querySelector('option[value="Desktop-Excel"]')`);
  assert.equal(await run(`${mode}.value`), '');
  await selectMode('Desktop-Excel');
  const { root } = await cdp.send('DOM.getDocument');
  const { nodeId } = await cdp.send('DOM.querySelector', { nodeId: root.nodeId, selector: 'input[type=file]' });
  await cdp.send('DOM.setFileInputFiles', { nodeId, files: [workbook] });
  await wait(`document.body.textContent.includes('已识别 ${preview.invoice.items.length} 行商品')`);
  const manual = await client.previewUploadedExcelImport({ fileName: 'manual.xls', schemeName: 'Desktop-Excel', body: new Blob([fs.readFileSync(workbook)]) });
  assert.equal(manual.success, true, JSON.stringify(manual.errors));
  assert.deepEqual(manual.invoice.items.map(item=>[item.styleNo,item.quantity]), preview.invoice.items.map(item=>[item.styleNo,item.quantity]));
  await selectMode('');
  await wait("!document.body.textContent.includes('已识别 ')");
  await run(`${sourceField}.focus(); true`);
  await cdp.send('Input.insertText', { text: 'DESKTOP-MANUAL-DECLARATION' });
  await run("[...document.querySelectorAll('button')].find(e=>e.textContent.trim()==='创建申报资料').click(); true");
  await wait("document.body.textContent.includes('申报资料已创建')");
  const independent = await client.listSingleWindowSources({ keyword: 'DESKTOP-MANUAL-DECLARATION' });
  assert.equal(independent.items.length, 1);
  const sourceId = independent.items[0].id;
  await assert.rejects(client.getInvoice({ id: sourceId }), error => error.status === 404);
  await captureScreenshot(cdp, path.join(output, 'Full-single-window-sources.png'), { captureBeyondViewport: false });
  await run(`[...document.querySelectorAll('a')].find(e=>e.getAttribute('href')===${JSON.stringify(`#/single-window/coo/${sourceId}`)}).click(); true`);
  const consignee = "[...document.querySelectorAll('label')].find(e=>e.querySelector('span')?.textContent.trim()==='收货人')?.querySelector('textarea')";
  await wait(consignee);
  await run(`${consignee}.focus(); ${consignee}.select(); true`);
  await cdp.send('Input.insertText', { text: '目的地收货人 / DESTINATION CUSTOMER' });
  await wait(`${consignee}.value===${JSON.stringify('目的地收货人 / DESTINATION CUSTOMER')}`);
  await wait("[...document.querySelectorAll('button')].some(e=>e.textContent.trim()==='保存草稿'&&!e.disabled)");
  await run("[...document.querySelectorAll('button')].find(e=>e.textContent.trim()==='保存草稿').click(); true");
  await waitFor(async () => (await client.getCustomsCooDocument({ sourceId })).consignee.includes('DESTINATION CUSTOMER'), 30000, 'Independent consignee was not saved');
  await captureScreenshot(cdp, path.join(output, 'Full-single-window-independent.png'), { captureBeyondViewport: false });
  const before = await client.listSingleWindowSources({});
  assert(preview.invoice.items.length > 0);
  assert.equal((await client.listSingleWindowSources({})).totalCount, before.totalCount);
  const imported = await client.createSingleWindowSource({ body: { referenceNo: 'DESKTOP-EXCEL-DECLARATION', invoice: preview.invoice } });
  assert.equal(imported.sourceKind, 'Excel');
  assert.equal((await client.getCustomsCooDocument({ sourceId: imported.id })).items.length, preview.invoice.items.length);
  const { profile, detected } = await verifyDesktopClientDirectories({run,cdp,output,client});
  const manualAcd = await client.getAgentConsignmentDocument({ sourceId });
  const savedAcd = await client.saveAgentConsignmentDocument({ sourceId, body: { ...manualAcd, copCusCode: 'TEST000001', gName: '测试货物', codeTS: '6109100010',
    declTotal: '100', ieDate: '20261109', tradeMode: '0110', oriCountry: '142', tradeCode: '1234567890', agentCode: '0987654321' } });
  const packagePath = path.join(output, 'desktop-manual.swpkg');
  const handoff = await client.saveAgentConsignmentSubmitPackageToPath({ sourceId, body: { packagePath, stationAssignmentCode: profile.stationAssignmentCode, expectedDraftRevision: savedAcd.document.draftRevision } });
  assert(fs.statSync(packagePath).size > 0);
  await client.uploadSingleWindowSubmitPackage({ body: new Blob([fs.readFileSync(packagePath)]), fileName: 'desktop-manual.swpkg' });
  const dispatch = await client.dispatchSingleWindowBatchToClient({ body: { batchId: handoff.trackingBatchId } });
  assert.equal(fs.readdirSync(dispatch.targetDirectory).filter(name => name.endsWith('.xml')).length, 1);
  if (process.env.EXPORTDOCMANAGER_SW_CLIENT_TRANSPORT === '1') {
    await verifyOfficialClientTransport({client,profile,detected,sourceId,output});
  }
  fs.writeFileSync(path.join(output, 'single-window-results.json'), JSON.stringify({ catalogVersionConflict: true, producerVersionConflict: true, manualSource: true, independentConsignee: true,
    excelPreviewRows: preview.invoice.items.length, manualExcelUiAndInvoiceUpload: true, templateDownload: true, swpkgExportImportDispatch: true, results }, null, 2));
}

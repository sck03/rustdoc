import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { captureScreenshot } from './web-runtime-browser-session.mjs';
import { waitFor } from './web-runtime-smoke-common.mjs';

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
  const agentDraft = await client.getAgentConsignmentDocument({ invoiceId });
  await client.saveAgentConsignmentDocument({ invoiceId, body: {
    ...agentDraft, expectedDraftRevision: agentDraft.draftRevision, copCusCode: '1234567890',
    gName: '测试货物', codeTS: '6109100000', declTotal: '114142', ieDate: '20261109',
    tradeMode: '0110', oriCountry: '142', tradeCode: '1234567890', agentCode: '1234567890',
  } });
  for (const [kind, label, fieldName, read] of [
    ['acd', '主要货物名称', 'gName', () => client.getAgentConsignmentDocument({ invoiceId })],
    ['coo', '原产地证编号', 'certNo', () => client.getCustomsCooDocument({ invoiceId })],
  ]) {
    await run(`location.hash=${JSON.stringify(`#/single-window/${kind}/${invoiceId}`)}; true`);
    const field = `[...document.querySelectorAll('label')].find(e=>e.textContent.trim().startsWith(${JSON.stringify(label)}))?.querySelector('input')`;
    const save = "[...document.querySelectorAll('button')].find(e=>e.textContent.trim()==='保存草稿')";
    await wait(`${field} && ${save} && !${save}.disabled`);
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
  fs.writeFileSync(path.join(output, 'single-window-results.json'), JSON.stringify({ catalogVersionConflict: true, producerVersionConflict: true, results }, null, 2));
}

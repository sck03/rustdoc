import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

export async function verifyReportPdfPreview({ repo, output, api, url, headers, page, run, wait, click, captureScreenshot }) {
  const spec = JSON.parse(fs.readFileSync(path.join(repo, 'crates/export-doc-contracts/src/openapi.json'), 'utf8'));
  const route = Object.entries(spec.paths).find(([, methods]) =>
    methods.post?.operationId === 'PreviewReportTemplateContent')[0];
  const templatePath = encodeURIComponent('Templates/Export/invoice_template.dtpl');
  const { content } = await api(`/api/reports/templates/content?reportType=ExportDocument&templatePath=${templatePath}`);
  const request = {
    reportType: 'ExportDocument', content, sampleProfile: 'exportLongItems',
    withSeal: false, previewFormat: 'PdfPage', pageIndex: 0,
  };
  const post = (body, requestHeaders = headers) => fetch(url + route, {
    method: 'POST', headers: requestHeaders, body: JSON.stringify(body),
  });
  const first = await api(route,request);
  assert(first.pageCount > 1, JSON.stringify({ ...first, html: first.html?.slice(0, 100) }));
  assert.equal(first.pageIndex, 0);
  assert.match(first.previewRevision, /^[0-9a-f]{64}$/);
  const bytes = Buffer.from(first.html.match(/base64,([^"\s]+)/)[1], 'base64');
  assert(bytes.subarray(0, 8).equals(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10])));
  fs.writeFileSync(path.join(output, 'actual-pdf-page.png'), bytes);

  const revision = { previewRevision: first.previewRevision };
  const next = await api(route, { ...request, ...revision, pageIndex: 1 });
  assert.equal(next.pageIndex, 1);
  assert.equal(next.previewRevision, first.previewRevision);
  const invalidPage = await post({ ...request, ...revision, pageIndex: first.pageCount });
  assert.equal(invalidPage.status, 400, 'page overflow is a validation error');
  const stale = await post({ ...request, ...revision, content: content.replace('INVOICE', 'INVOICE CHANGED') });
  assert.equal(stale.status, 409, 'changed drafts cannot reuse an old page revision');
  const anonymous = await post(request, { 'content-type': 'application/json' });
  assert.equal(anonymous.status, 401);

  await run(`(()=>{const select=[...document.querySelectorAll('select')].find(node=>[...node.options].some(option=>option.value==='PdfPage'));select.value='PdfPage';select.dispatchEvent(new Event('change',{bubbles:true}));})()`);
  await click('真实数据预览');
  await wait("document.querySelector('iframe[title=模板预览]')?.srcdoc.includes('data:image/png;base64,')",'Actual PDF page did not reach React');
  await captureScreenshot(page, path.join(output, 'actual-pdf-preview-ui.png'));

  await click('样例数据');
  await run(`(()=>{const select=[...document.querySelectorAll('select')].find(node=>[...node.options].some(option=>option.value==='exportLongItems'));select.value='exportLongItems';select.dispatchEvent(new Event('change',{bubbles:true}));})()`);
  await click('样例预览');
  const controls = 'document.querySelector(\'[aria-label="PDF 预览翻页"]\')';
  await wait(`${controls}?.querySelector('span')?.textContent.startsWith('1 / ') && ![...${controls}.querySelectorAll('button')].find(button=>button.textContent==='下一页').disabled`, 'Multi-page PDF controls missing');
  await click('下一页');
  await wait(`${controls}?.querySelector('span')?.textContent.startsWith('2 / ') && document.querySelector('iframe[title=模板预览]')?.srcdoc.includes('PDF 第 2 页')`, 'Next PDF page did not reach React');
  await captureScreenshot(page, path.join(output, 'actual-pdf-next-page-ui.png'));
  await click('上一页');
  await wait(`${controls}?.querySelector('span')?.textContent.startsWith('1 / ')`, 'Previous PDF page did not reach React');

  fs.writeFileSync(path.join(output, 'pdf-preview-result.json'), JSON.stringify({
    pageCount: first.pageCount, revision: first.previewRevision, invalidPageStatus: invalidPage.status,
    staleStatus: stale.status, anonymousStatus: anonymous.status, uiPagingPassed: true,
  }, null, 2));
}

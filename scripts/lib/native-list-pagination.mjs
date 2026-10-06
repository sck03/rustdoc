import assert from 'node:assert/strict';
import path from 'node:path';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import { captureScreenshot } from './web-runtime-browser-session.mjs';
import { waitFor } from './web-runtime-smoke-common.mjs';

export async function verifyDesktopListPagination({ run, request, token, cdp, output }) {
  const web = path.resolve(import.meta.dirname, '../../apps/export-doc-web');
  const require = createRequire(path.join(web, 'package.json'));
  const bundle = path.join(output, 'invoice-model.mjs');
  await require('esbuild').build({ stdin: { resolveDir: web, contents: "export { createEmptyInvoice } from './src/features/invoices/invoiceModel.ts';" },
    bundle: true, platform: 'node', format: 'esm', outfile: bundle, logLevel: 'silent' });
  const { createEmptyInvoice } = await import(pathToFileURL(bundle).href);
  for (let index = 0; index < 21; index++) {
    const created = await request('CreateInvoice', token, {
      ...createEmptyInvoice('2026-10-06'), invoiceNo: 'PAGE-REVIEW-' + String(index).padStart(2, '0'), customerNameEN: 'PAGINATION REVIEW',
    });
    assert.equal(created.status, 201);
  }
  await run("location.hash='#/invoices'; true");
  const wait = expression => waitFor(() => run(expression), 15000, 'Desktop list pagination timed out: ' + expression);
  await wait("document.querySelector('.pagination-bar') && !document.querySelector('.page-size-control select').disabled");
  await run("(()=>{const select=document.querySelector('.page-size-control select');Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype,'value').set.call(select,'20');select.dispatchEvent(new Event('change',{bubbles:true}));return true})()");
  await wait("!document.querySelector('.page-size-control select').disabled && document.querySelector('.pagination-bar').textContent.includes('第 1 / 2 页')");
  const first = await run("document.querySelector('tbody').textContent");
  await run("[...document.querySelectorAll('.pager-buttons button')].find(button=>button.textContent.trim()==='下一页').click(); true");
  await wait("document.querySelector('.pagination-bar').textContent.includes('第 2 / 2 页') && !document.querySelector('.page-size-control select').disabled");
  const second = await run("document.querySelector('tbody').textContent");
  assert.notEqual(first, second, 'Second page displays different persisted invoices');
  assert(second.includes('PAGE-REVIEW-'), 'Second page contains the actual HTTP result');
  await captureScreenshot(cdp, path.join(output, 'Full-invoice-second-page.png'));
}

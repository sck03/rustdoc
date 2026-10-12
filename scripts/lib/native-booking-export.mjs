import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { captureScreenshot } from './web-runtime-browser-session.mjs';
import { waitFor } from './web-runtime-smoke-common.mjs';

export async function verifyDesktopBookingExport({ run, request, token, invoice, cdp, output }) {
  const destination = path.join(output, 'desktop-booking.xlsx');
  const panel = `document.querySelector('[aria-label="报表预览"]')`;
  const button = `${panel}.querySelector('button[title="选择保存位置并导出托单 Excel"]')`;
  await run(`location.hash='#/invoices/${invoice.id}?section=report'; true`);
  await waitFor(() => run(`Boolean(${panel} && ${button} && !${button}.disabled)`), 30000, 'Booking export button did not become ready');
  const before = await request('ListInvoices', token);
  // Intercept only the file-picker IPC response through fetch; real React, jobs and XLSX output remain active.
  await run(`(()=>{const original=window.fetch,pickerUrl=window.__TAURI_INTERNALS__.convertFileSrc('select_save_excel_path','ipc');window.__bookingOriginalFetch=original;window.__bookingDialogCalls=0;const mock=(input,init)=>{if((input instanceof Request?input.url:String(input))===pickerUrl){window.__bookingDialogCalls++;return Promise.resolve(new Response(${JSON.stringify(JSON.stringify(destination))},{headers:{'Content-Type':'application/json','Tauri-Response':'ok'}}))}return original.call(window,input,init)};window.fetch=mock;if(window.fetch!==mock)throw new Error('Unable to isolate native save dialog');})()`);
  try {
    await run(`${button}.click(); true`);
    assert.equal(await run('window.__bookingDialogCalls'), 1, 'the quick export must use the isolated save dialog');
    await waitFor(async () => fs.existsSync(destination) || await run(`Boolean(${panel}.querySelector('[role=alert]'))`), 30000, 'Booking export did not complete');
    assert(fs.existsSync(destination), await run(`${panel}.textContent`));
    assert.equal(fs.readFileSync(destination).subarray(0, 2).toString(), 'PK');
    const preview = await request('PreviewExcelImport', token, { filePath: destination });
    assert.equal(preview.status, 200);
    assert.equal(preview.body.success, true);
    assert.equal(preview.body.invoice.invoiceNo, invoice.invoiceNo, 'the exported workbook must contain this saved invoice');
    assert.equal((await request('ListInvoices', token)).body.totalCount, before.body.totalCount, 'verification preview must not insert business records');
    await run('window.scrollTo(0,0); true');
    await captureScreenshot(cdp, path.join(output, 'Full-booking-export.png'), { captureBeyondViewport: false });
    fs.writeFileSync(path.join(output, 'booking-export-result.json'), JSON.stringify({ invoiceNo: invoice.invoiceNo, bytes: fs.statSync(destination).size, parsedInvoiceNo: preview.body.invoice.invoiceNo }, null, 2));
  } finally {
    await run('window.fetch=window.__bookingOriginalFetch;delete window.__bookingOriginalFetch;delete window.__bookingDialogCalls;true');
  }
}

import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { captureScreenshot } from './web-runtime-browser-session.mjs';
import { waitFor } from './web-runtime-smoke-common.mjs';

export async function verifyDesktopBookingExport({ run, request, token, invoice, cdp, output }) {
  const destination = path.join(output, 'desktop-booking.xlsx');
  const panel = `document.querySelector('[aria-label="报表预览"]')`;
  const button = `[...${panel}.querySelectorAll('button')].find(node=>node.textContent.trim()==='导出托单'&&node.getClientRects().length)`;
  await run(`location.hash='#/invoices/${invoice.id}?section=report'; true`);
  await waitFor(() => run(`Boolean(${panel} && ${button} && !${button}.disabled)`), 30000, 'Booking export button did not become ready');
  const before = await request('ListInvoices', token);
  // Stub only the native file picker; React, HTTP, job authorization and XLSX output stay real.
  await run(`(()=>{const bridge=window.__TAURI_INTERNALS__;window.__bookingOriginalInvoke=bridge.invoke;window.__bookingDialogCalls=0;bridge.invoke=(...args)=>{if(args[0]==='select_save_excel_path'){window.__bookingDialogCalls++;return Promise.resolve(${JSON.stringify(destination)})}return window.__bookingOriginalInvoke.apply(bridge,args)};})()`);
  try {
    await run(`${button}.click(); true`);
    await waitFor(async () => fs.existsSync(destination) || await run(`Boolean(${panel}.querySelector('[role=alert]'))`), 30000, 'Booking export did not complete');
    assert(fs.existsSync(destination), await run(`${panel}.textContent`));
    assert.equal(await run('window.__bookingDialogCalls'), 1);
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
    await run('window.__TAURI_INTERNALS__.invoke=window.__bookingOriginalInvoke;delete window.__bookingOriginalInvoke;delete window.__bookingDialogCalls;true');
  }
}

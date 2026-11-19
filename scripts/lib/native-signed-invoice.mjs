import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { captureScreenshot } from './web-runtime-browser-session.mjs';
import { waitFor } from './web-runtime-smoke-common.mjs';

export async function verifyDesktopSignedInvoice({ run, request, token, cdp, output, draft, templatePath }) {
  const body = { ...draft, invoiceNo: 'SIGNED-DESKTOP', items: [{ ...draft.items[0], quantity: 2,
    cartons: 1, unitPrice: 10, totalPrice: 20, purchasePrice: -1.005, purchaseTotal: -2.01,
    priceCalculationMode: 'UnitPriceDriven' }] };
  const created = await request('CreateInvoice', token, body);
  assert.equal(created.status, 201, JSON.stringify(created.body));
  const invoiceId = created.body.id;
  await run(`location.hash='#/invoices/${invoiceId}?section=items';true`);
  const input = `document.querySelector('input[aria-label="第 1 行金额"]')`;
  await waitFor(() => run(`Boolean(${input})`), 30000, 'Invoice amount editor missing');
  await run(`${input}.scrollIntoView({block:'center'});${input}.focus();${input}.select();true`);
  await cdp.send('Input.insertText', { text: '-1000' });
  await run(`${input}.blur();true`);
  await run("[...document.querySelectorAll('button')].find(e=>e.textContent.trim()==='保存发票').click();true");
  await waitFor(async () => (await request('GetInvoice', token, undefined, { id: invoiceId })).body.totalAmount === -1000,
    30000, 'Signed invoice was not saved');
  const saved = (await request('GetInvoice', token, undefined, { id: invoiceId })).body;
  assert.equal(saved.items[0].unitPrice, -500);
  assert.equal(saved.totalPurchaseAmount, -2.01);
  await cdp.send('Page.reload');
  await waitFor(() => run(`Boolean(${input} && Number(${input}.value)===-1000)`), 30000, 'Signed display did not roundtrip');
  await run(`${input}.scrollIntoView({block:'center'});true`);
  await captureScreenshot(cdp, path.join(output, 'Full-signed-invoice.png'));
  const report = { reportType: 'ExportDocument', templatePath };
  const preview = await request('PreviewInvoiceReportHtml', token, report, { invoiceId });
  assert.equal(preview.status, 200, JSON.stringify(preview.body));
  assert.match(preview.body.html, /-(?:[A-Z]{3}|[$¥€])?1,?000(?:\.00)?/);
  const destinationPath = path.join(output, 'signed-invoice.pdf');
  const job = await request('StartInvoiceReportPdfSaveToPathJob', token, { ...report, destinationPath }, { invoiceId });
  assert.equal(job.status, 202, JSON.stringify(job.body));
  await waitFor(() => fs.existsSync(destinationPath), 30000, 'Signed PDF missing');
  assert.equal(fs.readFileSync(destinationPath).subarray(0, 5).toString(), '%PDF-');
  const invalid = await request('CreateInvoice', token, { ...body, invoiceNo: 'INVALID-PHYSICAL', items: [{ ...body.items[0], quantity: -1 }] });
  assert.equal(invalid.status, 400, JSON.stringify(invalid.body));
  fs.writeFileSync(path.join(output, 'signed-invoice-result.json'), JSON.stringify({ invoiceId,
    totalAmount: saved.totalAmount, unitPrice: saved.items[0].unitPrice, purchase: saved.totalPurchaseAmount,
    negativeQuantityStatus: invalid.status, pdf: destinationPath }, null, 2));
}

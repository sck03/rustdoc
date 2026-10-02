import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { captureScreenshot } from './web-runtime-browser-session.mjs';
import { waitFor } from './web-runtime-smoke-common.mjs';

export async function verifyDesktopPayment({ run, request, staff, cdp, output }) {
  const created = await request('CreatePayment', staff.accessToken, { voucherNo: 'DESKTOP-PRINT-001',
    payeeName: '桌面付款验收', payerName: '桌面报销人', paymentDate: staff.user.businessDate, cnyAmount: 123.45, otherExpense: 123.45 });
  assert.equal(created.status, 201);
  const payment = created.body.payment;
  const wait = expression => waitFor(() => run(expression), 30000, 'Desktop payment state timed out');
  const panel = `document.querySelector('[aria-label="付款/报销单预览"]')`;
  const button = text => `[...${panel}.querySelectorAll('button')].find(button => button.textContent.trim() === ${JSON.stringify(text)})`;
  await run(`location.hash='#/payments/${payment.id}?section=report'; true`);
  await wait(`Boolean(${panel})`);
  await wait(`Boolean(${button('预览')} && !${button('预览')}.disabled)`);
  await run(`${button('预览')}.click(); true`);
  await wait(`Boolean(${panel}.querySelector('iframe')?.srcdoc.includes('<svg'))`);
  assert.equal(await run(`document.querySelector('#payment-tab-report').getAttribute('aria-selected')`), 'true');
  assert(await run(`!${button('打印')}.disabled`));
  assert(await run(`!${button('导出 PDF')}.disabled`), 'Saved own payment enables PDF export');
  await captureScreenshot(cdp, path.join(output, 'Full-employee-payment.png'));
  await run("document.querySelector('#payment-tab-basic').click(); true");
  await run(`(() => { const label = [...document.querySelectorAll('#payment-basic-section label')].find(label => label.textContent.trim().startsWith('付款单号')); const input = label.querySelector('input'); input.focus(); input.select(); })()`);
  await cdp.send('Input.insertText', { text: '桌面打印-001' });
  await run("document.querySelector('#payment-editor-navigation button[type=submit]').click(); true");
  await wait("document.body.innerText.includes('付款报销已保存。')");
  await run("document.querySelector('#payment-tab-report').click(); true");
  await wait(`Boolean(${button('预览')} && !${button('预览')}.disabled)`);
  await run(`${button('预览')}.click(); true`);
  await wait(`Boolean(${panel}.querySelector('iframe')?.srcdoc.includes('<svg'))`);
  await captureScreenshot(cdp, path.join(output, 'Full-employee-payment-chinese.png'));
  // The native dialog is an independent manual boundary. Its cancellation is
  // covered by the React fixture; here verify the actual desktop Rust save job.
  const pdfPath = path.join(output, 'desktop-payment.pdf');
  const templatePath = await run(`${panel}.querySelector('select').value`);
  const job = await request('StartPaymentVoucherPdfSaveToPathJob', staff.accessToken,
    { templatePath, destinationPath: pdfPath }, { paymentId: payment.id });
  assert.equal(job.status, 202, 'Desktop PDF job accepted');
  await waitFor(() => fs.existsSync(pdfPath), 30000, 'Actual desktop PDF save did not complete');
  assert.equal(fs.readFileSync(pdfPath).subarray(0, 5).toString(), '%PDF-');
}

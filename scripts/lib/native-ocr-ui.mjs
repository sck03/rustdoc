import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { captureScreenshot } from './web-runtime-browser-session.mjs';
import { waitFor } from './web-runtime-smoke-common.mjs';

export async function verifyDesktopOcr({ run, cdp, repo, output, context, token, operations }) {
  await run("location.hash='#/tools/ocr'; true");
  await waitFor(() => run("Boolean(document.querySelector('[aria-label=\"选择 OCR 图片\"]:not(:disabled)'))"), 30000, 'OCR resources did not become ready');
  const { root } = await cdp.send('DOM.getDocument');
  const { nodeId } = await cdp.send('DOM.querySelector', { nodeId: root.nodeId, selector: 'input[aria-label="上传 OCR 图片"]' });
  await cdp.send('DOM.setFileInputFiles', { nodeId, files: [path.join(repo, 'tests/OcrFixtures/exif_rotated.jpg')] });
  await waitFor(() => run("Boolean(document.querySelector('[aria-label=\"开始识别\"]:not(:disabled)'))"), 10000, 'Uploaded OCR image did not become ready');
  await run("document.querySelector('[aria-label=\"开始识别\"]').click(); true");
  await waitFor(() => run("document.querySelector('[aria-label=\"识别文本\"]')?.value.includes('COMMERCIAL INVOICE')"), 100000, 'Real React OCR request did not complete');
  const text = await run("document.querySelector('[aria-label=\"识别文本\"]').value");
  assert(text.includes('客户名称：示例贸易有限公司') && text.includes("Buyer's reference: PO-001"));
  assert.equal(await run("document.querySelector('.smart-ocr-preview-canvas img').naturalWidth"), 1300, 'phone orientation must match the displayed image');
  assert.equal(await run("document.querySelectorAll('.smart-ocr-line-box').length"), 5);
  await run("document.querySelector('[aria-label=\"缩小\"]').click(); true");
  await captureScreenshot(cdp, path.join(output, 'ocr-phone-image.png'));

  const operation = operations.get('UploadOcrImage');
  const url = new URL(context.apiBaseUrl + operation.url);
  url.searchParams.set('sourceName', 'invalid.png'); url.searchParams.set('sourceMimeType', 'image/png');
  const headers = { 'Content-Type': 'image/png', 'X-ExportDocManager-Desktop-Token': context.desktopAccessToken };
  const invalid = await fetch(url, { method: operation.method, headers: { ...headers, Authorization: `Bearer ${token}` }, body: 'not an image', signal: AbortSignal.timeout(30000) });
  assert.equal(invalid.status, 400, await invalid.text());
  const unauthorized = await fetch(url, { method: operation.method, headers, body: 'not an image', signal: AbortSignal.timeout(10000) });
  assert.equal(unauthorized.status, 401);
  fs.writeFileSync(path.join(output, 'ocr-result.json'), JSON.stringify({ text, invalidStatus: invalid.status, unauthorizedStatus: unauthorized.status }, null, 2));
}

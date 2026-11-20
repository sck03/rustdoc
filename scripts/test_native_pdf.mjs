import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { creditPdfFixtures } from './lib/credit-pdf-fixtures.mjs';

assert(process.argv.length === 5 || process.argv.length === 6, 'Pass the packaged executable, PDFium library and an anonymous generated PDF.');
const [executable, library, sample] = process.argv.slice(2).map(value => path.resolve(value));
const appRoot = path.resolve(process.argv[5] ?? path.dirname(executable));
const output = path.resolve(import.meta.dirname, '../.codex-runtime/native-pdf-validation');
fs.mkdirSync(output, { recursive: true });
const input = fs.readFileSync(sample);
function invoke(mode, bytes, page = 0, success = true, pdfium = library) {
  const result = spawnSync(executable, [mode, '--pdfium', pdfium, '--page', String(page), '--ocr-root', appRoot, '--cache-root', output], {
    input: bytes, cwd: output, windowsHide: true, timeout: 590_000, maxBuffer: 64 * 1024 * 1024,
    env: { ...process.env, TEMP: output, TMP: output, TMPDIR: output },
  });
  assert.ifError(result.error);
  assert.equal(result.status, success ? 0 : 1, result.stderr.toString());
  if (!success) assert.equal(result.stdout.length, 0, 'Failure must not publish partial PDF output.');
  return result.stdout;
}
const text = JSON.parse(invoke('--pdf-credit-worker', input));
assert(text.Ok?.trim().length > 0, JSON.stringify(text));
const image = invoke('--pdf-preview-worker', input);
assert(image.readUInt32LE(0) > 0 && image.readUInt32LE(4) > 0);
const pageCount = image.readUInt32LE(8);
assert(pageCount > 0);
assert.equal(image.readUInt32LE(12), 0);
assert.equal(image.subarray(16, 24).toString('hex'), '89504e470d0a1a0a');
fs.writeFileSync(path.join(output, 'preview.png'), image.subarray(16));
const count = Buffer.alloc(4), size = Buffer.alloc(8);
count.writeUInt32LE(2); size.writeBigUInt64LE(BigInt(input.length));
const merged = invoke('--pdf-merge-worker', Buffer.concat([count, size, input, size, input]));
assert(merged.subarray(0, 5).equals(Buffer.from('%PDF-')));
const mergedText = JSON.parse(invoke('--pdf-credit-worker', merged));
assert(mergedText.Ok.includes(text.Ok.trim()));
assert.equal(invoke('--pdf-preview-worker', merged).readUInt32LE(8), pageCount * 2);
fs.writeFileSync(path.join(output, 'merged.pdf'), merged);
for (const mode of ['--pdf-preview-worker']) {
  invoke(mode, Buffer.from('%PDF-corrupt'), 0, false);
  invoke(mode, input, 0, false, path.join(output, 'missing-pdfium-library'));
}
invoke('--pdf-preview-worker', input, pageCount, false);
invoke('--pdf-merge-worker', Buffer.alloc(4), 0, false);
invoke('--pdf-merge-worker', Buffer.concat([count, size, input]), 0, false);
assert.equal(JSON.parse(invoke('--pdf-credit-worker', Buffer.from('%PDF-corrupt'))).Err.status, 400);
assert.equal(JSON.parse(invoke('--pdf-credit-worker', input, 0, true, path.join(output, 'missing-pdfium-library'))).Err.status, 503);
for (const [name, fixture] of creditPdfFixtures()) {
  fs.writeFileSync(path.join(output, name + '.pdf'), fixture);
  const result = JSON.parse(invoke('--pdf-credit-worker', fixture));
  assert(result.Ok?.includes('NATIVE CREDIT COVER'), JSON.stringify(result));
  assert(result.Ok?.includes('INV-2026-001'), name + ': scanned invoice must be retained');
  if (name === 'mixed-pages') assert(result.Ok.indexOf('NATIVE CREDIT COVER') < result.Ok.indexOf('INV-2026-001'), 'Page order');
}
fs.writeFileSync(path.join(output, 'result.json'), JSON.stringify({
  executable, library, sample, pageCount, mergedPages: pageCount * 2,
  preview: [image.readUInt32LE(0), image.readUInt32LE(4)], passed: true,
}, null, 2));
console.log('Native PDF: mixed-page extraction, text/image on one page, preview, merge/reopen, typed failures and bounds passed.');

import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

assert(process.argv.length === 5, 'Pass the packaged executable, PDFium library and an anonymous generated PDF.');
const [executable, library, sample] = process.argv.slice(2).map(value => path.resolve(value));
const output = path.resolve(import.meta.dirname, '../.codex-runtime/native-pdf-validation');
fs.mkdirSync(output, { recursive: true });
const input = fs.readFileSync(sample);
function invoke(mode, bytes, page = 0, success = true, pdfium = library) {
  const result = spawnSync(executable, [mode, '--pdfium', pdfium, '--page', String(page)], {
    input: bytes, cwd: output, windowsHide: true, timeout: 30_000, maxBuffer: 64 * 1024 * 1024,
    env: { ...process.env, TEMP: output, TMP: output, TMPDIR: output },
  });
  assert.ifError(result.error);
  assert.equal(result.status, success ? 0 : 1, result.stderr.toString());
  if (!success) assert.equal(result.stdout.length, 0, 'Failure must not publish partial PDF output.');
  return result.stdout;
}
const text = JSON.parse(invoke('--pdf-text-worker', input));
assert(text.pageCount > 0 && text.text.trim().length > 0);
const image = invoke('--pdf-preview-worker', input);
assert(image.readUInt32LE(0) > 0 && image.readUInt32LE(4) > 0);
assert.equal(image.readUInt32LE(8), text.pageCount);
assert.equal(image.readUInt32LE(12), 0);
assert.equal(image.subarray(16, 24).toString('hex'), '89504e470d0a1a0a');
fs.writeFileSync(path.join(output, 'preview.png'), image.subarray(16));
const ocr = invoke('--pdf-ocr-page-worker', input);
assert.equal(ocr.subarray(0, 8).toString('hex'), '89504e470d0a1a0a');
fs.writeFileSync(path.join(output, 'ocr-page.png'), ocr);
const count = Buffer.alloc(4), size = Buffer.alloc(8);
count.writeUInt32LE(2); size.writeBigUInt64LE(BigInt(input.length));
const merged = invoke('--pdf-merge-worker', Buffer.concat([count, size, input, size, input]));
assert(merged.subarray(0, 5).equals(Buffer.from('%PDF-')));
const mergedText = JSON.parse(invoke('--pdf-text-worker', merged));
assert.equal(mergedText.pageCount, text.pageCount * 2);
assert(mergedText.text.includes(text.text.trim()));
fs.writeFileSync(path.join(output, 'merged.pdf'), merged);
for (const mode of ['--pdf-preview-worker', '--pdf-text-worker', '--pdf-ocr-page-worker']) {
  invoke(mode, Buffer.from('%PDF-corrupt'), 0, false);
  invoke(mode, input, 0, false, path.join(output, 'missing-pdfium-library'));
}
invoke('--pdf-preview-worker', input, text.pageCount, false);
invoke('--pdf-ocr-page-worker', input, text.pageCount, false);
invoke('--pdf-merge-worker', Buffer.alloc(4), 0, false);
invoke('--pdf-merge-worker', Buffer.concat([count, size, input]), 0, false);
fs.writeFileSync(path.join(output, 'result.json'), JSON.stringify({
  executable, library, sample, pageCount: text.pageCount, mergedPages: mergedText.pageCount,
  preview: [image.readUInt32LE(0), image.readUInt32LE(4)], passed: true,
}, null, 2));
console.log('Native PDF: text, preview, OCR page, merge/reopen, corrupt input, missing library and bounds passed.');

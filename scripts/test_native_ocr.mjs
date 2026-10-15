import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const repo = path.resolve(import.meta.dirname, '..');
assert(process.argv[2], 'Pass the packaged AppRoot; this test never uses business data.');
const app = path.resolve(process.argv[2]);
const directory = path.join(app, 'sidecar/ocr');
const worker = path.join(directory, process.platform === 'win32' ? 'exportdoc-ocr.exe' : 'exportdoc-ocr');
const library = path.join(directory, process.platform === 'win32' ? 'onnxruntime.dll' : process.platform === 'darwin' ? 'libonnxruntime.dylib' : 'libonnxruntime.so');
const models = path.join(app, 'OcrModels/PaddleOCR/V6');
const temporary = path.join(repo, '.codex-runtime/native-ocr-validation');
fs.mkdirSync(temporary, { recursive: true });
const environment = { ...process.env, ORT_DYLIB_PATH: library, TEMP: temporary, TMP: temporary, TMPDIR: temporary };
function invoke(input, health = false) {
  const result = spawnSync(worker, ['--model-root', models, health ? '--health' : '--recognize-stdin'], {
    input, cwd: temporary, env: environment, windowsHide: true, timeout: 90_000, maxBuffer: 4 * 1024 * 1024,
  });
  assert.ifError(result.error);
  assert.equal(result.status, 0, result.stderr?.toString());
  return JSON.parse(result.stdout.toString('utf8'));
}
assert.equal(invoke(undefined, true).ready, true);
const invoice = ['COMMERCIAL INVOICE', 'Invoice No: INV-2026-001', '客户名称：示例贸易有限公司', 'TOTAL: USD 12,345.67', "Buyer's reference: PO-001"].join('\n');
const rows = [];
for (const name of ['invoice', 'skew_7deg', 'rotated_90deg', 'exif_rotated', 'low_contrast', 'dense_lines', 'blank']) {
  const expected = name === 'dense_lines' ? Array.from({ length: 16 }, (_, index) => `ITEM ${String(index + 1).padStart(2, '0')} Cotton shirt 120 CTNS USD 345.67`).join('\n') : name === 'blank' ? '' : invoice;
  const start = performance.now();
  const result = invoke(fs.readFileSync(path.join(repo, `tests/OcrFixtures/${name}.${name === 'exif_rotated' ? 'jpg' : 'png'}`)));
  assert.equal(result.success, true, `${name}: ${result.error}`);
  assert.equal(result.fullText.replace(/\s/gu, ''), expected.replace(/\s/gu, ''), name);
  for (const line of result.lines) {
    assert(Number.isFinite(line.confidence) && line.confidence >= 0 && line.confidence <= 1, name);
    assert(['x', 'y', 'width', 'height'].every(key => Number.isInteger(line[key]) && line[key] >= 0 && line[key] <= 16384), name);
  }
  rows.push({ name, lines: result.lines.length, elapsedMs: Math.round(performance.now() - start) });
}
for (const bytes of [Buffer.alloc(0), Buffer.from('not an image'), Buffer.alloc(25 * 1024 * 1024 + 1)]) {
  const response = invoke(bytes);
  assert.equal(response.success, false);
  assert.equal(response.errorKind, 'invalid_input');
}
fs.writeFileSync(path.join(temporary, `result-${process.platform}-${process.arch}.json`), JSON.stringify({ platform: process.platform, architecture: process.arch, appRoot: app, rows }, null, 2));
console.log(`Native OCR: ${rows.length} real model samples, health and invalid-input boundaries passed (${process.platform}/${process.arch}).`);

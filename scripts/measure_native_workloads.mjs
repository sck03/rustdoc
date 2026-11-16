import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { spawnProcessTree, stopProcessTree } from './lib/child-process-tree.mjs';
import { officeReviewOperations, officeReviewInvoke } from './lib/native-office-ui.mjs';
import { measureProcessTree } from './lib/workload-memory.mjs';

const repo = path.resolve(import.meta.dirname, '..');
const app = path.resolve(process.argv[2] ?? 'artifacts/review-desktop/ExportDocManager.Tauri.Full');
const binary = path.join(repo, 'target/x86_64-pc-windows-gnu/release/examples/office_review.exe');
const output = path.join(repo, 'artifacts/native-workload-measurement', String(Date.now()));
const data = path.join(output, 'Data'); fs.mkdirSync(data, { recursive: true });
const require = createRequire(path.join(repo, 'apps/export-doc-web/package.json'));
const spec = JSON.parse(fs.readFileSync(path.join(repo, 'crates/export-doc-contracts/src/openapi.json'), 'utf8'));
const operations = officeReviewOperations(spec), sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const measurements = [], auditRows = 100000;
const environment = { ...process.env, TEMP: path.join(repo, '.codex-runtime/temp'), TMP: path.join(repo, '.codex-runtime/temp') };
const server = spawnProcessTree(binary, [data, app], { cwd: repo, env: environment, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
try {
  const url = await new Promise((resolve, reject) => {
    let log = ''; const timer = setTimeout(() => reject(Error(`Fixture startup timeout: ${log}`)), 60000);
    server.once('error', error => { clearTimeout(timer); reject(error); });
    server.once('exit', code => { clearTimeout(timer); reject(Error(`Fixture exited ${code}: ${log}`)); });
    server.stderr.on('data', chunk => { log += chunk; });
    server.stdout.on('data', chunk => { log += chunk; const match = log.match(/\{"url":"([^"]+)"\}/u); if (match) { clearTimeout(timer); resolve(match[1]); } });
  });
  const invoke = officeReviewInvoke(url, spec, operations);
  const session = await invoke('Login', { username: 'oa-review', password: 'Review-2026-Test' });
  const token = session.accessToken;
  const api = (name, body, parameters) => invoke(name, body, parameters, token);
  async function request(name, body, parameters = {}, signal = AbortSignal.timeout(180000)) {
    const op = operations.get(name); assert(op);
    const route = op.route.replace(/\{([^}]+)\}/gu, (_, key) => encodeURIComponent(parameters[key]));
    const query = new URLSearchParams(Object.entries(parameters).filter(([key]) => !op.route.includes(`{${key}}`)));
    const raw = body instanceof Uint8Array;
    const response = await fetch(url + route + (query.size ? '?' + query : ''), { method: op.method,
      signal, headers: { authorization: `Bearer ${token}`, 'content-type': raw ? 'application/octet-stream' : 'application/json' },
      body: body === undefined ? undefined : raw ? body : JSON.stringify(body) });
    if (!response.ok) throw Error(`${name}: ${response.status} ${await response.text()}`);
    return response;
  }
  async function idle() {
    const deadline = Date.now() + 30000;
    while (Date.now() < deadline) {
      const metrics = await api('GetRuntimeMetrics');
      if (metrics.jobs.active === 0 && metrics.storage.leased === 0 &&
        metrics.http.availableRequestSlots === metrics.http.requestCapacity &&
        metrics.http.availableBulkSlots === metrics.http.bulkCapacity) return metrics;
      await sleep(20);
    }
    throw Error('Resources did not return to idle');
  }
  async function measure(name, run) {
    const measured = await measureProcessTree(server.pid, run);
    measurements.push({ name, ...measured });
    fs.writeFileSync(path.join(output, 'result.json'), JSON.stringify({ platform: process.platform, configuration: 'Release',
      target: 'x86_64-pc-windows-gnu', binary, app, auditRows, measurements }, null, 2));
    console.log(`${name}: ${measured.elapsedMs.toFixed(1)} ms; sampled RSS ${(measured.peakRss / 1048576).toFixed(1)} MiB`);
  }
  const db = path.join(data, 'Database/exportdoc-native.db');
  await promisify(execFile)('python', ['-c', `import sqlite3,json,sys
con=sqlite3.connect(sys.argv[1]); body=json.dumps({'newValues':{'notes':'SAMPLE-'+'X'*2048,'status':'Draft'}})
con.executemany('INSERT INTO audit_logs(kind,record_id,version,action,actor_id,occurred_at,body) VALUES(?,?,?,?,?,?,?)',
 (('invoices',n+1,1,'update',1,'2020-01-01T00:00:00Z',body) for n in range(int(sys.argv[2]))));con.commit();con.close()`, db, String(auditRows)], { windowsHide: true, env: environment, timeout: 120000 });
  const auditCount = async () => (await api('ListAuditLogs', undefined, { endTime: '2020-12-31T00:00:00Z', pageSize: 1 })).totalCount;
  assert.equal(await auditCount(), auditRows);
  for (const [name, operation, body] of [
    ['audit-cancel', 'CleanupAuditLogs', { daysToKeep: 1, maxCount: auditRows, confirmed: true }],
    ['excel-cancel', 'DownloadAuditLogs', { endTime: '2020-12-31T00:00:00Z', maxCount: 50000 }],
  ]) await measure(name, async () => {
    const controller = new AbortController();
    const work = request(operation, body, {}, controller.signal).then(r => r.arrayBuffer()).catch(error => error);
    await sleep(50); const canceledAt = performance.now(); controller.abort();
    const outcome = await work; assert(outcome instanceof Error, 'Workload must still be running when canceled');
    const metrics = await idle();
    const recoveryMs = performance.now() - canceledAt;
    assert.equal(await auditCount(), auditRows, 'Canceled cleanup must roll back its transaction');
    return { cancelAfterMs: 50, recoveryMs, metrics };
  });
  await measure('excel-50000-capacity-rejection', async () => {
    await assert.rejects(request('DownloadAuditLogs', { endTime: '2020-12-31T00:00:00Z', maxCount: 50000 }), /120 MiB/);
    return { rows: 50000, rejectedAtExpandedMiB: 120, metrics: await idle() };
  });
  await measure('excel-20000-audit-rows', async () => {
    const response = await request('DownloadAuditLogs', { endTime: '2020-12-31T00:00:00Z', maxCount: 20000 });
    const bytes = Buffer.from(await response.arrayBuffer()); assert.equal(bytes.subarray(0, 2).toString(), 'PK');
    return { rows: 20000, bytes: bytes.length, metrics: await idle() };
  });
  await measure('audit-cleanup-100000', async () => {
    const result = await api('CleanupAuditLogs', { daysToKeep: 1, maxCount: auditRows, confirmed: true });
    assert.equal(await auditCount(), 0); return { result, metrics: await idle() };
  });
  const modelFile = path.join(output, 'model.mjs');
  await require('esbuild').build({ stdin: { loader: 'ts', resolveDir: path.join(repo, 'apps/export-doc-web'), contents:
    "export {createEmptyInvoice} from './src/features/invoices/invoiceModel.ts'; export {createEmptyInvoiceItem} from './src/features/invoices/invoiceItemsEditorModel.ts';" },
    bundle: true, platform: 'node', format: 'esm', outfile: modelFile, logLevel: 'silent' });
  const model = await import(pathToFileURL(modelFile).href), invoices = [];
  for (let index = 0; index < 10; index++) {
    const draft = { ...model.createEmptyInvoice(session.user.businessDate), invoiceNo: `MEASURE-${index}`, exporterNameEN: 'SAMPLE EXPORTER', customerNameEN: 'SAMPLE CUSTOMER', currency: 'USD',
      items: Array.from({ length: 40 }, (_, row) => ({ ...model.createEmptyInvoiceItem(), styleNo: `STYLE-${row}`, styleName: 'Sample product / 匿名测量商品', quantity: 20, cartons: 2, unitEN: 'PCS', ctnUnitEN: 'CTNS', unitPrice: 5, totalPrice: 100 })) };
    invoices.push((await api('CreateInvoice', draft)).id);
  }
  const templates = await api('ListReportTemplates', undefined, { reportType: 'ExportDocument' });
  const batch = { invoiceIds: invoices, templatePaths: ['invoice_template.dtpl', 'packing_list_template.dtpl'].map(name => {
    const template = templates.find(item => item.templatePath.endsWith(name)); assert(template, name); return template.templatePath;
  }), reportType: 'ExportDocument', withSeal: false };
  const terminal = job => ['Succeeded', 'Failed', 'Canceled'].includes(job.status);
  async function waitJob(job) {
    const deadline = Date.now() + 180000;
    while (!terminal(job) && Date.now() < deadline) { await sleep(50); job = await api('GetJob', undefined, { jobId: job.jobId }); }
    assert(terminal(job), 'Job timed out'); return job;
  }
  await measure('pdf-batch-cancel', async () => {
    let job = await api('StartInvoiceReportPdfZipDownloadJob', batch);
    await sleep(100); job = await api('GetJob', undefined, { jobId: job.jobId }); assert(!terminal(job));
    const canceledAt = performance.now(); await api('CancelJob', undefined, { jobId: job.jobId });
    job = await waitJob(job); assert.equal(job.status, 'Canceled'); const metrics = await idle();
    return { reports: 20, recoveryMs: performance.now() - canceledAt, job, metrics };
  });
  await measure('pdf-batch-20-reports', async () => {
    const job = await waitJob(await api('StartInvoiceReportPdfZipDownloadJob', batch));
    assert.equal(job.status, 'Succeeded', job.errorMessage);
    const bytes = Buffer.from(await (await request('DownloadJobResult', undefined, { jobId: job.jobId })).arrayBuffer());
    const end = bytes.lastIndexOf(Buffer.from([0x50, 0x4b, 0x05, 0x06])); assert(end >= 0); assert.equal(bytes.readUInt16LE(end + 10), 20);
    fs.writeFileSync(path.join(output, 'reports.zip'), bytes);
    return { reports: 20, rowsPerInvoice: 40, bytes: bytes.length, metrics: await idle() };
  });
  const image = fs.readFileSync(path.join(repo, 'tests/OcrFixtures/dense_lines.png'));
  await measure('ocr-cancel', async () => {
    const controller = new AbortController();
    const work = request('UploadOcrImage', image, { sourceName: 'dense_lines.png', sourceMimeType: 'image/png' }, controller.signal).then(r => r.json()).catch(error => error);
    await sleep(100); const canceledAt = performance.now(); controller.abort();
    assert(await work instanceof Error); const metrics = await idle();
    return { cancelAfterMs: 100, recoveryMs: performance.now() - canceledAt, metrics };
  });
  await measure('ocr-batch-8-images', async () => {
    for (let index = 0; index < 8; index++) {
      const result = await (await request('UploadOcrImage', image, { sourceName: 'dense_lines.png', sourceMimeType: 'image/png' })).json();
      assert.equal(result.lines.length, 16); assert(result.fullText.includes('ITEM 16'));
    }
    return { images: 8, linesPerImage: 16, concurrency: 1, metrics: await idle() };
  });
  console.log(`Workload evidence: ${output}`);
} finally { await stopProcessTree(server); }

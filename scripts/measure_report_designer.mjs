import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import http from 'node:http';
import { createRequire } from 'node:module';
import { navigationReorganizationUiFixture } from './lib/navigation-reorganization-ui-fixture.mjs';
import { locateChromeForTesting } from './lib/chromium-executable.mjs';
import { CdpClient, closeChrome, delay } from './lib/chromium-cdp.mjs';
import { startChrome, createPageSession, evaluate, captureScreenshot } from './lib/web-runtime-browser-session.mjs';
import { measureProcessTree } from './lib/workload-memory.mjs';

const repo = path.resolve(import.meta.dirname, '..'), web = path.join(repo, 'apps/export-doc-web');
const require = createRequire(path.join(web, 'package.json'));
const output = path.join(repo, 'artifacts/report-designer-measurement', String(Date.now()));
fs.mkdirSync(output, { recursive: true });
const source = name => JSON.stringify(path.join(web, 'src', name).replaceAll('\\', '/'));
const setup = `
import {parseReportDesignerV3Source} from ${source('features/report-designer/reportDesignerV3TemplateParser.ts')};
import {createV3TextElement} from ${source('features/report-designer/reportDesignerV3ElementFactories.ts')};
const schema=parseReportDesignerV3Source('','ExportDocument').schema;
schema.layers.forEach(layer=>layer.elements=[]);
schema.layers.find(layer=>layer.role==='Body').elements=Array.from({length:500},(_,index)=>({...createV3TextElement(1500+(index%10)*1800,4000+Math.floor(index/10)*420),
 id:'measure-'+index,text:'示例 '+index,widthHundredthMm:1750,heightHundredthMm:400,style:{fontSizePt:6,align:'Left'}}));
userTemplates[0]={...userTemplates[0],contentHtml:JSON.stringify(schema)};
client.getReportTemplateFieldCatalog=()=>Promise.resolve({reportType:'ExportDocument',categoryOrder:[],fields:[]});
window.cache=queries;
`;
const fixture = navigationReorganizationUiFixture(source, setup).replace("createRoot(document.getElementById('root')).render(",
  "const root=createRoot(document.getElementById('root'));window.unmount=()=>root.unmount();root.render(");
await require('esbuild').build({ stdin: { loader: 'tsx', resolveDir: web, contents: fixture },
  outfile: path.join(output, 'app.js'), bundle: true, minify: true, define: { 'process.env.NODE_ENV': '"production"' },
  format: 'esm', platform: 'browser', jsx: 'automatic', logLevel: 'silent' });
const server = http.createServer((request, response) => {
  const file = new URL(request.url, 'http://localhost').pathname.slice(1);
  if (!file) { response.setHeader('Content-Type', 'text/html; charset=utf-8'); response.end('<html lang="zh-CN"><link rel="stylesheet" href="/app.css"><div id="root"></div><script type="module" src="/app.js"></script></html>'); }
  else if (['app.js', 'app.css'].includes(file)) { response.setHeader('Content-Type', file.endsWith('.js') ? 'text/javascript' : 'text/css'); response.end(fs.readFileSync(path.join(output, file))); }
  else response.writeHead(404).end();
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
let chrome, cdp, page;
const read = async expression => (await evaluate(page, expression, true)).value;
async function wait(expression) {
  const deadline = Date.now() + 30000;
  while (Date.now() < deadline) { if (await read(`Boolean(${expression})`)) return; await delay(25); }
  throw Error(`Timed out: ${expression}`);
}
try {
  chrome = await startChrome({ browserExecutable: locateChromeForTesting(repo, 'headless-shell'), userDataDir: path.join(output, 'Chrome'), timeoutMs: 30000 });
  cdp = await CdpClient.connect(chrome.browserWebSocketUrl); page = await createPageSession(cdp);
  await page.send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 1000, deviceScaleFactor: 1, mobile: false });
  await page.send('Page.navigate', { url: `http://127.0.0.1:${server.address().port}/#/suppliers` });
  await wait('window.unmount && window.cache.isFetching()===0');
  await page.send('HeapProfiler.collectGarbage'); const before = await page.send('Runtime.getHeapUsage');
  const measured = await measureProcessTree(chrome.process.pid, async () => {
    const loadStarted = performance.now();
    await read("location.hash='#/reports/templates?reportType=ExportDocument&userTemplateId=23'");
    await wait("document.querySelectorAll('[data-v3-element-id]').length===500");
    const loadMs = performance.now() - loadStarted;
    const edits = [], heaps = [];
    for (let index = 0; index < 30; index++) {
      const elapsed = await read(`(async()=>{
        const start=performance.now();
        document.querySelector('[data-v3-element-id="measure-0"]').dispatchEvent(new MouseEvent('dblclick',{bubbles:true}));
        await new Promise(resolve=>requestAnimationFrame(resolve));
        const input=document.querySelector('textarea[aria-label="在画布编辑文字"]');
        Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value').set.call(input,'编辑回归 ${index}');
        input.dispatchEvent(new Event('input',{bubbles:true}));
        await new Promise(resolve=>requestAnimationFrame(resolve));
        input.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',ctrlKey:true,bubbles:true}));
        await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));
        return performance.now()-start;
      })()`);
      edits.push(elapsed); heaps.push((await page.send('Runtime.getHeapUsage')).usedSize);
    }
    assert((await read("document.querySelector('[data-v3-element-id=measure-0]').textContent")).includes('编辑回归 29'));
    await captureScreenshot(page, path.join(output, 'complex-template.png'));
    const disposeStarted = performance.now(); await read('window.unmount()');
    await wait("window.cache.getQueryCache().getAll().every(q=>q.queryKey[0]!=='reports'||!q.queryKey.includes('content')) && !document.querySelector('[data-v3-element-id]')");
    const disposalMs = performance.now() - disposeStarted;
    await page.send('HeapProfiler.collectGarbage'); const after = await page.send('Runtime.getHeapUsage');
    assert.deepEqual(await read('window.__errors'), []);
    const sorted = [...edits].sort((a,b)=>a-b);
    return { elements: 500, edits: 30, loadMs, interactionMs: { p50: sorted[14], p95: sorted[28], max: sorted[29] },
      peakSampledJsHeap: Math.max(...heaps), heapBefore: before.usedSize, heapAfter: after.usedSize, disposalMs };
  });
  fs.writeFileSync(path.join(output, 'result.json'), JSON.stringify({ configuration: 'React production bundle; synthetic API fixture; Windows Chromium', ...measured }, null, 2));
  console.log(JSON.stringify(measured)); console.log(`Designer workload evidence: ${output}`);
} finally { cdp?.close(); if (chrome) await closeChrome(chrome.browserWebSocketUrl, chrome.process); await new Promise(resolve => server.close(resolve)); }

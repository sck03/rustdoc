import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import http from "node:http";
import { createRequire } from "node:module";
import { CdpClient, closeChrome, delay } from "./lib/chromium-cdp.mjs";
import { locateChromeForTesting } from "./lib/report-regression-common.mjs";
import { startChrome, createPageSession, evaluate, captureScreenshot } from "./lib/web-runtime-browser-session.mjs";
import { navigationReorganizationUiFixture } from "./lib/navigation-reorganization-ui-fixture.mjs";

const repo = path.resolve(import.meta.dirname, ".."), web = path.join(repo, "apps/export-doc-web");
const output = path.join(repo, "artifacts/report-template-draft-ui");
const require = createRequire(path.join(web, "package.json"));
fs.mkdirSync(output, { recursive: true });
const source = name => JSON.stringify(path.join(web, "src", name).split(path.sep).join("/"));
const setup = `
import {ApiError} from ${source("api/index.ts")};
import {parseReportDesignerV3FromHtml} from ${source("features/report-designer/reportDesignerV3TemplateParser.ts")};
import {exportReportDesignerV3SchemaToHtml} from ${source("features/report-designer/reportDesignerV3HtmlExporter.ts")};
import {createV3TextElement} from ${source("features/report-designer/reportDesignerV3ElementFactories.ts")};
const scenario=new URLSearchParams(location.search).get('scenario');
if(scenario==='v3'){
 const schema=parseReportDesignerV3FromHtml('','ExportDocument').schema;
 schema.layers.forEach(layer=>layer.elements=[]);
 schema.layers.find(layer=>layer.role==='Body').elements.push({...createV3TextElement(1000,4000),text:'BASELINE CANVAS'});
 userTemplates[0]={...userTemplates[0],contentHtml:exportReportDesignerV3SchemaToHtml(schema,'ExportDocument')};
}
let file={...templates[0],content:'<html><body>File baseline</body></html>',revision:'file-1',storagePolicy:''};
client.getReportTemplateFieldCatalog=()=>call('fields',{}, {reportType:'ExportDocument',categoryOrder:[],fields:[]});
client.listUserReportTemplates=input=>window.__failReload?Promise.reject(new Error('reload unavailable')):call('userTemplates',input,userTemplates);
client.getReportTemplateContent=input=>window.__failReload?Promise.reject(new Error('reload unavailable')):window.__missingTemplate?Promise.reject(new ApiError(404,'Not Found','模板已删除')):call('templateContent',input,file);
client.saveUserReportTemplateDraft=async input=>{
 window.__calls.push({name:'saveUser',input});
 if(window.__pauseSave)await new Promise(resolve=>window.__finishSave=resolve);
 if(window.__failSave||input.body.expectedVersion!==userTemplates[0].versionNumber)throw new ApiError(409,'Conflict',JSON.stringify({message:'模板版本冲突，草稿已保留'}));
 userTemplates[0]={...userTemplates[0],name:input.body.name,contentHtml:input.body.contentHtml,versionNumber:userTemplates[0].versionNumber+1};
 return Promise.resolve(structuredClone(userTemplates[0]));
};
client.saveReportTemplateContent=input=>{
 window.__calls.push({name:'saveFile',input});
 if(input.body.expectedRevision!==file.revision)throw new ApiError(409,'Conflict',JSON.stringify({message:'文件版本冲突，草稿已保留'}));
 file={...file,content:input.body.content,revision:file.revision+'-next'};
 return Promise.resolve(structuredClone(file));
};
window.__remoteRefresh=async()=>{
 userTemplates[0]={...userTemplates[0],contentHtml:'<html><body>REMOTE VERSION</body></html>',versionNumber:2};
 file={...file,content:'<html><body>REMOTE FILE</body></html>',revision:'file-2'};
 await queries.refetchQueries();
};
window.__remoteRemove=async()=>{
 userTemplates.splice(0);templates.splice(0);window.__missingTemplate=true;
 await queries.refetchQueries();
};
window.__ready=()=>queries.isFetching()===0&&queries.isMutating()===0;
`;
await require("esbuild").build({ stdin: { loader: "tsx", resolveDir: web, contents: navigationReorganizationUiFixture(source, setup) },
  outfile: path.join(output, "app.js"), bundle: true, format: "esm", platform: "browser", jsx: "automatic", logLevel: "silent" });
const server = http.createServer((request, response) => {
  const name = new URL(request.url, "http://localhost").pathname.slice(1);
  if (!name) { response.setHeader("Content-Type", "text/html; charset=utf-8"); response.end('<!doctype html><html lang="zh-CN"><meta name="viewport" content="width=device-width, initial-scale=1"><title>模板草稿回归</title><link rel="stylesheet" href="/app.css"><div id="root"></div><script type="module" src="/app.js"></script></html>'); return; }
  if (!["app.js", "app.css"].includes(name)) { response.writeHead(404).end(); return; }
  response.setHeader("Content-Type", name.endsWith("js") ? "text/javascript" : "text/css");
  response.end(fs.readFileSync(path.join(output, name)));
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
let chrome, cdp, page;
const results = [], editor = 'textarea[aria-label="模板高级 HTML"]';
const saveEnabled = "[...document.querySelectorAll('.report-template-layout button[type=submit]')].some(node=>!node.disabled)";
const read = async expression => (await evaluate(page, expression, true)).value;
async function waitFor(expression) {
  const end = Date.now() + 20000;
  while (Date.now() < end) { if (await read(`Boolean(${expression})`)) return; await delay(50); }
  throw new Error(`Timeout: ${expression}\n${await read("document.body.innerText.slice(-2500)")}`);
}
async function settle() {
  await waitFor("window.__ready?.()");
  await read("new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))");
}
async function click(text, selector = "button") {
  await read(`(()=>{const node=[...document.querySelectorAll(${JSON.stringify(selector)})].find(node=>node.getClientRects().length&&!node.disabled&&node.textContent.trim()===${JSON.stringify(text)});if(!node)throw new Error('Missing button '+${JSON.stringify(text)});node.click()})()`);
}
async function input(selector, value) {
  await read(`(()=>{const node=document.querySelector(${JSON.stringify(selector)});const proto=node instanceof HTMLSelectElement?HTMLSelectElement.prototype:HTMLTextAreaElement.prototype;Object.getOwnPropertyDescriptor(proto,'value').set.call(node,${JSON.stringify(value)});node.dispatchEvent(new Event('input',{bubbles:true}));node.dispatchEvent(new Event('change',{bubbles:true}));})()`);
}
async function open(scenario, file = false) {
  if (page) await cdp.send("Target.closeTarget", { targetId: page.targetId });
  page = await createPageSession(cdp);
  await page.send("Emulation.setDeviceMetricsOverride", { width: 1440, height: 1000, deviceScaleFactor: 1, mobile: false });
  await page.send("Page.navigate", { url: `http://127.0.0.1:${server.address().port}/?scenario=${scenario}#/reports/templates?reportType=ExportDocument&${file ? "template=invoice_template.html" : "userTemplateId=23"}` });
  await waitFor("document.querySelector('.report-template-layout')"); await settle();
}
async function record(name) { assert.deepEqual(await read("window.__errors"), [], name); results.push(name); }
try {
  chrome = await startChrome({ browserExecutable: locateChromeForTesting(repo, "headless-shell"), userDataDir: path.join(output, "browser-profile"), timeoutMs: 30000 });
  cdp = await CdpClient.connect(chrome.browserWebSocketUrl);
  await open("classic"); await waitFor(`document.querySelector(${JSON.stringify(editor)})?.value.includes('Shared template')`);
  assert.equal(await read(saveEnabled), false); await record("database-template-first-load");
  await input(editor, "<html><body>LOCAL DRAFT</body></html>"); await waitFor(saveEnabled);
  await read("window.__remoteRefresh()"); await settle();
  assert((await read(`document.querySelector(${JSON.stringify(editor)}).value`)).includes("LOCAL DRAFT")); await record("query-refresh-retains-draft");
  await click("保存"); await waitFor("document.body.innerText.includes('模板版本冲突')");
  assert.equal(await read("window.__calls.find(call=>call.name==='saveUser').input.body.expectedVersion"), 1);
  assert((await read(`document.querySelector(${JSON.stringify(editor)}).value`)).includes("LOCAL DRAFT")); await record("save-uses-loaded-version-and-keeps-conflict-draft");
  await click("重新加载"); await waitFor("document.querySelector('.confirmation-dialog')"); await click("取消", ".confirmation-dialog button");
  assert((await read(`document.querySelector(${JSON.stringify(editor)}).value`)).includes("LOCAL DRAFT")); await record("cancel-reload-retains-draft");
  await read("window.__failReload=true"); await click("重新加载"); await waitFor("document.querySelector('.confirmation-dialog')"); await click("刷新报表模板", ".confirmation-dialog button"); await settle();
  assert((await read(`document.querySelector(${JSON.stringify(editor)}).value`)).includes("LOCAL DRAFT")); await record("failed-reload-retains-draft");
  await read("window.__failReload=false"); await click("重新加载"); await waitFor("document.querySelector('.confirmation-dialog')"); await click("刷新报表模板", ".confirmation-dialog button"); await settle();
  assert((await read(`document.querySelector(${JSON.stringify(editor)}).value`)).includes("REMOTE VERSION")); assert.equal(await read(saveEnabled), false); await record("confirmed-reload-adopts-new-baseline");
  await input(editor, "<html><body>NEW LOCAL DRAFT</body></html>"); await waitFor(saveEnabled); await click("保存"); await settle();
  assert.equal(await read("window.__calls.filter(call=>call.name==='saveUser').at(-1).input.body.expectedVersion"), 2); assert.equal(await read(saveEnabled), false); await record("save-after-reload");

  await open("v3"); await waitFor("document.querySelector('[data-v3-element-id]')");
  assert.equal(await read(saveEnabled), false); assert((await read("document.body.innerText")).includes("BASELINE CANVAS")); await record("canvas-first-load-keeps-content");
  await read('document.querySelector(".report-designer-v3-toolbar button[aria-label=文本]").click()'); await waitFor(saveEnabled);
  const count = await read("document.querySelectorAll('[data-v3-element-id]').length");
  await click("预览", ".report-template-designer-toolbar button"); await waitFor("document.querySelector('.report-template-designer-toolbar [aria-selected=true]')?.textContent.includes('预览')");
  await click("可视化设计", ".report-template-designer-toolbar button"); await settle();
  assert.equal(await read("document.querySelectorAll('[data-v3-element-id]').length"), count); assert.equal(await read(saveEnabled), true); await record("preview-roundtrip-retains-canvas-history");
  await read("window.__failSave=true;window.__pauseSave=true"); await click("保存"); await waitFor("window.__finishSave");
  await waitFor("document.querySelector('.report-designer-v3-layout.is-read-only')");
  assert.equal(await read('Boolean(document.querySelector(".report-designer-v3-toolbar button[aria-label=文本]:not(:disabled)"))'), false);
  assert.equal(await read("document.querySelectorAll('[data-v3-element-id]').length"), count);
  await record("pending-save-prevents-further-canvas-edits");
  await read("window.__pauseSave=false;window.__finishSave()"); await waitFor("document.body.innerText.includes('模板版本冲突')");
  assert.equal(await read("document.querySelectorAll('[data-v3-element-id]').length"), count); assert.equal(await read(saveEnabled), true); await record("failed-save-retains-canvas");
  await read('document.querySelector(".report-designer-v3-toolbar button[aria-label=图片]").click()'); await waitFor("document.querySelector('.report-designer-v3-image-editor select')");
  await input(".report-designer-v3-image-editor select", "Field"); await waitFor("document.querySelectorAll('.report-designer-v3-image-editor select').length===2");
  await input(".report-designer-v3-image-editor label:nth-child(2) select", ""); await waitFor(`!(${saveEnabled})`);
  await click("返回模板管理"); await waitFor("document.querySelector('.confirmation-dialog')"); await click("取消", ".confirmation-dialog button");
  assert((await read("window.__route")).startsWith("/reports/templates?")); assert.equal(await read(saveEnabled), false); await record("invalid-draft-keeps-leave-guard");
  await captureScreenshot(page, path.join(output, "invalid-draft-preserved.png"));
  await click("返回模板管理"); await waitFor("document.querySelector('.confirmation-dialog')"); await click("返回模板管理", ".confirmation-dialog button"); await waitFor("window.__route.startsWith('/reports/templates/manage')"); await record("confirmed-leave-discards-invalid-draft");

  await open("file", true); await waitFor(`document.querySelector(${JSON.stringify(editor)})?.value.includes('File baseline')`);
  await input(editor, "<html><body>LOCAL FILE DRAFT</body></html>"); await waitFor(saveEnabled); await read("window.__remoteRefresh()"); await settle();
  await click("保存"); await waitFor("document.body.innerText.includes('文件版本冲突')");
  assert.equal(await read("window.__calls.find(call=>call.name==='saveFile').input.body.expectedRevision"), "file-1");
  assert((await read(`document.querySelector(${JSON.stringify(editor)}).value`)).includes("LOCAL FILE DRAFT")); await record("file-refresh-and-conflict-retain-draft-and-revision");
  for (const fileTemplate of [false, true]) {
    await open("classic", fileTemplate); await waitFor(`document.querySelector(${JSON.stringify(editor)})`);
    await input(editor, "<html><body>DELETED REMOTELY, LOCAL DRAFT RETAINED</body></html>"); await waitFor(saveEnabled);
    await read("window.__remoteRemove()"); await settle();
    assert((await read(`document.querySelector(${JSON.stringify(editor)})?.value`))?.includes("LOCAL DRAFT RETAINED"));
    if (!fileTemplate) assert.equal(await read(saveEnabled), false);
    await click("返回模板管理"); await waitFor("document.querySelector('.confirmation-dialog')"); await click("取消", ".confirmation-dialog button");
    await record(fileTemplate ? "missing-file-keeps-draft-and-leave-guard" : "missing-database-template-keeps-draft-and-leave-guard");
  }
  fs.writeFileSync(path.join(output, "summary.json"), JSON.stringify({ passed: results.length, results }, null, 2));
  process.stdout.write(`Report template draft UI passed (${results.length} cases).\n`);
} finally {
  cdp?.close(); if (chrome) await closeChrome(chrome.browserWebSocketUrl, chrome.process);
  await new Promise(resolve => server.close(resolve));
}

import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import http from "node:http";
import { createRequire } from "node:module";
import { createHash } from "node:crypto";
import { CdpClient, closeChrome, delay } from "./lib/chromium-cdp.mjs";
import { locateChromeForTesting } from "./lib/report-regression-common.mjs";
import { startChrome, createPageSession, evaluate, captureScreenshot } from "./lib/web-runtime-browser-session.mjs";
import { verifyDesignerEditingUi } from "./lib/report-designer-editing-ui-scenarios.mjs";
import { verifyProductFieldsUi } from "./lib/report-designer-product-fields-ui.mjs";
import { verifyShippingMarksUi } from "./lib/report-shipping-marks-ui-scenarios.mjs";

const repo = path.resolve(import.meta.dirname, "..");
const web = path.join(repo, "apps/export-doc-web");
const output = path.join(repo, "artifacts/report-designer-v3-ui");
const require = createRequire(path.join(web, "package.json"));
fs.mkdirSync(output, { recursive: true });
const esbuild = require("esbuild");
const source = name => JSON.stringify(path.join(web, "src", name).replaceAll("\\", "/"));
const rasterImages = JSON.parse(fs.readFileSync(path.join(repo, "tests/ReportTemplateFixtures/raster-images.json"), "utf8"));
const reportFields = JSON.parse(fs.readFileSync(path.join(repo, "crates/export-doc-engine/resources/report-fields.json"), "utf8"));
const imageFixtures = ["png", "pngAlternate", "jpeg"].map((format, index) => {
  const bytes = Buffer.from(rasterImages[format], "base64"), sha256 = createHash("sha256").update(bytes).digest("hex");
  return { id: `img-${sha256}.${format === "jpeg" ? "jpg" : "png"}`, sha256, mediaType: format === "jpeg" ? "image/jpeg" : "image/png", byteLength: bytes.length,
    altText: `示例图片 ${index + 1}`, storagePolicy: "", ownsUpload: true, isReferenced: index === 0, canRecycle: index > 0, bytes: [...bytes] };
});
await esbuild.build({
  stdin: { loader: "tsx", resolveDir: web, contents: `
    import React from 'react';
    import { createRoot } from 'react-dom/client';
    import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
    import { PermissionAccessProvider } from ${source("app/PermissionAccessContext.tsx")};
    import { permissionResources } from ${source("app/permissionCatalog.ts")};
    import { ConfirmationProvider } from ${source("ui/ConfirmationProvider.tsx")};
    import { ReportDesignerV3Workspace } from ${source("features/report-designer/ReportDesignerV3Workspace.tsx")};
    import { parseReportDesignerV3Source } from ${source("features/report-designer/reportDesignerV3TemplateParser.ts")};
    import { exportReportDesignerV3SchemaToHtml } from ${source("features/report-designer/reportDesignerV3HtmlExporter.ts")};
    import { createV3FlowElement, createV3TextElement, createV3FieldElement, createV3LineElement, createV3PageNumberElement, createV3ImageElement } from ${source("features/report-designer/reportDesignerV3ElementFactories.ts")};
    import { createGridBlock, createDetailTableBlock, createRowBlock, createConditionalBlock, createDetailTableSideBand } from ${source("features/report-designer/reportDesignerBlockFactories.ts")};
    import { createShippingMarksScenario } from ${JSON.stringify(path.join(repo, "scripts/lib/report-shipping-marks-fixture.mjs").replaceAll("\\", "/"))};
    import { defaultReportDesigns } from ${JSON.stringify(path.join(repo, "scripts/lib/default-report-designs.mjs").replaceAll("\\", "/"))};
    import { renderReportDesignerLocalPreviewSample } from ${source("features/report-designer/reportDesignerPreviewSamples.ts")};
    import ${source("styles/cascade.css")};
    import ${source("styles/foundation.css")};
    import ${source("styles/workspaces.css")};
    import ${source("styles/responsive.css")};
    import ${source("styles/routes/reports.css")};
    const imageScenario = new URLSearchParams(location.search).has('images');
    const reportType = imageScenario ? 'PaymentVoucher' : 'ExportDocument';
    const schema = parseReportDesignerV3Source('', reportType).schema;
    schema.layers.forEach(layer => { layer.elements = []; if (layer.role === 'Header') layer.designHeightHundredthMm = 6000; });
    const header = schema.layers.find(layer => layer.role === 'Header');
    const body = schema.layers.find(layer => layer.role === 'Body');
    const grid = { ...createV3FlowElement(createGridBlock(), 1000, 700), id:'review-grid', zIndex:10000 };
    header.elements.push(grid);
    body.elements.push({ ...createV3FlowElement(createDetailTableBlock(), 1000, 14500), id:'review-detail', zIndex:10000 });
    if (new URLSearchParams(location.search).has('products')) schema.layers.forEach(layer=>layer.elements=[]);
    const stress = new URLSearchParams(location.search).has('stress');
    if (new URLSearchParams(location.search).has('editing')) {
      const style={fontSizePt:12,bold:true,align:'Right',color:'#334455',backgroundColor:'#fff0dd',borderStyle:'Solid',borderWidthPx:2,paddingHundredthMm:200};
      schema.layers.find(layer=>layer.role==='Overlay').elements.push(
        {...createV3TextElement(1500,8500),id:'edit-text',text:'外观样式',widthHundredthMm:6000,style},
        {...createV3TextElement(8500,8500),id:'edit-other',widthHundredthMm:2500,text:'目标文本'},
        {...createV3LineElement(1500,11000),id:'edit-line',style:{borderStyle:'Dashed',borderWidthPx:2,borderColor:'#334455'}},
        {...createV3FieldElement('Invoice.InvoiceNo',1500,13000),id:'edit-field',style},
        {...createV3PageNumberElement(1500,16000),id:'edit-page',format:'Current',prefix:'第',suffix:'页',style});
    }
    if (stress) {
      const overlay = schema.layers.find(layer => layer.role === 'Overlay');
      for (let index=0;index<900;index++) overlay.elements.push({ ...createV3TextElement(1000 + index % 40 * 450, 23000 + Math.floor(index / 40) * 220), id:'stress-'+index, text:String(index), widthHundredthMm:400, heightHundredthMm:400 });
    }
    let images=${JSON.stringify(imageFixtures)};
    window.__imageCalls=[];
    const client={
      downloadReportTemplateV3ImageResource:async({resourceId})=>{window.__imageCalls.push({kind:'download',resourceId});const image=images.find(i=>i.id===resourceId);return new Blob([new Uint8Array(image.bytes)],{type:image.mediaType});},
      queryReportTemplateV3ImageResources:async()=>{window.__imageCalls.push({kind:'list'});return {items:images.map(({bytes,...item})=>item),pageNumber:1,pageSize:12,totalCount:images.length,totalPages:1,hasPreviousPage:false,hasNextPage:false};},
      uploadReportTemplateV3ImageResource:async()=>{window.__imageCalls.push({kind:'upload'});return new Promise(resolve=>window.__finishImageUpload=()=>resolve((({bytes,...image})=>image)(images[1])));},
      recycleReportTemplateV3ImageResource:async({resourceId})=>{window.__imageCalls.push({kind:'recycle',resourceId});images=images.filter(i=>i.id!==resourceId);return {success:true,message:'图片资源已安全回收。'};}
    };
    window.__imageFixtures=images;
    if(imageScenario){
      schema.layers.forEach(layer=>layer.elements=[]);
      schema.resources=[(({bytes,ownsUpload,isReferenced,canRecycle,storagePolicy,...item})=>item)(images[0])];
      const overlay=schema.layers.find(layer=>layer.role==='Overlay');
      overlay.elements.push(...[0,1].map(index=>({...createV3ImageElement(1500+index*7000,6000),id:'image-'+index,resourceId:images[0].id,altText:'真实付款图片'})));
    }
    if(new URLSearchParams(location.search).has('marks')) Object.assign(schema,createShippingMarksScenario({parseReportDesignerV3Source,createRowBlock,createGridBlock,createConditionalBlock,createDetailTableBlock,createDetailTableSideBand,createV3FlowElement,createV3FieldElement}));
    if(new URLSearchParams(location.search).has('invoice')) Object.assign(schema,defaultReportDesigns()[0][1]);
    window.__designerSchema = schema;
    window.__designerUpdates = 0;
    window.__designerErrors = [];
    window.addEventListener('error', event => window.__designerErrors.push(event.message));
    window.addEventListener('unhandledrejection', event => window.__designerErrors.push(String(event.reason)));
    const content = JSON.stringify(schema);
    const fieldCatalog={reportType:'ExportDocument',categoryOrder:['单据备用字段','明细备用列'],fields:['Invoice','item'].flatMap(root=>Array.from({length:10},(_,index)=>({
      category:root==='Invoice'?'单据备用字段':'明细备用列',label:index===9?(root==='Invoice'?'船名航次':'客户货号'):(root==='Invoice'?'发票':'明细')+'备用 '+(index+1),value:'{{ '+root+'.Spare'+(index+1)+' }}',reportType:'ExportDocument'})))};
    fieldCatalog.fields.push({category:'单据信息',label:'唛头（文字 / 图片自动）',value:'{{ Invoice.ShippingMarks }}',reportType:'ExportDocument'});
    fieldCatalog.fields.push({category:'单据信息',label:'发票号',value:'{{ Invoice.InvoiceNo }}',reportType:'ExportDocument'});
    if(new URLSearchParams(location.search).has('invoice')) Object.assign(fieldCatalog,${JSON.stringify(reportFields)});
    window.__designerHtml = content;
    window.__exportDesignerHtml=()=>exportReportDesignerV3SchemaToHtml(window.__designerSchema);
    window.__renderMarksPreview=profile=>renderReportDesignerLocalPreviewSample(window.__designerHtml,profile);
    createRoot(document.getElementById('root')).render(<QueryClientProvider client={new QueryClient({defaultOptions:{queries:{retry:false}}})}><PermissionAccessProvider grants={[]} permissions={['view','upload','recycle'].map(action=>({resourceKey:permissionResources.reportResources,action,dataScope:'all'}))} canManageSettings={false}><ConfirmationProvider><div className="work-surface" style={{margin:'12px',padding:'8px'}}>
      <ReportDesignerV3Workspace client={imageScenario?client:undefined} reportType={reportType} displayName="表格设计交互验证" content={content} fieldCatalog={imageScenario?{reportType,fields:[],categoryOrder:[]}:fieldCatalog} editable={!new URLSearchParams(location.search).has('readonly')} onDesignerDraftChange={({content: html, isDirty, isValid}) => {
        window.__designerDraftState={isDirty,isValid};
        if(isValid) { window.__designerUpdates++; window.__designerHtml=isDirty?html:content; window.__designerSchema=parseReportDesignerV3Source(window.__designerHtml,reportType).schema; }
      }} />
    </div></ConfirmationProvider></PermissionAccessProvider></QueryClientProvider>);
  ` },
  outfile: path.join(output, "app.js"), bundle: true, format: "esm", platform: "browser", jsx: "automatic", nodePaths: [path.join(web, "node_modules")], logLevel: "silent",
});
const server = http.createServer((request, response) => {
  const name = new URL(request.url, "http://localhost").pathname.slice(1);
  if (!name) { response.setHeader("Content-Type", "text/html; charset=utf-8"); response.end('<!doctype html><html lang="zh-CN"><meta name="viewport" content="width=device-width, initial-scale=1"><title>报表设计交互验证</title><link rel="stylesheet" href="/app.css"><div id="root"></div><script type="module" src="/app.js"></script></html>'); return; }
  if (!["app.js", "app.css", "marks-text.html", "marks-image.html"].includes(name)) { response.writeHead(404).end(); return; }
  response.setHeader("Content-Type", name.endsWith("js") ? "text/javascript" : name.endsWith("html") ? "text/html; charset=utf-8" : "text/css");
  response.end(fs.readFileSync(path.join(output, name)));
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
const url = `http://127.0.0.1:${server.address().port}/`;
let chrome, cdp;
const results = [];
const primaryModifier = process.platform === "darwin" ? 4 : 2;
const read = async (page, expression) => (await evaluate(page, expression, true)).value;
async function waitFor(page, expression) {
  const limit = Date.now() + 20000;
  while (Date.now() < limit) { if (await read(page, `Boolean(${expression})`)) return; await delay(50); }
  throw new Error(`Timed out: ${expression}; ${await read(page, "document.body.innerText.slice(0,1600)")}`);
}
async function click(page, selector) { await read(page, `document.querySelector(${JSON.stringify(selector)}).click()`); await delay(70); }
async function key(page, key, modifiers = 0) {
  const keyCode = key === "Escape" ? 27 : key === "Enter" ? 13 : key.length === 1 ? key.toUpperCase().charCodeAt(0) : undefined;
  await page.send("Input.dispatchKeyEvent", { type: "keyDown", key, modifiers, windowsVirtualKeyCode: keyCode });
  await page.send("Input.dispatchKeyEvent", { type: "keyUp", key, modifiers, windowsVirtualKeyCode: keyCode });
}
async function drag(page, selector, dx, dy, beforeRelease) {
  await read(page, `document.querySelector(${JSON.stringify(selector)}).scrollIntoView({block:'center',inline:'center'})`);
  const rect = await read(page, `(() => {const r=document.querySelector(${JSON.stringify(selector)}).getBoundingClientRect();return {x:r.left+8,y:r.top+8}})()`);
  await page.send("Input.dispatchMouseEvent", {type:"mouseMoved",x:rect.x,y:rect.y});
  await page.send("Input.dispatchMouseEvent", {type:"mousePressed",x:rect.x,y:rect.y,button:"left",clickCount:1});
  for(let step=1;step<=12;step++) { await page.send("Input.dispatchMouseEvent", {type:"mouseMoved",x:rect.x+dx*step/12,y:rect.y+dy*step/12,button:"left",buttons:1}); await delay(18); }
  await beforeRelease?.();
  await page.send("Input.dispatchMouseEvent", {type:"mouseReleased",x:rect.x+dx,y:rect.y+dy,button:"left",clickCount:1});
  await delay(150);
}
try {
  chrome = await startChrome({browserExecutable:locateChromeForTesting(repo),userDataDir:path.join(output,`profile-${Date.now()}`),timeoutMs:30000});
  cdp = await CdpClient.connect(chrome.browserWebSocketUrl);
  const page = await createPageSession(cdp);
  for(const width of [1920,1366,1024,390]) {
    await page.send("Emulation.setDeviceMetricsOverride", {width,height:1000,deviceScaleFactor:1,mobile:width<500});
    await page.send("Page.navigate",{url});
    await waitFor(page, 'Boolean(document.querySelector("[data-v3-element-id=review-grid]"))');
    await read(page, `document.querySelector('[data-v3-element-id="review-grid"]').dispatchEvent(new PointerEvent('pointerdown',{bubbles:true,button:0,pointerId:77,clientX:0,clientY:0}));window.dispatchEvent(new PointerEvent('pointercancel',{pointerId:77}));`);
    await waitFor(page, 'document.querySelector(".report-designer-property-tabs [role=tab][aria-selected=true]")?.textContent === "单元格"');
    const controls = await read(page, `Array.from(document.querySelectorAll('.report-designer-v3-inspector input[type=checkbox]')).filter(input=>input.getClientRects().length).map(input=>{const r=input.getBoundingClientRect();return {width:r.width,height:r.height}})`);
    assert(controls.length >= 5 && controls.every(control=>control.width>=14&&control.width<=18&&control.height>=14&&control.height<=18), JSON.stringify(controls));
    await click(page,'.report-designer-property-tabs [role=tab]:nth-child(2)');
    assert(!await read(page,"[...document.querySelectorAll('.report-designer-v3-inspector [role=tab]')].some(node=>node.textContent==='外观')"), "tables use their own cell and table style editors");
    assert(await read(page, 'document.querySelector(".report-designer-v3-inspector").scrollWidth <= document.querySelector(".report-designer-v3-inspector").clientWidth + 1'), `inspector overflow at ${width}`);
    assert(await read(page, 'document.documentElement.scrollWidth <= innerWidth + 1'), `page overflow at ${width}`);
    await captureScreenshot(page,path.join(output,`grid-${width}.png`),{captureBeyondViewport:false});
    results.push({test:'inspector layout and checkbox size',width,passed:true});
  }
  await page.send("Emulation.setDeviceMetricsOverride", {width:1440,height:1000,deviceScaleFactor:1,mobile:false});
  await page.send("Page.navigate",{url});
  await waitFor(page, 'Boolean(document.querySelector("[data-v3-element-id=review-grid]"))');
  await drag(page,'[data-v3-element-id="review-grid"]',0,210);
  assert.equal(await read(page,'window.__designerSchema.layers.find(layer=>layer.elements.some(element=>element.id==="review-grid")).role'), 'Body', 'first drag must reparent AND publish a saveable draft');
  await click(page,'button[aria-label="撤销"]');
  await waitFor(page,'window.__designerSchema.layers.find(layer=>layer.elements.some(element=>element.id==="review-grid")).role === "Header"');
  await click(page,'button[aria-label="重做"]');
  await waitFor(page,'window.__designerSchema.layers.find(layer=>layer.elements.some(element=>element.id==="review-grid")).role === "Body"');
  results.push({test:'drag transfers region with undo/redo',passed:true});
  const panelClip=await read(page,`(()=>{const r=document.querySelector('.report-designer-v3-inspector').getBoundingClientRect();return {x:r.left,y:Math.max(0,r.top),width:r.width,height:Math.min(r.height,innerHeight-Math.max(0,r.top)),scale:1}})()`);
  const panelImage=await page.send('Page.captureScreenshot',{format:'png',clip:panelClip});
  fs.writeFileSync(path.join(output,'cell-inspector.png'),Buffer.from(panelImage.data,'base64'));
  assert(await read(page, `Math.abs(parseFloat(getComputedStyle(document.querySelector('.report-designer-v3-layer-body'),'::before').top)-parseFloat(getComputedStyle(document.querySelector('.report-designer-v3-layer-header'),'::after').height)-4)<1`),'region labels must follow the real band boundary');
  await read(page, `window.__retainedCell=document.querySelectorAll('[data-v3-element-id="review-grid"] [data-report-grid-cell-id]')[1];window.__retainedCell.dispatchEvent(new PointerEvent('pointerdown',{bubbles:true,button:0,pointerId:78}));window.dispatchEvent(new PointerEvent('pointercancel',{pointerId:78}));`);
  await delay(60);
  assert(await read(page, `window.__retainedCell === document.querySelectorAll('[data-v3-element-id="review-grid"] [data-report-grid-cell-id]')[1] && window.__retainedCell.classList.contains('is-designer-selected-cell')`),'selecting a cell must preserve the table DOM');
  results.push({test:'real region labels and cached cell selection',passed:true});
  const originalX=await read(page,'window.__designerSchema.layers.flatMap(layer=>layer.elements).find(element=>element.id==="review-grid").xHundredthMm');
  await read(page,'document.querySelector("[data-v3-element-id=review-grid]").focus()');
  await key(page,'ArrowRight');
  await drag(page,'[data-v3-element-id="review-grid"]',30,0,async()=>{await key(page,'z',primaryModifier);await delay(80);});
  assert.equal(await read(page,'window.__designerSchema.layers.flatMap(layer=>layer.elements).find(element=>element.id==="review-grid").xHundredthMm'),originalX);
  assert.equal(await read(page,'parseFloat(document.querySelector("[data-v3-element-id=review-grid]").style.left)'),originalX/100);
  results.push({test:'undo during drag preserves current state and DOM geometry',passed:true});
  await click(page,'.report-designer-property-tabs [role=tab]:nth-child(2)');
  const widthsBefore = await read(page,'window.__designerSchema.layers.flatMap(layer=>layer.elements).find(element=>element.id==="review-grid").block.columns.map(column=>column.widthPercent)');
  const updatesBeforeWidth = await read(page,'window.__designerUpdates');
  await drag(page,'.new-report-column-width-handle',24,0,async()=>assert.equal(await read(page,'window.__designerUpdates'),updatesBeforeWidth,'column preview must not publish document changes'));
  const widthsAfter = await read(page,'window.__designerSchema.layers.flatMap(layer=>layer.elements).find(element=>element.id==="review-grid").block.columns.map(column=>column.widthPercent)');
  assert(widthsAfter[0]>widthsBefore[0] && Math.abs(widthsAfter[0]+widthsAfter[1]-widthsBefore[0]-widthsBefore[1])<0.01);
  assert.equal(await read(page,'window.__designerUpdates'),updatesBeforeWidth+1);
  await click(page,'button[aria-label="撤销"]');
  assert.deepEqual(await read(page,'window.__designerSchema.layers.flatMap(layer=>layer.elements).find(element=>element.id==="review-grid").block.columns.map(column=>column.widthPercent)'),widthsBefore);
  results.push({test:'column resize is one undoable commit',passed:true});
  const cellSelector='[data-v3-element-id="review-grid"] [data-report-grid-cell-id]';
  await read(page, `document.querySelector(${JSON.stringify(cellSelector)}).dispatchEvent(new MouseEvent('dblclick',{bubbles:true}));`);
  await waitFor(page,'Boolean(document.querySelector(".report-designer-canvas-text-editor textarea"))');
  await page.send('Input.insertText',{text:'画布直接输入'});
  await key(page,'Enter',primaryModifier);
  await waitFor(page,'!document.querySelector(".report-designer-canvas-text-editor")');
  assert.equal(await read(page,'window.__designerSchema.layers.flatMap(layer=>layer.elements).find(element=>element.id==="review-grid").block.rows[0].cells[0].text'),'画布直接输入');
  await read(page, `document.querySelector(${JSON.stringify(cellSelector)}).dispatchEvent(new MouseEvent('dblclick',{bubbles:true}));`);
  await waitFor(page,'Boolean(document.querySelector(".report-designer-canvas-text-editor textarea"))');
  await page.send('Input.insertText',{text:'取消的内容'}); await key(page,'Escape');
  assert.equal(await read(page,'window.__designerSchema.layers.flatMap(layer=>layer.elements).find(element=>element.id==="review-grid").block.rows[0].cells[0].text'),'画布直接输入');
  results.push({test:'direct text edit and Escape cancellation',passed:true});
  await waitFor(page,'document.activeElement?.getAttribute("data-v3-element-id") === "review-grid"');
  await read(page, `document.querySelector(${JSON.stringify(cellSelector)}).dispatchEvent(new PointerEvent('pointerdown',{bubbles:true,button:0,pointerId:79}));window.dispatchEvent(new PointerEvent('pointercancel',{pointerId:79}));`);
  await delay(60);
  await read(page,'document.querySelector("[data-v3-element-id=review-grid]").focus()');
  await key(page,'F2');
  await waitFor(page,'Boolean(document.querySelector(".report-designer-canvas-text-editor textarea"))');
  assert.equal(await read(page,'document.querySelector(".report-designer-canvas-text-editor textarea").value'),'画布直接输入');
  await key(page,'Escape');
  await waitFor(page,'document.activeElement?.getAttribute("data-v3-element-id") === "review-grid"');
  results.push({test:'F2 edits selected grid cell and restores canvas focus',passed:true});
  await read(page,'document.querySelector("[data-v3-element-id=review-grid]").focus()');
  await key(page,'c',primaryModifier); await key(page,'v',primaryModifier); await delay(100);
  assert(!await read(page,'document.body.innerText.includes("当前草稿不能保存")'),'copying a table must keep a valid document');
  const gridTexts=await read(page,'window.__designerSchema.layers.flatMap(layer=>layer.elements).filter(element=>element.flowKind==="Grid").map(element=>element.block.rows[0].cells[0].text)');
  assert.deepEqual(gridTexts,['画布直接输入','画布直接输入'],'keyboard copy/paste must use the latest edited selection');
  results.push({test:'keyboard copy/paste after table editing',passed:true});
  await click(page,'.report-designer-v3-sidebar-tabs button:nth-child(3)');
  await click(page,'.report-designer-v3-layer-name');
  await click(page,'button[aria-label="明细表"]');
  assert(await read(page,'window.__designerSchema.layers.filter(layer=>layer.role!=="Body").every(layer=>layer.elements.every(element=>element.flowKind!=="DetailTable"))'));
  assert.equal(await read(page,'window.__designerSchema.layers.find(layer=>layer.role==="Body").elements.filter(element=>element.flowKind==="DetailTable").length'),2);
  assert(!await read(page,'document.body.innerText.includes("当前草稿不能保存")'));
  results.push({test:'detail insertion from header goes to body',passed:true});
  await click(page,'.report-designer-v3-inspector .report-designer-property-section summary');
  await read(page, `(() => {const input=Array.from(document.querySelectorAll('.report-designer-v3-inspector label')).find(label=>label.textContent.trim()==='Y (mm)').querySelector('input');input.focus();input.select()})()`);
  await page.send('Input.insertText',{text:'0'}); await key(page,'Enter');
  assert.equal(await read(page, `Array.from(document.querySelectorAll('.report-designer-v3-inspector label')).find(label=>label.textContent.trim()==='Y (mm)').querySelector('input').value`),'60');
  assert.equal(await read(page,'window.__designerSchema.layers.flatMap(layer=>layer.elements).filter(element=>element.flowKind==="DetailTable").at(-1).yHundredthMm'),6000);
  results.push({test:'coordinate editor displays canonical body bounds',passed:true});
  const detailWidth=await read(page,'window.__designerSchema.layers.flatMap(layer=>layer.elements).filter(element=>element.flowKind==="DetailTable").at(-1).widthHundredthMm');
  await read(page,'document.querySelector("button[aria-label=调整右边尺寸]").focus()');
  await key(page,'ArrowRight');
  assert.equal(await read(page,'window.__designerSchema.layers.flatMap(layer=>layer.elements).filter(element=>element.flowKind==="DetailTable").at(-1).widthHundredthMm'),detailWidth+100);
  results.push({test:'keyboard resize handles',passed:true});
  await evaluate(page,fs.readFileSync(require.resolve('axe-core/axe.min.js'),'utf8'),false);
  const accessibility = await read(page, `(async()=>{const audit=await axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21aa']}});return audit.violations.map(item=>({id:item.id,impact:item.impact,nodes:item.nodes.slice(0,3).map(node=>node.target)}));})()`);
  assert.deepEqual(accessibility,[],JSON.stringify(accessibility));
  results.push({test:'WCAG accessibility on real designer controls',passed:true});
  await captureScreenshot(page,path.join(output,'detail-properties.png'),{captureBeyondViewport:false});
  await page.send("Page.navigate",{url:`${url}?stress=1`});
  await waitFor(page,'document.querySelectorAll("[data-v3-element-id]").length >= 902');
  const before=await read(page,'window.__designerUpdates');
  await read(page, `window.__frameGaps=[];window.__lastFrame=performance.now();window.__frameLoop=()=>{const now=performance.now();window.__frameGaps.push(now-window.__lastFrame);window.__lastFrame=now;window.__frameId=requestAnimationFrame(window.__frameLoop)};window.__frameId=requestAnimationFrame(window.__frameLoop);`);
  await drag(page,'[data-v3-element-id="review-grid"]',20,190,async()=>{
    assert.equal(await read(page,'window.__designerUpdates'),before,'drag preview must not serialize/commit the document per frame');
    await read(page,'cancelAnimationFrame(window.__frameId)');
  });
  assert.equal(await read(page,'window.__designerUpdates'),before+1,'drag must create exactly one committed document');
  assert.equal(await read(page,'window.__designerErrors.length'),0);
  const gaps=await read(page,'window.__frameGaps.slice(2).sort((a,b)=>a-b)');
  results.push({test:'902-element transient drag commits once',frames:gaps.length,p95FrameIntervalMs:gaps[Math.floor(gaps.length*0.95)]??null,passed:true});
  await read(page,'document.querySelector("[data-v3-element-id=stress-0]").focus()');
  await key(page,'Enter'); await delay(80); await key(page,'F2');
  await waitFor(page,'Boolean(document.querySelector(".report-designer-canvas-text-editor textarea"))');
  const textUpdates=await read(page,'window.__designerUpdates');
  for(const text of ['复杂','模板','连续','文字','输入']) await page.send('Input.insertText',{text});
  assert.equal(await read(page,'window.__designerUpdates'),textUpdates,'typing must not serialize the complex document per character');
  await key(page,'Enter',primaryModifier);
  await waitFor(page,'document.activeElement?.getAttribute("data-v3-element-id") === "stress-0"');
  assert.equal(await read(page,'window.__designerUpdates'),textUpdates+1);
  await key(page,'z',primaryModifier);
  await waitFor(page,'window.__designerSchema.layers.flatMap(layer=>layer.elements).find(element=>element.id==="stress-0").text === "0"');
  results.push({test:'902-element text input commits once and undoes as one operation',passed:true});
  await verifyDesignerEditingUi({page,url,read,waitFor,click,key,modifier:primaryModifier,results});
  await verifyProductFieldsUi({page,url,read,waitFor,click,results,capture:() => captureScreenshot(page,path.join(output,'invoice-fields.png'))});
  await page.send("Page.navigate",{url});
  await waitFor(page,'document.querySelector("[data-v3-element-id=review-grid]")');
  await read(page,"[...document.querySelectorAll('button')].find(node=>node.textContent.trim()==='字段').click()");
  await waitFor(page,`document.querySelector('[aria-label="插入字段 船名航次"]')`);
  await click(page,'[aria-label="插入字段 船名航次"]');
  await waitFor(page,"window.__designerSchema.layers.flatMap(layer=>layer.elements).some(element=>element.fieldPath==='Invoice.Spare10')");
  assert(await read(page,"window.__designerHtml.includes('Invoice.Spare10')"));
  await captureScreenshot(page,path.join(output,'spare-field-picker.png'),{captureBeyondViewport:false});
  results.push({test:'API spare field groups insert a real selectable binding',passed:true});
  await page.send("Page.navigate",{url:`${url}?images=1`});
  await waitFor(page,'[...document.querySelectorAll("[data-v3-element-id] img")].length===2 && [...document.querySelectorAll("[data-v3-element-id] img")].every(img=>img.naturalWidth===16)');
  assert.equal(await read(page,'window.__imageCalls.filter(call=>call.kind==="download").length'),1,'duplicate image elements share one authorized request');
  assert.equal(await read(page,'window.__imageCalls.some(call=>call.kind==="list")'),false,'the resource library loads on demand');
  await read(page,'document.querySelector("[data-v3-element-id=image-0]").focus()'); await key(page,'Enter');
  await waitFor(page,'document.querySelector(".report-designer-v3-resource-gallery summary")');
  await click(page,'.report-designer-v3-resource-gallery summary');
  await waitFor(page,'document.querySelectorAll(".report-designer-v3-resource-card").length===3');
  await read(page,'document.querySelectorAll(".report-designer-v3-resource-card")[1].querySelector("button").click()');
  await waitFor(page,`window.__designerSchema.layers.flatMap(layer=>layer.elements).find(element=>element.id==='image-0').resourceId===${JSON.stringify(imageFixtures[1].id)}`);
  assert.equal(await read(page,'document.querySelectorAll(".report-designer-v3-resource-card")[1].querySelectorAll("button").length'),1,'a reused draft image cannot be recycled');
  await read(page,'document.querySelectorAll(".report-designer-v3-resource-card")[2].querySelector(".danger-button").click()');
  await waitFor(page,'document.querySelector(".confirmation-dialog")');
  await read(page,'[...document.querySelectorAll(".confirmation-dialog button")].find(button=>button.textContent.trim()==="确认回收").click()');
  await waitFor(page,'document.querySelectorAll(".report-designer-v3-resource-card").length===2');
  assert.equal(await read(page,'window.__imageCalls.filter(call=>call.kind==="recycle").length'),1);
  assert.equal(await read(page,'window.__designerErrors.length'),0);
  await captureScreenshot(page,path.join(output,'payment-image-resource-library.png'),{captureBeyondViewport:false});
  results.push({test:'payment images render, deduplicate, reuse and recycle safely',passed:true});
  await read(page,`(()=>{const input=document.querySelector('.report-designer-v3-image-editor input[type=file]');const transfer=new DataTransfer();transfer.items.add(new File([new Uint8Array(window.__imageFixtures[1].bytes)],'upload.png',{type:'image/png'}));input.files=transfer.files;input.dispatchEvent(new Event('change',{bubbles:true}));})()`);
  await waitFor(page,'window.__finishImageUpload');
  await waitFor(page,'window.__designerDraftState.isDirty && !window.__designerDraftState.isValid');
  await read(page,`[...document.querySelectorAll('.report-designer-v3-image-editor label')].find(label=>label.textContent.includes('来源为空时隐藏')).querySelector('input').click()`);
  await waitFor(page,`[...document.querySelectorAll('.report-designer-v3-image-editor label')].find(label=>label.textContent.includes('来源为空时隐藏')).querySelector('input').checked===false`);
  await read(page,'window.__finishImageUpload()');
  await waitFor(page,`document.body.innerText.includes('图片已上传并自动绑定')`);
  assert.equal(await read(page,`window.__designerSchema.layers.flatMap(layer=>layer.elements).find(element=>element.id==='image-0').hideWhenSourceEmpty`),false,'upload completion must retain newer image property edits');
  results.push({test:'upload completion uses the latest canvas draft',passed:true});
  await page.send("Page.navigate",{url:`${url}?images=1&readonly=1`});
  await waitFor(page,'document.querySelector("[data-v3-element-id=image-0] img")?.naturalWidth===16');
  assert.equal(await read(page,'Boolean(document.querySelector(".report-designer-v3-inspector"))'),false);
  results.push({test:'read-only payment canvas displays authorized images',passed:true});
  await verifyShippingMarksUi({page,url,read,waitFor,click,key,results,output});
  fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify({passed:true,results},null,2));
  console.log(`Report designer UI contracts passed (${results.length} cases).`);
} finally {
  cdp?.close();
  if(chrome) await closeChrome(chrome.browserWebSocketUrl,chrome.process);
  await new Promise(resolve=>server.close(resolve));
}

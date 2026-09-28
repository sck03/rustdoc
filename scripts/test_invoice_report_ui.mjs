import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { pathToFileURL } from "node:url";
import { CdpClient, closeChrome, delay } from "./lib/chromium-cdp.mjs";
import { locateChromeForTesting } from "./lib/report-regression-common.mjs";
import { startChrome, createPageSession, evaluate, captureScreenshot } from "./lib/web-runtime-browser-session.mjs";
import { spawnProcessTree, stopProcessTree } from "./lib/child-process-tree.mjs";

// Real React + Rust HTTP regression. Uses the existing isolated review host.
const repo = path.resolve(import.meta.dirname, "..");
const output = path.join(repo, "artifacts/invoice-report-ui", String(Date.now()));
fs.mkdirSync(output, { recursive: true });
const web = path.join(repo, "apps/export-doc-web");
const require = createRequire(path.join(web, "package.json"));
const model = path.join(output, "models.mjs");
await require("esbuild").build({ stdin: { loader: "ts", resolveDir: web, contents: `export {createEmptyInvoice} from ${JSON.stringify(path.join(web,"src/features/invoices/invoiceModel.ts"))}; export {createEmptyInvoiceItem} from ${JSON.stringify(path.join(web,"src/features/invoices/invoiceItemsEditorModel.ts"))};` }, bundle: true, platform: "node", format: "esm", outfile: model });
const { createEmptyInvoice, createEmptyInvoiceItem } = await import(pathToFileURL(model).href);
const executable = path.join(repo, "target/debug/examples", process.platform === "win32" ? "office_review.exe" : "office_review");
const server = spawnProcessTree(executable, [path.join(output, "Data")], { cwd: repo, windowsHide: true, stdio: ["ignore","pipe","pipe"] });
let chrome, cdp, page;
const run = async expression => (await evaluate(page, expression, true)).value;
async function wait(expression, message) {
  const end = Date.now() + 30000;
  while (Date.now() < end) { if (await run(`Boolean(${expression})`)) return; await delay(100); }
  throw new Error(`${message}\n${await run("document.body.innerText")}`);
}
async function click(text) {
  await wait(`[...document.querySelectorAll('button,a')].some(e=>e.textContent.trim()===${JSON.stringify(text)} && e.getClientRects().length && !e.disabled)`, `Control unavailable: ${text}`);
  await run(`(() => {const b=[...document.querySelectorAll('button,a')].find(e=>e.textContent.trim()===${JSON.stringify(text)} && e.getClientRects().length);if(!b||b.disabled)throw Error('Control unavailable: '+${JSON.stringify(text)});b.click();})()`);
}
async function navigate(url) { await page.send("Page.navigate", { url }); }
try {
  const url = await new Promise((resolve,reject) => {
    let log="";
    const timer=setTimeout(()=>reject(Error(log)),60000);
    server.once("error",error=>{clearTimeout(timer);reject(error);});
    server.once("exit",code=>{clearTimeout(timer);reject(Error(`Review host exited ${code}: ${log}`));});
    server.stderr.on("data",chunk=>{log+=chunk;});
    server.stdout.on("data",chunk=>{log+=chunk;const match=log.match(/\{"url":"([^"]+)"\}/);if(match){clearTimeout(timer);resolve(match[1]);}});
  });
  const login = await fetch(`${url}/api/auth/login`, { method:"POST",headers:{"content-type":"application/json"},body:JSON.stringify({username:"oa-review",password:"Review-2026-Test"}) }).then(r=>r.json());
  const headers={authorization:`Bearer ${login.accessToken}`,"content-type":"application/json"};
  const api=async (route,body) => {
    const response=await fetch(`${url}${route}`,{headers,method:body?"POST":"GET",body:body?JSON.stringify(body):undefined});
    const result=await response.json();assert(response.ok,JSON.stringify(result));return result;
  };
  const draft={...createEmptyInvoice("2026-09-28"),invoiceNo:"UI-SEAL-001",exporterNameEN:"SAMPLE EXPORT COMPANY",customerNameEN:"SAMPLE CUSTOMER",currency:"EUR"};
  draft.items=[1,2,3].map(n=>({...createEmptyInvoiceItem(),styleNo:`STYLE-${n}`,styleName:`Sample product ${n}`,quantity:1000+n,cartons:100+n,unitEN:"PCS",ctnUnitEN:"CTNS",unitPrice:10+n,totalPrice:(1000+n)*(10+n)}));
  const saved=await api("/api/invoices",draft);
  const invoiceId=saved.id;
  chrome=await startChrome({browserExecutable:locateChromeForTesting(repo),userDataDir:path.join(output,"Chrome"),timeoutMs:30000});
  cdp=await CdpClient.connect(chrome.browserWebSocketUrl);page=await createPageSession(cdp);
  await navigate(url);
  await wait("document.querySelector('input[autocomplete=username]')","Login missing");
  await run(`(() => {for(const [selector,value] of [['input[autocomplete=username]','oa-review'],['input[autocomplete=current-password]','Review-2026-Test']]){const e=document.querySelector(selector);Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(e,value);e.dispatchEvent(new Event('input',{bubbles:true}));}})()`);
  await click("登录");
  await wait("!document.querySelector('.login-submit-button')","Login failed");
  await navigate(`${url}/#/invoices/${invoiceId}`);
  await wait("document.querySelector('[aria-label=\"客户与出口商\"]')","Invoice missing");
  await run("[...document.querySelectorAll('details')].find(e=>e.querySelector('summary')?.textContent.includes('银行与印章')).open=true");
  const imageFile=path.join(output,"seal.png");
  fs.copyFileSync(path.join(repo,"crates/export-doc-engine/src/engine/reports/samples-shipping-marks.png"),imageFile);
  for(const label of ["单证章","报关章"]){
    const selector=`button[aria-label="上传${label}图片"]`;
    await wait(`document.querySelector(${JSON.stringify(selector)}) && !document.querySelector(${JSON.stringify(selector)}).disabled`,`${label} disabled without exporter`);
    await page.send("Page.setInterceptFileChooserDialog",{enabled:true});
    const chooser=cdp.waitForEvent("Page.fileChooserOpened");
    const position=await run(`(() => {const e=document.querySelector(${JSON.stringify(selector)});e.scrollIntoView({block:'center'});const r=e.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2};})()`);
    await page.send("Input.dispatchMouseEvent",{type:"mousePressed",button:"left",clickCount:1,...position});
    await page.send("Input.dispatchMouseEvent",{type:"mouseReleased",button:"left",clickCount:1,...position});
    const opened=await chooser;
    await page.send("DOM.setFileInputFiles",{backendNodeId:opened.backendNodeId,files:[imageFile]});
    await wait(`document.body.innerText.includes(${JSON.stringify(`${label}已上传`)})`,`${label} upload failed`);
  }
  await captureScreenshot(page,path.join(output,"invoice-seals.png"));
  await click("保存发票");
  await wait("/发票已(保存|创建)/.test(document.body.innerText)","Invoice save failed");
  const reloaded=await api(`/api/invoices/${invoiceId}`);
  assert.equal(reloaded.exporterId,0);
  assert.match(reloaded.docSealPath,/^Files\/Seals\/img-/);
  assert.match(reloaded.customsSealPath,/^Files\/Seals\/img-/);
  await navigate(`${url}/#/invoices/${invoiceId}`);
  await wait("document.querySelector('[aria-label=\"客户与出口商\"]')","Invoice reload failed");
  assert(await run("[...document.querySelectorAll('input')].filter(e=>e.value.startsWith('Files/Seals/img-')).length===2"),"Both seal references reload in React");
  await navigate(`${url}/#/reports/templates?template=invoice_template.dtpl&invoiceId=${invoiceId}`);
  await wait("document.querySelector('.report-designer-v3-workspace')","Designer missing");
  await click("预览");
  await click("样例预览");
  await wait("document.querySelector('iframe[title=模板预览]')?.srcdoc.includes('<svg')","Native sample missing");
  const sample=await run("document.querySelector('iframe[title=模板预览]').srcdoc");
  assert.match(sample,/USD\d/);assert.doesNotMatch(sample,/{{/);
  assert.match(sample, /viewBox="0 0/);
  assert.doesNotMatch(sample, /<text/);
  fs.writeFileSync(path.join(output,"sample.html"),sample);
  await captureScreenshot(page,path.join(output,"sample.png"));
  await click("当前单据");
  await wait("[...document.querySelectorAll('button')].some(e=>e.textContent.trim()==='真实数据预览'&&!e.disabled)","Saved preview not ready");
  await click("真实数据预览");
  await wait("document.querySelector('iframe[title=模板预览]')?.srcdoc.includes('UI-SEAL-001')","Saved invoice preview missing");
  const real=await run("document.querySelector('iframe[title=模板预览]').srcdoc");
  assert.match(real,/EUR11\.00/);assert.match(real,/EUR11011\.00/);
  assert.equal((real.match(/>EUR</g)||[]).length,0,"No detached currency heading");
  assert.equal(real.slice(0,real.indexOf('<svg')),sample.slice(0,sample.indexOf('<svg')),"Sample and saved source use identical page presentation");
  fs.writeFileSync(path.join(output,"saved.html"),real);
  await captureScreenshot(page,path.join(output,"saved.png"));
  console.log(`Invoice seals and native sample/saved preview passed: ${output}`);
} catch(error) {
  if(page){await captureScreenshot(page,path.join(output,"failure.png"));fs.writeFileSync(path.join(output,"failure.txt"),await run("document.body.innerText"));}
  throw error;
} finally {
  cdp?.close();if(chrome)await closeChrome(chrome.browserWebSocketUrl,chrome.process);await stopProcessTree(server,5000);
}

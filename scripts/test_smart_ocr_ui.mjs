import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { locateChromeForTesting } from './lib/chromium-executable.mjs';

const repo = path.resolve(import.meta.dirname, '..');
const web = path.join(repo, 'apps/export-doc-web');
const require = createRequire(path.join(web, 'package.json'));
const output = path.join(repo, 'artifacts/smart-ocr-ui');
const temporary = path.join(output, 'Temp');
fs.mkdirSync(temporary, { recursive: true });
Object.assign(process.env, { TEMP: temporary, TMP: temporary, TMPDIR: temporary });
const source = name => JSON.stringify(path.join(web, 'src', name).replaceAll('\\', '/'));
const bundle = path.join(output, 'app.js');
await require('esbuild').build({ stdin: { loader: 'tsx', resolveDir: web, contents: `
  import React, { useState } from 'react';
  import { createRoot } from 'react-dom/client';
  import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
  import { PermissionAccessProvider } from ${source('app/PermissionAccessContext.tsx')};
  import { SmartOcrPage } from ${source('features/tools/SmartOcrPage.tsx')};
  import { useSmartOcr } from ${source('features/tools/useSmartOcr.ts')};
  import ${source('styles/cascade.css')};
  import ${source('styles/foundation.css')};
  import ${source('styles/workspaces.css')};
  import ${source('styles/tools-ocr-exchange.css')};
  window.pending=[]; window.createdUrls=[]; window.revokedUrls=[];
  const create=URL.createObjectURL.bind(URL), revoke=URL.revokeObjectURL.bind(URL);
  URL.createObjectURL=blob=>{ const url=create(blob);window.createdUrls.push({url,blob});return url; };
  URL.revokeObjectURL=url=>{window.revokedUrls.push(url);revoke(url);};
  const delayed=(kind,input,options)=>new Promise((resolve,reject)=>window.pending.push({kind,input,signal:options.signal,resolve,reject}));
  const client={getHealth:async()=>({runtimeDependencies:[{key:'ocr-runtime',status:'ready',ready:true}]}),
    uploadOcrImage:(input,options)=>delayed('upload',input,options),
    recognizeOcrImage:(input,options)=>delayed('recognize',input,options),
    previewOcrImage:(input,options)=>delayed('preview',input,options)};
  function Probe(){const ocr=useSmartOcr(client,true);return <>
    <button onClick={()=>ocr.usePathImage('C:/first.png',true)}>第一张</button>
    <button onClick={()=>ocr.usePathImage('C:/second.png',true)}>第二张</button>
    <button onClick={()=>ocr.recognizeCurrentImage()}>识别</button>
    <output id='preview'>{ocr.imagePreviewUrl}</output><output id='result'>{ocr.recognizedText}</output><output id='message'>{ocr.message}</output>
  </>;}
  function App(){const[open,setOpen]=useState(true);return <><button onClick={()=>setOpen(false)}>离开</button>{open?(window.mode==='desktop'?<Probe/>:<SmartOcrPage client={client}/>):<p>已离开</p>}</>;}
  const query=new QueryClient({defaultOptions:{queries:{retry:false,refetchOnWindowFocus:false}}});
  createRoot(document.getElementById('root')).render(<React.StrictMode><QueryClientProvider client={query}>
    <PermissionAccessProvider permissions={window.mode==='denied'?[]:[{resourceKey:'document.ocr',action:'operate',dataScope:'all'}]}><App/></PermissionAccessProvider>
  </QueryClientProvider></React.StrictMode>);
` }, outfile: bundle, bundle: true, platform: 'browser', format: 'iife', jsx: 'automatic', logLevel: 'silent' });

const context = await require('playwright').chromium.launchPersistentContext(path.join(output, 'Chrome'), { executablePath: locateChromeForTesting(repo), headless: true });
const results=[];
try {
  async function scenario(name, mode, test) {
    const page=await context.newPage();page.setDefaultTimeout(10_000);
    const errors=[];page.on('pageerror',error=>errors.push(error.message));
    try {
      await page.setContent('<!doctype html><html lang="zh-CN"><title>OCR verification</title><div id="root"></div></html>');
      await page.evaluate(mode=>{window.mode=mode;if(mode==='desktop'){window.isTauri=true;window.__TAURI_INTERNALS__={invoke:async()=>null};}},mode);
      await page.addScriptTag({path:bundle});
      await page.addStyleTag({path:path.join(output,'app.css')});
      await test(page);assert.deepEqual(errors,[]);results.push(name);
    } finally {await page.close();}
  }
  const settle=page=>page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
  await scenario('browser file upload, one submission, source cancellation and Blob cleanup','browser',async page=>{
    await page.getByRole('button',{name:'选择 OCR 图片'}).waitFor({state:'visible'});
    assert.equal(await page.getByLabel('图片文件').getAttribute('readonly'),'');
    const fixture=path.join(repo,'tests/OcrFixtures/invoice.png');
    await page.getByLabel('上传 OCR 图片').setInputFiles(fixture);
    await page.getByRole('button',{name:'开始识别'}).click();
    await page.waitForFunction(()=>window.pending.length===1);
    assert.equal(await page.evaluate(()=>window.pending[0].kind),'upload');
    assert.equal(await page.evaluate(()=>window.pending[0].input.sourceName),'invoice.png');
    await page.evaluate(()=>{const button=document.querySelector('[aria-label="开始识别"]');button.click();button.click();});
    assert.equal(await page.evaluate(()=>window.pending.length),1);
    await page.getByLabel('上传 OCR 图片').setInputFiles(path.join(repo,'tests/OcrFixtures/skew_7deg.png'));
    await page.waitForFunction(()=>window.pending[0].signal.aborted);
    await page.evaluate(()=>window.pending[0].resolve({fullText:'过时结果',lines:[]}));await settle(page);
    assert.equal(await page.getByLabel('识别文本').inputValue(),'');
    await page.getByRole('button',{name:'开始识别'}).click();
    await page.waitForFunction(()=>window.pending.length===2);
    await page.evaluate(()=>window.pending[1].resolve({fullText:'当前图片',lines:[]}));
    await page.waitForFunction(()=>document.querySelector('[aria-label="识别文本"]').value==='当前图片');
    await page.screenshot({path:path.join(output,'browser-upload.png'),fullPage:true});
    await page.getByRole('button',{name:'离开',exact:true}).click();await settle(page);
    assert.deepEqual(await page.evaluate(()=>window.revokedUrls.sort()),await page.evaluate(()=>window.createdUrls.map(item=>item.url).sort()));
  });
  for (const reject of [false,true]) await scenario(`desktop late preview ${reject?'failure':'success'} cannot replace current image`,'desktop',async page=>{
    await page.getByRole('button',{name:'第一张'}).click();
    await page.waitForFunction(()=>window.pending.length===1);
    assert.equal(await page.evaluate(()=>window.pending[0].signal.aborted),false,'selecting a source must not cancel its newly started preview');
    await page.getByRole('button',{name:'第二张'}).click();
    await page.waitForFunction(()=>window.pending.length===2&&window.pending[0].signal.aborted);
    await page.evaluate(()=>window.pending[1].resolve(new Blob(['second'],{type:'image/png'})));
    await page.waitForFunction(()=>document.querySelector('#preview').textContent);
    const current=await page.locator('#preview').textContent();
    await page.evaluate(reject=>reject?window.pending[0].reject(new Error('旧图片失败')):window.pending[0].resolve(new Blob(['first'])),reject);await settle(page);
    assert.equal(await page.locator('#preview').textContent(),current);assert.equal(await page.locator('#message').textContent(),'');
    await page.getByRole('button',{name:'识别',exact:true}).click();
    await page.waitForFunction(()=>window.pending.length===3);
    await page.getByRole('button',{name:'离开',exact:true}).click();
    await page.waitForFunction(()=>window.pending[2].signal.aborted);
    await page.evaluate(()=>window.pending[2].reject(new Error('离开后的错误')));await settle(page);
  });
  await scenario('permission keeps recognition disabled','denied',async page=>{
    assert(await page.getByRole('button',{name:'选择 OCR 图片'}).isDisabled());
    assert(await page.getByRole('button',{name:'开始识别'}).isDisabled());
  });
  fs.writeFileSync(path.join(output,'result.json'),JSON.stringify({passed:results.length,results},null,2));
  console.log(`Smart OCR UI: ${results.length} scenarios passed.`);
} finally {await context.close();}

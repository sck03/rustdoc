import assert from 'node:assert/strict';
import { createReactUiFixture } from './lib/react-ui-fixture.mjs';
const {web, require, bundle, source: src, launch} = createReactUiFixture('recovery-pagination-ui');
await require('esbuild').build({stdin:{resolveDir:web,loader:'tsx',contents:`
import React from 'react';import {createRoot} from 'react-dom/client';
import {MemoryRouter} from 'react-router-dom';
import {QueryClient,QueryClientProvider} from '@tanstack/react-query';
import {ConfirmationProvider} from ${src('ui/ConfirmationProvider.tsx')};
import {RecoveryMaintenancePage} from ${src('features/settings/RecoveryMaintenancePage.tsx')};
import {BackupSchedulePanel} from ${src('features/settings/BackupSchedulePanel.tsx')};
const root=createRoot(document.getElementById('root'));
const cache=new QueryClient({defaultOptions:{queries:{retry:false}}});
window.render=(mode='maintenance')=>root.render(<MemoryRouter><QueryClientProvider client={cache}><ConfirmationProvider>{mode==='maintenance'?<RecoveryMaintenancePage/>:<BackupSchedulePanel controller={{status:{plan:{enabled:false},encryptionPasswordSet:mode==='saved',timeZone:'Asia/Shanghai'},draft:{plan:{enabled:false,retentionCount:14,retentionDays:0},encryptionPassword:''},confirmation:'',updatePlan:()=>{},setPassword:()=>{},setConfirmation:()=>{}}} disabled={false} onCredentials={()=>window.credentialsOpened=true}/>}</ConfirmationProvider></QueryClientProvider></MemoryRouter>);window.render();
`},outfile:bundle,bundle:true,platform:'browser',format:'iife',jsx:'automatic',logLevel:'silent'});
const browser=await launch();
try {
 const page=await browser.newPage();page.setDefaultTimeout(10000);
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 const first='a'.repeat(32),last='b'.repeat(32),cursor='point-'+first+'.commit';
 const point=id=>({id,createdAt:'2026-11-28T00:00:00Z',provider:'PostgreSQL',sizeBytes:100});
 let status={state:'Unlocked',busy:false,message:'',planId:'',locations:[],artifacts:[],points:[point(first)],nextCursor:cursor};
 let empty=false;const queries=[];
 await page.route('http://recovery.test/**',async route=>{
  const url=new URL(route.request().url());let body;
  if(url.pathname==='/')return route.fulfill({contentType:'text/html',body:'<!doctype html><html lang="zh-CN"><div id="root"></div></html>'});
  if(url.pathname==='/maintenance-api/session')body={csrfToken:'test',expiresInSeconds:3600};
  else if(url.pathname==='/maintenance-api/status')body=status;
  else {const request=route.request().postDataJSON();queries.push(request.after);status={...status,points:request.after?(empty?[]:[point(last)]):[point(first)],nextCursor:request.after?'':cursor};body=status;}
  return route.fulfill({contentType:'application/json',body:JSON.stringify(body)});
 });
 await page.goto('http://recovery.test/');await page.addScriptTag({path:bundle});
 const select=page.getByRole('combobox',{name:/^恢复点/});
 await select.selectOption(first);await page.getByRole('button',{name:'下一页恢复点',exact:true}).click();
 await page.waitForFunction(id=>!!document.querySelector('option[value="'+id+'"]'),last);
 assert.equal(await select.inputValue(),'');assert.equal(await page.getByRole('button',{name:'完整预检',exact:true}).isDisabled(),true);
 assert.equal(await page.getByRole('button',{name:'下一页恢复点',exact:true}).count(),0);
 await page.getByRole('button',{name:'返回首页恢复点',exact:true}).click();await select.selectOption(first);
 empty=true;await page.getByRole('button',{name:'下一页恢复点',exact:true}).click();
 await page.waitForFunction(()=>[...document.querySelectorAll('select')].some(select=>select.options[0]?.text==='请选择恢复点'&&select.matches(':disabled')));
 assert.equal(await page.getByRole('button',{name:'返回首页恢复点',exact:true}).isEnabled(),true);
  await page.reload();await page.addScriptTag({path:bundle});
  await page.getByRole('button',{name:'返回首页恢复点',exact:true}).waitFor();
  assert.equal(await select.isDisabled(),true);
 await page.getByRole('button',{name:'返回首页恢复点',exact:true}).click();await select.selectOption(first);
 assert.deepEqual(queries,[cursor,'',cursor,'']);
 await page.evaluate(()=>window.render('saved'));
 assert.equal(await page.getByLabel('恢复凭证',{exact:true}).count(),0);
 await page.getByRole('button',{name:'管理恢复凭证',exact:true}).click();assert.equal(await page.evaluate(()=>window.credentialsOpened),true);
 await page.evaluate(()=>window.render('new'));await page.getByLabel('恢复凭证',{exact:true}).waitFor();
 assert.deepEqual(errors,[]);
  console.log('Recovery UI: next/home navigation, empty-page reload, selection invalidation and single credential entry passed.');
} catch (cause) { console.error(await browser.pages().at(-1)?.locator("body").innerText()); throw cause; } finally {await browser.close();}

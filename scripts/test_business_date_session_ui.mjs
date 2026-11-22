import assert from 'node:assert/strict';
import path from 'node:path';
import { createReactUiFixture } from './lib/react-ui-fixture.mjs';

const { web, output, require, source, launch } = createReactUiFixture('business-date-session-ui');
const build = await require('esbuild').build({ write:false, bundle:true, format:'iife', platform:'browser', jsx:'automatic', logLevel:'silent', stdin:{loader:'tsx', resolveDir:web, contents:`
import React,{useCallback,useEffect,useRef,useState} from 'react';
import {createRoot} from 'react-dom/client';
import {useBusinessDateSessionRefresh} from ${source('app/useBusinessDateSessionRefresh.ts')};
import {subscribeToAuthenticationFailure} from ${source('api/authenticationFailureEvents.ts')};
import {ApiError} from ${source('api/index.ts')};
window.calls=[];window.authFailures=0;window.ApiError=ApiError;
Object.defineProperty(document,'visibilityState',{configurable:true,get:()=>window.hidden?'hidden':'visible'});
const user={id:1,businessDate:'2026-11-22',businessDateValidUntilUtc:'2026-11-22T16:00:00Z'};
const clients=[0,1].map(owner=>({getCurrentUser:init=>new Promise((resolve,reject)=>window.calls.push({owner,signal:init.signal,resolve,reject}))}));
function Probe({owner}) {
  const [session,store]=useState({accessToken:'test-session',apiBaseUrl:'http://example.test',user});
  const sessionRef=useRef(session);sessionRef.current=session;
  const validatedUserTokenRef=useRef(session.accessToken);
  const setSession=useCallback(value=>{sessionRef.current=value;store(value)},[]);
  useEffect(()=>subscribeToAuthenticationFailure(()=>window.authFailures++),[]);
  useBusinessDateSessionRefresh({client:clients[owner],desktopContextLoading:false,session,sessionRef,setSession,validatedUserTokenRef});
  return <output id='date'>{session.user.businessDate}</output>;
}
const root=createRoot(document.getElementById('root'));
window.render=(owner=0)=>root.render(<React.StrictMode><Probe owner={owner}/></React.StrictMode>);
window.hide=()=>root.unmount();window.render();
window.succeed=(index=0,stale=false)=>window.calls[index].resolve(stale?user:{...user,businessDate:'2026-11-23',businessDateValidUntilUtc:'2026-11-23T16:00:00Z'});
` }});
const context=await launch();
let passed=0;
async function scenario(check) {
  const page=await context.newPage(), errors=[];
  page.setDefaultTimeout(7000);page.on('pageerror',error=>errors.push(error.message));
  try {
    await page.clock.install({time:new Date('2026-11-22T15:59:59Z')});
    await page.setContent('<!doctype html><html lang="zh-CN"><title>业务日恢复回归</title><div id="root"></div></html>');
    await page.addScriptTag({content:build.outputFiles[0].text});
    await page.locator('#date').waitFor();
    await check(page);assert.deepEqual(errors,[]);passed++;
  } catch(error) { await page.screenshot({path:path.join(output,`failure-${passed}.png`)});throw error; }
  finally { await page.close(); }
}
async function crossBoundary(page) {
  await page.clock.runFor(2500);
  await page.waitForFunction(()=>window.calls.length===1);
}
try {
  for(const failure of ['network','http','timeout']) await scenario(async page=>{
    await crossBoundary(page);
    await page.evaluate(failure=>window.calls[0].reject(failure==='network'?new TypeError('temporary network outage'):failure==='timeout'?new DOMException('request deadline','TimeoutError'):new window.ApiError(503,'temporary outage','')),failure);
    await page.clock.runFor(61000);
    assert.equal(await page.evaluate(()=>window.calls.length),2,'A visible page recovers without a visibility event');
    await page.evaluate(()=>window.succeed(1));
    await page.waitForFunction(()=>document.querySelector('#date').textContent==='2026-11-23');
    await page.clock.runFor(120000);
    assert.equal(await page.evaluate(()=>window.calls.length),2,'Success schedules the next business boundary');
  });
  for(const status of [401,403]) await scenario(async page=>{
    await crossBoundary(page);
    await page.evaluate(status=>window.calls[0].reject(new window.ApiError(status,'permanent failure','')),status);
    await page.clock.runFor(600000);
    assert.deepEqual(await page.evaluate(()=>[window.calls.length,window.authFailures]),[1,status===401?1:0]);
  });
  for(const unmount of [false,true]) await scenario(async page=>{
    await crossBoundary(page);
    await page.evaluate(unmount=>unmount?window.hide():window.render(1),unmount);
    await page.waitForFunction(()=>window.calls[0].signal.aborted);
    await page.evaluate(()=>window.calls[0].resolve({businessDate:'obsolete',businessDateValidUntilUtc:'2026-11-23T16:00:00Z'}));
    if(unmount) { await page.clock.runFor(600000);assert.equal(await page.evaluate(()=>window.calls.length),1); }
    else {
      await page.clock.runFor(1);
      await page.waitForFunction(()=>window.calls.length===2);
      assert.equal(await page.locator('#date').textContent(),'2026-11-22');
      await page.evaluate(()=>window.succeed(1));
      await page.waitForFunction(()=>document.querySelector('#date').textContent==='2026-11-23');
    }
  });
  await scenario(async page=>{
    await page.evaluate(()=>{window.hidden=true;document.dispatchEvent(new Event('visibilitychange'));});
    await page.clock.runFor(120000);
    assert.equal(await page.evaluate(()=>window.calls.length),0);
    await page.evaluate(()=>{window.hidden=false;document.dispatchEvent(new Event('visibilitychange'));});
    await page.waitForFunction(()=>window.calls.length===1);
    await page.evaluate(()=>window.succeed());
    await page.waitForFunction(()=>document.querySelector('#date').textContent==='2026-11-23');
  });
  await scenario(async page=>{
    await crossBoundary(page);await page.evaluate(()=>window.succeed(0,true));
    await page.clock.runFor(1000);assert.equal(await page.evaluate(()=>window.calls.length),1);
    await page.clock.runFor(30000);assert.equal(await page.evaluate(()=>window.calls.length),2,'An old deadline uses bounded polling instead of spinning');
  });
  console.log(`Business-date session recovery: ${passed} scenarios passed; ${output}`);
} finally { await context.close(); }

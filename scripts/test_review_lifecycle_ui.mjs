import assert from "node:assert/strict";
import { createReactUiFixture } from "./lib/react-ui-fixture.mjs";
const {web,require,source,launch}=createReactUiFixture("review-lifecycle-ui");
const bundle=await require("esbuild").build({write:false,bundle:true,format:"iife",platform:"browser",jsx:"automatic",loader:{".css":"empty"},logLevel:"silent",stdin:{resolveDir:web,loader:"tsx",contents:`
import React from 'react';import {createRoot} from 'react-dom/client';
import {MemoryRouter} from 'react-router-dom';import {QueryClient,QueryClientProvider} from '@tanstack/react-query';
import {HsCodeKnowledgePage} from ${source('features/master-data/HsCodeKnowledgePage.tsx')};
import {AnnouncementsPage} from ${source('features/communication/AnnouncementsPage.tsx')};
import {ResourceSubmissionHistory} from ${source('features/office/ResourceSubmissionHistory.tsx')};
import {PermissionAccessProvider} from ${source('app/PermissionAccessContext.tsx')};
import {ConfirmationProvider} from ${source('ui/ConfirmationProvider.tsx')};
import {UnsavedChangesProvider} from ${source('ui/unsavedChangesGuard.tsx')};
window.calls=[];window.searches=[];window.saved=[];window.historyCalls=[];window.detailCalls=[];window.resourceId=7;
window.cache=new QueryClient({defaultOptions:{queries:{retry:false},mutations:{retry:false}}});
const permissions=['view','manage','operate'].flatMap(action=>['document.hs-knowledge','office.announcements'].map(resourceKey=>({resourceKey,action,dataScope:'all'})));
const user={id:1,companyScope:'TEST',departmentId:'GENERAL',contextId:'one',businessTimeZone:'Asia/Shanghai',capabilities:{permissions}};
const announcement={id:7,requestKey:'old',title:'Old A',body:'old body',status:'Draft',publishVersion:0,versionNumber:1,authorName:'测试',attachments:[],startsAt:'2026-11-20T00:00:00Z',expiresAt:'2026-12-20T00:00:00Z'};
const client={searchHsCodeKnowledge:async({query})=>{window.searches.push(query);return {message:'',items:[{name:query,rawCode:'5208',currentCode:'5208',canUse:true,matchReasons:[],conflictWarnings:[],resolutionStatus:'ManuallyVerified'}]};},
recordHsCodeKnowledgeFeedback:(r,o)=>new Promise(resolve=>window.calls.push({r,signal:o?.signal,resolve})),
manageAnnouncements:async()=>({items:[announcement],totalCount:1,pageNumber:1}),listAnnouncementDepartments:async()=>[],
getAnnouncement:(r,o)=>new Promise(resolve=>window.calls.push({r,signal:o?.signal,resolve:()=>resolve(announcement)})),
createAnnouncement:async({body})=>{window.saved.push({action:'create',body});return {...announcement,...body,id:8};},updateAnnouncement:async({body})=>{window.saved.push({action:'update',body});return {...announcement,...body};}};
for (const name of ['MeetingBooking','OfficeSupplyRequest']) {
 client['listSubmissionsOf'+name]=async({id,pageNumber})=>{window.historyCalls.push({name,id,pageNumber});return {items:[{id:id*100+pageNumber,submissionNumber:pageNumber,submittedAt:'2026-11-20T00:00:00Z'}],totalCount:21,pageNumber,pageSize:20};};
 client['getSubmissionOf'+name]=async({id,submissionId})=>{window.detailCalls.push({name,id,submissionId});return {id:submissionId,submissionNumber:submissionId%100,documentStatus:'NotApproved',formName:'历史申请',departmentName:'测试部门',request:{id,kind:name==='MeetingBooking'?'room':'supply',title:'历史标题 '+id,employeeName:'历史申请人',roomName:'会议室',attendeeCount:3,supplyName:'物品',quantity:2,unit:'件',purpose:'历史用途',attachments:[],approvalPlan:null}};};
}
const otherClient={...client};const root=createRoot(document.getElementById('root'));
window.render=(owner=0,enabled=true)=>root.render(<React.StrictMode><MemoryRouter><QueryClientProvider client={window.cache}><PermissionAccessProvider permissions={permissions.filter(p=>enabled||p.action==='view')}><ConfirmationProvider><UnsavedChangesProvider>{['rooms','supplies'].includes(window.mode)?<ResourceSubmissionHistory client={client} user={{...user,contextId:owner?'two':'one'}} kind={window.mode} row={{id:window.resourceId,versionNumber:1,status:'Pending'}}/>:window.mode==='hs'?<HsCodeKnowledgePage client={owner?otherClient:client} businessDate="2026-11-20"/>:<AnnouncementsPage client={owner?otherClient:client} user={user}/>}</UnsavedChangesProvider></ConfirmationProvider></PermissionAccessProvider></QueryClientProvider></MemoryRouter></React.StrictMode>);
window.close=()=>root.unmount();window.render();
`}});
const browser=await launch();let passed=0;
async function scenario(mode,run){const page=await browser.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));page.setDefaultTimeout(10000);try{await page.setContent('<!doctype html><html lang="zh-CN"><title>请求作用域回归</title><div id="root"></div></html>');await page.evaluate(mode=>window.mode=mode,mode);await page.addScriptTag({content:bundle.outputFiles[0].text});await run(page);assert.deepEqual(errors,[]);passed++;}finally{await page.close();}}
try{
 await scenario('hs',async page=>{const input=page.locator('.knowledge-search-form input');await input.fill('Cotton');await page.getByRole('button',{name:'查询本地知识库'}).click();await page.getByRole('button',{name:'确认匹配',exact:true}).click();await page.waitForFunction(()=>window.calls.length===1);await input.fill('Ceramics');await page.getByRole('button',{name:'查询本地知识库'}).click();await page.getByRole('heading',{name:'Ceramics',exact:true}).waitFor();assert.equal(await page.evaluate(()=>window.calls[0].signal.aborted),true);await page.evaluate(()=>window.calls[0].resolve({success:true}));await page.evaluate(()=>new Promise(requestAnimationFrame));assert.equal(await page.getByText(/已记录：当前商品/).count(),0);assert.deepEqual(await page.evaluate(()=>window.searches),['Cotton','Ceramics']);});
 for(const action of ['owner','permission','unmount'])await scenario('hs',async page=>{await page.locator('.knowledge-search-form input').fill('Cotton');await page.getByRole('button',{name:'查询本地知识库'}).click();await page.getByRole('button',{name:'确认匹配',exact:true}).click();await page.waitForFunction(()=>window.calls.length===1);await page.evaluate(action=>action==='unmount'?window.close():window.render(action==='owner'?1:0,action!=='permission'),action);await page.waitForFunction(()=>window.calls[0].signal.aborted);await page.evaluate(()=>window.calls[0].resolve({success:true}));assert.equal(await page.getByText(/已记录：当前商品/).count(),0);});
 await scenario('announcement',async page=>{await page.getByRole('button',{name:'继续编辑',exact:true}).click();await page.waitForFunction(()=>window.calls.length===1);await page.getByRole('button',{name:'新建公告',exact:true}).click();await page.getByLabel('公告标题',{exact:true}).fill('New B');await page.getByLabel('公告正文',{exact:true}).fill('New body');assert.equal(await page.evaluate(()=>window.calls[0].signal.aborted),true);await page.evaluate(()=>window.calls[0].resolve());await page.getByRole('dialog',{name:'新建公告',exact:true}).waitFor();assert.equal(await page.getByLabel('公告标题',{exact:true}).inputValue(),'New B');await page.getByRole('button',{name:'保存草稿',exact:true}).click();await page.waitForFunction(()=>window.saved.length===1);assert.equal(await page.evaluate(()=>window.saved[0].action),'create');assert.equal(await page.evaluate(()=>window.saved[0].body.title),'New B');});
 for(const kind of ['rooms','supplies']) await scenario(kind,async page=>{
   assert.equal(await page.evaluate(()=>window.historyCalls.length),0,'collapsed history must not fetch');
   await page.getByText('提交版本',{exact:true}).click();
   await page.getByRole('button',{name:/提交版本 1 ·/}).click();
   await page.getByRole('heading',{name:'历史申请 · 历史标题 7'}).waitFor();
   assert.deepEqual(await page.evaluate(()=>[...new Set(window.detailCalls.map(c=>c.submissionId))]),[701]);
   await page.getByRole('button',{name:'下一页',exact:true}).click();
   await page.getByRole('button',{name:/提交版本 2 ·/}).waitFor();
   assert.equal(await page.getByRole('article',{name:'提交时的申请资料'}).count(),0,'page switch clears selected version');
   await page.getByRole('button',{name:/提交版本 2 ·/}).click();
   await page.getByRole('heading',{name:'历史申请 · 历史标题 7'}).waitFor();
   await page.evaluate(()=>{window.resourceId=8;window.render(1);});
   await page.waitForFunction(()=>window.historyCalls.some(c=>c.id===8&&c.pageNumber===1));
   assert.equal(await page.getByRole('heading',{name:'历史申请 · 历史标题 7'}).count(),0,'record and identity switch clears selected snapshot');
   await page.getByRole('button',{name:/提交版本 1 ·/}).click();
   await page.getByRole('heading',{name:'历史申请 · 历史标题 8'}).waitFor();
 });
 console.log('Review lifecycle UI passed ('+passed+' scenarios).');
}finally{await browser.close();}

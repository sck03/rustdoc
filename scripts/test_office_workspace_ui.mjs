import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import http from "node:http";
import { createRequire } from "node:module";
import { CdpClient, closeChrome, delay } from "./lib/chromium-cdp.mjs";
import { locateChromeForTesting } from "./lib/report-regression-common.mjs";
import { startChrome, createPageSession, evaluate, captureScreenshot } from "./lib/web-runtime-browser-session.mjs";
import { runPersonnelOrganizationUi } from "./lib/personnel-organization-ui-scenarios.mjs";
import { runAdministrationMaintenanceUi } from "./lib/administration-maintenance-ui-scenarios.mjs";

const repo = path.resolve(import.meta.dirname, "..");
const web = path.join(repo, "apps/export-doc-web");
const output = path.join(repo, "artifacts/office-workspace-ui");
const require = createRequire(path.join(web, "package.json"));
fs.mkdirSync(output, { recursive: true });
const source = name => JSON.stringify(path.join(web, "src", name).replaceAll("\\", "/"));
await require("esbuild").build({ stdin: { loader: "tsx", resolveDir: web, contents: `
  import React from 'react';
  import { createRoot } from 'react-dom/client';
  import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
  import { MemoryRouter, Routes, Route } from 'react-router-dom';
  import { ConfirmationProvider } from ${source("ui/ConfirmationProvider.tsx")};
  import { UnsavedChangesProvider } from ${source("ui/unsavedChangesGuard.tsx")};
  import { MeetingRoomsPage } from ${source("features/office/MeetingRoomsPage.tsx")};
  import { OfficeSuppliesPage } from ${source("features/office/OfficeSuppliesPage.tsx")};
  import { PersonnelPage } from ${source("features/office/PersonnelPage.tsx")};
  import { AccessControlPage } from ${source("features/access-control/AccessControlPage.tsx")};
  import { OrganizationDirectoryPage } from ${source("features/organization/OrganizationDirectoryPage.tsx")};
  import ${source("styles/cascade.css")}; import ${source("styles/foundation.css")};
  import ${source("styles/workspaces.css")}; import ${source("styles/responsive.css")};
  const params = new URLSearchParams(location.search), mode=params.get('mode') || 'rooms', admin=params.get('role') !== 'employee';
  const register=params.get('role')==='register';
  const now=Date.now(), date=new Date(now).toISOString().slice(0,10);
  const actions=['view','create','cancel','edit','approve','issue','return','restock','manage'];
  const permissions=['office.rooms','office.supplies'].flatMap(resourceKey=>(admin?actions:actions.slice(0,4)).map(action=>({resourceKey,action,dataScope:admin?'company':'own'})));
  const personnelActions=['view','view-details','create','edit','delete','transition','assign'];
  permissions.push(...(admin?personnelActions:['view']).map(action=>({resourceKey:'office.people',action,dataScope:'company'})));
  const user={id:admin?99:1,username:admin?'admin':'employee',fullName:admin?'行政管理员':'示例员工',companyScope:'DEMO',departmentId:'D1',businessDate:date,businessTimeZone:'Asia/Shanghai',capabilities:{permissions:permissions.filter(p=>!register||!['approve','assign'].includes(p.action)),usesOfficeRegister:register,canManageUsers:admin}};
  const room={id:1,name:'三层第一会议室',location:'办公楼三层东侧',equipment:'投影设备、白板、视频会议终端',capacity:12,maximumBookingHours:8,advanceBookingDays:90,requiresKey:true,isActive:true,inUse:false,versionNumber:1};
  const supply={id:1,name:'会议投影设备',unit:'台',location:'行政办公室',description:'会议及培训临时借用，请按期归还。',isReturnable:true,isActive:true,stockQuantity:12,reservedQuantity:2,availableQuantity:10,minimumStock:3,lowStock:false,versionNumber:2};
  const booking={id:1,meetingRoomId:1,roomName:room.name,location:room.location,requiresKey:true,ownerUserId:1,applicantName:'示例员工',departmentId:'D1',title:'项目周会与交付沟通',attendeeCount:5,startsAt:new Date(now+7200000).toISOString(),endsAt:new Date(now+10800000).toISOString(),status:'Pending',createdAt:new Date(now-600000).toISOString(),versionNumber:1};
  const request={id:1,officeSupplyId:1,supplyName:supply.name,unit:'台',isReturnable:true,ownerUserId:1,applicantName:'示例员工',departmentId:'D1',purpose:'客户交流会议',quantity:2,returnedQuantity:0,returnDueDate:date,status:'Pending',createdAt:booking.createdAt,versionNumber:1};
  const page=items=>({items,totalCount:items.length,pageNumber:1,pageSize:24,totalPages:1});
  const departments=[{code:'D1',name:'业务部',isActive:true},{code:'D2',name:'运营部',isActive:true}].map(item=>({...item,companyCode:'DEMO',versionNumber:1,parentCode:null,managerEmployeeId:null,managerName:''}));
  const companies=[{code:'DEMO',name:'示例公司',isActive:true,versionNumber:1}];
  const imageFiles=new Map();
  const profile={fullName:'张宁',workEmail:'zhang.ning@example.test',workPhone:'010-5555 1001',workLocation:'总部三楼',personalPhone:'13800000000',emergencyContact:'家属',emergencyPhone:'13900000000',notes:'人事档案示例备注',identityNumber:'11010519491231002X',identityAuthority:'示例签发机关',registeredAddress:'测试地址',identityValidFrom:'2020-01-01',identityValidUntil:null,identityLongTerm:true};
  let person={employee:{id:1,employeeNumber:'EMP-001',fullName:profile.fullName,departmentId:'D1',departmentName:'业务部',jobTitle:'业务专员',workEmail:profile.workEmail,workPhone:profile.workPhone,workLocation:profile.workLocation,status:'Probation',canViewDetails:admin},profile,employmentType:'FullTime',hireDate:date,lastEffectiveDate:date,probationEndsOn:date,contractEndsOn:null,confirmedOn:null,departedOn:null,account:{id:1,username:'employee',fullName:profile.fullName,departmentId:'D1',isActive:true,versionNumber:1},versionNumber:1,canEdit:admin,canTransition:admin,canLinkAccount:admin};
  if(register){person.account=null;person.canLinkAccount=false;booking.status='Approved';request.status='Approved';}
  window.__personnelClear=false; window.__personnelConflict=false;
  const resources=['office.rooms','office.supplies'].map((key,index)=>({key,name:index?'物品领用':'会议室预约',group:'公司行政',workspace:'office',moduleKey:key,sortOrder:index,isTechnical:false,supportsDataScope:true,actions:actions.filter(action=>index||action!=='restock').map((key,index)=>({key,name:({view:'查看',create:'申请',cancel:'取消',approve:'审批',issue:'交接',return:'归还登记',restock:'库存补充',manage:'资源管理'})[key],description:'操作公司行政资源',sortOrder:index,presetLevel:index<1?'view':index<3?'operate':'manage'}))}));
  const template={id:7,code:'OfficeEmployee',name:'公司员工',description:'会议室与物品申请',isSystem:true,isActive:true,versionNumber:1,grants:permissions.filter(p=>p.resourceKey==='office.people'?p.action==='view':['view','create','cancel'].includes(p.action)).map(p=>({...p,dataScope:p.resourceKey==='office.people'?'company':'own'})),effectiveGrants:[]};
  resources.push({key:'office.people',name:'人员信息管理',group:'公司行政',workspace:'office',moduleKey:'office.people',sortOrder:350,isTechnical:false,supportsDataScope:true,actions:personnelActions.map((key,index)=>({key,name:({view:'公司通讯录','view-details':'人事档案与记录',create:'入职登记',edit:'编辑档案',transition:'转正、调岗与离职',assign:'关联账号'})[key],description:'人员管理动作',sortOrder:index,presetLevel:index===0?'view':index<4?'operate':'manage'}))});
  person.images=[];
  person.canDelete=admin;person.canCorrectRegistration=register;person.deleteRestriction='';
  let personDeleted=false;
  window.__officeCalls=[]; window.__officeErrors=[];
  window.addEventListener('error',e=>window.__officeErrors.push(e.message));
  window.addEventListener('unhandledrejection',e=>window.__officeErrors.push(String(e.reason)));
  const record=(name,input,value)=>{window.__officeCalls.push({name,input}); return Promise.resolve(value)};
  const client={
    deleteOrganizationCompany:input=>{companies.splice(companies.findIndex(item=>item.code===input.code),1);return record('deleteCompany',input,undefined)},
    deleteOrganizationDepartment:input=>{departments.splice(departments.findIndex(item=>item.code===input.code),1);return record('deleteDepartment',input,undefined)},
    getOrganizationDirectory:async()=>structuredClone({companies,departments}),
    createOrganizationCompany:input=>{const item={...input.body,versionNumber:1};companies.push(item);return record('createCompany',input,structuredClone(item))},
    updateOrganizationCompany:input=>{const item=companies.find(item=>item.code===input.code);Object.assign(item,input.body,{versionNumber:item.versionNumber+1});return record('updateCompany',input,structuredClone(item))},
    createOrganizationDepartment:input=>{const item={...input.body,versionNumber:1,managerName:input.body.managerEmployeeId?person.employee.fullName:''};departments.push(item);return record('createDepartment',input,structuredClone(item))},
    updateOrganizationDepartment:input=>{const item=departments.find(item=>item.code===input.code);Object.assign(item,input.body,{versionNumber:item.versionNumber+1,managerName:input.body.managerEmployeeId?person.employee.fullName:''});return record('updateDepartment',input,structuredClone(item))},
    listOrganizationManagers:async()=>page([{id:person.employee.id,fullName:person.employee.fullName,employeeNumber:person.employee.employeeNumber,departmentName:person.employee.departmentName}]),
    getPersonnelAvatar:input=>record('readAvatar',input,imageFiles.get('Avatar')),
    getPersonnelImage:input=>record('readImage',input,imageFiles.get(input.kind)),
    uploadPersonnelImage:input=>{const file=input.body.get('file');person.versionNumber++;const contentHash='image-'+person.versionNumber;imageFiles.set(input.kind,file);person.images=[...person.images.filter(item=>item.kind!==input.kind),{kind:input.kind,contentType:file.type,byteLength:file.size,contentHash}];if(input.kind==='Avatar')person.employee.avatarHash=contentHash;return record('uploadImage',{id:input.id,kind:input.kind,expectedVersion:Number(input.body.get('expectedVersion'))},structuredClone(person))},
    deletePersonnelImage:input=>{person.versionNumber++;person.images=person.images.filter(item=>item.kind!==input.kind);imageFiles.delete(input.kind);if(input.kind==='Avatar')person.employee.avatarHash=null;return record('deleteImage',input,structuredClone(person))},
    listMeetingRooms:async()=>page([room]), getMeetingRoomAvailability:async()=>[{startsAt:booking.startsAt,endsAt:booking.endsAt,status:'Approved'}],
    listMeetingBookings:async(input)=>record('listBookings',input,page(!input.status||input.status===booking.status?[booking]:[])),
    createMeetingBooking:input=>record('createBooking',input,{...booking,id:2,...input.body}),
    updateMeetingBooking:input=>{Object.assign(booking,input.body,{versionNumber:booking.versionNumber+1});return record('updateBooking',input,structuredClone(booking))},
    deleteMeetingRoom:input=>record('deleteRoom',input,undefined),
    createMeetingRoom:input=>record('createRoom',input,room), updateMeetingRoom:input=>record('updateRoom',input,room),
    listOfficeSupplies:async()=>page([supply]), listOfficeSupplyRequests:async(input)=>page(!input.status||input.status===request.status?[request]:[]),
    createOfficeSupplyRequest:input=>record('createSupplyRequest',input,request),
    updateOfficeSupplyRequest:input=>{Object.assign(request,input.body,{versionNumber:request.versionNumber+1});return record('updateRequest',input,structuredClone(request))},
    deleteOfficeSupply:input=>record('deleteSupply',input,undefined),
    createOfficeSupply:input=>record('createSupply',input,supply), updateOfficeSupply:input=>record('updateSupply',input,supply),
    restockOfficeSupply:input=>record('restock',input,{}), stocktakeOfficeSupply:input=>record('stocktake',input,{}),
    approveMeetingBooking:input=>{booking.status='Approved';return record('approveBooking',input,booking)},
    approveOfficeSupplyRequest:input=>{request.status='Approved';return record('approveSupply',input,request)},
    getMeetingBookingHistory:async()=>page([{id:1,action:'Submit',actorName:'示例员工',note:'',createdAt:booking.createdAt}]),
    getOfficeSupplyRequestHistory:async()=>page([{id:1,action:'Submit',actorName:'示例员工',note:'',createdAt:booking.createdAt}]),
    getOfficeStockHistory:async()=>page([{id:1,kind:'Restock',quantityDelta:12,stockAfter:12,actorName:'行政管理员',note:'采购入库',createdAt:booking.createdAt}]),
    listPermissionTemplates:async()=>({resources,templates:[template],dataScopes:['own','department','company','all'],accessLevels:['view','operate','manage'],securityNote:''}),
    listUsers:async()=>({users:[],companies:[{code:'DEMO',name:'示例公司',isActive:true,versionNumber:1}],departments:[],roles:['Admin','User'],permissionTemplates:[template]}),
    updatePermissionTemplate:input=>{Object.assign(template,input.body);return record('savePermissions',input,template)},
    getPersonnelOptions:async()=>({departments,canCreate:admin}),
    listPersonnel:async input=>page(!personDeleted&&(!input.keyword||person.employee.fullName.includes(input.keyword))&&(!input.status||person.employee.status===input.status)?[person.employee]:[]),
    getPersonnel:input=>record('getPerson',input,structuredClone(person)),
    deletePersonnel:input=>{personDeleted=true;return record('deletePerson',input,undefined)},
    createPersonnel:input=>{person={...person,account:null,canLinkAccount:true,profile:input.body.profile,hireDate:input.body.hireDate,employee:{...person.employee,id:2,employeeNumber:input.body.employeeNumber,fullName:input.body.profile.fullName,jobTitle:input.body.jobTitle,departmentId:input.body.departmentId}};return record('createPerson',input,structuredClone(person))},
    updatePersonnel:input=>{if(window.__personnelConflict){window.__personnelConflict=false;return Promise.reject(new Error('记录已被其他人修改，请刷新后重试。'))}person={...person,...input.body,versionNumber:person.versionNumber+1,employee:{...person.employee,fullName:input.body.profile.fullName}};return record('updatePerson',input,structuredClone(person))},
    getPersonnelHistory:async()=>page([{id:1,action:'Hire',effectiveDate:date,actorName:'人事管理员',summary:'入职登记',note:'办理入职手续',createdAt:new Date(now).toISOString()}]),
    getPersonnelClearance:async()=>{const managedDepartments=departments.filter(item=>item.managerEmployeeId===person.employee.id).map(({code,name})=>({code,name}));return window.__personnelClear?{isClear:true,canDepart:managedDepartments.length===0,managedDepartments,meetingCount:0,supplyCount:0,items:[]}:{isClear:false,canDepart:false,managedDepartments,meetingCount:1,supplyCount:0,items:[{kind:'rooms',requestId:1,resourceName:room.name,status:'InUse',outstandingQuantity:1}]}},
    confirmPersonnel:input=>{person={...person,employee:{...person.employee,status:'Active'},versionNumber:person.versionNumber+1};return record('confirmPerson',input,structuredClone(person))},
    transferPersonnel:input=>{person={...person,employee:{...person.employee,departmentId:input.body.departmentId,jobTitle:input.body.jobTitle},versionNumber:person.versionNumber+1};return record('transferPerson',input,structuredClone(person))},
    departPersonnel:input=>{person={...person,employee:{...person.employee,status:'Departed'},account:{...person.account,isActive:false},versionNumber:person.versionNumber+1};return record('departPerson',input,structuredClone(person))},
    rehirePersonnel:input=>record('rehirePerson',input,person),
    listPersonnelAccountOptions:async()=>page([{id:2,username:'new-account',fullName:'张宁',departmentId:'D1',isActive:true,versionNumber:3}]),
    linkPersonnelAccount:input=>{person={...person,canLinkAccount:false,account:{id:2,username:'new-account',fullName:person.employee.fullName,departmentId:'D1',isActive:true,versionNumber:4}};return record('linkPerson',input,person)},
  };
  const queries=new QueryClient({defaultOptions:{queries:{retry:false},mutations:{retry:false}}});
  const initialPath=mode==='organization'?'/system/organization':mode==='permissions'?'/permissions':mode==='directory'||mode==='people'&&!admin?'/office/directory':mode==='people'?'/office/people':mode==='supplies'?'/office/supplies':'/office/meeting-rooms';
  createRoot(document.getElementById('root')).render(<MemoryRouter initialEntries={[initialPath]}><QueryClientProvider client={queries}><ConfirmationProvider><UnsavedChangesProvider>
    <main className='workspace-content'><h1>公司行政工作台</h1><Routes>
      <Route path='/permissions' element={<AccessControlPage client={client} canManageUsers={true}/>}/>
      <Route path='/office/people' element={<PersonnelPage client={client} user={user}/>}/>
      <Route path='/office/directory' element={<PersonnelPage key='directory' client={client} user={user} directoryOnly/>}/>
      <Route path='/system/organization' element={<OrganizationDirectoryPage client={client} user={user}/>}/>
      <Route path='/office/supplies' element={<OfficeSuppliesPage client={client} user={user}/>}/>
      <Route path='/office/meeting-rooms' element={<MeetingRoomsPage client={client} user={user}/>}/>
    </Routes></main>
  </UnsavedChangesProvider></ConfirmationProvider></QueryClientProvider></MemoryRouter>);
` }, outfile:path.join(output,"app.js"), bundle:true, format:"esm", platform:"browser", jsx:"automatic", logLevel:"silent" });
const server=http.createServer((request,response)=>{
  const name=new URL(request.url,"http://localhost").pathname.slice(1);
  if(!name){response.setHeader("Content-Type","text/html; charset=utf-8");response.end('<!doctype html><html lang="zh-CN"><meta name="viewport" content="width=device-width, initial-scale=1"><title>公司行政验证</title><link rel="stylesheet" href="/app.css"><div id="root"></div><script type="module" src="/app.js"></script></html>');return;}
  if(!["app.js","app.css"].includes(name)){response.writeHead(404).end();return;}
  response.setHeader("Content-Type",name.endsWith("js")?"text/javascript":"text/css");response.end(fs.readFileSync(path.join(output,name)));
});
await new Promise(resolve=>server.listen(0,"127.0.0.1",resolve));
let chrome,cdp;
const results=[];
const read=async(page,expression)=>(await evaluate(page,expression,true)).value;
async function waitFor(page,expression){const until=Date.now()+20000;while(Date.now()<until){if(await read(page,`Boolean(${expression})`))return;await delay(60);}throw new Error(`Timed out: ${expression}; ${await read(page,"document.body.innerText.slice(0,2500)")}`);}
async function clickText(page,text,selector="button"){await read(page,`(()=>{const node=[...document.querySelectorAll(${JSON.stringify(selector)})].find(n=>n.getClientRects().length&&n.textContent.trim()===${JSON.stringify(text)});if(!node)throw new Error('Missing button: '+${JSON.stringify(text)});node.click()})()`);await delay(100);}
async function input(page,selector,value){await read(page,`(()=>{const node=document.querySelector(${JSON.stringify(selector)});Object.getOwnPropertyDescriptor(node instanceof HTMLSelectElement?HTMLSelectElement.prototype:node instanceof HTMLTextAreaElement?HTMLTextAreaElement.prototype:HTMLInputElement.prototype,'value').set.call(node,${JSON.stringify(value)});node.dispatchEvent(new Event('input',{bubbles:true}));node.dispatchEvent(new Event('change',{bubbles:true}))})()`);await delay(60);}
async function audit(page,label){
  await read(page,"document.fonts.ready.then(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(()=>resolve(true)))))");
  await evaluate(page,fs.readFileSync(require.resolve("axe-core/axe.min.js"),"utf8"),false);
  const violations=await read(page,"axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21aa']}}).then(result=>result.violations.map(v=>({id:v.id,nodes:v.nodes.map(n=>({target:n.target,summary:n.failureSummary}))})))");
  if(violations.length)await captureScreenshot(page,path.join(output,`${label}-failed.png`));
  assert.deepEqual(violations,[],`${label}: accessibility`);
  assert.equal(await read(page,"document.documentElement.scrollWidth <= innerWidth + 1"),true,`${label}: page overflow`);
  assert.equal(await read(page,"[...document.querySelectorAll('.office-workspace')].every(node=>node.classList.contains('work-surface') && parseFloat(getComputedStyle(node).paddingLeft)>=12)"),true,`${label}: shared workspace spacing`);
  assert.equal(await read(page,"[...document.querySelectorAll('.office-dialog-body')].every(n=>n.scrollWidth<=n.clientWidth+1)"),true,`${label}: dialog overflow`);
  assert.deepEqual(await read(page,"window.__officeErrors"),[],`${label}: browser errors`);
  results.push(label);
}
try {
  chrome=await startChrome({browserExecutable:locateChromeForTesting(repo),userDataDir:path.join(output,`profile-${Date.now()}`),timeoutMs:30000});
  cdp=await CdpClient.connect(chrome.browserWebSocketUrl);
  const page=await createPageSession(cdp);
  const open=async(mode,width,role="admin")=>{
    await page.send("Emulation.setDeviceMetricsOverride",{width,height:960,deviceScaleFactor:1,mobile:false});
    await page.send("Page.navigate",{url:`http://127.0.0.1:${server.address().port}/?mode=${mode}&role=${role}`});
    if(mode==='permissions'){await waitFor(page,"document.querySelector('.identity-management-tabs')");await clickText(page,'权限方案');}
    await waitFor(page,"document.querySelector('.office-resource-card, .permission-resource-card, .permission-module-grid, .personnel-card, .organization-node')");
  };
  for(const width of [1440,1024,768,390,320]){
    for(const mode of ["rooms","supplies"]){
      await open(mode,width);await audit(page,`${mode}-${width}`);
      await captureScreenshot(page,path.join(output,`${mode}-${width}.png`));
      if(mode==="supplies") {
        assert.equal(await read(page,"document.querySelector('.office-secondary-actions').open"),false);
        await clickText(page,"更多操作","summary");await audit(page,`supply-more-actions-${width}`);
        await clickText(page,"库存流水");await waitFor(page,"document.querySelector('.office-history')");
        await read(page,"document.querySelector('.office-dialog [aria-label=关闭窗口]').click()");
      }
      await clickText(page,mode==="rooms"?"查看日程与预约":"申请借用");
      await waitFor(page,"document.querySelector('[role=dialog]') && !document.querySelector('.office-dialog [aria-busy=true]')");
      await audit(page,`${mode}-dialog-${width}`);
      if(width===390)await captureScreenshot(page,path.join(output,`${mode}-dialog-${width}.png`));
    }
  }
  await open("rooms",1024,"employee");assert.equal(await read(page,"document.body.innerText.includes('添加会议室')"),false);
  await clickText(page,"查看日程与预约");await input(page,'input[name="title"]',"跨部门项目沟通");
  await clickText(page,"提交预约");await waitFor(page,"window.__officeCalls.some(c=>c.name==='createBooking') && !document.querySelector('.office-dialog')");
  const booking=await read(page,"window.__officeCalls.find(c=>c.name==='createBooking').input.body");
  assert.match(booking.requestKey,/^[0-9a-f-]{36}$/);assert.match(booking.startsAt,/Z$/);assert.equal(booking.title,"跨部门项目沟通");results.push("employee-booking");
  await clickText(page,"预约记录与审批");await waitFor(page,"document.querySelector('.office-request-card')");assert.equal(await read(page,"[...document.querySelectorAll('button')].some(b=>b.textContent.trim()==='批准')"),false);results.push("employee-permissions");
  await open("rooms",1024);await clickText(page,"预约记录与审批");await waitFor(page,"document.querySelector('.office-request-card')");await clickText(page,"批准");
  await clickText(page,"批准",".office-dialog button");await waitFor(page,"window.__officeCalls.some(c=>c.name==='approveBooking')");
  assert.equal((await read(page,"window.__officeCalls.find(c=>c.name==='approveBooking').input.body")).expectedVersion,1);results.push("admin-approval");
  await open("supplies",390);await clickText(page,"补充库存");await input(page,'textarea[name="note"]',"采购入库");await input(page,'input[name="quantity"]',"8");await clickText(page,"确认入库");
  await waitFor(page,"window.__officeCalls.some(c=>c.name==='restock')");assert.equal((await read(page,"window.__officeCalls.find(c=>c.name==='restock').input.body")).quantity,8);results.push("stock-replenishment");
  await open("supplies",390);await clickText(page,"申请借用");await input(page,'textarea[name="purpose"]',"保留草稿");await read(page,"document.querySelector('.office-dialog [aria-label=关闭窗口]').click()");
  await waitFor(page,"document.querySelector('.confirmation-dialog')");await audit(page,"nested-discard-confirmation");await clickText(page,"取消",".confirmation-dialog button");assert.equal(await read(page,"document.querySelector('textarea[name=purpose]').value"),"保留草稿");
  await open("permissions",1440);await waitFor(page,"document.querySelector('.permission-template-meta-grid input')?.value === '公司员工'");await audit(page,"office-permission-modules");await captureScreenshot(page,path.join(output,"permission-modules.png"));
  await read(page,"document.querySelector('input[aria-label=\"会议室预约：审批\"]').click()");
  await read(page,"(()=>{const select=document.querySelector('select[aria-label=\"会议室预约审批数据范围\"]');select.value='company';select.dispatchEvent(new Event('change',{bubbles:true}))})()");
  await clickText(page,"保存方案");await waitFor(page,"window.__officeCalls.some(c=>c.name==='savePermissions')");
  const grants=await read(page,"window.__officeCalls.find(c=>c.name==='savePermissions').input.body.grants");
  assert(grants.some(g=>g.resourceKey==='office.rooms'&&g.action==='approve'&&g.dataScope==='company'));assert(grants.some(g=>g.resourceKey==='office.supplies'&&g.action==='create'));results.push("permission-module-save");
  await page.send("Emulation.setDeviceMetricsOverride",{width:390,height:960,deviceScaleFactor:1,mobile:false});await audit(page,"office-permission-modules-mobile");
  for(const width of [1440,1024,768,390,320]){
    await open("people",width);await audit(page,`people-${width}`);await captureScreenshot(page,path.join(output,`people-${width}.png`));
    await clickText(page,"人员档案");await waitFor(page,"document.querySelector('.personnel-facts')");await audit(page,`people-detail-${width}`);
    if(width===390)await captureScreenshot(page,path.join(output,"people-detail-390.png"));
  }
  await open("people",390,"employee");assert.equal(await read(page,"document.body.innerText.includes('入职登记') || document.body.innerText.includes('人员档案') || document.body.innerText.includes('13800000000')"),false);results.push("personnel-directory-privacy");
  await open("people",1024);await clickText(page,"入职登记");await input(page,'input[name="employeeNumber"]',"EMP-002");await input(page,'input[name="fullName"]',"李明");await input(page,'input[name="jobTitle"]',"运营专员");
  await audit(page,"personnel-hire-form");await read(page,"(()=>{const button=[...document.querySelectorAll('button')].find(n=>n.textContent.trim()==='登记入职');button.click();button.click()})()");
  await waitFor(page,"window.__officeCalls.some(c=>c.name==='createPerson') && document.querySelector('.personnel-facts')");
  assert.equal((await read(page,"window.__officeCalls.filter(c=>c.name==='createPerson')")).length,1);results.push("personnel-hire-single-submit");
  await clickText(page,"关联账号");await waitFor(page,"document.querySelector('.personnel-account-list input')");await read(page,"document.querySelector('.personnel-account-list input').click()");await clickText(page,"确认关联");
  await waitFor(page,"window.__officeCalls.some(c=>c.name==='linkPerson')");assert.equal((await read(page,"window.__officeCalls.find(c=>c.name==='linkPerson').input.body")).expectedAccountVersion,3);results.push("personnel-account-link");
  await open("people",390);await clickText(page,"人员档案");await waitFor(page,"document.querySelector('.personnel-facts')");await clickText(page,"编辑档案");await input(page,'input[name="workPhone"]',"分机 1002");
  await read(page,"window.__personnelConflict=true");await clickText(page,"保存档案");await waitFor(page,"document.body.innerText.includes('记录已被其他人修改')");assert.equal(await read(page,"document.querySelector('input[name=workPhone]').value"),"分机 1002");results.push("personnel-conflict-keeps-draft");
  await clickText(page,"保存档案");await waitFor(page,"window.__officeCalls.some(c=>c.name==='updatePerson') && !document.querySelector('input[name=workPhone]')");
  await clickText(page,"办理转正");await input(page,'textarea[name="note"]',"试用期考核通过");await clickText(page,"办理转正",".office-dialog-backdrop:last-of-type button");await waitFor(page,"window.__officeCalls.some(c=>c.name==='confirmPerson')");results.push("personnel-confirm");
  await clickText(page,"办理离职");await waitFor(page,"document.body.innerText.includes('还有预约 1 笔')");assert.equal(await read(page,"[...document.querySelectorAll('.office-dialog-backdrop:last-of-type button')].find(n=>n.textContent.trim()==='办理离职').disabled"),true);
  await audit(page,"personnel-departure-blocked");await read(page,"window.__personnelClear=true");await clickText(page,"重新核对",".office-dialog-backdrop:last-of-type button");await waitFor(page,"document.body.innerText.includes('未结清事项为 0')");await input(page,'textarea[name="note"]',"实物与工作交接完成");await clickText(page,"办理离职",".office-dialog-backdrop:last-of-type button");await waitFor(page,"window.__officeCalls.some(c=>c.name==='departPerson')");results.push("personnel-departure-after-clearance");
  await open("people",1024);await clickText(page,"人员档案");await waitFor(page,"document.querySelector('.personnel-facts')");await clickText(page,"任职与操作记录");await waitFor(page,"document.querySelector('.office-history li')");await audit(page,"personnel-history");
  await clickText(page,"交接事项");await waitFor(page,"document.querySelector('.personnel-clearance a')");await read(page,"document.querySelector('.personnel-clearance a').click()");await waitFor(page,"document.querySelector('.office-request-card')");
  assert(await read(page,"window.__officeCalls.some(c=>c.name==='listBookings' && c.input.requestId===1 && c.input.mineOnly===false)"));results.push("personnel-clearance-opens-exact-request");
  await open("permissions",390);await waitFor(page,"document.querySelector('input[aria-label=\"人员信息管理：人事档案与记录\"]')");
  await read(page,"document.querySelector('input[aria-label=\"人员信息管理：人事档案与记录\"]').click()");await clickText(page,"保存方案");await waitFor(page,"window.__officeCalls.some(c=>c.name==='savePermissions')");
  assert(await read(page,"window.__officeCalls.find(c=>c.name==='savePermissions').input.body.grants.some(g=>g.resourceKey==='office.people' && g.action==='view-details')"));await audit(page,"personnel-permission-module-save");
  for(const width of [1024,390]){
    await open("rooms",width,"register");await clickText(page,"查看日程与预约");
    await waitFor(page,"document.querySelector('.remote-select-controls select')?.options.length>1");
    assert(await read(page,"[...document.querySelectorAll('button')].find(n=>n.textContent.trim()==='登记预约').disabled"));
    await input(page,".remote-select-controls select","1");await input(page,'input[name="title"]',"行政登记例会");
    await audit(page,`local-booking-${width}`);await captureScreenshot(page,path.join(output,`local-booking-${width}.png`));
    await clickText(page,"登记预约");await waitFor(page,"window.__officeCalls.some(c=>c.name==='createBooking')");
    assert.equal((await read(page,"window.__officeCalls.find(c=>c.name==='createBooking').input.body")).employeeId,1);
    await clickText(page,"预约与钥匙交接记录");await waitFor(page,"document.querySelector('.office-request-card')");
    assert.equal(await read(page,"[...document.querySelectorAll('button')].some(n=>n.textContent.trim()==='批准')"),false);results.push(`local-booking-submit-${width}`);
    await open("supplies",width,"register");await clickText(page,"登记借用");
    await waitFor(page,"document.querySelector('.remote-select-controls select')?.options.length>1");
    await input(page,".remote-select-controls select","1");await input(page,'textarea[name="purpose"]',"培训使用");
    await audit(page,`local-supply-${width}`);await clickText(page,"登记领用");await waitFor(page,"window.__officeCalls.some(c=>c.name==='createSupplyRequest')");
    assert.equal((await read(page,"window.__officeCalls.find(c=>c.name==='createSupplyRequest').input.body")).employeeId,1);results.push(`local-supply-submit-${width}`);
  }
  await open("people",1024,"register");await clickText(page,"人员档案");await waitFor(page,"document.querySelector('.personnel-facts')");
  assert.equal(await read(page,"[...document.querySelectorAll('button')].some(n=>n.textContent.trim()==='关联账号')"),false);
  await clickText(page,"办理离职");await waitFor(page,"document.body.innerText.includes('还有预约 1 笔')");
  assert(await read(page,"[...document.querySelectorAll('.office-dialog-backdrop:last-of-type button')].find(n=>n.textContent.trim()==='办理离职').disabled"));
  await audit(page,"local-departure-blocked-without-account");
  await open("people",1024,"register");await clickText(page,"人员档案");await waitFor(page,"document.querySelector('.personnel-facts')");await clickText(page,"交接事项");
  await waitFor(page,"document.querySelector('.personnel-clearance')");await read(page,"[...document.querySelectorAll('.personnel-clearance a')].find(n=>n.textContent==='全部预约记录').click()");
  await waitFor(page,"window.__officeCalls.some(c=>c.name==='listBookings'&&c.input.employeeId===1)");results.push("local-employee-clearance-navigation");
  await runPersonnelOrganizationUi({page,open,read,waitFor,clickText,input,audit,results,output,captureScreenshot});
  await runAdministrationMaintenanceUi({page,open,read,waitFor,clickText,input,audit,results,output,captureScreenshot});
  fs.writeFileSync(path.join(output,"summary.json"),JSON.stringify({passed:results.length,results},null,2));
  process.stdout.write(`Office UI contracts passed (${results.length} cases).\n`);
} finally {cdp?.close();if(chrome)await closeChrome(chrome.browserWebSocketUrl,chrome.process);await new Promise(resolve=>server.close(resolve));}

import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { createReactUiFixture } from "./lib/react-ui-fixture.mjs";
const { web, output, require, bundle, source, launch } = createReactUiFixture("oa-review-lifecycle-ui");
await require("esbuild").build({ stdin: { loader: "tsx", resolveDir: web, contents: `
import React,{useState} from "react";
import {createRoot} from "react-dom/client";
import {QueryClient,QueryClientProvider} from "@tanstack/react-query";
import {MemoryRouter} from "react-router-dom";
import {ApiError} from ${source("api/index.ts")};
import {ConfirmationProvider} from ${source("ui/ConfirmationProvider.tsx")};
import {UnsavedChangesProvider} from ${source("ui/unsavedChangesGuard.tsx")};
import {OaRequestsPage} from ${source("features/oa/OaRequestsPage.tsx")};
import {OaSubmissionHistory} from ${source("features/oa/OaSubmissionHistory.tsx")};
import {canReassignApproval} from ${source("features/oa/approvalPermissions.ts")};
import {SettingsValidationPanel} from ${source("features/settings/SettingsPagePanels.tsx")};
import {LicensePage} from ${source("features/system/LicensePage.tsx")};
import {RuntimeDiagnosticsSection} from ${source("features/settings/RuntimeDiagnosticsSection.tsx")};
import {notificationActionLabel} from ${source("features/communication/communicationModel.ts")};
import {ApprovalGraphEditor} from ${source("features/oa/ApprovalGraphEditor.tsx")};
import {removeWorkflowStage,stageRemovalBlock} from ${source("features/oa/workflowGraphModel.ts")};
const kinds=["personnel","leave","overtime","expense","travel","purchase","general"];
const user={id:1,contextId:"unit-1",companyScope:"DEFAULT",departmentId:"GENERAL",businessDate:"2026-11-26",businessTimeZone:"Asia/Shanghai",capabilities:{canManageUsers:false,unitManagement:{workflows:true},permissions:["view","approve"].map(action=>({resourceKey:"office.leave",action,dataScope:"company"}))}};
const row={id:42,kind:"leave",status:"Pending",versionNumber:1,companyScope:"DEFAULT",departmentId:"GENERAL",ownerUserId:2,title:"审批生命周期验证",employeeId:2,employeeName:"模拟申请人",updatedAt:"2026-11-26T00:00:00Z",attachments:[],canReview:true,reviewTaskKeys:["review-1"]};
window.permissionResults=kinds.map(kind=>{
 const pending={...row,kind,approvalPlan:{steps:[{status:"Waiting"}]}};
 return [canReassignApproval(user,pending),canReassignApproval(user,{...pending,status:"Suspended"}),canReassignApproval({...user,capabilities:{...user.capabilities,unitManagement:{workflows:false}}},pending),canReassignApproval(user,{...pending,approvalPlan:{steps:[]}})];
});
window.notification=notificationActionLabel({action:"approval-reassign",actionRequired:true});
window.reads=0;window.version=1;
const queries=new QueryClient({defaultOptions:{queries:{retry:false,refetchOnWindowFocus:false}}});window.queries=queries;
const client={getLeaveRequest:async()=>{if(window.failure)throw new ApiError(window.failure,"failed","模拟读取失败");return {...structuredClone(row),versionNumber:window.version}},
 getLicenseStatus:async()=>({isRegistered:false,isTrialExpired:false,licenseState:"Trial",trialDays:7,daysRemaining:7,expireDate:"2026-12-02"}),
 registerLicense:(_input,init)=>new Promise(resolve=>{window.signal=init.signal;window.finish=resolve}),
 checkReportTemplateStorage:init=>new Promise(resolve=>{window.signal=init.signal;window.finish=resolve}),
 approveLeaveRequest:async input=>{window.command=input;throw new ApiError(409,"Conflict","申请已更新，请重新核对")},
 listSubmissionsOfLeaveRequest:async()=>({items:[{id:9,submissionNumber:1,submittedAt:"2026-11-26T00:00:00Z",status:"Pending"}],totalCount:1,pageNumber:1,pageSize:20}),
 getSubmissionOfLeaveRequest:async()=>{window.reads++;return {id:9,submissionNumber:1,formName:"请假单",departmentName:"业务部",request:row,documentStatus:"NotApproved"}}};
const stage=(key,name,dependsOn=[],changes={})=>({key,name,kind:"Approval",phase:"Decision",dependsOn,source:"DepartmentManager",approverUserIds:[],decision:"All",selfApproval:"Skip",repeatedApprover:"Allow",conditions:[],dueHours:48,...changes});
const stages=[stage("department","部门负责人审批"),stage("executive","公司负责人审批",["department"],{source:"CompanyExecutive"}),stage("receipt","办理登记",["department","executive"],{kind:"Handling",phase:"Fulfillment",source:"Handlers"})];
window.graphModel={stage,removeWorkflowStage,stageRemovalBlock};
function Graph(){const[value,setValue]=useState(stages);window.graph=value;return <ApprovalGraphEditor client={client} stages={value} onChange={setValue} accounts={[]} handlers={[]} units={[]} currentCompanyCode="DEFAULT" definitions={[]}/>;}
function App(){const[open,setOpen]=useState(true);return <><button onClick={()=>setOpen(false)}>卸载页面</button>{open&&(window.mode==="graph"?<Graph/>:window.mode==="license"?<LicensePage client={client}/>:window.mode==="diagnostics"?<RuntimeDiagnosticsSection client={client} canManageSettings health={null} isBusy={false} onRefresh={()=>{}} onPathError={()=>{}}/>:window.mode==="history"?<OaSubmissionHistory client={client} user={user} row={row}/>:window.mode==="settings"?<SettingsValidationPanel result={{isValid:false,messages:[{level:"error",propertyName:"server",message:"服务地址无效"}]}}/>:<OaRequestsPage client={client} user={user} kind="leave"/>)}</>}
createRoot(document.getElementById("root")).render(<MemoryRouter initialEntries={["/office/requests/leave?requestId=42"]}><QueryClientProvider client={queries}><ConfirmationProvider><UnsavedChangesProvider><App/></UnsavedChangesProvider></ConfirmationProvider></QueryClientProvider></MemoryRouter>);
` }, outfile: bundle, bundle: true, platform: "browser", format: "iife", jsx: "automatic", logLevel: "silent" });
const context=await launch();const results=[];
async function scenario(mode,check){const page=await context.newPage(),errors=[];page.setDefaultTimeout(10000);page.on("pageerror",e=>errors.push(e.message));try{
 await page.setContent('<!doctype html><html lang="zh-CN"><title>OA lifecycle</title><div id="root"></div></html>');
 await page.evaluate(mode=>window.mode=mode,mode);await page.addScriptTag({path:bundle});await check(page);assert.deepEqual(errors,[]);results.push(mode);
}finally{await page.close()}}
try{
 await scenario("graph",async page=>{
  const executive=page.getByRole("group",{name:"2 · 公司负责人审批",exact:true});
  await executive.getByRole("button",{name:"移除节点",exact:true}).click();
  assert.deepEqual(await page.evaluate(()=>window.graph.map(node=>[node.key,node.dependsOn])),[["department",[]],["receipt",["department"]]]);
  assert.equal(await page.getByRole("group",{name:"1 · 部门负责人审批",exact:true}).getByRole("button",{name:"移除节点",exact:true}).isDisabled(),true);
  await page.getByText("流程至少保留一个审批决定节点。",{exact:true}).waitFor();
  await page.getByRole("button",{name:"撤销",exact:true}).click();
  assert.deepEqual(await page.evaluate(()=>window.graph[2].dependsOn),["department","executive"]);
  await page.getByRole("button",{name:"重做",exact:true}).click();assert.equal(await executive.count(),0);
  const protection=await page.evaluate(()=>{
   const {stage,removeWorkflowStage,stageRemovalBlock}=window.graphModel;
   const target=stage("review","分支审批",["branch"]);
   const branch=stage("branch","条件",[],{kind:"Exclusive",branches:[{target:"review",isDefault:true}]});
   const other=stage("other","独立审批");
   const paired=stage("fork","并行",[],{kind:"Fork",pairKey:"join"});
   const leaf=stage("leaf","单个分支",["fork"]),join=stage("join","汇合",["fork","leaf"],{kind:"Join",pairKey:"fork"});
   return {branch:Boolean(stageRemovalBlock([branch,target,other],target)),pair:Boolean(stageRemovalBlock([paired,other],paired)),parallel:Boolean(stageRemovalBlock([paired,leaf,join,other],leaf)),unchanged:removeWorkflowStage([branch,target,other],"review").length};
  });
  assert.deepEqual(protection,{branch:true,pair:true,parallel:true,unchanged:3});
 });
 for(const mode of ["license","diagnostics"]) await scenario(mode,async page=>{
  if(mode==="license")await page.getByLabel("注册码",{exact:true}).fill("EDM2-test");
  await page.getByRole("button",{name:mode==="license"?"注册授权":"检查模板目录",exact:true}).click();
  await page.waitForFunction(()=>window.signal);
  await page.getByRole("button",{name:"卸载页面"}).click();
  await page.waitForFunction(()=>window.signal.aborted);
  await page.evaluate(()=>window.finish({success:true,status:{},writable:true}));
  assert.equal(await page.evaluate(()=>window.queries.getMutationCache().getAll().length),0);
 });
 await scenario("detail",async page=>{
  await page.getByRole("button",{name:"批准",exact:true}).click();await page.getByLabel("处理说明").fill("未保存的中文审批意见");
  for(const status of [503,0]){
   await page.evaluate(status=>{window.failure=status;return window.queries.refetchQueries({queryKey:["office","oa","leave"]})},status);
   if(status)await page.getByText(/刷新失败/).waitFor();
   assert.equal(await page.getByLabel("处理说明").inputValue(),"未保存的中文审批意见");
  }
  await page.evaluate(()=>{window.version=2;return window.queries.refetchQueries({queryKey:["office","oa","leave"]})});
  await page.getByRole("dialog").getByRole("button",{name:"批准",exact:true}).click();
  await page.waitForFunction(()=>window.command);
  assert.equal(await page.evaluate(()=>window.command.body.expectedVersion),1);
  assert.equal(await page.getByLabel("处理说明").inputValue(),"未保存的中文审批意见");
  await page.evaluate(()=>{window.failure=403;return window.queries.refetchQueries({queryKey:["office","oa","leave"]})});
  await page.getByText("申请读取失败",{exact:true}).waitFor();assert.equal(await page.getByLabel("处理说明").count(),0);
  assert.deepEqual(await page.evaluate(()=>window.permissionResults),Array.from({length:7},()=>[true,true,false,false]));
  assert.equal(await page.evaluate(()=>window.notification),"审批已移交，待您审批");
 });
 await scenario("history",async page=>{
  await page.getByRole("button",{name:/提交版本 1/}).click();await page.getByText("请假单 · 审批生命周期验证",{exact:true}).waitFor();
  assert.equal(await page.evaluate(()=>window.reads),1);
  assert.equal(await page.evaluate(()=>window.queries.getQueryCache().getAll().filter(q=>q.queryKey[1]==="approval-document").length),1);
  await page.getByRole("button",{name:"卸载页面"}).click();
  await page.waitForFunction(()=>!window.queries.getQueryCache().getAll().some(q=>q.queryKey[1]==="approval-document"));
 });
 await scenario("settings",async page=>{
  await page.getByText("错误 1 项，警告 0 项。",{exact:true}).waitFor();assert.equal(await page.getByRole("button",{name:/自动修复/}).count(),0);
 });
 fs.writeFileSync(path.join(output,"summary.json"),JSON.stringify({passed:true,results},null,2));console.log("OA review lifecycle: "+results.length+" scenarios passed; "+output);
}finally{await context.close()}

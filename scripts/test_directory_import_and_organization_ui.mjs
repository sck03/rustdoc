import assert from "node:assert/strict";
import { createReactUiFixture } from "./lib/react-ui-fixture.mjs";

const { web, bundle, source, launch, require } = createReactUiFixture("directory-import-organization-ui");
await require("esbuild").build({ stdin: { loader: "tsx", resolveDir: web, contents: `
import React, {useState} from "react"; import {createRoot} from "react-dom/client";
import {QueryClient,QueryClientProvider} from "@tanstack/react-query";
import {MemoryRouter} from "react-router-dom";
import {DirectoryImportPreview} from ${source("ui/DirectoryImportPreview.tsx")};
import {OrganizationCompanyForm,OrganizationDepartmentForm} from ${source("features/organization/OrganizationForms.tsx")};
import {ConfirmationProvider} from ${source("ui/ConfirmationProvider.tsx")};
import {UnsavedChangesProvider} from ${source("ui/unsavedChangesGuard.tsx")};
const company={id:1,code:"TEST",name:"测试公司",isActive:true,unitKind:"Company",versionNumber:1,executiveEmployeeId:7,executiveAppointmentId:"a",executiveName:"同一人员",positions:[],additionalLeaders:[]};
const department={id:2,code:"DEP",name:"测试部门",companyCode:"TEST",isActive:true,versionNumber:1,managerEmployeeId:7,managerAppointmentId:"a",managerName:"同一人员",additionalLeaders:[]};
const client={listOrganizationManagers:async()=>({items:[{id:7,fullName:"同一人员",employeeNumber:"E7",appointments:[{id:"a",departmentName:"部门甲",jobTitle:"任职甲"},{id:"b",departmentName:"部门乙",jobTitle:"任职乙"}]}],pageNumber:1,pageSize:20,totalCount:1,totalPages:1})};
const rows=Array.from({length:65},(_,index)=>({rowNumber:index+2,name:"记录"+(index+1),contactName:"",countryRegion:"CN",error:index===64?"最后一行错误":"",isDuplicate:index===40}));
function App(){const [open,setOpen]=useState(true);return window.mode==="preview"?<DirectoryImportPreview rows={rows} busy={false}/>:!open?<div>已关闭</div>:window.mode==="company"?<OrganizationCompanyForm client={client} record={company} onClose={()=>setOpen(false)} onSaved={()=>{}}/>:<OrganizationDepartmentForm client={client} company={company} record={department} departments={[department]} onClose={()=>setOpen(false)} onSaved={()=>{}}/>;}
createRoot(document.getElementById("root")).render(<MemoryRouter><QueryClientProvider client={new QueryClient({defaultOptions:{queries:{retry:false}}})}><ConfirmationProvider><UnsavedChangesProvider><App/></UnsavedChangesProvider></ConfirmationProvider></QueryClientProvider></MemoryRouter>);
` }, outfile: bundle, bundle: true, platform: "browser", format: "iife", jsx: "automatic", logLevel: "silent" });
const context = await launch();
try {
  for (const mode of ["preview", "company", "department"]) {
    const page = await context.newPage(); page.setDefaultTimeout(6000);
    const errors = []; page.on("pageerror", error => errors.push(error.message));
    await page.setContent('<html lang="zh-CN"><title>目录导入与组织草稿回归</title><div id="root"></div></html>');
    await page.evaluate(mode => { window.mode = mode; }, mode); await page.addScriptTag({path:bundle});
    if (mode === "preview") {
      await page.getByText("记录1", {exact:true}).waitFor();
      assert.equal(await page.locator("tbody tr").count(), 30);
      await page.getByLabel("跳转页", {exact:true}).fill("3"); await page.getByRole("button", {name:"跳转",exact:true}).click();
      await page.getByText("最后一行错误", {exact:true}).waitFor();
      assert.equal(await page.locator("tbody tr").count(), 5);
      await page.getByLabel("预检结果", {exact:true}).selectOption("invalid");
      assert.equal(await page.locator("tbody tr").count(), 1);
      await page.getByLabel("预检结果", {exact:true}).selectOption("duplicate");
      await page.getByText("记录41", {exact:true}).waitFor();
      assert.equal(await page.locator("tbody tr").count(), 1);
    } else {
      await page.getByRole("button", {name:mode==="company"?"选择公司领导审批负责人":"选择负责人",exact:true}).click();
      await page.getByRole("button", {name:/任职乙/}).click();
      await page.getByRole("button", {name:"关闭窗口",exact:true}).click();
      await page.getByText(/未保存/).first().waitFor();
      assert.equal(await page.getByText("已关闭", {exact:true}).count(), 0);
    }
    assert.deepEqual(errors, []); await page.close();
  }
  console.log("directory import paging/filter and organization appointment draft protection: 3 scenarios passed");
} finally { await context.close(); }

import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { createReactUiFixture } from "./lib/react-ui-fixture.mjs";

const { web, output, require, bundle, source, launch } = createReactUiFixture("workspace-refresh-ui");
await require("esbuild").build({ stdin: { loader: "tsx", resolveDir: web, contents: `
import React,{useState} from "react"; import {createRoot} from "react-dom/client";
import {QueryClient,QueryClientProvider} from "@tanstack/react-query";
import {MemoryRouter} from "react-router-dom";
import {ConfirmationProvider} from ${source("ui/ConfirmationProvider.tsx")};
import {UnsavedChangesProvider} from ${source("ui/unsavedChangesGuard.tsx")};
import {ResourceRequestDetails} from ${source("features/office/ResourceRequestDetails.tsx")};
import {UnitWorkflowTasksPanel} from ${source("features/oa/UnitWorkflowTasksPanel.tsx")};
import {useOfficeOperation} from ${source("features/office/useOfficeData.ts")};
import {useReportTemplatePreviewSources} from ${source("features/reports/useReportTemplatePreviewSources.ts")};
import {ReportTemplatePreviewWorkspace} from ${source("features/reports/ReportTemplatePreviewWorkspace.tsx")};
import {buildInvoicePreviewOptions,buildPaymentPreviewOptions} from ${source("features/reports/reportTemplateDesignerModel.ts")};
const queries=new QueryClient({defaultOptions:{queries:{retry:false}}});window.queries=queries;
window.reads=0;window.writes=[];window.sourceRequests=[];window.taskReads=0;window.done=0;
const user={id:1,contextId:"unit-1",companyScope:"ACME",businessDate:"2026-11-20",businessTimeZone:"Asia/Shanghai",capabilities:{permissions:["rooms","supplies"].flatMap(kind=>["view","create"].map(action=>({resourceKey:"office."+kind,action,dataScope:"own"})))}};
const initial={id:7,ownerUserId:1,companyScope:"ACME",status:"Draft",versionNumber:1,title:"原主题",attendeeCount:2,startsAt:"2026-11-21T08:00:00+08:00",endsAt:"2026-11-21T09:00:00+08:00",quantity:1,unit:"件",purpose:"原用途",isReturnable:false,attachments:[{id:1,fileName:"version-1.txt",sizeBytes:100}]};
let record=window.mode==="rooms"?{...initial,meetingRoomId:10}:{...initial,officeSupplyId:20};
const get=async()=>{window.reads++;if(window.failDetails)throw new Error("模拟详情读取失败");return structuredClone(record)};
const update=async input=>{window.writes.push(input);if(input.body.expectedVersion!==record.versionNumber)throw new Error("版本冲突，请核对修改");return {...record,...input.body}};
const sourceRows=Array.from({length:120},(_,i)=>({id:i+1,invoiceNo:"DOC-"+String(i+1).padStart(3,"0"),invoiceDate:"2026-11-20",paymentDate:"2026-11-20",totalAmount:"1.00",cnyAmount:"1.00",currency:"USD"}));
const list=async(input,init)=>{
  window.sourceRequests.push({input,signal:init.signal});
  if(input.keyword==="slow")return new Promise(resolve=>window.finishSlow=()=>resolve({items:[{...sourceRows[0],invoiceNo:"STALE"}],totalCount:1,pageNumber:1,pageSize:50}));
  const filtered=sourceRows.filter(row=>row.invoiceNo.includes(input.keyword??""));
  return {items:filtered.slice((input.pageNumber-1)*input.pageSize,input.pageNumber*input.pageSize),totalCount:filtered.length,pageSize:input.pageSize,pageNumber:input.pageNumber};
};
const client={getMeetingBooking:get,getOfficeSupplyRequest:get,updateDraftOfMeetingBooking:update,updateDraftOfOfficeSupplyRequest:update,
  listInvoices:list,listPayments:list,getUnitWorkflowTasks:async()=>{window.taskReads++;return {items:[],totalCount:0,pageNumber:1,pageSize:20}}};
function Resource(){const[row,setRow]=useState(record);window.advance=(status="Draft")=>{record={...record,versionNumber:record.versionNumber+1,status,attachments:[{id:1,fileName:"version-"+(record.versionNumber+1)+".txt",sizeBytes:100}]};setRow({...record})};return <ResourceRequestDetails client={client} user={user} kind={window.mode} row={row}/>}
function Preview(){const[mode,setMode]=useState("sample"),[selected,setSelected]=useState(120);const reportType=window.mode==="payments"?"PaymentVoucher":"ExportDocument";
  const model=useReportTemplatePreviewSources({client,reportType,enabled:mode==="savedSource",canPreviewInvoiceSource:true,canPreviewPaymentSource:true});
  const options=reportType==="PaymentVoucher"?buildPaymentPreviewOptions(model.previewPaymentsQuery.data?.items??[],selected):buildInvoicePreviewOptions(model.previewInvoicesQuery.data?.items??[],selected);
  return <ReportTemplatePreviewWorkspace mode={mode} onModeChange={setMode} sampleProfile="standard" sampleProfiles={[]} onSampleProfileChange={()=>{}}
    selectedSourceValue={String(selected)} sourceOptions={options} sourceDirectory={model.previewSourceDirectory} onSourceChange={value=>setSelected(Number(value))}
    renderedHtml="" isBusy={false} pdf={{format:"Svg",pageIndex:0,pageCount:0,onFormatChange:()=>{},onPageChange:()=>{}}}/>;
}
function Operation(){const[owner,setOwner]=useState(client);const op=useOfficeOperation([],false,"record-7",owner);
  window.switchClient=()=>setOwner({...client});window.start=()=>op.run(signal=>new Promise(resolve=>{window.signal=signal;window.finish=resolve}),()=>window.done++);
  return <output data-operation>{String(op.busy)} {op.error}</output>;
}
createRoot(document.getElementById("root")).render(<MemoryRouter><QueryClientProvider client={queries}><ConfirmationProvider><UnsavedChangesProvider>
  {window.mode==="tasks"?<UnitWorkflowTasksPanel client={client} user={user} accounts={[]}/>:window.mode==="operation"?<Operation/>:["rooms","supplies"].includes(window.mode)?<Resource/>:<Preview/>}
</UnsavedChangesProvider></ConfirmationProvider></QueryClientProvider></MemoryRouter>);
` }, outfile: bundle, bundle: true, platform: "browser", format: "iife", jsx: "automatic", logLevel: "silent" });

const context = await launch();
const results = [];
async function scenario(mode, check) {
  const page = await context.newPage(), errors = [];
  page.setDefaultTimeout(10000); page.on("pageerror", error => errors.push(error.message));
  try {
    await page.setContent('<!doctype html><html lang="zh-CN"><title>Workspace refresh</title><div id="root"></div></html>');
    await page.evaluate(value => window.mode = value, mode); await page.addScriptTag({ path: bundle });
    await check(page); assert.deepEqual(errors, []); results.push({ mode, passed: true });
  } catch (error) { await page.screenshot({ path: path.join(output, `failure-${mode}.png`), fullPage: true }); throw error; }
  finally { await page.close(); }
}
try {
  for (const mode of ["rooms", "supplies"]) await scenario(mode, async page => {
    await page.getByText("申请附件、审批单与任务移交", { exact: true }).click();
    await page.getByText(/version-1.txt/).waitFor();
    await page.getByRole("button", { name: "继续编辑草稿", exact: true }).click();
    const field = page.getByLabel(mode === "rooms" ? "会议主题" : "领用用途", { exact: true });
    await field.fill("未保存的中文草稿");
    await page.evaluate(() => window.advance());
    await page.getByText(/version-2.txt/).waitFor();
    assert.equal(await field.inputValue(), "未保存的中文草稿");
    await page.evaluate(() => { window.failDetails = true; window.advance(); });
    await page.getByText(/资料刷新失败/).waitFor();
    assert.equal(await field.inputValue(), "未保存的中文草稿");
    await page.evaluate(() => { window.failDetails = false; void window.queries.refetchQueries({ queryKey: ["office", "resource-request"] }); });
    await page.getByText(/version-3.txt/).waitFor();
    await page.getByRole("button", { name: "保存草稿", exact: true }).click();
    await page.getByText(/版本冲突，请核对修改/).waitFor();
    assert.equal(await page.evaluate(() => window.writes[0].body.expectedVersion), 1, "background refresh cannot advance the editor's concurrency baseline");
    assert.equal(await field.inputValue(), "未保存的中文草稿");
  });
  for (const mode of ["invoices", "payments"]) await scenario(mode, async page => {
    await page.getByText("当前单据", { exact: true }).waitFor();
    assert.equal(await page.evaluate(() => window.sourceRequests.length), 0, "sample preview does not read saved documents");
    await page.getByText("当前单据", { exact: true }).click();
    const select = page.getByLabel("预览单据", { exact: true });
    await page.waitForFunction(() => window.sourceRequests.length === 1);
    assert.equal(await select.inputValue(), "120", "route-selected source survives a page that does not contain it");
    await page.getByRole("button", { name: "下一页", exact: true }).click();
    await page.waitForFunction(() => window.sourceRequests.at(-1).input.pageNumber === 2);
    await select.selectOption("51");
    await page.getByLabel("检索预览单据", { exact: true }).fill("DOC-117");
    await page.waitForFunction(() => document.querySelector('option[value="117"]'));
    assert.equal(await select.inputValue(), "51"); await select.selectOption("117");
    assert.equal(await page.evaluate(() => window.sourceRequests.at(-1).input.pageNumber), 1);
    await page.getByLabel("检索预览单据", { exact: true }).fill("slow");
    await page.waitForFunction(() => typeof window.finishSlow === "function");
    await page.getByLabel("检索预览单据", { exact: true }).fill("DOC-118");
    await page.waitForFunction(() => document.querySelector('option[value="118"]'));
    assert.equal(await page.evaluate(() => window.sourceRequests.find(r => r.input.keyword === "slow").signal.aborted), true);
    await page.evaluate(() => window.finishSlow());
    assert.equal(await page.getByText(/STALE/).count(), 0);
    assert.equal(await select.inputValue(), "117");
  });
  await scenario("tasks", async page => {
    const summary = page.getByText("在途任务与职责交接", { exact: true }); await summary.waitFor();
    assert.equal(await page.evaluate(() => window.taskReads), 0);
    await summary.click(); await page.getByText("当前没有等待交接的在途任务。", { exact: true }).waitFor();
    assert.equal(await page.evaluate(() => window.taskReads), 1);
    await summary.click();
    await page.waitForFunction(() => window.queries.getQueryCache().getAll().find(q => q.queryKey[0] === "unit-workflow-tasks").isDisabled());
    await page.evaluate(() => window.queries.invalidateQueries({ queryKey: ["unit-workflow-tasks"] }));
    assert.equal(await page.evaluate(() => window.taskReads), 1);
    await summary.click(); await page.waitForFunction(() => window.taskReads === 2);
  });
  await scenario("operation", async page => {
    await page.locator("[data-operation]").waitFor();
    await page.evaluate(() => { window.start(); }); await page.waitForFunction(() => window.signal);
    await page.evaluate(() => window.switchClient()); await page.waitForFunction(() => window.signal.aborted);
    await page.evaluate(() => window.finish("late result"));
    assert.equal(await page.evaluate(() => window.done), 0);
    assert.match(await page.locator("[data-operation]").textContent(), /^false/);
    assert.equal(await page.evaluate(() => window.queries.getMutationCache().getAll().length), 0);
  });
  fs.writeFileSync(path.join(output, "summary.json"), JSON.stringify({ passed: true, results }, null, 2));
  console.log(`Workspace refresh UI: ${results.length} scenarios passed; ${output}`);
} finally { await context.close(); }

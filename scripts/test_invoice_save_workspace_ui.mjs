import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { locateChromeForTesting } from "./lib/chromium-executable.mjs";
import { runInvoiceItemsGridCases } from "./lib/invoice-items-grid-ui-cases.mjs";

const root = path.resolve(import.meta.dirname, ".."), web = path.join(root, "apps/export-doc-web");
const require = createRequire(path.join(web, "package.json"));
const output = path.join(root, "artifacts/invoice-save-workspace-ui", String(Date.now()));
fs.mkdirSync(path.join(output, "Temp"), { recursive: true });
Object.assign(process.env, { TEMP:path.join(output, "Temp"), TMP:path.join(output, "Temp") });
const bundle = path.join(output, "app.js");
const source = name => JSON.stringify(path.join(web, "src", name).replaceAll("\\", "/"));
await require("esbuild").build({ stdin:{ resolveDir:web, loader:"tsx", contents:`
import React,{useState} from "react"; import {createRoot} from "react-dom/client";
import {QueryClient,QueryClientProvider} from "@tanstack/react-query";
import {MemoryRouter,useLocation} from "react-router-dom";
import {useInvoicePersistenceOperations} from ${source("features/invoices/useInvoicePersistenceOperations.ts")};
import {useInvoiceItemsWorkspace} from ${source("features/invoices/useInvoiceItemsWorkspace.ts")};
import {createEmptyInvoice} from ${source("features/invoices/invoiceModel.ts")};
import {createEmptyInvoiceItem} from ${source("features/invoices/invoiceItemsEditorModel.ts")};
import {InvoiceItemsEditor} from ${source("features/invoices/InvoiceItemsEditor.tsx")};
import {ConfirmationProvider} from ${source("ui/ConfirmationProvider.tsx")};
window.requests=[]; window.resets=0; window.productWrites=[];
const queryClient=new QueryClient({defaultOptions:{queries:{staleTime:30000,retry:false},mutations:{retry:false}}});
queryClient.setQueryData(["dashboard"],{old:true});queryClient.setQueryData(["query","invoices",{}],{old:true});
window.queryClient=queryClient;
const client={updateInvoice:(request,init)=>new Promise((resolve,reject)=>window.requests.push({request,signal:init.signal,resolve,reject})),
createInvoice:(request,init)=>new Promise((resolve,reject)=>window.requests.push({request,signal:init.signal,resolve,reject})),
listProducts:async request=>{window.productQuery=request;return {items:request.productCode?[{id:51,productCode:request.productCode,rowVersion:"native:1"}]:[],pageNumber:1,totalCount:1}},
updateProduct:async request=>{window.productWrites.push({kind:"update",request});return request.body;},
createProduct:async request=>{window.productWrites.push({kind:"create",request});return request.body;}};
function Harness(){
const [id,setId]=useState(window.newInvoice?0:7),[shown,setShown]=useState(true);
window.switchSource=setId;window.hide=()=>setShown(false);
return shown?<Editor key={id} id={id}/>:<div>unmounted</div>;
}
function Editor({id}){
const [invoice,setInvoice]=useState(()=>({...createEmptyInvoice("2026-10-25"),id,invoiceNo:"before-save",rowVersion:"native:1",items:Array.from({length:window.initialRows??1},(_,index)=>({...createEmptyInvoiceItem(id),poNumber:"PO-"+index,styleNo:"ZZ-1",styleName:"item",quantity:1,unitPrice:1}))}));
const [message,setMessage]=useState(null),[success,setSuccessMessage]=useState(null),[baseline,setPersistedInvoiceDraft]=useState(null);
const [feedback,setPendingHsFeedback]=useState([]);const location=useLocation();window.locationState=location.state;window.currentRoute=location.pathname;
const mutations=useInvoicePersistenceOperations({client,invoice,invoiceId:id,isNew:id===0,pendingHsFeedback:feedback,resetItemEditHistory:()=>window.resets++,
setConcurrencyMessage:setMessage,setInvoice,setMessage,setPendingHsFeedback,setPersistedInvoiceDraft,setPersistedInvoiceStatus:()=>{},setSuccessMessage});
const items=useInvoiceItemsWorkspace({client,invoice,setInvoice,setSuccessMessage,isEditable:true,canSaveToProductLibrary:true,setMessage});
window.actions={save:()=>mutations.saveInvoiceMutation.mutate({...invoice}),product:()=>items.saveItemToProductLibrary(0),
paste:()=>{try{items.pasteItemTable(0,"styleNo",Array(5001).fill(["item"]));}catch(error){setMessage(error.message);}}};
window.baseline=baseline;window.invoice=invoice;window.saveBusy=mutations.saveInvoiceMutation.isPending;
const grid=window.gridMode?<InvoiceItemsEditor client={client} items={invoice.items} currency="USD" totals={invoice}
defaultSpareColumnCount={10} canRedoItemEdit={items.canRedoItemEdit} canUndoItemEdit={items.canUndoItemEdit}
canSaveToProductLibrary={false} canUseHsKnowledge={false} isProductLibraryBusy={false}
onAddItem={items.addItem} onApplyProductLibraryItem={items.applyProductLibraryItem} onChangeItem={items.patchItem}
onClearItemCells={cells=>{window.lastClearCount=cells.length;items.clearItemCells(cells)}}
onDuplicateItem={items.duplicateItem} onFillDownItemCells={cells=>{window.lastFillCount=cells.length;items.fillDownItemCells(cells)}}
onFillDownItemField={items.fillDownItemField} onMoveItem={items.moveItem} onPasteItemTable={items.pasteItemTable}
onRedoItemEdit={items.redoItemEdit} onRefreshProductLibrary={items.refreshProductLibrary} onOpenProductLibrary={items.openProductLibrary}
onRemoveItem={items.removeItem} onSaveItemToProductLibrary={items.saveItemToProductLibrary} onSearchProductLibrary={items.searchProductLibrary}
onUndoItemEdit={items.undoItemEdit} productLibraryMessage={null} productLibraryProducts={[]}
productLibraryPageNumber={1} productLibraryPageSize={30} productLibraryTotalCount={0} productLibraryTotalPages={1}
onProductLibraryPageChange={items.setProductLibraryPageNumber} onProductLibraryPageSizeChange={items.changeProductLibraryPageSize}
onHsKnowledgeFeedback={()=>{}} unitOptions={[]}/>:null;
return <div><input aria-label="invoice-number" value={invoice.invoiceNo} onChange={e=>setInvoice(current=>({...current,invoiceNo:e.target.value}))}/>
<button onClick={window.actions.save}>save</button><button onClick={window.actions.product}>product</button><button onClick={window.actions.paste}>paste</button>
<output data-message>{message}</output><output data-success>{success}</output><output data-product>{items.productLibraryMessage}</output>{grid}</div>;
}
window.finish=(index=0)=>{const item=window.requests[index];item.resolve({id:7,isUpdate:!window.newInvoice,invoice:{...item.request.body,id:7,rowVersion:"native:2"}})};
createRoot(document.getElementById("root")).render(<MemoryRouter><QueryClientProvider client={queryClient}><ConfirmationProvider><Harness/></ConfirmationProvider></QueryClientProvider></MemoryRouter>);
` }, outfile:bundle,bundle:true,platform:"browser",format:"iife",jsx:"automatic",logLevel:"silent" });
const context=await require("playwright").chromium.launchPersistentContext(path.join(output,"Chrome"),{executablePath:locateChromeForTesting(root),headless:true});
let passed=0;
async function scenario(check,{isNew=false,gridMode=false,initialRows=1}={}){
  const page=await context.newPage();page.setDefaultTimeout(gridMode?20000:7000);const errors=[];page.on("pageerror",error=>errors.push(error.message));
  try{
    await page.setContent('<!doctype html><html lang="zh-CN"><title>Invoice save regression</title><div id="root"></div></html>');
    if(gridMode)await page.addStyleTag({content:".item-editor-frame{height:600px;overflow:auto}.item-editor-table{border-collapse:collapse}.item-editor-table tbody tr:not(.item-virtual-spacer-row){height:42px}.item-virtual-spacer-cell{padding:0;border:0}"});
    await page.evaluate(value=>Object.assign(window,value),{newInvoice:isNew,gridMode,initialRows});await page.addScriptTag({path:bundle});
    await page.getByRole("button",{name:"save",exact:true}).waitFor();await check(page);assert.deepEqual(errors,[]);passed++;
  }catch(error){if(errors.length)console.error("Browser errors:",errors);await page.screenshot({path:path.join(output,`failure-${passed}.png`),fullPage:false,timeout:5000}).catch(()=>{});throw error;}
  finally{await page.close();}
}
try{
  await scenario(async page=>{
    await page.getByRole("button",{name:"save",exact:true}).click();await page.waitForFunction(()=>window.requests.length===1);
    await page.getByLabel("invoice-number").fill("newer unsaved");await page.evaluate(()=>window.finish());
    await page.waitForFunction(()=>window.invoice.rowVersion==="native:2");
    assert.equal(await page.getByLabel("invoice-number").inputValue(),"newer unsaved");
    assert.equal(await page.evaluate(()=>window.baseline.invoiceNo),"before-save");assert.equal(await page.evaluate(()=>window.resets),0);
    assert.equal(await page.evaluate(()=>window.queryClient.getQueryState(["dashboard"]).isInvalidated),true);
    assert.equal(await page.evaluate(()=>window.queryClient.getQueryState(["query","invoices",{}]).isInvalidated),true);
    await page.getByRole("button",{name:"save",exact:true}).click();await page.waitForFunction(()=>window.requests.length===2);
    assert.equal(await page.evaluate(()=>window.requests[1].request.body.rowVersion),"native:2");await page.evaluate(()=>window.finish(1));
  });
  await scenario(async page=>{
    await page.getByRole("button",{name:"save",exact:true}).click();await page.waitForFunction(()=>window.requests.length===1);
    await page.getByLabel("invoice-number").fill("new after create");await page.evaluate(()=>window.finish());
    await page.waitForFunction(()=>window.currentRoute==="/invoices/7");
    assert.equal(await page.evaluate(()=>window.locationState.invoiceSaveContinuation.invoice.invoiceNo),"new after create");
    assert.equal(await page.evaluate(()=>window.locationState.invoiceSaveContinuation.invoice.rowVersion),"native:2");
  },{isNew:true});
  await scenario(async page=>{
    await page.getByRole("button",{name:"save",exact:true}).click();await page.waitForFunction(()=>window.requests.length===1);
    await page.evaluate(()=>window.switchSource(8));await page.waitForFunction(()=>window.invoice.id===8);
    await page.getByLabel("invoice-number").fill("source eight");await page.evaluate(()=>window.finish());
    await page.waitForFunction(()=>window.requests[0].signal.aborted);
    assert.equal(await page.getByLabel("invoice-number").inputValue(),"source eight");assert.equal(await page.evaluate(()=>window.invoice.id),8);
  });
  await scenario(async page=>{
    await page.getByRole("button",{name:"save",exact:true}).click();await page.waitForFunction(()=>window.requests.length===1);
    await page.evaluate(()=>window.hide());await page.getByText("unmounted",{exact:true}).waitFor();
    await page.evaluate(()=>window.requests[0].reject(new Error("late old failure")));
    await page.waitForFunction(()=>window.requests[0].signal.aborted);assert.equal(await page.locator("[data-message]").count(),0);
  });
  await scenario(async page=>{
    await page.getByRole("button",{name:"save",exact:true}).click();await page.waitForFunction(()=>window.requests.length===1);
    await page.evaluate(()=>{window.queryClient.invalidateQueries=async()=>{throw new Error("refresh failure")};window.finish()});
    await page.waitForFunction(()=>document.querySelector("[data-message]").textContent.includes("已保存"));
    assert.equal(await page.evaluate(()=>window.baseline.rowVersion),"native:2");
    assert.equal(await page.evaluate(()=>window.resets),1);
  });
  await scenario(async page=>{
    await page.evaluate(()=>{window.actions.save();window.actions.save()});await page.waitForFunction(()=>window.requests.length===1);
    await page.waitForFunction(()=>window.saveBusy===true);
    await page.evaluate(()=>window.finish());await page.waitForFunction(()=>window.invoice.rowVersion==="native:2");
    assert.equal(await page.evaluate(()=>window.requests.length),1);
  });
  await scenario(async page=>{
    await page.getByRole("button",{name:"product",exact:true}).click();await page.getByRole("button",{name:"更新商品",exact:true}).click();
    await page.waitForFunction(()=>window.productWrites.length===1);
    assert.equal(await page.evaluate(()=>window.productQuery.productCode),"ZZ-1");
    assert.equal(await page.evaluate(()=>window.productWrites[0].kind),"update");
    assert.equal(await page.evaluate(()=>window.productWrites[0].request.id),51);
    await page.getByRole("button",{name:"paste",exact:true}).click();
    assert.match(await page.locator("[data-message]").textContent(),/5000/);assert.equal(await page.evaluate(()=>window.invoice.items.length),1);
  });
  await runInvoiceItemsGridCases(scenario);
  console.log(`Invoice save workspace UI: ${passed} passed; ${output}`);
}finally{await context.close();}

import assert from "node:assert/strict";
import { createReactUiFixture } from "./lib/react-ui-fixture.mjs";
const { web, bundle, source, require, launch } = createReactUiFixture("supplier-overview-ui");
await require("esbuild").build({stdin:{loader:"tsx",resolveDir:web,contents:`
import React,{useState} from "react"; import {createRoot} from "react-dom/client";
import {QueryClient,QueryClientProvider} from "@tanstack/react-query";
import {SupplierAssessmentOverview} from ${source("features/suppliers/SupplierAssessmentOverview.tsx")};
window.requests=[];
const clients=[0,1].map(identity=>({getSupplierAssessmentOverview:(input,{signal})=>new Promise((resolve,reject)=>window.requests.push({identity,input,signal,resolve,reject}))}));
const cache=new QueryClient({defaultOptions:{queries:{retry:false}}});window.cache=cache;
function App(){const [client,setClient]=useState(0),[open,setOpen]=useState(true);window.switchClient=()=>setClient(1);window.close=()=>setOpen(false);
return <QueryClientProvider client={cache}>{open&&<SupplierAssessmentOverview client={clients[client]} onOpenSupplier={()=>{}}/>}</QueryClientProvider>;}
createRoot(document.getElementById("root")).render(<App/>);
`},outfile:bundle,bundle:true,format:"iife",platform:"browser",jsx:"automatic",logLevel:"silent"});
const browser=await launch();let passed=0;
const result=(name="当前供应商")=>({totalSuppliers:100,assessedSuppliers:90,unassessedSuppliers:10,preferredCount:30,qualifiedCount:30,watchCount:20,pausedCount:10,
  averageQualityScore:4,averageDeliveryScore:3,averageServiceScore:4,averagePriceScore:3,totalCount:90,pageNumber:1,pageSize:30,
  items:[{supplierCompanyId:1,supplierName:name,supplierStatus:"Active",category:"制造",latestAssessmentDate:"2026-11-20",latestAssessmentKind:"定期评价",assessmentCount:2,averageScore:3.5,qualityScore:4,deliveryScore:3,serviceScore:4,priceScore:3,conclusion:"合格"}]});
async function scenario(check){const page=await browser.newPage(),errors=[];page.on('pageerror',e=>errors.push(e.message));try{
  await page.setContent('<html lang="zh-CN"><div id="root"></div></html>');await page.addScriptTag({path:bundle});
  await page.waitForFunction(()=>window.requests.length===1);await check(page);assert.deepEqual(errors,[]);passed++;
}finally{await page.close();}}
try{
  await scenario(async page=>{
    await page.evaluate(value=>window.requests[0].resolve(value),result());
    await page.getByRole('button',{name:'下一页',exact:true}).click();
    await page.waitForFunction(()=>window.requests.length===2);
    assert.equal(await page.evaluate(()=>window.requests[1].input.pageNumber),2);
    await page.evaluate(value=>window.requests[1].resolve({...value,pageNumber:2}),result("第二页供应商"));
    await page.getByText("第二页供应商",{exact:true}).waitFor();
    await page.getByRole('button',{name:'需要关注',exact:true}).click();
    await page.waitForFunction(()=>window.requests.length===3);
    assert.deepEqual(await page.evaluate(()=>window.requests[2].input),{filter:'attention',pageNumber:1,pageSize:30});
    await page.evaluate(value=>window.requests[2].resolve({...value,totalCount:30}),result("关注供应商"));
    await page.getByText('90 / 100 家已确认评价',{exact:true}).waitFor();
  });
  for(const change of ['switch','unmount'])for(const outcome of ['resolve','reject'])await scenario(async page=>{
    await page.evaluate(change=>change==='switch'?window.switchClient():window.close(),change);
    await page.waitForFunction(()=>window.requests[0].signal.aborted);
    await page.evaluate(({outcome,value})=>outcome==='resolve'?window.requests[0].resolve(value):window.requests[0].reject(new Error('旧请求失败')),{outcome,value:result('旧供应商')});
    if(change==='switch'){
      await page.waitForFunction(()=>window.requests.length===2);
      await page.evaluate(value=>window.requests[1].resolve(value),result());
      await page.getByText('当前供应商',{exact:true}).waitFor();
    }
    assert.doesNotMatch(await page.locator('body').innerText(),/旧供应商|旧请求失败/);
  });
  console.log('Supplier overview UI: '+passed+' passed.');
}finally{await browser.close();}

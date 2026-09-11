export function documentEditorUiFixture(source) {
  return `
import ${source("styles/cascade.css")}; import ${source("styles/foundation.css")};
import ${source("styles/workspaces.css")}; import ${source("styles/responsive.css")};
import React from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import { MemoryRouter, Routes, Route, useLocation } from 'react-router-dom';
import { PermissionAccessProvider } from ${source("app/PermissionAccessContext.tsx")};
import { permissionResources, permissionActions } from ${source("app/permissionCatalog.ts")};
import { ConfirmationProvider } from ${source("ui/ConfirmationProvider.tsx")};
import { UnsavedChangesProvider } from ${source("ui/unsavedChangesGuard.tsx")};
import { InvoiceEditorPage } from ${source("features/invoices/InvoiceEditorPage.tsx")};
import { PaymentEditorPage } from ${source("features/payments/PaymentEditorPage.tsx")};
import { SettingsPage } from ${source("features/settings/SettingsPage.tsx")};
import { ReportExportDefaultsPanel } from ${source("features/reports/ReportExportDefaultsPanel.tsx")};
import { useReportExportDefaults } from ${source("features/reports/useReportExportDefaults.ts")};
import { ApiError } from ${source("api/index.ts")};
import { createEmptyInvoice } from ${source("features/invoices/invoiceModel.ts")};
import { createEmptyInvoiceItem } from ${source("features/invoices/invoiceItemsEditorModel.ts")};
import { createEmptyPayment } from ${source("features/payments/paymentModel.ts")};
import { setNestedValue } from ${source("features/settings/settingsValueUtils.ts")};
const params=new URLSearchParams(location.search), mode=params.get('mode')||'new', readonly=params.get('role')==='reader';
const date='2026-09-11';
window.__calls=[]; window.__errors=[]; window.__failCustomOption=false;
addEventListener('error',event=>window.__errors.push(event.message));
addEventListener('unhandledrejection',event=>window.__errors.push(String(event.reason)));
let settings={revision:0,batchExport:{items:[],mergePdf:false,zipAfterExport:false,outputFileNamePattern:'发票号',outputFolderPattern:'发票号'},system:{itemEntryBlankRowCount:3,itemEntrySpareColumnCount:Number(params.get('spares')||0),documentFieldLabels:params.has('renamed')
  ?{invoice:{spare1:'船名航次'},item:{spare1:'材质规格',spare10:'客户货号'},payment:{spare1:'费用归属'}}:{invoice:{},item:{},payment:{}}}};
let invoice={...createEmptyInvoice(date),id:7,invoiceNo:'INV-2026-091',customerNameEN:'NORTHSTAR TRADING',exporterNameEN:'BRIDGE EXPORT',exporterNameCN:'示例出口公司',currency:'USD',rowVersion:1,
  items:[{...createEmptyInvoiceItem(7),styleNo:'STYLE-091',styleName:'COTTON SHIRT',quantity:5,unitPrice:10,totalPrice:50,spare10:params.has('populated')?'保留原始备注':''}],totalAmount:50};
let payment={...createEmptyPayment(date),id:9,invoiceNo:'PAY-2026-091',paymentMethod:'电汇',receiptDate:date,rowVersion:1};
const custom={PaymentMethod:['支票','电汇','预付'],PaymentPayerName:[],Currency:['USD','CNY'],SupervisionMode:['一般贸易'],PaymentTerms:['T/T']};
const record=(name,input,result)=>{window.__calls.push({name,input});return Promise.resolve(structuredClone(result))};
const page=items=>({items,pageNumber:1,pageSize:50,totalCount:items.length,totalPages:1,hasNextPage:false,hasPreviousPage:false});
const saveInvoice=(name,input)=>{invoice={...input.body,id:7,rowVersion:(invoice.rowVersion||0)+1};return record(name,input,{id:7,invoice,isUpdate:name==='updateInvoice'})};
const conflict=()=>new ApiError(409,'Conflict',JSON.stringify({message:'数据已被其他用户更新，请重新加载最新设置后再保存。'}));
const savePayment=(name,input)=>{
  if(name==='updatePayment'&&input.body.rowVersion!==payment.rowVersion)return Promise.reject(conflict());
  payment={...input.body,id:9,rowVersion:(payment.rowVersion||0)+1};return record(name,input,{id:9,payment});
};
const client={
  getInvoice:input=>record('getInvoice',input,invoice),listInvoiceStatusHistory:()=>Promise.resolve([]),
  createInvoice:input=>saveInvoice('createInvoice',input),updateInvoice:input=>saveInvoice('updateInvoice',input),
  getPayment:input=>{if(window.__failPaymentReload){window.__failPaymentReload=false;return Promise.reject(new Error('付款读取失败'));}return record('getPayment',input,payment);},
  createPayment:input=>savePayment('createPayment',input),updatePayment:input=>savePayment('updatePayment',input),
  getSettings:()=>{if(window.__failSettingsReload){window.__failSettingsReload=false;return Promise.reject(new Error('设置读取失败'));}return record('getSettings',{}, {settings,canManageSettings:!readonly});},
  updateSettings:input=>{
    if(readonly)return Promise.reject(new ApiError(403,'Forbidden',''));
    if(input.body.settings.revision!==settings.revision)return Promise.reject(conflict());
    settings={...structuredClone(input.body.settings),revision:settings.revision+1};
    return record('updateSettings',input,{success:true,settings,message:'设置已保存。',requiresRestart:false});
  },
  listCustomOptions:input=>record('listCustomOptions',input,{optionType:input.optionType,options:custom[input.optionType]||[],allowCustomValues:true}),
  saveCustomOption:input=>{
    window.__calls.push({name:'saveCustomOption',input});
    if(window.__failCustomOption){window.__failCustomOption=false;return Promise.reject(new Error('候选项服务暂时不可用'));}
    const values=custom[input.optionType]??=[];if(!values.some(value=>value.toLowerCase()===input.body.value.toLowerCase()))values.push(input.body.value);
    return Promise.resolve({optionType:input.optionType,options:[...values],allowCustomValues:true});
  },
  listUnits:()=>Promise.resolve([]),listProducts:()=>Promise.resolve(page([])),
  listCustomersPage:()=>Promise.resolve(page([])),listExportersPage:()=>Promise.resolve(page([])),listPayeesPage:()=>Promise.resolve(page([])),
  listReportTemplates:input=>record('listReportTemplates',input,[]),
  reviewInvoice:input=>record('reviewInvoice',input,{ready:true,issues:[]}),
};
const queries=new QueryClient({defaultOptions:{queries:{retry:false,refetchOnWindowFocus:false},mutations:{retry:false}}});
window.__reloadOptions=()=>queries.invalidateQueries({queryKey:['custom-options']});
window.__updateDefaultSpares=async count=>{settings.system.itemEntrySpareColumnCount=count;await queries.invalidateQueries({queryKey:['settings']});};
window.__renameField=async(group,key,name)=>{settings.system.documentFieldLabels[group][key]=name;await queries.invalidateQueries({queryKey:['settings']});};
window.__changeSettingOnServer=(path,value)=>{settings=structuredClone(settings);setNestedValue(settings,path,value);settings.revision++;};
window.__changePaymentOnServer=patch=>{payment={...payment,...patch,rowVersion:payment.rowVersion+1};};
function LocationProbe(){const current=useLocation();window.__route=current.pathname+current.search;return null;}
function ExportDefaultsFixture(){
  const query=useQuery({queryKey:['settings'],queryFn:()=>client.getSettings()});
  const defaults=useReportExportDefaults({client,response:query.data,refetch:query.refetch,onFeedback:message=>{window.__feedback=message;}});
  return <ReportExportDefaultsPanel {...defaults} isBusy={defaults.isBusy||query.isFetching} canManageSettings={!readonly} templates={[]}/>;
}
const grants=['document.invoices','document.payments','document.master-data','document.reports','document.excel'].map(moduleKey=>({moduleKey,accessLevel:readonly?'view':'manage'}));
const permissions=Object.values(permissionResources).flatMap(resourceKey=>Object.values(permissionActions).map(action=>({resourceKey,action,dataScope:'all'})));
const entry=mode==='payment'?'/payments/9':mode==='payment-new'?'/payments/new':mode==='settings'?'/settings':mode==='export-defaults'?'/export-defaults':mode==='edit'?'/invoices/7':'/invoices/new';
createRoot(document.getElementById('root')).render(<MemoryRouter initialEntries={[entry]}><QueryClientProvider client={queries}>
  <PermissionAccessProvider grants={grants} permissions={permissions} canManageSettings={!readonly}><ConfirmationProvider><UnsavedChangesProvider>
    <main className='workspace-content'><h1>单据工作台</h1><LocationProbe/><Routes>
      <Route path='/invoices/new' element={<InvoiceEditorPage client={client} businessDate={date} mode='new'/>}/>
      <Route path='/invoices/:invoiceId' element={<InvoiceEditorPage client={client} businessDate={date} mode='edit'/>}/>
      <Route path='/payments/:paymentId' element={<PaymentEditorPage client={client} businessDate={date} mode='edit'/>}/>
      <Route path='/payments/new' element={<PaymentEditorPage client={client} businessDate={date} mode='new'/>}/>
      <Route path='/settings' element={<SettingsPage client={client} canManageSettings={!readonly} canManageUsers={!readonly} canUseDocumentWorkspace={true} productName='单据工作台'/>}/>
      <Route path='/export-defaults' element={<ExportDefaultsFixture/>}/>
      <Route path='*' element={<p>已返回列表</p>}/>
    </Routes></main>
  </UnsavedChangesProvider></ConfirmationProvider></PermissionAccessProvider>
</QueryClientProvider></MemoryRouter>);
`;
}

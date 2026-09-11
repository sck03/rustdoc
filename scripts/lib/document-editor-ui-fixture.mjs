export function documentEditorUiFixture(source) {
  return `
import ${source("styles/cascade.css")}; import ${source("styles/foundation.css")};
import ${source("styles/workspaces.css")}; import ${source("styles/responsive.css")};
import React from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Routes, Route, useLocation } from 'react-router-dom';
import { PermissionAccessProvider } from ${source("app/PermissionAccessContext.tsx")};
import { permissionResources, permissionActions } from ${source("app/permissionCatalog.ts")};
import { ConfirmationProvider } from ${source("ui/ConfirmationProvider.tsx")};
import { UnsavedChangesProvider } from ${source("ui/unsavedChangesGuard.tsx")};
import { InvoiceEditorPage } from ${source("features/invoices/InvoiceEditorPage.tsx")};
import { PaymentEditorPage } from ${source("features/payments/PaymentEditorPage.tsx")};
import { RuntimeDatabaseSettingsPanel } from ${source("features/settings/RuntimeDatabaseSettingsPanel.tsx")};
import { createEmptyInvoice } from ${source("features/invoices/invoiceModel.ts")};
import { createEmptyInvoiceItem } from ${source("features/invoices/invoiceItemsEditorModel.ts")};
import { createEmptyPayment } from ${source("features/payments/paymentModel.ts")};
const params=new URLSearchParams(location.search), mode=params.get('mode')||'new', readonly=params.get('role')==='reader';
const date='2026-09-11';
window.__calls=[]; window.__errors=[]; window.__failCustomOption=false;
addEventListener('error',event=>window.__errors.push(event.message));
addEventListener('unhandledrejection',event=>window.__errors.push(String(event.reason)));
let settings={system:{itemEntryBlankRowCount:3,itemEntrySpareColumnCount:Number(params.get('spares')||0)}};
let invoice={...createEmptyInvoice(date),id:7,invoiceNo:'INV-2026-091',customerNameEN:'NORTHSTAR TRADING',exporterNameEN:'BRIDGE EXPORT',exporterNameCN:'示例出口公司',currency:'USD',rowVersion:1,
  items:[{...createEmptyInvoiceItem(7),styleNo:'STYLE-091',styleName:'COTTON SHIRT',quantity:5,unitPrice:10,totalPrice:50,spare10:params.has('populated')?'保留原始备注':''}],totalAmount:50};
let payment={...createEmptyPayment(date),id:9,invoiceNo:'PAY-2026-091',paymentMethod:'电汇',receiptDate:date,rowVersion:1};
const custom={PaymentMethod:['支票','电汇','预付'],PaymentPayerName:[],Currency:['USD','CNY'],SupervisionMode:['一般贸易'],PaymentTerms:['T/T']};
const record=(name,input,result)=>{window.__calls.push({name,input});return Promise.resolve(structuredClone(result))};
const page=items=>({items,pageNumber:1,pageSize:50,totalCount:items.length,totalPages:1,hasNextPage:false,hasPreviousPage:false});
const saveInvoice=(name,input)=>{invoice={...input.body,id:7,rowVersion:(invoice.rowVersion||0)+1};return record(name,input,{id:7,invoice,isUpdate:name==='updateInvoice'})};
const savePayment=(name,input)=>{payment={...input.body,id:9,rowVersion:(payment.rowVersion||0)+1};return record(name,input,{id:9,payment})};
const client={
  getInvoice:input=>record('getInvoice',input,invoice),listInvoiceStatusHistory:()=>Promise.resolve([]),
  createInvoice:input=>saveInvoice('createInvoice',input),updateInvoice:input=>saveInvoice('updateInvoice',input),
  getPayment:input=>record('getPayment',input,payment),createPayment:input=>savePayment('createPayment',input),updatePayment:input=>savePayment('updatePayment',input),
  getSettings:()=>record('getSettings',{}, {settings,canManageSettings:!readonly}),
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
function LocationProbe(){const current=useLocation();window.__route=current.pathname+current.search;return null;}
function SettingsFixture(){const [draft,setDraft]=React.useState(settings);return <section className='work-surface'>
  <RuntimeDatabaseSettingsPanel settings={draft} secrets={null} canManageSettings={!readonly} updateSecrets={false} isBusy={false} canSelectDesktopDirectory={false}
    onChange={(path,value)=>{setDraft(current=>({...current,[path[0]]:{...current[path[0]],[path[1]]:value}}));window.__settingPatch={path,value};}} onSelectDefaultExportDirectory={()=>{}} />
</section>;}
const grants=['document.invoices','document.payments','document.master-data','document.reports','document.excel'].map(moduleKey=>({moduleKey,accessLevel:readonly?'view':'manage'}));
const permissions=Object.values(permissionResources).flatMap(resourceKey=>Object.values(permissionActions).map(action=>({resourceKey,action,dataScope:'all'})));
const entry=mode==='payment'?'/payments/9':mode==='settings'?'/settings':mode==='edit'?'/invoices/7':'/invoices/new';
createRoot(document.getElementById('root')).render(<MemoryRouter initialEntries={[entry]}><QueryClientProvider client={queries}>
  <PermissionAccessProvider grants={grants} permissions={permissions} canManageSettings={!readonly}><ConfirmationProvider><UnsavedChangesProvider>
    <main className='workspace-content'><h1>单据工作台</h1><LocationProbe/><Routes>
      <Route path='/invoices/new' element={<InvoiceEditorPage client={client} businessDate={date} mode='new'/>}/>
      <Route path='/invoices/:invoiceId' element={<InvoiceEditorPage client={client} businessDate={date} mode='edit'/>}/>
      <Route path='/payments/:paymentId' element={<PaymentEditorPage client={client} businessDate={date} mode='edit'/>}/>
      <Route path='/settings' element={<SettingsFixture/>}/>
      <Route path='*' element={<p>已返回列表</p>}/>
    </Routes></main>
  </UnsavedChangesProvider></ConfirmationProvider></PermissionAccessProvider>
</QueryClientProvider></MemoryRouter>);
`;
}

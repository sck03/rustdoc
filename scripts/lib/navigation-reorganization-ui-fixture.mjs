export function navigationReorganizationUiFixture(source, setup = "") {
  return `
import React from 'react';
import {createRoot} from 'react-dom/client';
import {HashRouter,useLocation} from 'react-router-dom';
import {QueryClient,QueryClientProvider} from '@tanstack/react-query';
import {WorkspaceShell} from ${source("app/WorkspaceShell.tsx")};
import {AppWorkspaceRoutes} from ${source("app/AppWorkspaceRoutes.tsx")};
import {getWorkspaceRouteItems} from ${source("app/workspaceNavigation.ts")};
import {getProductEditionPresentation} from ${source("app/productEdition.ts")};
import {isRouteAccessAllowed} from ${source("app/routeAccess.ts")};
import {PermissionAccessProvider} from ${source("app/PermissionAccessContext.tsx")};
import {permissionResources,permissionActions} from ${source("app/permissionCatalog.ts")};
import {ConfirmationProvider} from ${source("ui/ConfirmationProvider.tsx")};
import {UnsavedChangesProvider} from ${source("ui/unsavedChangesGuard.tsx")};
import ${source("styles/cascade.css")}; import ${source("styles/foundation.css")};
import ${source("styles/workspaces.css")}; import ${source("styles/responsive.css")};
const role=new URLSearchParams(location.search).get('role')||'admin';
const rows=Array.from({length:135},(_,i)=>({id:i+1,name:'示例单位 '+String(i+1).padStart(3,'0'),countryRegion:'CN',website:'',status:'合作中',category:'纺织',mainProducts:'服装',notes:'',source:'展会',versionNumber:1}));
const opportunities=rows.map(row=>({...row,title:'商机 '+row.id,crmCustomerId:row.id,customerName:row.name,productId:null,productCode:'',productName:'',stage:'线索',quotationNo:'QT-'+row.id,estimatedAmount:100,currency:'USD',probabilityPercent:20,nextAction:'确认需求',allowedNextStages:['需求确认']}));
const invoices=rows.map(row=>({id:row.id,invoiceNo:'INV-'+row.id,customerName:row.name,invoiceDate:'2026-09-12',currency:'USD',totalAmount:100,type:'出口',status:'Draft',exporterName:'示例出口公司',destinationCountry:'US',portOfLoading:'宁波',portOfDestination:'纽约',contractNo:''}));
const deliveries=Array.from({length:65},(_,i)=>({deliveryId:'delivery-'+i,jobId:'',kind:'EmailTool',recipient:'buyer'+i+'@example.test',subject:'业务邮件 '+i,attachmentCount:0,status:i%2?'Uncertain':'Sent',errorMessage:'',createdAt:'2026-09-12T01:00:00Z',updatedAt:'2026-09-12T01:00:00Z'}));
const templates=[{templatePath:'invoice_template.html',displayName:'标准发票',reportType:'ExportDocument',withSealDefault:true},{templatePath:'packing.html',displayName:'装箱单',reportType:'ExportDocument',withSealDefault:false}];
const userTemplates=[{id:23,name:'公司共享发票',reportType:'ExportDocument',shareScope:'Company',status:'Published',contentHtml:'<html><body>Shared template</body></html>',versionNumber:1,canEdit:true,canShare:true,canPublish:false,canArchive:true}];
const settings={revision:0,system:{appName:'业务系统',itemEntryBlankRowCount:20,itemEntrySpareColumnCount:0,documentFieldLabels:{invoice:{},item:{},payment:{}}},reportTemplateDefaults:{exportDocumentTemplatePath:'invoice_template.html'},batchExport:{items:[],mergePdf:true,zipAfterExport:true,outputFileNamePattern:'发票号',outputFolderPattern:'发票号'},email:{smtpHost:'smtp.example.test'},webDav:{},singleWindow:{customsCooDefaults:{}},ai:{}};
window.__calls=[];window.__errors=[];
addEventListener('error',event=>window.__errors.push(event.message));
addEventListener('unhandledrejection',event=>window.__errors.push(String(event.reason)));
const call=(name,input,result)=>{window.__calls.push({name,input});return Promise.resolve(structuredClone(result));};
const page=(items,input={})=>{const pageNumber=input.pageNumber||1,pageSize=input.pageSize||20;return {items:items.slice((pageNumber-1)*pageSize,pageNumber*pageSize),pageNumber,pageSize,totalCount:items.length,totalPages:Math.ceil(items.length/pageSize),hasPreviousPage:pageNumber>1,hasNextPage:pageNumber*pageSize<items.length};};
const matches=(items,input={})=>items.filter(row=>!input.keyword||JSON.stringify(row).includes(input.keyword));
const exact=(items,id)=>{const item=items.find(row=>row.id===id);if(!item)throw new Error('记录不存在或无权访问');return item;};
const client={
 queryCrmCustomers:input=>call('customers',input,page(matches(rows,input),input)),getCrmCustomer:input=>call('customer',input,exact(rows,input.id)),
 queryCrmContacts:input=>call('customerContacts',input,page([{id:1,crmCustomerId:input.customerId,name:'示例联系人',title:'采购',email:'buyer@example.test',phone:'',instantMessaging:'',isPrimary:true,versionNumber:1}],input)),
 queryCrmFollowUps:input=>call('followups',input,page(input.followUpId?[{id:input.followUpId,crmCustomerId:135,customerName:'示例单位 135',crmContactId:1,contactName:'示例联系人',type:'邮件',summary:'确认样品',nextAction:'发送报价',followedUpAt:'2026-09-12T01:00:00Z',isCompleted:false,versionNumber:1}]:[],input)),
 updateCrmCustomer:input=>call('saveCustomer',input,{...exact(rows,input.id),...input.body,versionNumber:2}),
 querySuppliers:input=>call('suppliers',input,page(matches(rows,input),input)),getSupplier:input=>call('supplier',input,exact(rows,input.id)),
 querySupplierContacts:input=>call('supplierContacts',input,page([{id:1,supplierCompanyId:input.supplierId,name:'供应商联系人',title:'业务员',email:'sales@example.test',phone:'',instantMessaging:'',isPrimary:true,versionNumber:1}],input)),
 querySalesOpportunities:input=>call('opportunities',input,page(matches(opportunities,input),input)),getSalesOpportunity:input=>call('opportunity',input,exact(opportunities,input.id)),
 listSalesOpportunityHistory:input=>call('opportunityHistory',input,[{id:1,versionNumber:1,changeType:'新建',stage:'线索',quotationNo:'QT-'+input.id,estimatedAmount:100,currency:'USD',probabilityPercent:20,changedBy:'示例人员',createdAt:'2026-09-12T01:00:00Z'}]),
 listProducts:input=>call('products',input,page([],input)),
 listInvoices:input=>call('invoices',input,page(matches(invoices,input),input)),
 getSettings:()=>call('settings',{}, {settings,secrets:{},storagePolicy:''}),
 updateSettings:input=>{Object.assign(settings,input.body.settings);settings.revision++;return call('saveSettings',input,{settings,secrets:{},message:'已保存',requiresRestart:false});},
 getHealth:()=>call('health',{}, {status:'ok',databaseProviderKey:'Sqlite'}),
 getCustomsCooIssuingAuthorities:()=>call('authorities',{},[]),
 listReportTemplates:input=>call('templates',input,templates),listUserReportTemplates:input=>call('userTemplates',input,userTemplates),
 listUserReportTemplateVersions:input=>call('templateVersions',input,[]),
 getReportTemplateContent:input=>call('templateContent',input,{...templates.find(row=>row.templatePath===input.templatePath),content:'<html><body>Invoice</body></html>',revision:'fixture-revision',storagePolicy:''}),
 getEmailToolStatus:()=>call('emailStatus',{}, {isConfigured:true,smtpHost:'smtp.example.test',smtpPort:587,enableSsl:true,fromAddress:'sender@example.test',fromDisplayName:'业务部'}),
 listEmailDeliveries:input=>call('deliveries',input,page(matches(deliveries,input).filter(row=>!input.status||row.status===input.status),input)),
 sendEmail:input=>call('sendEmail',input,{success:true,message:'邮件已发送'}),
 startInvoiceReportPdfZipDownloadJob:input=>{window.__calls.push({name:'batchExport',input});throw new Error('测试依赖不可用');},
};
let modules=[...new Set(getWorkspaceRouteItems().flatMap(item=>item.moduleKey?[item.moduleKey]:[]))];
let permissions=Object.values(permissionResources).flatMap(resourceKey=>Object.values(permissionActions).map(action=>({resourceKey,action,dataScope:'all'})));
permissions.push(...modules.flatMap(resourceKey=>['view','operate','manage'].map(action=>({resourceKey,action,dataScope:'all'}))));
if(role==='customer-only'){modules=['sales.crm'];permissions=[{resourceKey:permissionResources.crmCustomers,action:'view',dataScope:'own'}];}
if(role==='delivery-only'){modules=['common.email'];permissions=[{resourceKey:permissionResources.emailDelivery,action:'view-delivery',dataScope:'own'}];}
const capabilities={productEdition:'Full',canManageSettings:role==='admin',canManageUsers:role==='admin',canUseDocumentWorkspace:role==='admin',canUseSalesWorkspace:role!=='delivery-only',enabledModules:modules,moduleAccess:modules.map(moduleKey=>({moduleKey,accessLevel:role==='admin'?'manage':'view'})),permissions,availableFeatures:['worklist','business-attachments']};
const user={id:1,username:'demo',fullName:'示例人员',role:'Admin',companyScope:'DEMO',departmentId:'SALES',businessTimeZone:'Asia/Shanghai',businessDate:'2026-09-12',isActive:true,capabilities};
const queries=new QueryClient({defaultOptions:{queries:{retry:false},mutations:{retry:false}}});
function Workspace(){const current=useLocation();window.__route=current.pathname+current.search;return <WorkspaceShell pathname={current.pathname} apiBaseUrl='/fixture' user={user} isDesktopRuntime={false} onLogout={()=>{}} connectivityOverride='online' serviceAvailabilityOverride='available'>
  <AppWorkspaceRoutes client={client} activeProduct={getProductEditionPresentation('Full')} user={user} canManageAuditLogs={true}
    routeAccessAllowed={isRouteAccessAllowed({pathname:current.pathname,user,canManageSystem:capabilities.canManageSettings,isDesktopRuntime:false})}/>
</WorkspaceShell>;}
${setup}
createRoot(document.getElementById('root')).render(<HashRouter><QueryClientProvider client={queries}><PermissionAccessProvider grants={capabilities.moduleAccess} permissions={permissions} canManageSettings={capabilities.canManageSettings}><ConfirmationProvider><UnsavedChangesProvider><Workspace/></UnsavedChangesProvider></ConfirmationProvider></PermissionAccessProvider></QueryClientProvider></HashRouter>);
`;
}

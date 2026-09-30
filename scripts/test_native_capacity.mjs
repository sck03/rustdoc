import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { randomBytes, randomUUID } from 'node:crypto';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { spawnProcessTree, stopProcessTree } from './lib/child-process-tree.mjs';
import { cargoArtifactDirectory } from './lib/cargo-paths.mjs';

// Invoked only by the isolated PostgreSQL test runner. No production endpoint
// or caller-selected database is accepted by this workload.
const repo=path.resolve(import.meta.dirname,'..');
const root=path.join(repo,'.codex-runtime','native-capacity',randomUUID());
fs.mkdirSync(root,{recursive:true});
const web=path.join(repo,'apps/export-doc-web');
const require=createRequire(path.join(web,'package.json'));
const models=path.join(root,'invoice-models.mjs');
await require('esbuild').build({stdin:{loader:'ts',resolveDir:web,contents:`export {createEmptyInvoice} from ${JSON.stringify(path.join(web,'src/features/invoices/invoiceModel.ts'))}; export {createEmptyInvoiceItem} from ${JSON.stringify(path.join(web,'src/features/invoices/invoiceItemsEditorModel.ts'))};`},bundle:true,platform:'node',format:'esm',outfile:models});
const {createEmptyInvoice,createEmptyInvoiceItem}=await import(pathToFileURL(models).href);
const executable=path.join(cargoArtifactDirectory(repo),'export-doc-server'+(process.platform==='win32'?'.exe':''));
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
const results=[];
assert.equal(process.env.EXPORTDOC_CAPACITY_ISOLATED,'1','Use the isolated PostgreSQL test runner');

async function phase(size,key) {
  const connection=process.env[`EXPORTDOC_TEST_${key}_APP`];
  const maintenance=process.env[`EXPORTDOC_TEST_${key}_MAINTENANCE`];
  for(const value of [connection,maintenance]) assert(value && /(?:^|\s)host=127\.0\.0\.1(?:\s|$)/u.test(value) && new RegExp(`(?:^|\\s)dbname=native_capacity_${size}(?:\\s|$)`,'u').test(value),'Expected a runner-owned loopback capacity database');
  const output=path.join(root,`pool-${size}`);const data=path.join(output,'Data');fs.mkdirSync(data,{recursive:true});
  const endpoint=path.join(data,'endpoint.json');const bootstrap=randomBytes(32).toString('hex');
  const password=`Capacity-${randomBytes(16).toString('hex')}`;
  const log=fs.createWriteStream(path.join(output,'server.log'));
  const server=spawnProcessTree(executable,['--app-root',repo,'--data-root',data,'--bind','127.0.0.1:0','--endpoint-file',endpoint,'--initialize-schema'],{
    cwd:repo,windowsHide:true,stdio:['ignore','pipe','pipe'],env:{...process.env,
      EXPORTDOCMANAGER_POSTGRES_CONNECTION:connection,EXPORTDOCMANAGER_POSTGRES_MAINTENANCE_CONNECTION:maintenance,
      EXPORTDOCMANAGER_POSTGRES_OWNER:'native_owner',EXPORTDOCMANAGER_BOOTSTRAP_TOKEN:bootstrap,
      EXPORTDOCMANAGER_POSTGRES_POOL_SIZE:String(size),EXPORTDOCMANAGER_POSTGRES_POOL_WAIT_MS:'5000',
      TEMP:path.join(repo,'.codex-runtime/temp'),TMP:path.join(repo,'.codex-runtime/temp'),
    }});
  server.stdout.pipe(log,{end:false});server.stderr.pipe(log,{end:false});
  let exited=false;server.once('exit',()=>{exited=true;});
  try {
    const deadline=Date.now()+60000;
    while(!fs.existsSync(endpoint) && Date.now()<deadline){assert(!exited,'Capacity server exited during startup');await delay(100);}
    assert(fs.existsSync(endpoint),'Capacity server startup timed out');
    const origin=JSON.parse(fs.readFileSync(endpoint,'utf8')).apiBaseUrl;
    assert.equal(new URL(origin).hostname,'127.0.0.1');
    const document=await fetch(`${origin}/openapi/v1.json`).then(r=>r.json());
    const operations=new Map(Object.entries(document.paths).flatMap(([route,methods])=>Object.entries(methods).map(([method,op])=>[op.operationId,{method:method.toUpperCase(),route,schema:op.requestBody?.content?.['application/json']?.schema}])));
    // Required transport fields come from OpenAPI; business values stay explicit.
    function requestBody(schema,value,depth=0) {
      if(!schema || depth>20)return value;
      if(schema.$ref)return requestBody(document.components.schemas[schema.$ref.split('/').at(-1)],value,depth+1);
      const types=Array.isArray(schema.type)?schema.type:[schema.type];
      if(value===undefined) {
        if(types.includes('null'))return null;
        if(schema.default!==undefined)return structuredClone(schema.default);
        if(schema.enum?.length)return schema.enum[0];
        if(types.includes('array'))return [];
        if(types.includes('boolean'))return false;
        if(types.includes('integer')||types.includes('number'))return 0;
        if(types.includes('string'))return '';
        value={};
      }
      if(Array.isArray(value))return value.map(item=>requestBody(schema.items,item,depth+1));
      if(value && typeof value==='object' && schema.properties) {
        const result={...value};
        for(const key of new Set([...Object.keys(result),...(schema.required??[])]))result[key]=requestBody(schema.properties[key],result[key],depth+1);
        return result;
      }
      return value;
    }
    let measure=false;const latencies=[];const statuses={};let retried=0;
    async function api(name,token='',body,parameters={},query={},firstLogin=false) {
      const op=operations.get(name);assert(op,`Missing generated operation: ${name}`);
      const route=op.route.replace(/\{([^}]+)\}/gu,(_,key)=>{assert(parameters[key]!==undefined,`Missing ${key}`);return encodeURIComponent(parameters[key]);});
      const url=new URL(route,origin);for(const [key,value] of Object.entries(query))url.searchParams.set(key,String(value));
      const multipart=body instanceof FormData;
      const payload=body===undefined?undefined:(multipart?body:requestBody(op.schema,body));
      const started=performance.now();
      for(let attempt=0;;attempt++) {
        const response=await fetch(url,{method:op.method,signal:AbortSignal.timeout(35000),headers:{...(multipart?{}:{'content-type':'application/json'}),...(token?{authorization:`Bearer ${token}`} : {}),...(firstLogin?{'x-exportdocmanager-bootstrap-token':bootstrap}:{})},body:payload===undefined?undefined:(multipart?payload:JSON.stringify(payload))});
        if(measure)statuses[response.status]=(statuses[response.status]??0)+1;
        const value=await response.json();
        if(response.status===429 && attempt<12){if(measure)retried++;await delay(100+attempt*50);continue;}
        assert(response.ok,`${name}: ${response.status} ${JSON.stringify(value)} (${response.headers.get('x-request-id')})`);
        if(measure)latencies.push(performance.now()-started);
        return value;
      }
    }
    const admin=await api('Login','',{username:'admin',password},{},{},true);const token=admin.accessToken;
    const date=admin.user.businessDate;assert(/^\d{4}-\d{2}-\d{2}$/u.test(date));
    const invoice=(number)=>{
      const draft={...createEmptyInvoice(date),invoiceNo:number,exporterNameEN:'CAPACITY SAMPLE EXPORTER',customerNameEN:'Capacity sample customer',currency:'USD'};
      draft.items=[{...createEmptyInvoiceItem(),styleNo:'CAP-ITEM',styleName:'Sample product',quantity:10,cartons:1,unitEN:'PCS',ctnUnitEN:'CTNS',unitPrice:1.25,totalPrice:12.5}];return draft;
    };
    for(let n=0;n<200;n++)await api('CreateInvoice',token,invoice(`CAP-SEED-${n}`));
    const users=[];
    for(let n=0;n<20;n++) {
      const account=(await api('createUserAccount',token,{username:`capacity-${n}`,fullName:`Capacity ${n}`,role:'Admin',departmentId:'GENERAL',companyScope:'DEFAULT',isActive:true,resetPassword:password})).user;
      const login=await api('Login','',{username:account.username,password});
      const row=(await api('CreateInvoice',login.accessToken,invoice(`CAP-USER-${n}`))).invoice;
      users.push({account,token:login.accessToken,invoice:row});
    }
    const requester=(await api('createUserAccount',token,{username:'capacity-requester',fullName:'Capacity requester',role:'OfficeManager',departmentId:'GENERAL',companyScope:'DEFAULT',isActive:true,resetPassword:password})).user;
    const person=await api('CreatePersonnel',token,{requestKey:randomUUID(),employeeNumber:'CAP-REQUESTER',departmentId:'GENERAL',jobTitle:'Capacity tester',employmentType:'FullTime',hireDate:date,profile:{fullName:'Capacity requester'}});
    await api('LinkPersonnelAccount',token,{expectedVersion:person.versionNumber,userId:requester.id,expectedAccountVersion:requester.versionNumber},{id:person.employee.id});
    const requesterToken=(await api('Login','',{username:requester.username,password})).accessToken;
    const approvals=[];
    for(let n=0;n<5;n++) {
      let row=await api('CreateExpenseRequest',requesterToken,{requestKey:randomUUID(),title:`Capacity expense ${n}`,reason:'Isolated concurrency verification',currency:'CNY',lines:[{category:'Travel',spentOn:date,description:'Sample receipt',amount:'12.34'}]});
      const receipt=new FormData();receipt.append('expectedVersion',String(row.versionNumber));
      receipt.append('file',new Blob([fs.readFileSync(path.join(repo,'crates/export-doc-engine/src/engine/reports/samples-shipping-marks.png'))],{type:'image/png'}),'capacity-receipt.png');
      row=await api('UploadAttachmentToExpenseRequest',requesterToken,receipt,{id:row.id});
      approvals.push(await api('SubmitExpenseRequest',requesterToken,{expectedVersion:row.versionNumber,note:'Capacity submit'},{id:row.id}));
    }
    const catalog=await api('ListReportTemplates',token,undefined,{}, {reportType:'ExportDocument'});
    const template=catalog.find(row=>/invoice_template\.dtpl$/u.test(row.templatePath));assert(template);
    async function waitJob(job,owner) {
      const deadline=Date.now()+120000;
      while(Date.now()<deadline) {
        const row=await api('GetJob',owner,undefined,{jobId:job.jobId});
        if(row.status==='Succeeded')return row;
        assert(!['Failed','Canceled'].includes(row.status),`Export ${row.status}`);await delay(200);
      }
      throw Error('Export completion timed out');
    }
    await waitJob(await api('StartInvoiceReportPdfDownloadJob',token,{templatePath:template.templatePath,withSeal:false},{invoiceId:users[0].invoice.id}),token);
    const before=await api('GetRuntimeMetrics',token);const exports=[];
    measure=true;const started=performance.now();
    await Promise.all(users.map(async(user,worker)=>{
      for(let n=0;n<50;n++) {
        if(n===10 && worker>=1 && worker<=5) {
          const row=approvals[worker-1];const approved=await api('ApproveExpenseRequest',user.token,{expectedVersion:row.versionNumber,note:'Capacity approval'},{id:row.id});assert.equal(approved.status,'Approved');
        } else if(n===25 && worker<2) {
          exports.push([await api('StartInvoiceReportPdfDownloadJob',user.token,{templatePath:template.templatePath,withSeal:false},{invoiceId:user.invoice.id}),user.token]);
        } else if(n%10===0) {
          const saved=await api('UpdateInvoice',user.token,{...user.invoice,contractNo:`capacity-${worker}-${n}`},{id:user.invoice.id});assert.equal(saved.success,true);user.invoice=saved.invoice;
        } else {
          switch(n%4) {
            case 0:{const page=await api('ListInvoices',user.token,undefined,{}, {pageNumber:1+n%5,pageSize:20,keyword:'Capacity'});assert.equal(page.totalCount,220);assert.equal(page.items.length,20);break;}
            case 1:assert.equal((await api('GetInvoice',user.token,undefined,{id:user.invoice.id})).id,user.invoice.id);break;
            case 2:await api('ListJobs',user.token,undefined,{}, {pageNumber:1,pageSize:20});break;
            default:await api('ListExpenseRequest',user.token,undefined,{}, {pageNumber:1,pageSize:20,mineOnly:false});
          }
        }
      }
    }));
    for(const [job,owner] of exports)await waitJob(job,owner);
    const durationMs=performance.now()-started;measure=false;
    for(let n=0;n<users.length;n++){const row=await api('GetInvoice',users[n].token,undefined,{id:users[n].invoice.id});assert.equal(row.contractNo,`capacity-${n}-40`);}
    let after=await api('GetRuntimeMetrics',token);
    for(let attempt=0;after.jobs.active>0 && attempt<50;attempt++){await delay(100);after=await api('GetRuntimeMetrics',token);}
    assert.equal(after.storage.capacity,size);assert.equal(after.storage.failed,false);assert.equal(after.jobs.active,0);
    let memory=null;
    if(process.platform==='win32') {
      const {stdout}=await promisify(execFile)('pwsh',['-NoProfile','-Command','Get-Process -Id $env:CAPACITY_PID | Select-Object WorkingSet64,PeakWorkingSet64 | ConvertTo-Json -Compress'],{windowsHide:true,timeout:10000,env:{...process.env,CAPACITY_PID:String(server.pid)}});
      memory=JSON.parse(stdout);
    }
    latencies.sort((a,b)=>a-b);const percentile=p=>latencies[Math.min(latencies.length-1,Math.ceil(latencies.length*p)-1)];
    const result={poolSize:size,clients:20,sessionRole:'Admin',invoices:220,approvals:5,exports:2,logicalOperations:latencies.length,durationMs,operationsPerSecond:latencies.length/(durationMs/1000),p95Ms:percentile(.95),p99Ms:percentile(.99),statuses,retried429:retried,memory,metricsBefore:before,metricsAfter:after};
    fs.writeFileSync(path.join(output,'result.json'),JSON.stringify(result,null,2));results.push(result);
    console.log(`Pool ${size}: ${result.logicalOperations} operations; ${result.operationsPerSecond.toFixed(1)}/s; P95 ${result.p95Ms.toFixed(1)} ms; P99 ${result.p99Ms.toFixed(1)} ms; 429 retries ${retried}`);
  } finally {await stopProcessTree(server,5000);log.end();}
}
for(const [size,key] of [[1,'CAPACITY_ONE'],[4,'CAPACITY_FOUR']])await phase(size,key);
fs.writeFileSync(path.join(root,'result.json'),JSON.stringify({platform:process.platform,configuration:'Debug',results},null,2));
console.log(`Native 20-session capacity evidence: ${root}`);

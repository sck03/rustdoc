import { readFileSync, writeFileSync, existsSync } from "node:fs";
import { randomBytes, createHash } from "node:crypto";
import path from "node:path";

const args=process.argv.slice(2);
const option=name=>{const index=args.indexOf(name);return index<0?undefined:args[index+1];};
if(!args.includes("--isolated-validation"))throw new Error("This check creates a test account and must only run against an isolated validation deployment.");
const origin=new URL(option("--url")??"http://127.0.0.1:5195");
if(!["127.0.0.1","localhost","[::1]"].includes(origin.hostname))throw new Error("Validation must target a loopback container port.");
const root=path.resolve(option("--runtime")??"deploy/rust-native/runtime");
const marker=JSON.parse(readFileSync(path.join(root,"native-runtime.json"),"utf8").replace(/^\uFEFF/u,""));
if(marker.purpose!=="exportdoc-rust-native-docker")throw new Error("Not a prepared native Docker validation directory.");
const statePath=path.join(root,"validation-session.json");
const resume=args.includes("--resume");
if(resume!==existsSync(statePath))throw new Error("Validation session state does not match the requested phase.");
const state=resume?JSON.parse(readFileSync(statePath,"utf8")):{password:`Native-${randomBytes(24).toString("hex")}`};
let access="";
async function request(route,options={},label="HTTP request"){
  const target=new URL(route,origin);
  if(target.origin!==origin.origin)throw new Error("Native validation refused an external response URL.");
  const response=await fetch(target,{...options,redirect:"error",headers:{...(access?{authorization:`Bearer ${access}`} : {}),...options.headers},signal:AbortSignal.timeout(35000)});
  if(!response.ok)throw new Error(`Native validation request failed: ${label} (${response.status})`);
  return response;
}
const index=await request("/");
if(!(await index.text()).includes('id="root"'))throw new Error("The existing React build is not served.");
const contract=await (await request("/openapi/v1.json")).json();
const routes=new Map();
for(const [route,methods]of Object.entries(contract.paths))for(const [method,operation]of Object.entries(methods))if(operation.operationId)routes.set(operation.operationId,{route,method:method.toUpperCase()});
async function operation(name,{parameters={},body,headers={}}={}){
  const endpoint=routes.get(name);if(!endpoint)throw new Error(`Missing generated operation ${name}`);
  const route=endpoint.route.replace(/\{([^}]+)\}/g,(_,key)=>encodeURIComponent(parameters[key]??""));
  return request(route,{method:endpoint.method,headers:{...(body===undefined?{}:{"content-type":"application/json"}),...headers},...(body===undefined?{}:{body:JSON.stringify(body)})},name);
}
const login=await (await operation("Login",{body:{username:"admin",password:state.password},headers:{"x-exportdocmanager-bootstrap-token":readFileSync(path.join(root,"bootstrap-token.txt"),"utf8")}})).json();
access=login.accessToken;
if(!access)throw new Error("Native login returned no session.");
if(!resume){
  const job=await (await operation("StartExcelTemplateDownloadJob")).json();
  state.jobId=job.jobId;
}
let job;
const deadline=Date.now()+120000;
while(Date.now()<deadline){
  job=await (await operation("GetJob",{parameters:{jobId:state.jobId}})).json();
  if(job.status==="Succeeded")break;
  if(["Failed","Canceled"].includes(job.status))throw new Error(`Native file task ${job.status}`);
  await new Promise(resolve=>setTimeout(resolve,250));
}
if(job?.status!=="Succeeded")throw new Error("Native file task timed out.");
const issued=await operation("CreateJobDownloadTicket",{parameters:{jobId:state.jobId}});
const cookie=issued.headers.get("set-cookie")?.split(";")[0];
const ticket=await issued.json();
if(!cookie)throw new Error("The download ticket is not bound to a browser session.");
const ticketUrl=new URL(ticket.downloadUrl,origin);
if(ticketUrl.origin!==origin.origin)throw new Error("A download ticket must use the same origin.");
const anonymous=await fetch(ticketUrl,{redirect:"error",signal:AbortSignal.timeout(10000)});
if(anonymous.status!==404)throw new Error("A copied download URL must not authorize a download.");
const download=await request(ticket.downloadUrl,{headers:{cookie}});
const bytes=Buffer.from(await download.arrayBuffer());
if(bytes.subarray(0,2).toString()!=="PK")throw new Error("Native Excel output is not a workbook.");
const digest=createHash("sha256").update(bytes).digest("hex");
if(resume&&digest!==state.digest)throw new Error("The saved task output changed across container restart.");
if(!resume){state.digest=digest;writeFileSync(statePath,JSON.stringify(state),{mode:0o600,flag:"wx"});}
await operation("Logout");
console.log(`Native Docker validation passed: React, Rust login, Excel, cookie-bound download${resume?", persisted output after restart":""}.`);

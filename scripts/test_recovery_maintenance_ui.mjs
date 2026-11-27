import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import net from "node:net";
import {randomBytes,randomUUID} from "node:crypto";
import {createRequire} from "node:module";
import {spawnProcessTree,stopProcessTree} from "./lib/child-process-tree.mjs";
import {cargoArtifactDirectory} from "./lib/cargo-paths.mjs";
import {locateChromeForTesting} from "./lib/chromium-executable.mjs";
const repo=path.resolve(import.meta.dirname,"..");
const connection=process.env.EXPORTDOC_TEST_MAINTENANCE_WEB_APP;
const maintenance=process.env.EXPORTDOC_TEST_MAINTENANCE_WEB_MAINTENANCE;
for(const value of [connection,maintenance]) assert(value&&/(?:^|\s)host=127\.0\.0\.1(?:\s|$)/u.test(value)&&/(?:^|\s)dbname=native_maintenance_web(?:\s|$)/u.test(value),"Use the isolated PostgreSQL test runner");
const root=path.join(repo,".codex-runtime/maintenance-web-tests",randomUUID());
const data=path.join(root,"Data");fs.mkdirSync(data,{recursive:true});
const token=randomBytes(32).toString("hex");
for(const [name,value] of [["app.txt",connection],["maintenance.txt",maintenance],["token.txt",token]]) fs.writeFileSync(path.join(root,name),value,{mode:0o600});
const listener=net.createServer();await new Promise(resolve=>listener.listen(0,"127.0.0.1",resolve));const port=listener.address().port;await new Promise(resolve=>listener.close(resolve));const url=`http://127.0.0.1:${port}`;
const env={...process.env,EXPORTDOCMANAGER_POSTGRES_CONNECTION_FILE:path.join(root,"app.txt"),EXPORTDOCMANAGER_POSTGRES_MAINTENANCE_CONNECTION_FILE:path.join(root,"maintenance.txt"),EXPORTDOCMANAGER_MAINTENANCE_TOKEN_FILE:path.join(root,"token.txt"),EXPORTDOCMANAGER_POSTGRES_OWNER:"native_owner"};
for(const key of ["EXPORTDOCMANAGER_POSTGRES_CONNECTION","EXPORTDOCMANAGER_POSTGRES_MAINTENANCE_CONNECTION","EXPORTDOCMANAGER_MAINTENANCE_TOKEN","EXPORTDOCMANAGER_FILE_STORE_ROOTS"])delete env[key];
const binary=path.join(cargoArtifactDirectory(repo),`export-doc-server${process.platform==="win32"?".exe":""}`);
const server=spawnProcessTree(binary,["--app-root",root,"--data-root",data,"--web-root",path.join(repo,"apps/export-doc-web/dist"),"--bind",`127.0.0.1:${port}`,"--maintenance-web"],{cwd:repo,env,windowsHide:true,stdio:["ignore","pipe","pipe"]});
let log="";server.stdout.on("data",v=>{log+=v;});server.stderr.on("data",v=>{log+=v;});
let browser;
try{
 const deadline=Date.now()+30000;let ready=false;
 while(Date.now()<deadline){try{const response=await fetch(url,{signal:AbortSignal.timeout(1000)});if(response.ok){ready=true;break;}}catch{}if(server.exitCode!==null)throw new Error(`Maintenance exited ${server.exitCode}`);await new Promise(r=>setTimeout(r,150));}assert(ready,"Independent maintenance started with an empty business database");
 assert.equal((await fetch(`${url}/maintenance-api/status`)).status,401);
 const require=createRequire(path.join(repo,"apps/export-doc-web/package.json"));const {chromium}=require("playwright");
 browser=await chromium.launchPersistentContext(path.join(root,"Chrome"),{executablePath:locateChromeForTesting(repo),headless:true,viewport:{width:1440,height:1000}});
 const page=await browser.newPage();const errors=[];page.on("pageerror",e=>errors.push(e.message));page.setDefaultTimeout(15000);
 await page.goto(url+"/#/maintenance");await page.getByLabel("部署端一次性凭证").fill(token);await page.getByRole("button",{name:"进入维护",exact:true}).click();await page.getByRole("button",{name:"解锁仓库",exact:true}).waitFor();
 const status=await page.evaluate(async()=>{const r=await fetch("/maintenance-api/status");return {status:r.status,body:await r.json()};});assert.equal(status.status,200);assert(Array.isArray(status.body.locations));assert(!JSON.stringify(status.body).includes("password="));
 const noCsrf=await page.evaluate(async()=>{const r=await fetch("/maintenance-api/cancel",{method:"POST",headers:{"content-type":"application/json"},body:"{}"});return r.status;});assert.equal(noCsrf,403);
 const cookies=await browser.cookies(url);const cookie=cookies.map(c=>`${c.name}=${c.value}`).join("; ");const cross=await fetch(`${url}/maintenance-api/cancel`,{method:"POST",headers:{cookie,origin:"https://untrusted.invalid","content-type":"application/json"},body:"{}"});assert.equal(cross.status,403);
 await page.screenshot({path:path.join(root,"maintenance-desktop.png"),fullPage:true});await page.setViewportSize({width:390,height:844});await page.screenshot({path:path.join(root,"maintenance-mobile.png"),fullPage:true});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>window.innerWidth),false);assert.deepEqual(errors,[]);
 fs.writeFileSync(path.join(root,"result.json"),JSON.stringify({checks:["empty database independent startup","React login","maintenance session status","missing CSRF rejected","foreign origin rejected","390px layout"],passed:true},null,2));console.log(`Independent recovery maintenance UI passed: ${root}`);
}catch(error){fs.writeFileSync(path.join(root,"server.log"),log);if(browser)await browser.pages().at(-1)?.screenshot({path:path.join(root,"failure.png"),fullPage:true}).catch(()=>{});throw error;}
finally{if(browser)await browser.close();await stopProcessTree(server);for(const name of ["app.txt","maintenance.txt","token.txt"])fs.rmSync(path.join(root,name),{force:true});}

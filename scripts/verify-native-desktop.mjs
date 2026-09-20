import { readFileSync, mkdirSync, writeFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),"..");
const args=process.argv.slice(2);
const option=name=>{const index=args.indexOf(name);return index<0?undefined:args[index+1];};
function execute(command,parameters) {
  const result=spawnSync(command,parameters,{cwd:root,encoding:"utf8",timeout:120000,maxBuffer:16*1024*1024,env:process.env});
  if(result.error||result.status!==0)throw new Error(`${command} failed: ${result.error?.message??result.stderr}`);
  return result.stdout;
}
const cargo=process.platform==="win32"?"cargo.exe":"cargo";
const rustc=process.platform==="win32"?"rustc.exe":"rustc";
const target=option("--target")??execute(rustc,["-vV"]).match(/^host: (.+)$/m)?.[1];
if(!target)throw new Error("Cannot determine the native Rust target.");
function dependencies(pkg) {
  const output=execute(cargo,["tree","--locked","-p",pkg,"--target",target,"--edges","normal","--prefix","none","--format","{p}"]);
  return [...new Set(output.split(/\r?\n/).map(line=>line.match(/^([^ ]+) v([^ ]+)/)?.slice(1)).filter(Boolean).map(([name,version])=>`${name}@${version}`))].sort();
}
const desktop=dependencies("export-doc-tauri");
const domain=dependencies("export-doc-domain");
function reject(graph,names,label) {
  const found=graph.filter(pkg=>names.includes(pkg.split("@")[0]));
  if(found.length)throw new Error(`${label} unexpectedly depends on ${found.join(", ")}`);
}
reject(desktop,["slint","i-slint-core","egui","eframe","postgres","tokio-postgres"],"SQLite desktop");
reject(domain,["tauri","slint","rfd","rusqlite","postgres","axum","reqwest","ureq","windows","winapi"],"Pure domain");
if(!desktop.some(pkg=>pkg.startsWith("rusqlite@")))throw new Error("The desktop must use the SQLite adapter.");
const manifest=readFileSync(path.join(root,"apps/export-doc-tauri/src-tauri/Cargo.toml"),"utf8");
const tauriVersion=manifest.match(/^tauri\s*=\s*\{[^\n]*version\s*=\s*"=([^"]+)"/m)?.[1];
if(!tauriVersion?.startsWith("2.")||!desktop.includes(`tauri@${tauriVersion}`))throw new Error("Tauri must use an exact locked stable 2.x version.");
const output=path.resolve(option("--output")??path.join(root,"artifacts/native-validation/dependencies"));
mkdirSync(output,{recursive:true});
writeFileSync(path.join(output,`${target}.json`),JSON.stringify({target,tauriVersion,webView:true,desktop,domain},null,2)+"\n");
console.log(`Native dependency boundaries passed: ${target}; Tauri ${tauriVersion}; ${desktop.length} runtime packages; no retired UI or server database in desktop.`);

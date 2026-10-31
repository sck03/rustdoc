import { appendFileSync, readFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';

export function validationPlan(files, mode = 'quick', force = false) {
  if (!['quick','full'].includes(mode)) throw new Error('Validation mode must be quick or full.');
  const changed=files.map(file=>file.replaceAll('\\','/'));
  const all=force||mode==='full'||changed.some(file=>['.github/workflows/rust-native-validation.yml','.github/workflows/native-full-validation.yml'].includes(file));
  const shared=/^(?:Cargo\.(?:toml|lock)|version\.json|rust-toolchain\.toml|\.cargo\/|crates\/|apps\/export-doc-server\/|tools\/excel-analyzer-rs\/|third_party\/|eng\/native-runtime-packages\.json|Resources\/|Templates\/|OcrModels\/)/u;
  const desktop=/^apps\/export-doc-tauri\//u;
  const rust=all||changed.some(file=>shared.test(file)||desktop.test(file)||/^apps\/exportdoc-ocr-rs\//u.test(file));
  const web=all||changed.some(file=>/^(?:apps\/export-doc-web\/|crates\/export-doc-contracts\/|Resources\/Fonts\/|Templates\/|scripts\/(?:test_[^/]+_ui\.mjs|test_permission_scheme_model\.mjs|test_frontend_|lib\/(?:chromium|web-runtime|child-process)|provision-report-fonts\.mjs))/u.test(file));
  const office=mode==='full'||changed.some(file=>/^crates\/(?:export-doc-engine\/src\/engine\/(?:oa\/|office|personnel|units|auth|account_permissions)|export-doc-domain\/src\/(?:permissions|units|approval)|export-doc-contracts\/src\/openapi\/(?:office|units|permission_))/u.test(file));
  return {rust,web,desktop:all||changed.some(file=>desktop.test(file)),desktopCheck:all||changed.some(file=>shared.test(file)||desktop.test(file)),office,full:mode==='full'};
}
function git(args) {
  const result=spawnSync('git',args,{encoding:'utf8',windowsHide:true});
  if(result.status!==0) throw new Error(`git ${args[0]} failed: ${result.stderr}`);
  return result.stdout;
}
if (process.argv[1] && import.meta.url===pathToFileURL(process.argv[1]).href) {
  const event=JSON.parse(readFileSync(process.env.GITHUB_EVENT_PATH,'utf8'));
  const mode=event.inputs?.validation_mode??'quick';
  const head=process.env.GITHUB_SHA;
  if(!/^[a-f0-9]{40}$/u.test(head??'')) throw new Error('A valid workflow commit is required.');
  const manual=process.env.GITHUB_EVENT_NAME==='workflow_dispatch';
  const base=event.pull_request?.base?.sha??event.before;
  let changed=[];
  const first=!base||/^0{40}$/u.test(base);
  if(!manual&&!first) {
    if(!/^[a-f0-9]{40}$/u.test(base)) throw new Error('A valid comparison commit is required.');
    if(spawnSync('git',['cat-file','-e',`${base}^{commit}`],{windowsHide:true}).status!==0) git(['fetch','--no-tags','--depth=1','origin',base]);
    changed=git(['diff','--name-only','-z',base,head]).split('\0').filter(Boolean);
  }
  const plan=validationPlan(changed,mode,manual||first);
  appendFileSync(process.env.GITHUB_OUTPUT,Object.entries(plan).map(([key,value])=>`${key}=${value}\n`).join(''));
  process.stdout.write(`${JSON.stringify({changed,plan},null,2)}\n`);
}

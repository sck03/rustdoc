import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { captureScreenshot } from './web-runtime-browser-session.mjs';
import { waitFor } from './web-runtime-smoke-common.mjs';
import { createSingleWindowTestDirectories } from './single-window-test-directories.mjs';

export async function verifyDesktopClientDirectories({ run, cdp, output, client }) {
  let detected;
  try { detected = await run("window.__TAURI_INTERNALS__.invoke('detect_single_window_client_directories')"); }
  catch (error) { if (process.env.EXPORTDOCMANAGER_REQUIRE_SW_CLIENT === '1') throw error; }
  await run("location.hash='#/single-window/operation-center'; true");
  const field = label => `[...document.querySelectorAll('.single-window-station-profile label, .single-window-station-profile .path-field')].find(e=>e.querySelector('.form-field-label > span, .path-field-label')?.textContent.trim()===${JSON.stringify(label)})?.querySelector('input')`;
  const button = "[...document.querySelectorAll('.single-window-station-profile button')].find(e=>e.textContent.trim()==='保存档案')";
  await waitFor(() => run(`Boolean(${field('档案名称')})`),30000,'Station profile was not displayed');
  const fill = async (label,text) => {
    await run(`${field(label)}.focus(); ${field(label)}.select(); true`);
    await cdp.send('Input.insertText',{text});
    await waitFor(() => run(`${field(label)}.value===${JSON.stringify(text)}`),10000,label);
  };
  const entries = [['customsCooDirectories','COO'],['agentConsignmentDirectories','ACD']];
  const boxes = [['outBox','发送目录 OutBox'],['sentBox','已发送目录 SentBox'],['inBox','回执目录 InBox'],['failBox','失败目录 FailBox']];
  if (detected) {
    assert.deepEqual(Object.keys(detected).sort(),entries.map(([key])=>key).sort());
    for(const [key,label] of entries) {
      assert(detected[key], `${label} task was not detected`);
      assert.deepEqual(Object.keys(detected[key]).sort(),boxes.map(([key])=>key).sort());
      for(const [box,title] of boxes) await waitFor(() => run(`${field(`${label} ${title}`)}?.value===${JSON.stringify(detected[key][box])}`),30000,'Automatic directory population failed');
    }
    await captureScreenshot(cdp,path.join(output,'Full-single-window-client-detection.png'),{captureBeyondViewport:true});
  }
  await fill('档案名称','隔离桌面测试'); await fill('公司抬头','DEFAULT'); await fill('操作卡标识','TEST-ONLY');
  const isolated = {customsCooDirectories:createSingleWindowTestDirectories(path.join(output,'coo')),agentConsignmentDirectories:createSingleWindowTestDirectories(path.join(output,'acd'))};
  for(const [key,label] of entries) for(const [box,title] of boxes) await fill(`${label} ${title}`,isolated[key][box]);
  await waitFor(() => run(`Boolean(${button} && !${button}.disabled)`),10000,'Profile save not ready');
  await run(`${button}.click(); true`);
  const profile = await waitFor(async()=>{
    const profile=(await client.getSingleWindowClientProfiles()).profiles[0];
    return profile?.customsCooDirectories.outBox===isolated.customsCooDirectories.outBox ? profile : null;
  },30000,'Manually corrected directories were not saved');
  for(const [key] of entries) assert.deepEqual(profile[key],isolated[key]);
  // Avoid screenshotting the assignment credential displayed after save.
  fs.writeFileSync(path.join(output,'client-directory-results.json'),JSON.stringify({automaticDetection:Boolean(detected),manualCorrection:true,savedEightDirectories:true},null,2));
  return {profile,detected};
}

export function findOfficialClientSentFiles(sentBox, payloads) {
  const entries=fs.readdirSync(sentBox,{recursive:true,withFileTypes:true}).filter(entry=>entry.isFile());
  const matches=payloads.map(payload=>{
    const name=path.parse(path.basename(payload.relativePath));
    // The official client appends a timestamp when archiving; verify the content too.
    const entry=entries.find(entry=>{
      if (entry.name!==name.base && !(entry.name.startsWith(`${name.name}_`) && entry.name.endsWith(name.ext))) return false;
      const archived=path.join(entry.parentPath,entry.name);
      return fs.statSync(archived).size===payload.sizeBytes &&
        createHash('sha256').update(fs.readFileSync(archived)).digest('hex')===payload.sha256.toLowerCase();
    });
    return entry ? path.join(entry.parentPath,entry.name) : null;
  });
  return matches.length && matches.every(Boolean) ? matches : null;
}

// Opt-in only: this sends the anonymous declaration made by the desktop test to
// the already configured official client. Never used by ordinary CI runs.
export async function verifyOfficialClientTransport({client,profile,detected,sourceId,output}) {
  assert(detected?.customsCooDirectories && detected?.agentConsignmentDirectories);
  const body = Object.fromEntries(['profileKey','profileName','companyScope','cardIdentifier','customsCooDirectories','agentConsignmentDirectories','canSubmitCustomsCoo','canSubmitAgentConsignment'].map(key=>[key,profile[key]]));
  await client.saveSingleWindowClientProfile({body:{...body,...detected}});
  try {
    const packagePath=path.join(output,'official-client-anonymous-acd.swpkg');
    const handoff=await client.saveAgentConsignmentSubmitPackageToPath({sourceId,body:{packagePath,stationAssignmentCode:profile.stationAssignmentCode}});
    await client.uploadSingleWindowSubmitPackage({fileName:path.basename(packagePath),body:new Blob([fs.readFileSync(packagePath)])});
    const dispatched=await client.dispatchSingleWindowBatchToClient({body:{batchId:handoff.trackingBatchId}});
    assert.equal(path.resolve(dispatched.targetDirectory),path.resolve(detected.agentConsignmentDirectories.outBox));
    const sent=await waitFor(()=>findOfficialClientSentFiles(detected.agentConsignmentDirectories.sentBox,handoff.manifest.payloadFiles),
      90000,'Official client did not archive the anonymous payload to SentBox');
    const receipts=await client.collectSingleWindowClientReceipts({body:{batchId:handoff.trackingBatchId}});
    fs.writeFileSync(path.join(output,'official-client-transport.json'),JSON.stringify({batchReference:handoff.manifest.batchReference,sentFiles:sent,receiptFileCount:receipts.receiptFiles.length,businessAcceptanceConfirmed:false},null,2));
  } finally { await client.saveSingleWindowClientProfile({body}); }
}

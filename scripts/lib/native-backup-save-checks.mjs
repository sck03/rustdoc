import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { delay } from './chromium-cdp.mjs';
import { captureScreenshot } from './web-runtime-browser-session.mjs';
import { withNativeFilePicker } from './native-file-picker.mjs';

export async function verifyDesktopBackupSave({run,request,token,cdp,output,dataRoot}) {
  const call=async(id,body,parameters={})=>{
    const response=await request(id,token,body,parameters);
    assert.equal(response.status,200,`${id}: ${JSON.stringify(response.body)}`);
    return response.body;
  };
  const wait=async(check)=>{const deadline=Date.now()+60000;while(Date.now()<deadline){if(await check())return;await delay(100);}throw Error('Backup save did not finish: '+await run("location.hash+'\\n'+document.body.innerText"));};
  const status=await call('GetBackupPlan');
  await call('SaveBackupPlan',{expectedRevision:status.revision,expectedSettingsRevision:status.settingsRevision,plan:status.plan,webDav:status.webDav,webDavPassword:null,s3SecretAccessKey:null,s3SessionToken:null,encryptionPassword:'Desktop-Test-Backup-2026'});
  const job=await call('RunBackupPlan');
  await wait(async()=>{const current=await call('GetJob',undefined,{jobId:job.jobId});assert(!['Failed','Canceled'].includes(current.status),JSON.stringify(current));return current.status==='Succeeded';});
  const archive=(await call('ListRecoveryArchives')).items[0];
  const savedStatus=await call('GetBackupPlan');
  const repository=path.join(dataRoot,'Backups/RecoveryRepositories',`recovery-${savedStatus.repositoryId}`);
  const destination=path.join(output,'saved-desktop.edmpoint');
  // Supply the user's selection at the native-dialog boundary; keep the real React,
  // generated client, desktop header, HTTP adapter, worker and filesystem publication.
  await run("location.hash='#/system/backup?section=points';true");
  await withNativeFilePicker({run,command:'select_save_file_path',destination},async()=>{
    await wait(()=>run("[...document.querySelectorAll('button')].some(button=>button.textContent.trim()==='下载'&&!button.disabled)"));
    await run("[...document.querySelectorAll('button')].find(button=>button.textContent.trim()==='下载'&&!button.disabled).click();true");
    await wait(()=>fs.existsSync(destination));
    await wait(()=>run("document.body.innerText.includes('便携恢复点已导出')"));
    const bytes=fs.readFileSync(destination);
    const magic=Buffer.from('EDM-RECOVERY-POINT-1\n');
    assert.deepEqual(bytes.subarray(0,magic.length),magic);
    let offset=magic.length,lastName='',frames=0;
    while(true) {
      const length=bytes.readUInt16LE(offset);offset+=2;if(!length)break;
      const name=bytes.subarray(offset,offset+length).toString();offset+=length;
      assert.match(name,/^(repository\.json|(?:blob|manifest|point)-[a-zA-Z0-9.-]+)$/u);
      const size=bytes.readUInt32LE(offset);offset+=4;
      assert.deepEqual(bytes.subarray(offset,offset+size),fs.readFileSync(path.join(repository,name)),name);
      offset+=size;lastName=name;frames++;
    }
    assert.equal(offset,bytes.length,'portable export has no truncation or trailing bytes');
    assert.equal(lastName,`point-${archive.id}.commit`);assert(frames>=3);
    const sha256=crypto.createHash('sha256').update(bytes).digest('hex');
    await captureScreenshot(cdp,path.join(output,'Full-backup-saved.png'));
    fs.writeFileSync(path.join(output,'backup-save-result.json'),JSON.stringify({id:archive.id,size:bytes.length,sha256,saved:true},null,2));
  });
}

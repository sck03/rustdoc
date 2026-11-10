import assert from 'node:assert/strict';
import { delay } from './chromium-cdp.mjs';

// The same assertions run against Chromium/HTTP and the packaged WebView2 UI.
export async function verifySharedControlLayout({run,capture,station=false}) {
  async function wait(expression){
    const deadline=Date.now()+20000;
    while(Date.now()<deadline){if(await run(expression))return;await delay(100);}
    assert(await run(expression),expression);
  }
  async function go(route,expression){await run(`location.hash=${JSON.stringify('#'+route)};true`);await wait(expression);}
  const button=name=>`Array.from(document.querySelectorAll('button')).find(b=>b.textContent.trim()===${JSON.stringify(name)})`;
  await go('/settings',`!!${button('邮件设置')}`);
  for(const [name,label,file] of [['邮件设置','单据邮件正文','email-body'],['AI 服务','AI 系统提示词','ai-prompt']]){
    await run(`${button(name)}.click();true`);
    const selector=JSON.stringify(`textarea[aria-label="${label}"]`);
    await wait(`!!document.querySelector(${selector})`);
    const geometry=await run(`(()=>{const e=document.querySelector(${selector}),r=e.getBoundingClientRect();e.scrollIntoView({block:'center'});return {height:r.height,rows:e.rows,resize:getComputedStyle(e).resize,width:r.width};})()`);
    assert(geometry.height>=150 && geometry.rows>=7 && geometry.width>=200,`${label}: ${JSON.stringify(geometry)}`);
    assert.equal(geometry.resize,'vertical');
    if(file==='email-body') assert(await run(`Math.abs(document.querySelector('input[aria-label="单据邮件主题"]').getBoundingClientRect().top-document.querySelector(${selector}).getBoundingClientRect().top)<2`),'subject and body start on the same row');
    await capture(file);
  }
  await go('/worklist',`!!document.querySelector('[data-ui="pagination"] .page-size-control select')`);
  for(const [position,name] of [[0,'worklist-top'],[Number.MAX_SAFE_INTEGER,'worklist-bottom']]){
    await run(`window.scrollTo(0,${position});new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))`);
    assert(await run(`Math.abs(document.querySelector('.workspace-header').getBoundingClientRect().top)<2`),'workspace header stays at the viewport top while scrolling');
    await capture(name);
  }
  const aligned=await run(`(()=>{const label=document.querySelector('.page-size-control'),children=Array.from(label.children).map(e=>{const r=e.getBoundingClientRect();return {x:r.x,y:r.y+r.height/2}});label.scrollIntoView({block:'center'});return children;})()`);
  assert(aligned[0].x<aligned[1].x && aligned[1].x<aligned[2].x,'page size text and select stay in one row');
  assert(Math.max(...aligned.map(r=>r.y))-Math.min(...aligned.map(r=>r.y))<3,'page size vertical alignment');
  await capture('worklist-pagination');
  await go('/office/announcements',`!!${button('新建公告')}`);
  await run(`${button('新建公告')}.click();true`);
  await wait(`!!document.querySelector('input[type="file"]')`);
  const file=await run(`(()=>{const e=document.querySelector('[data-ui="file-input"]'),button=e.querySelector('[data-file-button]'),s=getComputedStyle(button);e.scrollIntoView({block:'center'});return {height:button.getBoundingClientRect().height,padding:parseFloat(s.paddingLeft),border:s.borderStyle,label:button.textContent,empty:e.querySelector('[data-file-selection]').textContent};})()`);
  assert(file.height>=28 && file.padding>=12 && file.border==='solid',JSON.stringify(file));
  assert.equal(file.label,'选择文件');assert.equal(file.empty,'未选择文件');
  await capture('attachment-file-button');
  if(station){
    await go('/single-window/operation-center',`!!document.querySelector('select[aria-label="选择操作档案"]')`);
    const fields=await run(`(()=>{const select=document.querySelector('select[aria-label="选择操作档案"]'),input=document.querySelector('input[aria-label="档案名称"]');select.scrollIntoView({block:'center'});return [select,input].map(e=>{const r=e.getBoundingClientRect();return {top:r.top,height:r.height,field:e.closest('[data-ui="field"]')!==null}});})()`);
    assert(fields.every(e=>e.field),'both controls use the shared field');
    assert(Math.abs(fields[0].top-fields[1].top)<2 && Math.abs(fields[0].height-fields[1].height)<2,JSON.stringify(fields));
    await capture('station-profile-alignment');
  }
}

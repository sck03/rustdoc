import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { captureScreenshot } from './web-runtime-browser-session.mjs';
import { delay } from './chromium-cdp.mjs';

export async function verifyDesktopMaintenance({ run, request, token, businessDate, cdp, output }) {
  const wait = async condition => {
    const end = Date.now() + 30000;
    while (Date.now() < end) { if (await condition()) return; await delay(100); }
    throw new Error('Desktop maintenance state did not settle.');
  };
  const call = async (operation, body, parameters = {}, expected = 200) => {
    const result = await request(operation, token, body, parameters);
    assert.equal(result.status, expected, `${operation}: ${JSON.stringify(result.body)}`);
    return result.body;
  };
  const click = async label => {
    await wait(() => run(`Boolean([...document.querySelectorAll('button')].find(node=>!node.disabled&&node.getClientRects().length&&node.textContent.trim()===${JSON.stringify(label)}))`));
    await run(`(()=>{const node=[...document.querySelectorAll('button')].find(node=>!node.disabled&&node.getClientRects().length&&node.textContent.trim()===${JSON.stringify(label)});node.click();})()`);
  };
  const file = path.join(output, 'annual-maintenance.csv');
  const annual = async (csv, mode) => {
    fs.writeFileSync(file, csv);
    const preview = await call('PreviewHsCodesImportFromPath', { filePath:file, mode, sourceName:'桌面年度税则验收', effectiveYear:Number(businessDate.slice(0,4)) });
    await call('CommitHsCodesImport', { token:preview.token });
  };
  await annual('HS编码,商品名称\n6309000000,维护甲\n6309000001,维护乙\n', 'Incremental');
  await annual('HS编码,商品名称\n6309000000,维护甲\n', 'CompleteSnapshot');
  const getCode = code => call('GetHsCode', undefined, { code });
  const a = await getCode('6309000000'), b = await getCode('6309000001');
  assert.equal(b.status, 'SuspectedObsolete');
  await call('UpdateHsCode', { ...a, notes:'其他人在预检后重新核对' }, { code:a.code });
  await call('DeleteHsCodesBatch', { ids:[a.id,b.id], expectedVersions:[a,b].map(row=>({id:row.id,rowVersion:row.rowVersion})) }, {}, 409);
  assert.equal((await getCode(a.code)).id, a.id);
  const review = await call('PreviewHsCodesObsolete');
  assert(review.items.some(row=>row.id===b.id));
  await run("location.hash='#/master-data/hs-codes';true");
  await wait(() => run(`Boolean(document.querySelector('input[type=checkbox][aria-label*="${b.code}"]'))`));
  await run(`document.querySelector('input[type=checkbox][aria-label*="${b.code}"]').click();true`);
  await click('确认作废');
  await wait(() => run("Boolean(document.querySelector('textarea[aria-label=确认作废原因]'))"));
  assert.equal(await run("document.querySelector('.confirmation-dialog-confirm').disabled"), true);
  await run("document.querySelector('textarea[aria-label=确认作废原因]').focus()");
  await cdp.send('Input.insertText', { text:'核对官方年度公告后确认作废' });
  await click('确认 1 条作废');
  await wait(async () => (await getCode(b.code)).status === 'Obsolete');
  const obsolete = await getCode(b.code);
  assert.equal(obsolete.obsoleteReason, '核对官方年度公告后确认作废');
  assert.equal((await call('PreviewHsCodeCleanup')).eligibleCount, 0);
  await captureScreenshot(cdp, path.join(output, 'Full-hs-maintenance.png'));

  let template = await call('CreateUserReportTemplate', { reportType:'ExportDocument', name:'桌面维护草稿' }, {}, 201);
  await run(`location.hash='#/reports/templates?reportType=ExportDocument&userTemplateId=${template.id}';true`);
  await wait(() => run("Boolean(document.querySelector('[data-v3-page-canvas]'))"));
  const before = new Set(await run("[...document.querySelectorAll('[data-v3-element-id]')].map(node=>node.dataset.v3ElementId)"));
  await run("document.querySelector('.report-designer-v3-toolbar button[aria-label=文本]').click();true");
  let element;
  await wait(async () => {
    element = (await run("[...document.querySelectorAll('[data-v3-element-id]')].map(node=>node.dataset.v3ElementId)")).find(id=>!before.has(id));
    return Boolean(element);
  });
  await run(`document.querySelector('[data-v3-element-id="${element}"]').dispatchEvent(new MouseEvent('dblclick',{bubbles:true}));true`);
  await wait(() => run("Boolean(document.querySelector('textarea[aria-label=在画布编辑文字]'))"));
  await run("document.querySelector('textarea[aria-label=在画布编辑文字]').focus()");
  await cdp.send('Input.dispatchKeyEvent', { type:'keyDown', key:'a', code:'KeyA', modifiers:2, windowsVirtualKeyCode:65 });
  await cdp.send('Input.dispatchKeyEvent', { type:'keyUp', key:'a', code:'KeyA', modifiers:2, windowsVirtualKeyCode:65 });
  await cdp.send('Input.insertText', { text:'桌面未确认文字保存回读' });
  await wait(() => run("[...document.querySelectorAll('.report-template-layout button[type=submit]')].some(node=>!node.disabled)"));
  await run("[...document.querySelectorAll('.report-template-layout button[type=submit]')].find(node=>!node.disabled).click();true");
  await wait(async () => {
    template = await call('GetUserReportTemplate', undefined, {id:template.id});
    return template.versionNumber === 2 && template.contentHtml.includes('桌面未确认文字保存回读');
  });
  await captureScreenshot(cdp, path.join(output, 'Full-pending-text-saved.png'));
  for (let version=3;version<=26;version++) {
    template = await call('SaveUserReportTemplateDraft', {reportType:'ExportDocument',name:`桌面历史维护 ${version}`,contentHtml:template.contentHtml,expectedVersion:template.versionNumber}, {id:template.id});
  }
  const usage = await call('GetUserReportTemplateHistoryRetention', undefined, {id:template.id});
  assert.equal(usage.totalCount, 26); assert.equal(usage.eligibleCount, 6);
  const plan = await call('PreviewUserReportTemplateHistoryCleanup', {retainLatest:20}, {id:template.id});
  assert.equal(plan.items.length, 6);
  await call('CommitUserReportTemplateHistoryCleanup', {token:plan.token,reason:'桌面验收保留最近20版'}, {id:template.id});
  await call('CommitUserReportTemplateHistoryCleanup', {token:plan.token,reason:'重复提交应拒绝'}, {id:template.id}, 409);
  const retained = await call('GetUserReportTemplateHistoryRetention', undefined, {id:template.id});
  assert.equal(retained.totalCount, 20);
  const current = await call('GetUserReportTemplate', undefined, {id:template.id});
  assert.equal(current.contentHtml, template.contentHtml); assert.equal(current.versionNumber, 26);
  await run("location.hash='#/dashboard';true");
  fs.writeFileSync(path.join(output,'maintenance-result.json'),JSON.stringify({hsCode:b.code,status:obsolete.status,cleanupEligible:0,templateId:template.id,version:26,retainedHistory:20,pendingTextSaved:true},null,2));
}

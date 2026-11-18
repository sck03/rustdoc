import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { captureScreenshot } from './web-runtime-browser-session.mjs';
import { waitFor } from './web-runtime-smoke-common.mjs';

export async function verifyDesktopSalesConsistency({ run, request, token, cdp, output }) {
  const wait = expression => waitFor(() => run(`Boolean(${expression})`), 20000, expression);
  const button = name => `[...document.querySelectorAll('button')].find(e=>e.getClientRects().length&&e.textContent.trim()===${JSON.stringify(name)})`;
  const customer = await request('CreateCrmCustomer', token, { id: 0, name: '商机桌面验收客户' });
  assert.equal(customer.status, 201, JSON.stringify(customer.body));
  const created = await request('CreateSalesOpportunity', token, { id: 0, crmCustomerId: customer.body.id, title: '桌面商机初稿',
    quotationNo: 'DESKTOP-SALES', estimatedAmount: 125.75, currency: 'USD', probabilityPercent: 20, expectedCloseDate: '2026-12-15' });
  assert.equal(created.status, 201, JSON.stringify(created.body));
  const id = created.body.id;
  await run(`location.hash=${JSON.stringify(`#/crm/opportunities?opportunityId=${id}&view=editor`)}; true`);
  await wait("document.querySelector('input[name=title]')?.value==='桌面商机初稿'");
  await run("document.querySelector('input[name=title]').focus();document.querySelector('input[name=title]').select();true");
  await cdp.send('Input.insertText', { text: '桌面中文商机修订' });
  await run(`(()=>{const save=${button('保存商机')};save.click();save.click();return true;})()`);
  await wait("document.body.innerText.includes('商机已保存。')");
  const saved = await request('GetSalesOpportunity', token, undefined, { id });
  assert.equal(saved.status, 200); assert.equal(saved.body.title, '桌面中文商机修订');
  assert.equal(saved.body.versionNumber, 2); assert.equal(saved.body.expectedCloseDate, '2026-12-15');
  await run(`${button('报价与阶段历史')}.click();true`);
  await wait("document.querySelector('[aria-label=\"报价与阶段历史\"]')?.textContent.includes('V2')");
  await captureScreenshot(cdp, path.join(output, 'Full-sales-history.png'));
  await run(`${button('商机资料')}.click();true`); await wait(button('归档商机'));
  await run(`${button('归档商机')}.click();true`); await wait("document.querySelector('[role=dialog]')");
  await run("[...document.querySelectorAll('[role=dialog] button')].find(e=>e.textContent.trim()==='确认').click();true");
  await wait("document.body.innerText.includes('商机已归档')");
  const includeArchived = "[...document.querySelectorAll('label')].find(e=>e.textContent.includes('含归档'))?.querySelector('input')";
  await wait(includeArchived); await run(`${includeArchived}.click();true`); await wait(button('查看历史'));
  await run(`${button('查看历史')}.click();true`); await wait("document.querySelector('[aria-label=\"报价与阶段历史\"]')?.textContent.includes('归档')");
  const history = await request('ListSalesOpportunityHistory', token, undefined, { id });
  assert.equal(history.status, 200); assert.equal(history.body.items[0].changeType, '归档'); assert.equal(history.body.items.length, 3);
  await run("location.hash='#/system/maintenance?section=diagnostics';true");
  await wait("[...document.querySelectorAll('summary')].some(e=>e.textContent==='资源使用')");
  await wait("document.body.innerText.includes('4/4 核心路径正常')");
  assert.equal(await run("document.querySelectorAll('.runtime-path-row').length"), 4);
  assert.equal(await run("document.querySelector('[aria-label=\"SQLite\"] .runtime-path-requirement')?.textContent"), '核心必需');
  assert.equal(await run("document.querySelectorAll('.runtime-path-group').length"), 2);
  await run("[...document.querySelectorAll('summary')].find(e=>e.textContent==='资源使用').click();true");
  await wait("document.querySelector('[aria-label=\"运行资源指标\"]')?.textContent.includes('JSON 缓冲预算')");
  const metrics = await request('GetRuntimeMetrics', token);
  assert.equal(metrics.status, 200, JSON.stringify(metrics.body)); assert.equal(metrics.body.http.responseMemory.capacityBytes, 134217728);
  await captureScreenshot(cdp, path.join(output, 'Full-runtime-metrics.png'));
  await run("[...document.querySelectorAll('summary')].find(e=>e.textContent==='资源使用').click();document.querySelector('.runtime-path-groups').scrollIntoView({block:'end'});true");
  await captureScreenshot(cdp, path.join(output, 'Full-runtime-paths.png'));
  fs.writeFileSync(path.join(output, 'sales-consistency-result.json'), JSON.stringify({ runtime: 'Tauri+React+Rust+SQLite', opportunityId: id,
    savedVersion: saved.body.versionNumber, historyVersions: history.body.items.length, runtimeDirectories: 4, databaseRequirement: 'required',
    jsonBudgetBytes: metrics.body.http.responseMemory.capacityBytes }, null, 2));
}

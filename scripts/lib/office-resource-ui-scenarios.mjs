import assert from 'node:assert/strict';
import path from 'node:path';

// Runs against the same real React/HTTP host as the six OA request workflows.
export async function exerciseOfficeResources({ url, page, output, run, wait, click, fill, navigate, captureScreenshot }) {
  const finish = async label => {
    await run("document.querySelector('[role=dialog] button[type=submit]').click()");
    await wait("!document.querySelector('[role=dialog]')", `${label} failed`);
  };
  const employee = async () => {
    await fill('登记人员', 'OA-001');
    await wait("document.querySelector('[role=option]')?.textContent.includes('OA-001')", 'Resource employee search failed');
    await run("document.querySelector('[role=option]').click()");
  };
  const action = async label => { await click(label); await finish(label); };
  await navigate(`${url}/#/office/meeting-rooms`);
  await wait("document.querySelector('[aria-label=会议室预约工作区]')", 'Rooms missing');
  await click('添加会议室'); await fill('会议室名称', '资源流程验收会议室'); await fill('位置', '测试三层'); await finish('Create room');
  await wait("document.querySelector('.office-resource-card')", 'Room save missing');
  await click('查看日程与预约'); await employee(); await fill('会议主题', '资源流程验收会议'); await finish('Book room');
  await click('预约与钥匙交接记录');
  await wait("document.querySelector('.office-request-card')?.textContent.includes('资源流程验收会议')", 'Full admin lost new booking');
  assert.equal(await run("document.querySelector('.office-filter select').value"), '');
  await action('发放钥匙'); await wait("document.querySelector('.office-badge')?.textContent==='使用中'", 'Room not in use');
  await action('归还钥匙'); await wait("document.querySelector('.office-badge')?.textContent==='已完成'", 'Room not completed');
  await captureScreenshot(page, path.join(output, 'resource-meeting-completed.png'));

  await navigate(`${url}/#/office/supplies`);
  await wait("document.querySelector('[aria-label=物品领用工作区]')", 'Supplies missing');
  await click('添加物品'); await fill('物品名称', '资源流程验收设备');
  await run("document.querySelector('input[name=isReturnable]').click()"); await finish('Create supply');
  await wait("document.querySelector('.office-resource-card')", 'Supply save missing');
  await click('补充库存'); await fill('本次补充数量（件）', '5'); await fill('入库说明／采购单号', '匿名测试入库'); await finish('Restock');
  await click('登记借用'); await employee(); await fill('申请数量（件）', '3'); await fill('领用用途', '匿名测试借用'); await finish('Request supply');
  await navigate(`${url}/#/office/approvals`);
  await wait("[...document.querySelectorAll('.office-resource-card li')].some(n=>n.textContent.includes('资源流程验收设备'))", 'Resource request missing from hub');
  await captureScreenshot(page, path.join(output, 'resource-hub-pending.png'));
  await run("[...document.querySelectorAll('.office-resource-card li a')].find(n=>n.textContent.includes('资源流程验收设备')).click()");
  await wait("document.querySelector('.office-request-card')?.textContent.includes('资源流程验收设备')", 'Hub did not open exact request');
  await action('确认发放'); await click('登记归还');
  await run("(() => {const n=document.querySelector('input[name=quantity]');Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(n,'1');n.dispatchEvent(new Event('input',{bubbles:true}));})()");
  await finish('Partial return'); await wait("document.querySelector('.office-badge')?.textContent==='部分归还'", 'Partial return missing');
  await action('登记归还'); await wait("document.querySelector('.office-badge')?.textContent==='已归还'", 'Return not completed');
  await captureScreenshot(page, path.join(output, 'resource-supply-returned.png'));
  await click('物品与登记');
  // The directory can render cached stock while its invalidated query reloads.
  await wait("document.querySelector('.office-stock-number') && document.querySelector('[aria-label=刷新办公物品]')?.disabled===false", 'Stock refresh did not finish');
  assert.equal(await run("document.querySelector('.office-stock-number strong').textContent"), '5');
  assert.deepEqual(await run('window.__oaErrors'), []);
}

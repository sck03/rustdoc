import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { captureScreenshot } from './web-runtime-browser-session.mjs';
import { waitFor } from './web-runtime-smoke-common.mjs';

export async function verifyDesktopBusinessAttachments({ run, request, token, invoice, cdp, output }) {
  const workbook = path.join(output, 'desktop-booking.xlsx');
  const bytes = fs.readFileSync(workbook);
  const sha256 = createHash('sha256').update(bytes).digest('hex');
  const button = (label, selector = 'button') => `[...document.querySelectorAll(${JSON.stringify(selector)})].find(node=>node.getClientRects().length&&node.textContent.trim()===${JSON.stringify(label)})`;
  const ready = expression => waitFor(() => run(`Boolean(${expression})`), 30000, `Attachment UI did not become ready: ${expression}`);
  async function click(label, selector) {
    const expression = button(label, selector);
    await ready(`${expression} && !${expression}.disabled`);
    await run(`${expression}.click(); true`);
  }
  async function input(selector, value) {
    await run(`(()=>{const node=document.querySelector(${JSON.stringify(selector)});Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(node,${JSON.stringify(value)});node.dispatchEvent(new Event('input',{bubbles:true}));node.dispatchEvent(new Event('change',{bubbles:true}));})()`);
  }
  async function selectFiles(files = [workbook]) {
    await ready("document.querySelector('input[type=file]')");
    const { root } = await cdp.send('DOM.getDocument');
    const { nodeId } = await cdp.send('DOM.querySelector', { nodeId: root.nodeId, selector: 'input[type=file]' });
    await cdp.send('DOM.setFileInputFiles', { nodeId, files });
  }
  const before = await request('ListBusinessAttachments', token);
  assert.equal(before.status, 200);
  await run("location.hash='#/business-attachments'; true");
  await click('管理分类');
  await ready('document.querySelector(\'[aria-label="资料分类管理"] form input\')');
  await input('[aria-label="资料分类管理"] form input', '托单验收');
  await click('新增分类');
  await ready('document.querySelector(\'[aria-label="删除分类托单验收"]\')');
  await click('关闭分类管理');
  await click('上传资料');
  await ready('document.querySelector(\'[aria-label="选择关联发票"] .business-records-card\')');
  await input('[aria-label="搜索关联发票"]', invoice.invoiceNo);
  await click('搜索发票');
  const choice = `document.querySelector('[aria-label="选择关联发票"] button[aria-label=${JSON.stringify(`选择 ${invoice.invoiceNo}（${invoice.type}）`)}]')`;
  await ready(choice);
  await captureScreenshot(cdp, path.join(output, 'Full-attachment-invoice-picker.png'), { captureBeyondViewport: false });
  await run(`${choice}.click(); true`);
  await ready('document.querySelector(\'form[aria-label="上传业务资料"]\')');
  assert.equal(await run('location.hash'), `#/invoices/${invoice.id}/attachments?upload=new`);
  await selectFiles(); await click('上传并保留版本');
  await ready("document.querySelector('.attachment-detail') && !document.querySelector('input[type=file]')");
  const listed = await request('ListBusinessAttachments', token);
  assert.equal(listed.status, 200);
  const item = listed.body.page.items.find(value => value.invoiceId === invoice.id && value.title === 'desktop-booking');
  assert(item, 'the global upload must archive under the selected invoice');
  assert.equal(await run('location.hash'), `#/invoices/${invoice.id}/attachments?attachmentId=${item.id}`);
  const details = async () => {
    const response = await request('GetBusinessAttachment', token, undefined, { id: item.id });
    assert.equal(response.status, 200); return response.body;
  };
  const uploaded = await details();
  assert.equal(uploaded.revisions.length, 1);
  assert.equal(uploaded.revisions[0].length, bytes.length);
  assert.equal(uploaded.revisions[0].sha256.toLowerCase(), sha256);
  await click('修正资料信息');
  const metadata = 'form[aria-label="修正资料信息"]';
  await ready(`document.querySelector('${metadata}')`);
  await input(`${metadata} fieldset label:first-child input`, '已核对的托单');
  await input(`${metadata} > label input`, '核对关联发票后修正资料名称');
  await click('保存资料信息');
  await ready(`!document.querySelector('${metadata}')`);
  const corrected = await details();
  assert.equal(corrected.attachment.title, '已核对的托单');
  assert.equal(corrected.revisions[0].sha256.toLowerCase(), sha256);
  await click('上传新版本'); await selectFiles(); await click('上传并保留版本');
  await ready("document.querySelectorAll('.attachment-detail ol .attachment-version').length===2 && !document.querySelector('input[type=file]')");
  await input('.attachment-detail > .business-records-form input', '确认第二版为本次交付文件');
  await run("(()=>{const row=[...document.querySelectorAll('.attachment-detail ol .attachment-version')].find(node=>node.querySelector('h3').textContent.startsWith('v2 '));[...row.querySelectorAll('button')].find(node=>node.textContent.trim()==='设为有效版本').click();})()");
  await click('确认', '[role=dialog] button');
  await ready("document.querySelector('.attachment-detail [data-current=true] h3')?.textContent.startsWith('v2')");
  const versioned = await details();
  assert.equal(versioned.attachment.currentRevision, 2);
  assert.equal(versioned.attachment.title, '已核对的托单');
  assert.deepEqual(versioned.revisions.map(value => value.revision).sort((left, right) => right - left), [2, 1]);
  assert(versioned.revisions.every(value => value.sha256.toLowerCase() === sha256));
  assert(versioned.events.some(value => value.action === 'Edit'));
  await captureScreenshot(cdp, path.join(output, 'Full-attachment-versions.png'), { captureBeyondViewport: false });
  await input('.attachment-detail > .business-records-form input', '结束匿名验收，清理测试附件');
  await click('删除业务资料'); await click('永久删除', '[role=dialog] button');
  await ready("!document.querySelector('.attachment-detail') && document.body.innerText.includes('全部版本已删除')");
  assert.equal((await request('GetBusinessAttachment', token, undefined, { id: item.id })).status, 404);
  assert.equal((await request('ListBusinessAttachments', token)).body.page.totalCount, before.body.page.totalCount);
  await click('管理分类');
  await ready('document.querySelector(\'[aria-label="删除分类托单验收"]:not(:disabled)\')');
  await run('document.querySelector(\'[aria-label="删除分类托单验收"]\').click(); true');
  await click('删除分类', '[role=dialog] button');
  await ready('!document.querySelector(\'[aria-label="删除分类托单验收"]\')');
  await click('关闭分类管理');
  await click('管理分类');
  await input('[aria-label="资料分类管理"] form input', '批量附件验收');
  await click('新增分类');
  await ready('document.querySelector(\'[aria-label="删除分类批量附件验收"]\')');
  await click('关闭分类管理');
  const batchFiles = ['desktop-batch-one.txt', 'desktop-batch-two.txt'].map((name, index) => {
    const file = path.join(output, name); fs.writeFileSync(file, `匿名桌面附件 ${index + 1}`); return file;
  });
  await click('上传资料'); await selectFiles(batchFiles);
  assert(await run("document.querySelector('input[type=file]').multiple"));
  await captureScreenshot(cdp, path.join(output, 'Full-multiple-attachments.png'), { captureBeyondViewport: false });
  await click('上传并保留版本（2）');
  await ready("!document.querySelector('form[aria-label=\"上传业务资料\"]')");
  const batchItems = (await request('ListBusinessAttachments', token)).body.page.items.filter(value => value.invoiceId === invoice.id && value.title.startsWith('desktop-batch-'));
  assert.equal(batchItems.length, 2);
  for (const item of batchItems) {
    const details = (await request('GetBusinessAttachment', token, undefined, { id: item.id })).body;
    assert.equal(details.revisions.length, 1);
    const file = batchFiles.find(file => path.parse(file).name === item.title); assert(file);
    assert.equal(details.revisions[0].sha256.toLowerCase(), createHash('sha256').update(fs.readFileSync(file)).digest('hex'));
  }
  fs.writeFileSync(path.join(output, 'business-attachments-result.json'), JSON.stringify({ invoiceId: invoice.id, attachmentId: item.id, bytes: bytes.length, sha256, correctedTitle: corrected.attachment.title, confirmedRevision: 2, retainedRevisions: [2, 1], deleted: true, batchAttachmentIds: batchItems.map(item => item.id) }, null, 2));
}

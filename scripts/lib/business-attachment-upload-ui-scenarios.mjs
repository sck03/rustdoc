import assert from 'node:assert/strict';

export async function verifyGlobalAttachmentUploadUi({ page, open, read, waitFor, click, input, audit, results }) {
  const picker = '[aria-label="选择关联发票"]';
  const form = 'form[aria-label="上传业务资料"]';
  const uploadReady = "[...document.querySelectorAll('button')].some(node=>node.textContent==='上传资料'&&!node.disabled)";
  await open('attachments-global');
  await waitFor(page, uploadReady);
  assert(await read(page, "document.body.innerText.includes('不会自动列出发票记录')"));
  await click(page, '上传资料');
  await waitFor(page, `document.querySelector('${picker} .business-records-card')`);
  assert.equal(await read(page, `document.querySelectorAll('${picker} .business-records-card').length`), 2);
  await audit(page, 'global-attachment-invoice-picker');
  await input(page, `${picker} .page-size-control select`, '50');
  await waitFor(page, "window.__calls.some(call=>call.name==='listInvoices'&&call.input.pageSize===50)");
  await input(page, `${picker} input[aria-label="搜索关联发票"]`, 'MISSING');
  await click(page, '搜索发票');
  await waitFor(page, "document.body.innerText.includes('没有匹配的发票')");
  await input(page, `${picker} input[aria-label="搜索关联发票"]`, 'INV-2026-091');
  await click(page, '搜索发票');
  await waitFor(page, `document.querySelectorAll('${picker} .business-records-card').length===2`);
  await click(page, '取消选择');
  await waitFor(page, uploadReady);
  assert(!await read(page, `Boolean(document.querySelector('${picker}'))`));
  results.push('global-upload-picker-search-page-size-and-cancel');

  await click(page, '上传资料');
  await waitFor(page, `document.querySelectorAll('${picker} .business-records-card').length===2`);
  await read(page, `document.querySelector('${picker} button[aria-label="选择 INV-2026-091（实际数据）"]').click()`);
  await waitFor(page, `document.querySelector('${form}')`);
  assert.equal(await read(page, 'window.__route'), '/invoices/2/attachments?upload=new');
  await click(page, '取消', `${form} button`);
  await waitFor(page, "window.__route==='/invoices/2/attachments'");
  await click(page, '上传资料');
  await waitFor(page, `document.querySelector('${form} input[type=file]')`);
  await read(page, `(()=>{const node=document.querySelector('${form} input[type=file]'),data=new DataTransfer();data.items.add(new File(['匿名客户交付说明'],'交付说明.txt',{type:'text/plain'}));node.files=data.files;node.dispatchEvent(new Event('change',{bubbles:true}))})()`);
  await click(page, '上传并保留版本');
  await waitFor(page, "document.querySelector('.attachment-detail') && !document.querySelector('form[aria-label=\"上传业务资料\"]')");
  assert.equal(await read(page, "window.__calls.find(call=>call.name==='upload').input.invoiceId"), 2);
  assert.equal(await read(page, 'window.__route'), '/invoices/2/attachments?attachmentId=11');
  assert(await read(page, "document.querySelector('.attachment-detail').textContent.includes('交付说明')"));
  results.push('global-upload-selects-invoice-type-and-opens-saved-details');

  await open('attachments-global', 390, 'limited');
  await waitFor(page, uploadReady); await click(page, '上传资料');
  await waitFor(page, `document.querySelectorAll('${picker} .business-records-card').length===2`);
  await audit(page, 'global-attachment-picker-mobile');
  await read(page, `document.querySelector('${picker} button[aria-label="选择 INV-2026-091（报关数据）"]').click()`);
  await waitFor(page, "document.body.innerText.includes('不能上传到这张发票')");
  assert(!await read(page, `Boolean(document.querySelector('${form}'))`));
  await click(page, '关闭上传');
  await waitFor(page, "window.__route==='/invoices/1/attachments'");
  results.push('global-upload-respects-selected-invoice-permissions');
}

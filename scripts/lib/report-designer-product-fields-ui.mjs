import assert from 'node:assert/strict';

export async function verifyProductFieldsUi({ page, url, read, waitFor, click, results }) {
  await page.send('Page.navigate', { url: url + '?products=1' });
  await waitFor(page, '!!document.querySelector("[data-v3-page-canvas]")');
  await click(page, 'button[aria-label="选择字段"]');
  await read(page, `(()=>{for(const node of document.querySelectorAll('.report-designer-v3-sidebar details'))node.open=true;})()`);
  const drop = async (label, x, y) => {
    await read(page, `(()=>{const source=document.querySelector('button[aria-label="插入字段 ${label}"]'); const canvas=document.querySelector('[data-v3-page-canvas]');const rect=canvas.getBoundingClientRect();const data=new DataTransfer();source.dispatchEvent(new DragEvent('dragstart',{bubbles:true,dataTransfer:data}));canvas.dispatchEvent(new DragEvent('drop',{bubbles:true,cancelable:true,dataTransfer:data,clientX:rect.left+rect.width*${x}/21000,clientY:rect.top+rect.height*${y}/29700}));})()`);
  };
  await drop('明细备用 1', 6000, 11000);
  await waitFor(page, "window.__designerSchema.layers.flatMap(l=>l.elements).some(e=>e.fieldPath==='item.Spare1')");
  await drop('客户货号', 13000, 11000);
  await waitFor(page, "window.__designerSchema.layers.flatMap(l=>l.elements).filter(e=>e.fieldPath?.startsWith('item.')).length===2");
  const positions=await read(page,"window.__designerSchema.layers.flatMap(l=>l.elements).filter(e=>e.fieldPath?.startsWith('item.')).map(e=>[e.xHundredthMm,e.yHundredthMm])");
  assert.deepEqual(positions,[[6000,11000],[13000,11000]]);
  assert.equal(await read(page,"document.querySelectorAll('.report-designer-product-copy').length"),4);
  await drop('发票号',1000,7000);
  await waitFor(page,"window.__designerSchema.layers.flatMap(l=>l.elements).filter(e=>e.fieldPath==='Invoice.InvoiceNo').length===1");
  assert.equal(await read(page,"document.querySelectorAll('.report-designer-product-copy').length"),4,'fixed document fields must never repeat with products');
  await click(page,'button[aria-label="撤销"]');
  await waitFor(page,"!window.__designerSchema.layers.flatMap(l=>l.elements).some(e=>e.fieldPath==='Invoice.InvoiceNo')");
  await click(page,'button[aria-label="重做"]');
  await waitFor(page,"window.__designerSchema.layers.flatMap(l=>l.elements).some(e=>e.fieldPath==='Invoice.InvoiceNo')");
  assert.equal(await read(page,"window.__designerDraftState.isValid"),true);
  assert.deepEqual(await read(page,'window.__designerErrors'),[]);
  results.push({test:'free product fields retain physical drop positions, repeat guides, fixed document fields and undo/redo',passed:true});
}

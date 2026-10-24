import assert from "node:assert/strict";

export async function runInvoiceSaveCases({open,currentPage,read,field,clickText,waitFor,results}) {
  for(const mode of ["edit","new"]){
    await open(mode);const page=currentPage();
    if(mode==="new")await field(page,"发票号","SAVE-CONTINUATION");
    await field(page,"合同号","submitted contract");
    await read(page,"window.__delayInvoiceSave=true");await clickText(page,"保存发票");
    await waitFor(page,"window.__invoiceSaves.length===1");
    await field(page,"合同号","newer unsaved contract");
    await read(page,"window.__finishInvoiceSave()");
    await waitFor(page,"window.__route==='/invoices/7'&&document.querySelector('.editor-save-state')?.getAttribute('data-state')==='dirty'");
    const contract=()=>read(page,"[...document.querySelectorAll('label')].find(n=>n.querySelector('.form-field-label')?.textContent.trim()==='合同号')?.querySelector('input')?.value");
    assert.equal(await contract(),"newer unsaved contract",mode+": late edits survive response and route transition");
    assert.equal(await read(page,"document.body.innerText.includes('服务器上的发票已更新')"),false,"our own save is not a remote conflict");
    await clickText(page,"保存发票");await waitFor(page,"window.__invoiceSaves.length===2");
    const writes=await read(page,"window.__calls.filter(call=>call.name==='createInvoice'||call.name==='updateInvoice')");
    assert.equal(writes[1].name,"updateInvoice");assert.equal(writes[1].input.body.contractNo,"newer unsaved contract");
    assert.equal(writes[1].input.body.rowVersion,2);
    await read(page,"window.__finishInvoiceSave(1)");
    await waitFor(page,"document.querySelector('.editor-save-state')?.getAttribute('data-state')==='saved'");
    results.push(`invoice-${mode}-save-retains-later-edit`);
  }
  await open("new");const page=currentPage();await field(page,"发票号","LEAVE-PENDING");
  await read(page,"window.__delayInvoiceSave=true");await clickText(page,"保存发票");await waitFor(page,"window.__invoiceSaves.length===1");
  await read(page,"window.__navigate('/invoices')");await waitFor(page,"document.body.innerText.includes('已返回列表')");
  await read(page,"window.__finishInvoiceSave()");await waitFor(page,"window.__invoiceSaves[0].signal.aborted");
  assert.equal(await read(page,"window.__route"),"/invoices");
  results.push("invoice-save-cannot-navigate-after-unmount");
}

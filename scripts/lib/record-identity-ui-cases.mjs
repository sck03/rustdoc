import assert from "node:assert/strict";

export async function runRecordIdentityCases({ open, currentPage, read, clickText, waitFor, results }) {
  await open("edit");
  let page = currentPage();
  await clickText(page, "删除");
  await read(page, "window.__failInvoiceReadId=8;window.__navigate('/invoices/8')");
  await waitFor(page, "document.body.innerText.includes('发票读取失败')");
  assert.equal(await read(page, "document.querySelectorAll('form.entity-form').length"), 0);
  await clickText(page, "确认删除");
  await read(page, "document.dispatchEvent(new KeyboardEvent('keydown',{key:'s',ctrlKey:true,bubbles:true}))");
  assert.equal(await read(page, "window.__calls.filter(call=>['deleteInvoice','updateInvoice'].includes(call.name)).length"), 0);
  await read(page, "window.__navigate('/invoices/9')");
  await waitFor(page, "document.querySelector('.editor-title')?.textContent.includes('INV-9')");
  await clickText(page, "删除"); await clickText(page, "确认删除");
  await waitFor(page, "window.__calls.some(call=>call.name==='deleteInvoice')");
  assert.equal(await read(page, "window.__calls.find(call=>call.name==='deleteInvoice').input.id"), 9);
  results.push("invoice-failed-navigation-cancels-old-confirmation-and-draft");

  for (const [entity, type] of [["customers", "Customer"], ["exporters", "Exporter"], ["payees", "Payee"], ["products", "Product"], ["ports", "Port"], ["units", "Unit"]]) {
    await open("master", 1440, "&entity=" + entity); page = currentPage();
    await clickText(page, "删除");
    await read(page, `window.__failMasterRead=${JSON.stringify(entity + ":2")};window.__navigate(${JSON.stringify("/master-data/" + entity + "/2")})`);
    await waitFor(page, "document.body.innerText.includes('主数据读取失败')");
    assert.equal(await read(page, "document.querySelectorAll('form.entity-form,[role=dialog]').length"), 0, entity);
    await read(page, "document.dispatchEvent(new KeyboardEvent('keydown',{key:'s',ctrlKey:true,bubbles:true}))");
    assert.equal(await read(page, `window.__calls.filter(call=>['delete${type}','update${type}'].includes(call.name)).length`), 0, entity);
    await read(page, `window.__navigate(${JSON.stringify("/master-data/" + entity + "/3")})`);
    await waitFor(page, `document.querySelector('.editor-title')?.textContent.includes(${JSON.stringify("MASTER-" + entity + "-3")})`);
    await clickText(page, "删除"); await clickText(page, "确认删除");
    await waitFor(page, `window.__calls.some(call=>call.name==='delete${type}')`);
    assert.equal(await read(page, `window.__calls.find(call=>call.name==='delete${type}').input.id`), 3, entity);
    results.push(entity + "-failed-navigation-discards-old-record-ownership");
  }
}

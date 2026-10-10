import assert from "node:assert/strict";
import path from "node:path";
import { captureScreenshot } from "./web-runtime-browser-session.mjs";

export async function verifyConditionalUi({ page, url, read, waitFor, click, key, results, output }) {
  await page.send("Page.navigate", { url: `${url}?invoice=1` });
  const block = "window.__designerSchema.layers.flatMap(l=>l.elements).find(e=>e.id==='special-terms').block";
  await waitFor(page, "document.querySelector('[data-v3-element-id=special-terms]')");
  await read(page, "[...document.querySelectorAll('.report-designer-v3-sidebar-tabs button')].find(e=>e.textContent.trim()==='组件').click()");
  assert(await read(page, "[...document.querySelectorAll('button')].some(e=>e.textContent.trim()==='条件显示'&&e.getClientRects().length&&!e.closest('details:not([open])'))"), "condition palette is visible without opening advanced layout");
  async function selectBlock() {
    await read(page, "(()=>{const e=document.querySelector('[data-v3-element-id=special-terms]');e.dispatchEvent(new PointerEvent('pointerdown',{bubbles:true,button:0,pointerId:96}));window.dispatchEvent(new PointerEvent('pointercancel',{pointerId:96}));e.focus()})()");
    await waitFor(page, "document.querySelector('[role=group][aria-label=\"条件 1\"]')");
  }
  const control = (index, label) => `[...document.querySelector('[role=group][aria-label="条件 ${index}"]').querySelectorAll('label')].find(e=>e.firstElementChild?.textContent===${JSON.stringify(label)}).querySelector('select,input')`;
  async function choose(index, label, value) {
    await read(page, `(()=>{const e=${control(index, label)};e.value=${JSON.stringify(value)};e.dispatchEvent(new Event('change',{bubbles:true}))})()`);
  }
  async function value(index, text) {
    await read(page, `(()=>{const e=${control(index, "比较值")};e.focus();e.select()})()`);
    await page.send("Input.insertText", { text });
    await key(page, "Enter");
  }
  const add = () => read(page, "[...document.querySelectorAll('.new-report-conditional-properties button')].find(e=>e.textContent.trim()==='添加条件').click()");
  await selectBlock();
  assert.equal(await read(page, `${control(1, "条件字段")}.tagName`), "SELECT");
  assert(await read(page, "[...document.querySelectorAll('.new-report-conditional-properties label')].some(e=>e.firstElementChild?.textContent==='字段'&&e.querySelector('select'))"), "condition and output fields use catalog dropdowns");
  await choose(1, "条件字段", "Invoice.Currency");
  await choose(1, "判断", "Equals");
  await value(1, "USD");
  await waitFor(page, `${block}.condition.value==='USD'`);
  await add();
  await choose(2, "条件字段", "Invoice.TotalAmount");
  await choose(2, "比较类型", "Number");
  await choose(2, "判断", "GreaterOrEqual");
  await value(2, "1000.50");
  await add();
  await choose(3, "条件字段", "Invoice.InvoiceDate");
  await choose(3, "比较类型", "Date");
  await choose(3, "判断", "LessOrEqual");
  await value(3, "2026-02-30");
  await waitFor(page, "[...document.querySelectorAll('[role=alert]')].some(e=>e.textContent.includes('日期须为有效'))");
  assert.equal(await read(page, "window.__designerDraftState.isValid"), false);
  await value(3, "2026-10-10");
  await read(page, "(()=>{const e=[...document.querySelectorAll('.new-report-conditional-properties label')].find(e=>e.firstElementChild?.textContent==='规则组合').querySelector('select');e.value='Any';e.dispatchEvent(new Event('change',{bubbles:true}))})()");
  await waitFor(page, `${block}.matchMode==='Any'&&window.__designerDraftState.isValid`);
  const saved = await read(page, "window.__designerHtml");
  const script = await page.send("Page.addScriptToEvaluateOnNewDocument", { source: `window.__restoredCommercial=${JSON.stringify(saved)};` });
  await page.send("Page.navigate", { url: `${url}?invoice=1&conditional-roundtrip=1` });
  await waitFor(page, "document.querySelector('[data-v3-element-id=special-terms]')");
  await selectBlock();
  assert.equal(await read(page, `${block}.matchMode`), "Any");
  assert.deepEqual(await read(page, `${block}.additionalConditions.map(r=>[r.fieldPath,r.comparisonType,r.operator,r.value])`), [["Invoice.TotalAmount","Number","GreaterOrEqual","1000.50"],["Invoice.InvoiceDate","Date","LessOrEqual","2026-10-10"]]);
  await click(page, 'button[aria-label="删除条件 2"]');
  await waitFor(page, `${block}.additionalConditions.length===1`);
  await click(page, 'button[aria-label="撤销"]');
  await waitFor(page, `${block}.additionalConditions.length===2`);
  await captureScreenshot(page, path.join(output, "conditional-rules.png"), { captureBeyondViewport: false });
  for (let count = 3; count < 8; count++) { await add(); await waitFor(page, `${block}.additionalConditions.length===${count}`); }
  assert(await read(page, "[...document.querySelectorAll('.new-report-conditional-properties button')].find(e=>e.textContent.trim()==='添加条件').disabled"));
  assert.equal(await read(page, "document.querySelector('.report-designer-v3-inspector').scrollWidth <= document.querySelector('.report-designer-v3-inspector').clientWidth + 1"), true);
  await page.send("Page.removeScriptToEvaluateOnNewDocument", { identifier: script.identifier });
  assert.deepEqual(await read(page, "window.__designerErrors"), []);
  results.push({ test: "conditional text/number/date rules, validation, save/reopen, removal/undo and bounded combinations", passed: true });
}

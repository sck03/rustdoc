import assert from "node:assert/strict";
import path from "node:path";
import { captureScreenshot } from "./web-runtime-browser-session.mjs";

export async function verifyDetailProperties({ page, url, read, waitFor, click, key, results, output }) {
  const block = 'window.__designerSchema.layers.flatMap(layer=>layer.elements).find(e=>e.id==="review-detail").block';
  const control = (label, kind = "select") => `[...document.querySelectorAll('.new-report-detail-properties label')].find(n=>n.firstElementChild?.textContent===${JSON.stringify(label)}).querySelector('${kind}')`;
  const choose = async (label, value) => {
    await read(page, `(()=>{const n=${control(label)};n.value=${JSON.stringify(value)};n.dispatchEvent(new Event('change',{bubbles:true}))})()`);
  };
  const tab = async label => {
    await read(page, `[...document.querySelectorAll('.new-report-detail-properties [role=tab]')].find(n=>n.textContent===${JSON.stringify(label)}).click()`);
  };
  const open = async label => {
    await read(page, `[...document.querySelectorAll('.new-report-detail-properties summary')].find(n=>n.textContent===${JSON.stringify(label)}).parentElement.open=true`);
  };
  await page.send("Page.navigate", { url });
  await waitFor(page, 'document.querySelector("[data-v3-element-id=review-detail]")');
  await read(page, 'document.querySelector("[data-v3-element-id=review-detail]").focus()');
  await key(page, "Enter");
  await waitFor(page, "document.querySelector('.new-report-detail-column-card')");
  const before = await read(page, block);
  await read(page, `${control("标题", "input")}.focus();${control("标题", "input")}.select()`);
  await page.send("Input.insertText", { text: "商品中文标题" });
  await key(page, "Enter");
  await waitFor(page, `${block}.columns[0].title==="商品中文标题"`);
  await click(page, 'button[aria-label="撤销"]');
  assert.deepEqual(await read(page, block), before, "one undo restores the whole title");
  await open("组合排版");
  await choose("内容", "Composite");
  await waitFor(page, `${block}.columns[0].contentKind==="Composite"`);
  assert.equal(await read(page, `${block}.columns[0].content[0].fieldPath`), before.columns[0].fieldPath);
  await read(page, "[...document.querySelectorAll('.new-report-detail-properties label')].find(n=>n.textContent==='不显示空内容行').querySelector('input').click()");
  await click(page, 'button[aria-label="复制列"]');
  await waitFor(page, `${block}.columns.length===${before.columns.length + 1}`);
  const copy = await read(page, `${block}.columns[1]`);
  assert.equal(copy.omitEmptyLines, true);
  assert.equal(await read(page, `${control("选择要修改的列")}.value`), copy.id);
  assert.equal(await read(page, "document.querySelectorAll('.new-report-detail-column-card').length"), 1, "column count must not multiply hidden editors");
  await click(page, 'button[aria-label="删除列"]');
  await waitFor(page, `${block}.columns.length===${before.columns.length}`);
  await click(page, 'button[aria-label="撤销"]');
  await waitFor(page, `${block}.columns.length===${before.columns.length + 1}`);
  assert.equal(await read(page, `${block}.columns[1].omitEmptyLines`), true);
  results.push({ test: "detail title commits once; mode switch and copied columns preserve configuration through undo", passed: true });
  await tab("表格样式");
  await choose("设置文字样式", "headerStyle");
  await waitFor(page, `${control("设置文字样式")}.value==="headerStyle"`);
  const sizes = await read(page, `({header:${block}.headerStyle.fontSizePt,body:${block}.bodyStyle.fontSizePt})`);
  await read(page, `${control("字号", "input")}.focus();${control("字号", "input")}.select()`);
  await page.send("Input.insertText", { text: "14" }); await key(page, "Tab");
  await waitFor(page, `${block}.headerStyle.fontSizePt===14`);
  assert.equal(await read(page, `${block}.bodyStyle.fontSizePt`), sizes.body);
  await tab("分页与高级"); await open("末页合计");
  assert.equal(await read(page, "[...document.querySelectorAll('.new-report-summary-cell-editor')].filter(n=>n.getClientRects().length).length"), 1);
  await tab("表格样式");
  await waitFor(page, `${control("设置文字样式")}.value==="headerStyle"`);
  results.push({ test: "header and body styles are independent; totals edit one column and tabs preserve selection", passed: true });
  for (const width of [1366, 390]) {
    await page.send("Emulation.setDeviceMetricsOverride", { width, height: 1000, deviceScaleFactor: 1, mobile: width < 500 });
    if (width < 500) await click(page, ".report-designer-v3-compact-tabs button:nth-child(3)");
    for (const label of ["商品列", "表格样式", "分页与高级"]) {
      await tab(label);
      assert(await read(page, 'document.querySelector(".report-designer-v3-inspector").scrollWidth <= document.querySelector(".report-designer-v3-inspector").clientWidth + 1'));
      await captureScreenshot(page, path.join(output, `detail-${width}-${label}.png`), { captureBeyondViewport: false });
    }
  }
  await page.send("Emulation.setDeviceMetricsOverride", { width: 1440, height: 1000, deviceScaleFactor: 1, mobile: false });
  assert.deepEqual(await read(page, "window.__designerErrors"), []);
}

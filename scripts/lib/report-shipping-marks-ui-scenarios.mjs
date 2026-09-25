import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";

export async function verifyShippingMarksUi({ page, url, read, waitFor, click, key, results, output }) {
  await page.send("Page.navigate", { url: `${url}?marks=1` });
  await waitFor(page, 'document.querySelector("[data-v3-element-id=marks-field]")');
  await read(page, "[...document.querySelectorAll('button')].find(node=>node.textContent.trim()==='字段').click()");
  await waitFor(page, 'document.querySelector(\'[aria-label="插入字段 唛头（文字 / 图片自动）"]\')');
  assert.equal(await read(page, '[...document.querySelectorAll("[aria-label^=插入字段]")].filter(node=>node.textContent.includes("唛头")).length'), 1);
  await click(page, '[aria-label="插入字段 唛头（文字 / 图片自动）"]');
  await waitFor(page, 'window.__designerSchema.layers.flatMap(layer=>layer.elements).filter(element=>element.fieldPath==="Invoice.ShippingMarks").length===2');
  assert(await read(page, 'document.querySelector(".report-designer-v3-inspector").textContent.includes("等比例缩放")'));
  await click(page, 'button[aria-label="撤销"]');
  await waitFor(page, 'window.__designerSchema.layers.flatMap(layer=>layer.elements).filter(element=>element.fieldPath==="Invoice.ShippingMarks").length===1');
  await read(page, 'document.querySelector("[data-v3-element-id=marks-field]").focus()');
  await key(page, "Enter");
  await read(page, "(()=>{const node=[...document.querySelectorAll('.report-designer-v3-inspector label')].find(label=>label.firstElementChild?.textContent==='高 (mm)').querySelector('input');node.focus();node.select()})()");
  await page.send("Input.insertText", { text: "22" });
  await key(page, "Enter");
  await waitFor(page, 'window.__designerSchema.layers.flatMap(layer=>layer.elements).find(element=>element.id==="marks-field").heightHundredthMm===2200');
  results.push({ test: "single shipping-mark field inserts, resizes and undoes", passed: true });

  const previews = await read(page, '({text:window.__renderMarksPreview("exportStandard"),image:window.__renderMarksPreview("exportImageMarks")})');
  for (const [kind, html] of Object.entries(previews)) {
    const name = `marks-${kind}.html`;
    fs.writeFileSync(path.join(output, name), html);
    await page.send("Page.navigate", { url: new URL(name, url).href });
    await waitFor(page, 'document.querySelectorAll(".edm-report-field-content").length===5');
    assert(await read(page, '(()=>{const outer=document.querySelector(".edm-detail-layout").getBoundingClientRect(),inner=document.querySelector(".edm-detail-table").getBoundingClientRect();return inner.right<=outer.right+1})()'), "detail columns must fit beside the shipping mark");
    if (kind === "image") {
      await waitFor(page, 'document.images.length===5 && [...document.images].every(image=>image.naturalWidth===160)');
      const issues = await read(page, `(()=>{const issues=[];for(const image of document.images){const box=image.getBoundingClientRect(),parent=image.parentElement.getBoundingClientRect();if(Math.abs(box.width/box.height-1.6)>0.02||box.width>parent.width+1)issues.push('image aspect or width');const fixed=image.closest('.edm-v3-element-field');if(fixed&&box.bottom>fixed.getBoundingClientRect().bottom+1)issues.push('fixed field height');if(!image.src.startsWith('data:image/png;base64,'))issues.push('image source');}if(document.body.innerText.includes('ORDER SAMPLE'))issues.push('stale text');return issues})()`);
      assert.deepEqual(issues, [], "images must fit every field/cell/side band, preserving aspect ratio");
    } else {
      assert.equal(await read(page, "document.images.length"), 0);
      assert.equal(await read(page, '(document.body.innerText.match(/ORDER SAMPLE/g)||[]).length'), 5);
      assert.equal(await read(page, 'getComputedStyle(document.querySelector(".edm-report-field-content")).whiteSpace'), "pre-wrap");
    }
    const screenshot = await page.send("Page.captureScreenshot", { format: "png", captureBeyondViewport: false });
    fs.writeFileSync(path.join(output, `marks-${kind}.png`), Buffer.from(screenshot.data, "base64"));
    const pdf = await page.send("Page.printToPDF", { preferCSSPageSize: true, printBackground: true, displayHeaderFooter: false });
    const bytes = Buffer.from(pdf.data, "base64");
    assert(bytes.length > 1000 && bytes.subarray(0, 4).toString() === "%PDF");
    if (kind === "image") assert.match(bytes.toString("latin1"), /\/Subtype\s*\/Image/);
    fs.writeFileSync(path.join(output, `marks-${kind}.pdf`), bytes);
    results.push({ test: `same template renders ${kind} marks in all five placements and prints PDF`, passed: true });
  }
}

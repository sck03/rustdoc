import assert from "node:assert/strict";

const cell = (row, field) => `input[data-invoice-item-row="${row}"][data-invoice-item-field="${field}"]`;

export async function runInvoiceItemsGridCases(scenario) {
  await scenario(async page => {
    await page.locator(cell(0, "styleNo")).focus();
    await page.evaluate(selector => {
      const data = new DataTransfer();
      data.setData("text/plain", Array.from({ length:1000 }, (_, i) => "PASTE-"+i).join("\n"));
      document.querySelector(selector).dispatchEvent(new ClipboardEvent("paste", { bubbles:true, cancelable:true, clipboardData:data }));
    }, cell(0, "styleNo"));
    await page.waitForFunction(() => window.invoice.items.length === 1000);
    await page.evaluate(() => {
      const frame = document.querySelector(".item-editor-frame");
      frame.scrollTop = frame.scrollHeight;
      frame.dispatchEvent(new Event("scroll", { bubbles:true }));
    });
    await page.locator(cell(999, "styleNo")).waitFor();
    await page.locator(cell(999, "styleNo")).focus();
    await page.evaluate(selector => {
      window.shrinkFrames = [];
      const record = () => {
        const cells = [...document.querySelectorAll('input[data-invoice-item-field="styleNo"]')];
        if (window.invoice.items.length === 200) window.shrinkFrames.push(cells.map(input => Number(input.dataset.invoiceItemRow)));
        if (window.shrinkFrames.length < 12) requestAnimationFrame(record);
      };
      requestAnimationFrame(record);
      document.querySelector(selector).dispatchEvent(new KeyboardEvent("keydown", { key:"z", ctrlKey:true, bubbles:true, cancelable:true }));
    }, cell(999, "styleNo"));
    await page.waitForFunction(() => window.shrinkFrames.length === 12);
    const frames = await page.evaluate(() => window.shrinkFrames);
    assert.ok(frames.every(rows => rows.length > 0 && rows.every(row => row >= 0 && row < 200)));
    await page.locator(cell(199, "styleNo")).waitFor();
    assert.equal(await page.evaluate(() => window.invoice.items[199].styleNo), "ZZ-1");
  }, { gridMode:true, initialRows:200 });

  await scenario(async page => {
    await page.evaluate(() => {
      Object.defineProperty(navigator, "clipboard", { configurable:true, value:{
        writeText:async text => { window.copiedText=text; },
        readText:async () => window.copiedText ?? "",
      } });
      // about:blank exercises the same selection fallback as an insecure host.
      document.execCommand = command => {
        if (command !== "copy" || document.activeElement?.tagName !== "TEXTAREA") return false;
        window.copiedText = document.activeElement.value;
        return true;
      };
    });
    const first = page.locator(cell(0, "poNumber"));
    await first.click();
    await page.evaluate(() => {
      const frame = document.querySelector(".item-editor-frame");
      frame.scrollTop = frame.scrollHeight;
      frame.dispatchEvent(new Event("scroll", { bubbles:true }));
    });
    await page.locator(cell(4999, "spare10")).waitFor();
    await page.locator(cell(4999, "spare10")).click({ modifiers:["Shift"] });
    await page.getByRole("button", { name:"复制选中单元格", exact:true }).click();
    await page.waitForFunction(() => typeof window.copiedText === "string");
    const copied = await page.evaluate(() => ({ rows:window.copiedText.split("\n").length, columns:window.copiedText.split("\n")[0].split("\t").length }));
    assert.deepEqual(copied, { rows:5000, columns:38 });
    await page.getByRole("button", { name:"向下填充当前单元格", exact:true }).click();
    await page.waitForFunction(() => window.invoice.items[4999].poNumber === "PO-0");
    assert.equal(await page.evaluate(() => window.lastFillCount), 190000);
    await page.getByRole("button", { name:"清空选中单元格", exact:true }).click();
    await page.waitForFunction(() => window.invoice.items[0].poNumber === "" && window.invoice.items[4999].poNumber === "");
    assert.equal(await page.evaluate(() => window.lastClearCount), 190000);
  }, { gridMode:true, initialRows:5000 });
}

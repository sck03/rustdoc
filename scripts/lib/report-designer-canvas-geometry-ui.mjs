import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { captureScreenshot } from './web-runtime-browser-session.mjs';

export async function verifyInvoiceCanvasAlignment(run) {
  const measured = await run(`(()=>{
    const canvas=document.querySelector('[data-v3-page-canvas]').getBoundingClientRect();
    const scale=canvas.width/(210*96/25.4);
    const element=document.querySelector('[data-v3-element-id="invoice-items"]');
    const header=element.querySelector('.edm-detail-layout > thead .edm-detail-table');
    const body=element.querySelector('.edm-detail-layout > tbody .edm-detail-table');
    const cells=[...header.rows[header.rows.length-1].cells].map(cell=>cell.getBoundingClientRect());
    const row=[...body.rows].find(row=>row.cells.length===cells.length);
    const bodyCells=[...row.cells].map(cell=>cell.getBoundingClientRect());
    return {scale,errors:cells.flatMap((cell,index)=>[(cell.left-bodyCells[index].left)/scale,(cell.right-bodyCells[index].right)/scale])};
  })()`);
  assert(measured.errors.every(error => Math.abs(error) <= 1), `Header and body rules differ in canvas pixels: ${JSON.stringify(measured)}`);
  return measured;
}

export async function verifyCanvasGeometry({ page, url, read, waitFor, key, results, output }) {
  await page.send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 1000, deviceScaleFactor: 1, mobile: false });
  await page.send('Page.navigate', { url: url + '?invoice=1' });
  await waitFor(page, 'document.querySelector("[data-v3-element-id=invoice-items] table")');
  const original = await read(page, 'window.__designerHtml');
  const measurements = [];
  for (const percent of [50, 75, 100, 125, 150]) {
    await read(page, `(()=>{const node=document.querySelector('[aria-label="选择缩放比例"]');node.value='${percent}';node.dispatchEvent(new Event('change',{bubbles:true}));})()`);
    await waitFor(page, `Math.abs(document.querySelector('[data-v3-page-canvas]').getBoundingClientRect().width/(210*96/25.4)-${percent / 100})<0.002`);
    measurements.push(await verifyInvoiceCanvasAlignment(expression => read(page, expression)));
  }
  await read(page, "document.querySelector('[data-v3-element-id=invoice-items]').focus()");
  await key(page, 'Enter');
  await verifyInvoiceCanvasAlignment(expression => read(page, expression));
  await read(page, "[...document.querySelectorAll('.report-designer-v3-workspace label')].find(node=>node.textContent.trim()==='组件边界').querySelector('input').click()");
  await verifyInvoiceCanvasAlignment(expression => read(page, expression));
  assert.equal(await read(page, 'window.__designerHtml'), original, 'zoom and selection cannot rewrite the printable geometry');
  fs.writeFileSync(path.join(output, 'canvas-grid-alignment.json'), JSON.stringify(measurements, null, 2));
  await captureScreenshot(page, path.join(output, 'invoice-grid-alignment.png'), { captureBeyondViewport: false });
  results.push({ test: 'invoice header and body lines align across five zoom levels, selection and bounds without mutating the document', passed: true });
}

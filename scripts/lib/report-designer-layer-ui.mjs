import assert from 'node:assert/strict';
import path from 'node:path';
import { captureScreenshot } from './web-runtime-browser-session.mjs';

export async function verifyLayerClarity({ page, url, read, waitFor, click, results, output }) {
  await page.send('Page.navigate', { url: `${url}?invoice=1` });
  await waitFor(page, 'document.querySelector("[data-v3-page-canvas]")');
  const before = await read(page, 'window.__designerHtml');
  const pageScroll = await read(page, 'window.scrollY');
  await click(page, '[aria-label="画布图层导航"] [data-layer-role="Footer"]');
  await waitFor(page, 'document.querySelector(".report-designer-v3-layer-footer.is-active")');
  await waitFor(page, `(()=>{const node=document.querySelector('.report-designer-v3-layer-footer.is-active .report-designer-layer-anchor').getBoundingClientRect();const view=document.querySelector('.report-designer-v3-canvas-scroll').getBoundingClientRect();return node.top>=view.top && node.top<view.bottom})()`);
  assert.equal(await read(page, 'window.scrollY'), pageScroll, 'layer navigation scrolls the canvas without moving the toolbar');
  await click(page, '.report-designer-v3-sidebar-tabs button:nth-child(3)');
  assert.equal(await read(page, 'document.querySelectorAll(".report-designer-layer-settings[open]").length'), 0);
  await read(page, `document.querySelector('[data-v3-element-id="total-label"]').focus()`);
  await page.send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Enter', windowsVirtualKeyCode: 13 });
  await page.send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Enter', windowsVirtualKeyCode: 13 });
  await waitFor(page, 'document.querySelector(".report-designer-selection-readout").textContent.includes("TOTAL")');
  for (const zoom of ['50', '100']) {
    await read(page, `(()=>{const input=document.querySelector('[aria-label="选择缩放比例"]');input.value='${zoom}';input.dispatchEvent(new Event('change',{bubbles:true}));})()`);
    await waitFor(page, `document.querySelector('.report-designer-v3-zoom-readout').textContent==='${zoom}%'`);
    const size = await read(page, `(()=>{const handle=document.querySelector('.report-designer-v3-handle');return handle.getBoundingClientRect().width})()`);
    assert(Math.abs(size - 8) < 1, 'handles retain their screen size at different zooms');
  }
  await read(page, `[...document.querySelectorAll('.report-designer-v3-canvas-meta label')].find(e=>e.textContent.includes('组件边界')).querySelector('input').click()`);
  await waitFor(page, '!document.querySelector(".has-element-bounds")');
  assert.equal(await read(page, 'window.__designerHtml'), before, 'navigation, zoom and bounds do not change printable content');
  await read(page, `[...document.querySelectorAll('.report-designer-v3-canvas-meta label')].find(e=>e.textContent.includes('组件边界')).querySelector('input').click()`);
  await click(page, 'button[aria-label="适合宽度"]');
  await captureScreenshot(page, path.join(output, 'invoice-layer-navigation.png'));
  results.push({ test: 'readable layer navigation, constant handles and display-only bounds', passed: true });

  await page.send('Page.navigate', { url });
  await waitFor(page, 'document.querySelector(".report-designer-v3-band-resizer-header")');
  const startHeight = await read(page, 'window.__designerSchema.layers.find(l=>l.role==="Header").designHeightHundredthMm');
  const point = await read(page, `(()=>{const node=document.querySelector('.report-designer-v3-band-resizer-header');node.scrollIntoView({block:'center'});const r=node.getBoundingClientRect();return {x:r.left+r.width/2,y:r.top+r.height/2}})()`);
  await page.send('Input.dispatchMouseEvent', { type: 'mousePressed', ...point, button: 'left', clickCount: 1 });
  await page.send('Input.dispatchMouseEvent', { type: 'mouseReleased', ...point, y: point.y + 12, button: 'left', clickCount: 1 });
  await waitFor(page, `window.__designerSchema.layers.find(l=>l.role==='Header').designHeightHundredthMm>${startHeight}`);
  await click(page, 'button[aria-label="撤销"]');
  await waitFor(page, '!window.__designerDraftState.isDirty');
  const restored = await read(page, 'window.__designerHtml');
  await page.send('Input.dispatchMouseEvent', { type: 'mousePressed', ...point, button: 'left', clickCount: 1 });
  await page.send('Input.dispatchMouseEvent', { type: 'mouseMoved', ...point, y: point.y + 12, button: 'left', buttons: 1 });
  await read(page, 'window.dispatchEvent(new Event("blur"))');
  await page.send('Input.dispatchMouseEvent', { type: 'mouseReleased', ...point, y: point.y + 12, button: 'left', clickCount: 1 });
  assert.equal(await read(page, 'window.__designerHtml'), restored, 'cancelled band resizing preserves the draft');
  results.push({ test: 'band release uses terminal coordinates and blur cancels the preview', passed: true });
}

import assert from "node:assert/strict";

export async function verifyDetailVisibility({page, url, read, waitFor, results}) {
  await page.send("Page.navigate", {url});
  await waitFor(page, "!!document.querySelector('[data-v3-element-id=review-detail]')");
  await read(page, "(()=>{const n=document.querySelector('[data-v3-element-id=review-detail]');n.dispatchEvent(new PointerEvent('pointerdown',{bubbles:true,button:0,pointerId:88}));window.dispatchEvent(new PointerEvent('pointercancel',{pointerId:88}));})()");
  await waitFor(page, "document.querySelectorAll('.new-report-detail-column-card').length>1");
  const visibleCards = () => read(page, "[...document.querySelectorAll('.new-report-detail-column-card')].filter(n=>getComputedStyle(n).display!=='none').length");
  assert.equal(await visibleCards(), 1);
  await read(page, "(()=>{const n=document.querySelector('.new-report-detail-column-card:not([hidden]) details');n.open=true;const select=document.querySelector('.new-report-detail-properties select');select.value=select.options[1].value;select.dispatchEvent(new Event('change',{bubbles:true}));})()");
  await waitFor(page, "document.querySelector('.new-report-detail-column-card').hidden");
  assert.equal(await visibleCards(), 1);
  await read(page, "(()=>{const select=document.querySelector('.new-report-detail-properties select');select.value=select.options[0].value;select.dispatchEvent(new Event('change',{bubbles:true}));})()");
  await waitFor(page, "!document.querySelector('.new-report-detail-column-card').hidden");
  assert(await read(page, "document.querySelector('.new-report-detail-column-card details').open"), "switching columns must retain expanded settings");
  for (const label of ["打印样式", "高级设置", "商品列"]) {
    await read(page, `[...document.querySelectorAll('.new-report-detail-properties [role=tab]')].find(n=>n.textContent===${JSON.stringify(label)}).click()`);
    assert(await read(page, "[...document.querySelectorAll('.new-report-detail-properties [hidden]')].every(n=>getComputedStyle(n).display==='none')"), "inactive controls must stay hidden");
  }
  // Render the same composite through canvas CSS and the standalone HTML export CSS.
  await read(page, `(()=>{
    const html=window.__visibilityComposite();
    const host=document.createElement('div');host.id='visibility-probe';host.style.width='200px';host.innerHTML=html;document.body.append(host);
    const frame=document.createElement('iframe');frame.id='visibility-export';frame.srcdoc=window.__exportDesignerHtml();document.body.append(frame);
  })()`);
  await waitFor(page, "!!document.querySelector('#visibility-export').contentDocument?.querySelector('.edm-v3-page')");
  const measurements=await read(page, `(()=>{
    const doc=document.querySelector('#visibility-export').contentDocument;const host=doc.createElement('div');host.style.width='200px';host.innerHTML=window.__visibilityComposite();doc.body.append(host);
    return [document.querySelector('#visibility-probe'),host].map(root=>[...root.querySelectorAll('.edm-detail-composite-line')].map(n=>({display:n.ownerDocument.defaultView.getComputedStyle(n).display,height:n.getBoundingClientRect().height,columns:n.ownerDocument.defaultView.getComputedStyle(n).gridTemplateColumns})));
  })()`);
  for(const lines of measurements) {
    assert.equal(lines[0].display,"none");assert.equal(lines[0].height,0);
    assert.equal(lines[1].display,"grid");assert(lines[1].height>0);
    assert.equal(lines[2].display,"grid");assert.equal(lines[2].columns,"100px 100px");
  }
  await read(page, "document.querySelector('#visibility-probe').remove();document.querySelector('#visibility-export').remove()");
  results.push({test:"detail columns and tabs retain drafts while hidden; empty composite lines collapse in canvas and export",passed:true});
}

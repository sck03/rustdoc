import assert from "node:assert/strict";

export async function verifyDesignerEditingUi({ page, url, read, waitFor, click, key, modifier, results }) {
  await page.send("Page.navigate", { url: `${url}?editing=1` });
  await waitFor(page, 'document.querySelector("[data-v3-element-id=edit-text]")');
  const select = async (id, additive = false) => {
    await read(page, `(()=>{const node=document.querySelector('[data-v3-element-id="${id}"]');node.dispatchEvent(new PointerEvent('pointerdown',{bubbles:true,button:0,pointerId:91,ctrlKey:${additive}}));window.dispatchEvent(new PointerEvent('pointercancel',{pointerId:91}));node.focus()})()`);
    await waitFor(page, `document.querySelector('[data-v3-element-id="${id}"]').classList.contains('is-selected')`);
  };
  const press = text => read(page, `(()=>{const node=[...document.querySelectorAll('.report-designer-v3-inspector button')].find(node=>node.getClientRects().length&&node.textContent.trim()===${JSON.stringify(text)});if(!node)throw new Error('Missing control: '+${JSON.stringify(text)});node.click()})()`);
  const number = async (label, value, action = "Enter") => {
    await read(page, `(()=>{const node=[...document.querySelectorAll('.report-designer-v3-inspector label')].find(node=>node.firstElementChild?.textContent===${JSON.stringify(label)}).querySelector('input');node.focus();node.select()})()`);
    await page.send("Input.insertText", { text: value });
    await key(page, action);
  };
  await select("edit-text");
  const countBefore = await read(page, "document.querySelectorAll('[data-v3-element-id]').length");
  await key(page, "v", modifier);
  assert.equal(await read(page, "document.querySelectorAll('[data-v3-element-id]').length"), countBefore, "empty clipboard must not duplicate selection");
  await read(page, "document.activeElement.blur()");
  await key(page, "Delete");
  assert.equal(await read(page, "document.querySelectorAll('[data-v3-element-id]').length"), countBefore, "designer shortcuts must stay inside its workspace");
  results.push({ test: "empty clipboard and shortcut scope", passed: true });

  await select("edit-text");
  await press("布局");
  const before = await read(page, "window.__designerSchema.layers.flatMap(layer=>layer.elements).find(element=>element.id==='edit-text').xHundredthMm");
  await number("X (mm)", "45", "Escape");
  assert.equal(await read(page, "window.__designerSchema.layers.flatMap(layer=>layer.elements).find(element=>element.id==='edit-text').xHundredthMm"), before);
  await number("X (mm)", "45");
  await waitFor(page, "window.__designerSchema.layers.flatMap(layer=>layer.elements).find(element=>element.id==='edit-text').xHundredthMm===4500");
  await click(page, 'button[aria-label="撤销"]');
  await waitFor(page, `window.__designerSchema.layers.flatMap(layer=>layer.elements).find(element=>element.id==='edit-text').xHundredthMm===${before}`);
  results.push({ test: "number draft cancels and commits as one undo step", passed: true });

  await click(page, 'button[aria-label="复制样式"]');
  await select("edit-other");
  await click(page, 'button[aria-label="应用样式"]');
  await waitFor(page, "window.__designerSchema.layers.flatMap(layer=>layer.elements).find(element=>element.id==='edit-other').style.align==='Right'");
  assert.equal(await read(page, "window.__designerSchema.layers.flatMap(layer=>layer.elements).find(element=>element.id==='edit-other').widthHundredthMm"), 2500);
  await select("edit-text", true);
  await press("相同大小");
  await waitFor(page, "window.__designerSchema.layers.flatMap(layer=>layer.elements).find(element=>element.id==='edit-text').widthHundredthMm===2500");
  await click(page, 'button[aria-label="撤销"]');
  await waitFor(page, "window.__designerSchema.layers.flatMap(layer=>layer.elements).find(element=>element.id==='edit-text').widthHundredthMm===6000");
  results.push({ test: "format painter and equal sizes preserve geometry and undo", passed: true });

  await select("edit-page");
  assert.equal(await read(page, "document.querySelector('[data-v3-element-id=edit-page] .report-designer-v3-preview-page-number').textContent"), "第1页");
  await select("edit-line");
  assert.equal(await read(page, "getComputedStyle(document.querySelector('[data-v3-element-id=edit-line] .report-designer-v3-preview-line')).borderTopStyle"), "dashed");
  await read(page, "(()=>{const node=[...document.querySelectorAll('.report-designer-v3-inspector label')].find(node=>node.firstElementChild?.textContent==='线型').querySelector('select');Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype,'value').set.call(node,'None');node.dispatchEvent(new Event('change',{bubbles:true}))})()");
  await waitFor(page, "getComputedStyle(document.querySelector('[data-v3-element-id=edit-line] .report-designer-v3-preview-line')).display==='none'");
  await click(page, 'button[aria-label="撤销"]');
  results.push({ test: "page format and dashed/hidden line previews", passed: true });

  await read(page, "(()=>{const frame=document.createElement('iframe');frame.id='export-check';frame.title='导出布局验证';Object.assign(frame.style,{position:'fixed',left:'-10000px',width:'800px',height:'1200px',visibility:'hidden'});document.body.append(frame);frame.srcdoc=window.__designerHtml})()");
  await waitFor(page, "document.querySelector('#export-check').contentDocument.querySelector('.edm-v3-element-field')");
  const differences = await read(page, `(()=>{const doc=document.querySelector('#export-check').contentDocument;const differences=[];for(const [id,type] of [['edit-text','text'],['edit-field','field'],['edit-page','pagenumber']]){const canvas=document.querySelector('[data-v3-element-id="'+id+'"]');const exported=doc.querySelector('.edm-v3-element-'+type);const a=getComputedStyle(canvas),b=doc.defaultView.getComputedStyle(exported);for(const property of ['fontSize','fontWeight','textAlign','color','backgroundColor','paddingTop','paddingLeft','borderTopWidth','borderTopStyle','width','height'])if(a[property]!==b[property])differences.push({id,property,canvas:a[property],exported:b[property]});const child=canvas.querySelector('span');if(getComputedStyle(child).color!==b.color)differences.push({id,property:'childColor'});}return differences})()`);
  assert.deepEqual(differences, [], "canvas and exported primitive styles must agree");
  await read(page, "document.querySelector('#export-check').remove()");
  results.push({ test: "canvas/export text, field, and page styles agree", passed: true });

  await select("edit-text");
  await key(page, "c", modifier);
  await click(page, '.report-designer-v3-sidebar-tabs button:last-child');
  await click(page, '.report-designer-v3-layer-name');
  await click(page, 'button[aria-label="粘贴"]');
  await waitFor(page, "window.__designerSchema.layers.find(layer=>layer.role==='Header').elements.filter(element=>element.type==='Text').length===1");
  assert(await read(page, "window.__designerSchema.layers.find(layer=>layer.role==='Overlay').elements.some(element=>element.id==='edit-text')"));
  results.push({ test: "paste targets selected region and preserves original", passed: true });
  assert.deepEqual(await read(page, "window.__designerErrors"), []);
}

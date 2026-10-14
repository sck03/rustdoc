import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

export async function verifyRowAuthoring({page,url,read,waitFor,click,results,output}) {
  await page.send('Page.navigate',{url:url+'?blank=1'});
  await waitFor(page,'window.__designerDraftState && document.querySelector("[data-v3-page-canvas]")');
  await read(page,"document.querySelector('.report-designer-v3-advanced-tools').open=true");
  await click(page,'button[aria-label="多列行"]');
  const block="window.__designerSchema.layers.flatMap(layer=>layer.elements).find(element=>element.flowKind==='Row').block";
  const control=label=>`[...document.querySelectorAll('.new-report-row-properties label')].find(node=>node.firstElementChild?.textContent===${JSON.stringify(label)}).querySelector('input,select,textarea')`;
  const fill=async(label,value)=>{
    await read(page,`${control(label)}.focus();${control(label)}.select()`);
    await page.send('Input.insertText',{text:String(value)});
    await read(page,control(label)+'.blur()');
  };
  await waitFor(page,"document.querySelector('.new-report-row-properties')");
  assert(await read(page,block+".columns.every(column=>column.contentKind==='Text'&&!column.text)"));
  await fill('列数',1000000);
  await waitFor(page,block+'.columns.length===100');
  assert.equal(await read(page,"document.querySelectorAll('.new-report-row-properties .new-report-detail-column-card').length"),1,'large rows mount only the active column editor');
  assert(await read(page,"[...document.querySelectorAll('.new-report-row-properties button')].find(n=>n.textContent==='新增列').disabled"));
  await fill('列数',3);
  await waitFor(page,block+'.columns.length===3');
  await fill('宽度(%)',37.123456);
  const widths=await read(page,block+'.columns.map(column=>column.widthPercent)');
  await fill('固定内容','检查项目\nInspection');
  await waitFor(page,block+'.columns[0].text==='+JSON.stringify('检查项目\nInspection'));
  assert.deepEqual(await read(page,block+'.columns.map(column=>column.widthPercent)'),widths);
  await click(page,'.new-report-row-properties [aria-label="复制列"]');
  await waitFor(page,block+'.columns.length===4');
  assert.equal(await read(page,block+'.columns[1].text'),'检查项目\nInspection');
  await click(page,'button[aria-label="撤销"]');
  await waitFor(page,block+'.columns.length===3');
  await read(page,`(()=>{const n=${control('选择要修改的列')};n.value=${block}.columns[2].id;n.dispatchEvent(new Event('change',{bubbles:true}));})()`);
  await fill('固定内容','核对结果\nResult');
  assert.equal(await read(page,'window.__designerDraftState.isValid'),true);
  fs.writeFileSync(path.join(output,'generic-row-authored-document.json'),await read(page,'window.__designerHtml'));
  results.push({test:'generic rows bound column allocation, retain exact widths and edit one column with undo',passed:true});
}

import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { captureScreenshot } from './web-runtime-browser-session.mjs';
import { waitFor } from './web-runtime-smoke-common.mjs';

export async function verifyDesktopSupplierConsistency({ run, request, token, businessDate, cdp, output }) {
  const wait = expression => waitFor(() => run(`Boolean(${expression})`), 20000, expression);
  const button = name => `[...document.querySelectorAll('button')].find(e=>e.getClientRects().length&&e.textContent.trim()===${JSON.stringify(name)})`;
  const supplier = await request('CreateSupplier', token, { name: '供应商桌面验收', category: '服装' });
  assert.equal(supplier.status, 201, JSON.stringify(supplier.body));
  const supplierId = supplier.body.id;
  const product = await request('CreateProduct', token, { productCode: 'SUP-DESKTOP', nameCN: '供货验收产品' });
  assert.equal(product.status, 201, JSON.stringify(product.body));
  await run(`location.hash=${JSON.stringify(`#/suppliers?supplierId=${supplierId}&view=products`)};true`);
  await wait(button('新增供货关系'));
  await run(`${button('新增供货关系')}.click();true`);
  await wait(`document.querySelector('select[name=productId] option[value="${product.body.id}"]')`);
  await run(`(()=>{const select=document.querySelector('select[name=productId]');Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype,'value').set.call(select,${JSON.stringify(String(product.body.id))});select.dispatchEvent(new Event('change',{bubbles:true}));return true;})()`);
  await run("document.querySelector('input[name=referencePrice]').focus();document.querySelector('input[name=referencePrice]').select();true");
  await cdp.send('Input.insertText', { text: '-9007199254740993.0100' });
  await run(`${button('保存供货关系')}.click();true`);
  await wait("document.body.innerText.includes('供货关系已保存。')");
  const links = await request('QuerySupplierProductLinks', token, undefined, { supplierId });
  assert.equal(links.status, 200, JSON.stringify(links.body));
  assert.equal(links.body.items[0].referencePrice, '-9007199254740993.01');
  assert.equal(await run("document.querySelector('input[name=referencePrice]').value"), '-9007199254740993.01');
  await captureScreenshot(cdp, path.join(output, 'Full-supplier-price.png'));
  for (let index = 0; index < 21; index++) {
    const result = await request('CreateSupplierAssessment', token, {
      id: 0, supplierCompanyId: supplierId,
      assessmentDate: businessDate, assessmentKind: '定期评价', qualityScore: index % 5 + 1,
      deliveryScore: 4, serviceScore: 4, priceScore: 4, conclusion: '合格', notes: `分页评价 ${index}`,
    }, { supplierId });
    assert.equal(result.status, 201, JSON.stringify(result.body));
  }
  await run("window.__supplierReads=[];const original=window.fetch;window.fetch=(input,...args)=>{const url=typeof input==='string'?input:input.url;if(url?.includes('assessment-analytics'))window.__supplierReads.push(url);return original(input,...args);};true");
  await run(`location.hash=${JSON.stringify(`#/suppliers?supplierId=${supplierId}&view=assessments`)};true`);
  await wait(button('记录新评价'));
  assert.equal(await run('window.__supplierReads.length'), 0, 'analytics is loaded only on demand');
  await run(`${button('记录新评价')}.click();true`);
  await wait("document.querySelector('textarea[name=notes]')");
  await run("document.querySelector('textarea[name=notes]').focus();true");
  await cdp.send('Input.insertText', { text: '中文评价保存回读' });
  await run(`${button('保存评价草稿')}.click();true`);
  await wait("document.body.innerText.includes('供应商评价已保存。')");
  await run(`${button('返回评价记录')}.click();true`);
  await wait("document.querySelectorAll('.supplier-assessment-workspace tbody tr').length===20");
  await run(`${button('下一页')}.click();true`);
  await wait("document.querySelectorAll('.supplier-assessment-workspace tbody tr').length===2");
  await run(`${button('趋势分析')}.click();true`);
  await wait("document.querySelector('.supplier-dimension-list')");
  const analytics = await request('GetSupplierAssessmentAnalytics', token, undefined, { supplierId });
  assert.equal(analytics.status, 200, JSON.stringify(analytics.body));
  assert.equal(analytics.body.totalCount, 22); assert.equal(analytics.body.qualityTotal, 65);
  assert.equal(analytics.body.recent.length, 12);
  assert.equal(await run("document.querySelector('.supplier-dimension-row strong').textContent"), '2.95');
  assert.equal(await run('window.__supplierReads.length'), 1);
  await captureScreenshot(cdp, path.join(output, 'Full-supplier-analytics.png'));
  fs.writeFileSync(path.join(output, 'supplier-consistency-result.json'), JSON.stringify({ runtime: 'Tauri+React+Rust+SQLite', supplierId,
    referencePrice: links.body.items[0].referencePrice, assessmentCount: analytics.body.totalCount, recentCount: 12, qualityTotal: 65 }, null, 2));
}

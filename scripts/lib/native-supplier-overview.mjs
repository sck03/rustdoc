import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { captureScreenshot } from './web-runtime-browser-session.mjs';
import { waitFor } from './web-runtime-smoke-common.mjs';

export async function verifyDesktopSupplierOverview({ run, request, token, businessDate, cdp, output }) {
  for (let index = 0; index < 31; index++) {
    const supplier = await request('CreateSupplier', token, { name: '概览分页供应商-' + index, category: '服装' });
    assert.equal(supplier.status, 201, JSON.stringify(supplier.body));
    const supplierId = supplier.body.id;
    const assessment = await request('CreateSupplierAssessment', token, { id: 0, supplierCompanyId: supplierId,
      assessmentDate: businessDate, assessmentKind: '定期评价', qualityScore: 4, deliveryScore: 3,
      serviceScore: 4, priceScore: 3, conclusion: index < 2 ? '观察' : '合格', notes: '隔离概览验收' }, { supplierId });
    assert.equal(assessment.status, 201, JSON.stringify(assessment.body));
    const confirmed = await request('ConfirmSupplierAssessment', token, undefined,
      { supplierId, id: assessment.body.id, expectedVersion: assessment.body.versionNumber });
    assert.equal(confirmed.status, 200, JSON.stringify(confirmed.body));
  }
  const wait = expression => waitFor(() => run(
    'Boolean(' + expression + ')'), 20000, expression);
  const section = "document.querySelector('.supplier-procurement-overview')";
  const button = name => "[..." + section + ".querySelectorAll('button')].find(e=>e.textContent.trim()===" + JSON.stringify(name) + ")";
  const reads = [];
  const observe = event => { const message = JSON.parse(event.data);
    if (message.method === 'Network.requestWillBeSent' && message.params.request.url.includes('/assessment-overview')) reads.push(message.params.request.url); };
  await cdp.send('Network.enable'); cdp.socket.addEventListener('message', observe);
  try {
    await run("location.hash='#/suppliers?view=overview';true");
    await wait(section + "?.querySelectorAll('tbody tr').length===30");
    const summary = await run(section + ".querySelector('.supplier-assessment-summary-grid').innerText");
    await run(button('下一页') + '.click();true');
    await wait(section + "?.querySelectorAll('tbody tr').length===1");
    assert.equal(await run(section + ".querySelector('.supplier-assessment-summary-grid').innerText"), summary);
    await run(button('需要关注') + '.click();true');
    await wait(section + "?.querySelectorAll('tbody tr').length===2");
    assert.equal(await run(section + ".querySelector('.supplier-assessment-summary-grid').innerText"), summary);
    assert(reads.some(url => new URL(url).searchParams.get('pageNumber') === '2'));
    assert(reads.some(url => new URL(url).searchParams.get('filter') === 'attention' && new URL(url).searchParams.get('pageNumber') === '1'));
    const invalid = await request('GetSupplierAssessmentOverview', token, undefined, { filter: 'invalid' });
    assert.equal(invalid.status, 400);
    await captureScreenshot(cdp, path.join(output, 'Full-supplier-overview.png'));
    fs.writeFileSync(path.join(output, 'supplier-overview-result.json'), JSON.stringify({ rows: 31, pageSizes: [30,1], attention: 2, unchangedTotals: true, requests: reads.length, invalidFilterStatus: invalid.status }, null, 2));
  } finally { cdp.socket.removeEventListener('message', observe); }
}

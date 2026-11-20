import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { captureScreenshot } from './web-runtime-browser-session.mjs';
import { waitFor } from './web-runtime-smoke-common.mjs';

export async function verifyDesktopPacking({ run, request, token, cdp, output }) {
  const wait = expression => waitFor(() => run(`Boolean(${expression})`), 20000, expression);
  const button = name => `[...document.querySelectorAll('button')].find(e=>e.textContent.trim()===${JSON.stringify(name)})`;
  const field = label => `[...document.querySelectorAll('label')].find(e=>e.querySelector('span')?.textContent===${JSON.stringify(label)})?.querySelector('input,select')`;
  async function edit(label, text) {
    await wait(field(label));
    await run(`${field(label)}.focus(); ${field(label)}.select(); true`);
    await cdp.send('Input.insertText', { text });
  }
  const draft = { id: 0, expectedVersion: 0, name: '桌面装柜分页-000', containerType: '验收柜',
    container: { length: 100, width: 100, height: 100, volume: 1, maxWeight: 1000 },
    rules: { allowRotation: true, usePalletConstraints: false, minimumSupportAreaPercent: 100, requireSameFootprintStacking: true },
    cargoItems: [{ name: '匿名纸箱', length: 50, width: 50, height: 50, weight: 10, quantity: 8, maxTopLoadWeight: 10, preferredZone: 'Auto', loadSequence: 1, priorityGroup: 'A' }] };
  for (let i = 0; i < 101; i++) {
    const saved = await request('SaveContainerPackingProject', token, { ...draft, name: `桌面装柜分页-${String(i).padStart(3, '0')}` });
    assert.equal(saved.status, 200, JSON.stringify(saved.body));
  }
  await run("location.hash='#/tools/container-packing'; true");
  await wait(`${button('末页')} && !${button('末页')}.disabled`);
  for (const width of [1024,1366,1920]) {
    await cdp.send('Emulation.setDeviceMetricsOverride',{width,height:900,deviceScaleFactor:1,mobile:false});
    await run('new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))');
    const positions=await run("[...document.querySelectorAll('.container-packing-project-actions button')].map(button=>button.getBoundingClientRect().top)");
    assert.equal(positions.length,4);
    assert.equal(await run("getComputedStyle(document.querySelector('.job-tool-panel')).display"),'grid');
    assert.equal(await run("getComputedStyle(document.querySelector('.tool-panel-heading')).display"),'flex');
    if(width>1180) assert.equal(await run("new Set([...document.querySelector('.container-packing-grid').children].map(e=>e.getBoundingClientRect().top)).size"),1);
    assert.equal(new Set(positions).size,1,'packing buttons share a row at '+width);
    await captureScreenshot(cdp,path.join(output,'Full-packing-layout-'+width+'.png'));
  }
  await cdp.send('Emulation.clearDeviceMetricsOverride');
  await run(`${button('末页')}.click(); true`);
  await wait("[...document.querySelectorAll('option')].some(e=>e.textContent.includes('桌面装柜分页-000'))");
  await edit('搜索方案', '桌面装柜分页-000');
  await wait(`${field('已存方案')}?.options.length===2`);
  await run(`(()=>{const select=${field('已存方案')};select.value=select.options[1].value;select.dispatchEvent(new Event('change',{bubbles:true}));return true;})()`);
  await run(`${button('加载方案')}.click(); true`);
  await wait("document.body.innerText.includes('装柜方案已加载。')");
  await run(`${button('分析')}.click(); true`);
  await wait(`${button('导出 PDF')} && !${button('导出 PDF')}.disabled`);
  await edit('柜长 cm', '110');
  await wait(`${button('导出 PDF')}.disabled`);
  await captureScreenshot(cdp, path.join(output, 'Full-packing-stale-analysis.png'));
  await run(`${button('分析')}.click(); true`);
  await wait(`!${button('导出 PDF')}.disabled`);
  const input = { ...draft, container: { ...draft.container, length: 110 } };
  const analyzed = await request('AnalyzeContainerPacking', token, input);
  assert.equal(analyzed.status, 200, JSON.stringify(analyzed.body));
  const destinationPath = path.join(output, 'Full-packing.pdf');
  const saved = await request('SaveContainerPackingPdfToPath', token, {
    projectName: draft.name, containerType: draft.containerType, destinationPath,
    container: input.container, analysis: analyzed.body.analysis,
  });
  assert.equal(saved.status, 200, JSON.stringify(saved.body));
  assert.equal(fs.readFileSync(destinationPath).subarray(0, 5).toString(), '%PDF-');
  await captureScreenshot(cdp, path.join(output, 'Full-packing-current-analysis.png'));
  await run(`${button('保存方案')}.click(); true`);
  await wait("document.body.innerText.includes('装柜方案已保存。')");
}

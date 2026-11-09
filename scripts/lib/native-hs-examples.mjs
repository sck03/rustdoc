import assert from 'node:assert/strict';
import path from 'node:path';
import { captureScreenshot } from './web-runtime-browser-session.mjs';
import { waitFor } from './web-runtime-smoke-common.mjs';

export async function verifyDesktopHsExamples({ run, request, token, cdp, output }) {
  const wait = expression => waitFor(() => run(`Boolean(${expression})`), 15000, expression);
  const button = (name, root = 'document') => `[...(${root}?.querySelectorAll('button')??[])].find(e=>e.textContent.trim()===${JSON.stringify(name)})`;
  const click = async expression => { await wait(`${expression} && !${expression}.disabled`); await run(`${expression}.click(); true`); };
  const field = label => `[...document.querySelectorAll('.knowledge-editor label')].find(e=>e.firstChild?.textContent===${JSON.stringify(label)})?.querySelector('input,textarea')`;
  async function type(label, text) {
    await run(`${field(label)}.focus(); true`);
    await cdp.send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'a', code: 'KeyA', modifiers: 2, windowsVirtualKeyCode: 65 });
    await cdp.send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'a', code: 'KeyA', modifiers: 2, windowsVirtualKeyCode: 65 });
    await cdp.send('Input.insertText', { text });
  }
  const list = async () => {
    const result = await request('ListHsCodeKnowledgeExamples', token);
    assert.equal(result.status, 200, JSON.stringify(result.body));
    return result.body.items;
  };
  await run("location.hash='#/master-data/hs-knowledge/examples'; true");
  await click(button('新增实例'));
  await type('商品名称', '桌面实例并发验收');
  await type('历史/原始编码', '6109100000');
  await click(button('保存实例'));
  await wait("document.querySelector('.inline-notice[data-tone=success]') && !document.querySelector('.knowledge-editor button[type=submit]').disabled");
  const saved = (await list()).find(row => row.productName === '桌面实例并发验收');
  assert(saved?.versionNumber > 0, 'Real React save returns a versioned persisted example');
  const body = { id: saved.id, expectedVersion: saved.versionNumber, rawReportedHsCode: saved.rawReportedHsCode, resolvedCurrentHsCode: saved.resolvedCurrentHsCode ?? '',
    productName: saved.productName, specification: '另一个会话更新', source: saved.source, sourceYear: saved.sourceYear,
    resolutionStatus: saved.resolutionStatus, isManuallyVerified: saved.isManuallyVerified };
  const changed = await request('SaveHsCodeKnowledgeExample', token, body);
  assert.equal(changed.status, 200, JSON.stringify(changed.body));
  await type('规格与申报要素', '应保留的中文草稿');
  await click(button('保存实例'));
  await wait("document.querySelector('.inline-notice[data-tone=error]') && !document.querySelector('.knowledge-editor button[type=submit]').disabled");
  assert.equal(await run(`${field('规格与申报要素')}.value`), '应保留的中文草稿');
  assert.equal((await list()).find(row => row.id === saved.id).specification, '另一个会话更新');
  await captureScreenshot(cdp, path.join(output, 'Full-hs-example-conflict.png'));
  await click(button('关闭'));
  await click(button('关闭编辑', "document.querySelector('[data-dialog-purpose=confirmation]')"));
  await wait("!document.querySelector('.knowledge-editor')");
  await click(button('刷新'));
  await wait("document.querySelector('.knowledge-table')?.textContent.includes('另一个会话更新')");
  const row = "[...document.querySelectorAll('.knowledge-table article')].find(e=>e.textContent.includes('桌面实例并发验收'))";
  await click(button('删除', `(${row})`));
  await click(button('确认删除', "document.querySelector('[data-dialog-purpose=confirmation]')"));
  await waitFor(async () => !(await list()).some(row => row.id === saved.id), 15000, 'Versioned React delete did not remove the current example');
}

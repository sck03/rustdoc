import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import http from 'node:http';
import { createReactUiFixture } from "./lib/react-ui-fixture.mjs";
import { navigationReorganizationUiFixture } from './lib/navigation-reorganization-ui-fixture.mjs';

const { root: repo, web, output, require, bundle, source, launch } = createReactUiFixture("supplier-lifecycle-ui");
const setup = `
window.cache=queries;window.pending=[];window.downloads=0;
URL.createObjectURL=()=>{window.downloads++;return 'blob:fixture'};URL.revokeObjectURL=()=>{};
const pending=(kind,input,{signal})=>new Promise((resolve,reject)=>window.pending.push({kind,input,signal,resolve,reject}));
client.exportSuppliers=(input,options)=>pending('export',input,options);
client.updateSupplier=(input,options)=>pending('save',input,options);
client.createSupplierContact=(input,options)=>pending('contact',input,options);
client.querySupplierContacts=(input,options)=>window.pauseContacts?pending('contacts',input,options):call('supplierContacts',input,page([{...supplierContact,supplierCompanyId:input.supplierId}],input));
`;
await require('esbuild').build({ stdin: { loader: 'tsx', resolveDir: web, contents: navigationReorganizationUiFixture(source, setup) },
  outfile: path.join(output, 'app.js'), bundle: true, format: 'esm', platform: 'browser', jsx: 'automatic', logLevel: 'silent' });
const server = http.createServer((request, response) => {
  const file = new URL(request.url, 'http://localhost').pathname.slice(1);
  if (!file) { response.setHeader('Content-Type', 'text/html; charset=utf-8'); response.end('<html lang="zh-CN"><link rel="stylesheet" href="/app.css"><div id="root"></div><script type="module" src="/app.js"></script></html>'); }
  else if (['app.js', 'app.css'].includes(file)) { response.setHeader('Content-Type', file.endsWith('.js') ? 'text/javascript' : 'text/css'); response.end(fs.readFileSync(path.join(output, file))); }
  else response.writeHead(404).end();
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const browser = await launch({ viewport: { width: 1440, height: 1000 } });
let passed = 0;
async function scenario(check) {
  const page = await browser.newPage(); page.setDefaultTimeout(10000);
  try {
    await page.goto(`http://127.0.0.1:${server.address().port}/#/suppliers`);
    await page.getByRole('heading', { name: '供应商目录', exact: true }).waitFor();
    await check(page); assert.deepEqual(await page.evaluate(() => window.__errors), []); passed++;
  } catch (error) { await page.screenshot({ path: path.join(output, `failure-${passed}.png`) }); throw error; }
  finally { await page.close(); }
}
const open = async (page, id, view = 'profile') => {
  await page.evaluate(({id,view}) => { location.hash = `#/suppliers?supplierId=${id}&view=${view}`; }, {id,view});
  await page.getByRole('heading', { name: `示例单位 ${String(id).padStart(3,'0')}`, exact: true }).waitFor();
};
try {
  await scenario(async page => {
    for (let id = 1; id <= 12; id++) await open(page, id);
    await page.waitForFunction(() => window.cache.getQueryCache().getAll().filter(q => q.queryKey[0] === 'supplier').length === 1);
    assert.equal(await page.evaluate(() => window.__calls.filter(c => c.name === 'supplierContacts').length), 0);
    await page.getByRole('tab', { name: '联系人', exact: true }).click();
    await page.getByRole('cell', { name: '供应商联系人', exact: true }).waitFor();
    assert.equal(await page.evaluate(() => window.__calls.filter(c => c.name === 'supplierContacts').length), 1);
  });
  await scenario(async page => {
    await page.getByRole('button', { name: '导出当前筛选', exact: true }).click();
    await page.waitForFunction(() => window.pending.length === 1);
    await page.getByRole('button', { name: '导出当前筛选', exact: true }).dispatchEvent('click');
    assert.equal(await page.evaluate(() => window.pending.length), 1);
    await open(page, 1); await page.waitForFunction(() => window.pending[0].signal.aborted);
    await page.evaluate(async () => { window.pending[0].resolve(new Blob(['late'])); await new Promise(r => setTimeout(r, 30)); });
    assert.equal(await page.evaluate(() => window.downloads), 0);
    assert.equal(await page.evaluate(() => window.cache.getMutationCache().getAll().length), 0);
  });
  await scenario(async page => {
    await open(page, 1); await page.getByRole('button', { name: '保存供应商', exact: true }).click();
    await page.waitForFunction(() => window.pending.length === 1);
    await open(page, 2); await page.waitForFunction(() => window.pending[0].signal.aborted);
    await page.evaluate(async () => { window.pending[0].resolve({id:1,name:'LATE',versionNumber:2}); await new Promise(r=>setTimeout(r,30)); });
    assert.equal(await page.getByLabel('名称', { exact: true }).inputValue(), '示例单位 002');
  });
  await scenario(async page => {
    await open(page, 1, 'contacts'); await page.getByRole('button', { name: '新增联系人', exact: true }).click();
    await page.getByLabel('姓名', { exact: true }).fill('新增联系人回归');
    await page.getByRole('button', { name: '保存联系人', exact: true }).click();
    await page.waitForFunction(() => window.pending.length === 1);
    await page.evaluate(() => { window.pauseContacts=true;window.pending[0].resolve({id:99,supplierCompanyId:1,name:'新增联系人回归',versionNumber:1}); });
    await page.waitForFunction(() => window.pending.length === 2);
    await page.waitForFunction(() => !document.querySelector('button[type="submit"]')?.disabled);
    assert.equal(await page.getByLabel('姓名', { exact: true }).inputValue(), '新增联系人回归');
    assert(await page.getByRole('button', { name: '保存联系人', exact: true }).isEnabled(), 'Refresh must not extend the save lock');
    await page.getByRole('tab', { name: '基本资料', exact: true }).click();
    await page.waitForFunction(() => window.pending[1].signal.aborted);
    await page.evaluate(async () => { window.pending[1].reject(new Error('LATE REFRESH')); await new Promise(r=>setTimeout(r,30)); });
    assert.doesNotMatch(await page.locator('body').innerText(), /LATE REFRESH/);
  });
  console.log(`Supplier lifecycle UI: ${passed} passed; ${output}`);
} finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }

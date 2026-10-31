import assert from 'node:assert/strict';
import path from 'node:path';

export async function verifyBusinessWorkspaceLayouts({ page, invoke, token, url, output, businessDate, require }) {
  const customers = [];
  for (const [name, country] of [['远航国际采购有限公司', '中国'], ['Northwind Distribution and International Trading', '美国'], ['海川商贸', '新加坡']]) {
    const customer = await invoke('CreateCrmCustomer', { id: 0, name, countryRegion: country, website: '', source: '展会', notes: '布局验收示例资料' }, {}, token);
    customers.push(customer);
    await invoke('CreateSupplier', { id: 0, name: `${name}供货单位`, countryRegion: country, category: '成品', website: '', mainProducts: '服装及配套产品', notes: '' }, {}, token);
  }
  await invoke('CreateCrmFollowUp', { id: 0, crmCustomerId: customers[0].id, type: '电话', summary: '确认样品和交货安排', nextAction: '发送更新后的报价', followedUpAt: new Date().toISOString(), nextFollowUpAt: new Date(Date.now() + 3600000).toISOString() }, {}, token);
  await invoke('CreateSalesOpportunity', { id: 0, crmCustomerId: customers[0].id, title: '秋季服装采购', currency: 'USD', estimatedAmount: 12500, probabilityPercent: 50, expectedCloseDate: businessDate, nextAction: '确认规格', notes: '' }, {}, token);

  const capture = async (name, route, ready) => {
    await page.goto(`${url}/#${route}`);
    await ready();
    for (const width of [1440, 390]) {
      await page.setViewportSize({ width, height: width === 390 ? 844 : 1000 });
      await page.evaluate(() => document.fonts.ready);
      assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `${name}: no page overflow at ${width}`);
      await page.screenshot({ path: path.join(output, `layout-${name}-${width}.png`), fullPage: true });
    }
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.addScriptTag({ path: require.resolve('axe-core/axe.min.js') });
    const violations = await page.evaluate(async () => (await window.axe.run(document.querySelector('main'))).violations.filter(item => ['serious', 'critical'].includes(item.impact)).map(item => ({ id: item.id, targets: item.nodes.map(node => node.target) })));
    assert.deepEqual(violations, [], `${name}: accessibility`);
  };
  await capture('invoices', '/invoices', () => page.getByRole('cell', { name: 'CURRENCY-USD', exact: true }).waitFor());
  await capture('customers', '/crm/follow-ups?view=directory', () => page.getByRole('cell', { name: customers[0].name, exact: true }).waitFor());
  assert.equal(await page.getByRole('group', { name: '所选记录操作' }).count(), 0);
  await page.getByRole('checkbox', { name: `选择客户 ${customers[0].name}`, exact: true }).check();
  await page.getByRole('button', { name: '取消选择', exact: true }).click();
  assert.equal(await page.getByRole('group', { name: '所选记录操作' }).count(), 0);
  await page.getByRole('textbox', { name: '搜索客户目录' }).fill('Northwind');
  await page.getByRole('search', { name: '筛选客户目录' }).getByRole('button', { name: '搜索', exact: true }).click();
  await page.getByRole('cell', { name: customers[0].name, exact: true }).waitFor({ state: 'hidden' });
  assert.equal(await page.getByRole('cell', { name: customers[1].name, exact: true }).count(), 1);
  await capture('suppliers', '/suppliers', () => page.getByRole('cell', { name: `${customers[0].name}供货单位`, exact: true }).waitFor());
  await capture('sales', '/crm/dashboard', () => page.getByRole('region', { name: '近期跟进待办', exact: true }).getByText('发送更新后的报价', { exact: true }).waitFor());
  assert.equal(await page.getByRole('region', { name: '商机阶段漏斗', exact: true }).count(), 0);
  await page.getByRole('tab', { name: '经营分析', exact: true }).click();
  await page.getByRole('heading', { name: '商机阶段漏斗', exact: true }).waitFor();
  assert.equal(await page.getByRole('heading', { name: '近期跟进待办', exact: true }).isVisible(), false);
  await page.screenshot({ path: path.join(output, 'layout-sales-analysis.png'), fullPage: true });
  await page.getByRole('tab', { name: '跟进与成交', exact: true }).click();
  await page.getByRole('row').filter({ hasText: '发送更新后的报价' }).click();
  await page.waitForURL(/followUpId=\d+/);
  await page.getByText('正在查看指定跟进事项。', { exact: false }).waitFor();
  await page.getByRole('row').filter({ hasText: '发送更新后的报价' }).waitFor();
  await page.goto(`${url}/#/crm/dashboard`);
  await page.getByRole('row').filter({ hasText: '秋季服装采购' }).click();
  await page.waitForURL(/opportunityId=\d+/);
  assert.equal(await page.getByRole('textbox', { name: '商机名称', exact: true }).inputValue(), '秋季服装采购');
  await capture('applications', '/office/approvals?view=start', () => page.getByRole('searchbox', { name: '查找申请类型' }).waitFor());
  const cards = page.locator('.oa-hub-start-card');
  const count = await cards.count();
  await page.getByRole('searchbox', { name: '查找申请类型' }).fill('费用报销');
  assert.equal(await cards.count(), 1);
  await page.getByRole('searchbox', { name: '查找申请类型' }).fill('');
  assert.equal(await cards.count(), count);
  await page.getByRole('combobox', { name: '申请分类' }).selectOption('人事与考勤');
  assert.equal(await cards.count(), 3);
  await capture('jobs', '/jobs', () => page.getByRole('heading', { name: '任务记录', exact: true }).waitFor());
  assert.equal(await page.getByRole('region', { name: '新建任务', exact: true }).isVisible(), false);
  await page.getByRole('button', { name: '新建文件任务', exact: true }).click();
  const reportDetails = page.locator('.job-create-panel details').filter({ hasText: '批量报表 ZIP' });
  await reportDetails.locator('summary').click();
  await page.getByRole('button', { name: '收起新建任务', exact: true }).click();
  await page.getByRole('button', { name: '新建文件任务', exact: true }).click();
  assert.equal(await reportDetails.evaluate(element => element.open), true, 'closing task tools preserves the current tool');
}

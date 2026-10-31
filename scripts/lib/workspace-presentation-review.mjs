import assert from 'node:assert/strict';
import path from 'node:path';

export async function verifyWorkspacePresentation({ page, url, output }) {
  await page.goto(`${url}/#/office/approvals?view=start`);
  const applications = page.locator('.oa-hub-start-card a.command-button:not(.secondary)');
  await applications.first().waitFor();
  const colors = await applications.evaluateAll(links => links.map(link => getComputedStyle(link).color));
  assert(colors.every(color => color === 'rgb(255, 255, 255)'), 'primary application links retain readable white button text');
  await page.screenshot({ path: path.join(output, 'application-start.png'), fullPage: true });

  for (const width of [1168, 1024, 390]) {
    await page.setViewportSize({ width, height: 900 });
    for (const section of ['search', 'online', 'annual', 'history', 'examples', 'transfer']) {
      await page.goto(`${url}/#/master-data/hs-knowledge/${section}`);
      const navigation = page.getByRole('navigation', { name: 'HS知识中心功能' });
      await navigation.getByRole('link', { name: '知识库导入导出' }).waitFor();
      await page.evaluate(() => document.fonts.ready);
      const tops = await navigation.getByRole('link').evaluateAll(links => links.map(link => link.getBoundingClientRect().top));
      assert.equal(tops.length, 7);
      assert(Math.max(...tops) - Math.min(...tops) < 2, `${section} keeps seven navigation links on one row at ${width}px`);
      assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `${section} does not overflow the page at ${width}px`);
      if (section === 'online') await page.screenshot({ path: path.join(output, `hs-online-${width}.png`) });
    }
  }

  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto(`${url}/#/system/organization`);
  const tabs = page.getByRole('tablist', { name: '组织与任职分区' });
  await tabs.getByRole('tab', { name: '公司与部门', exact: true }).waitFor();
  const members = page.getByRole('region', { name: '单位任职管理', exact: true });
  assert.equal(await members.count(), 0, 'appointment workspace is only mounted when needed');
  await page.screenshot({ path: path.join(output, 'organization-departments.png') });
  await tabs.getByRole('tab', { name: '任职与岗位', exact: true }).click();
  const search = members.getByRole('textbox', { name: '搜索单位任职人员' });
  await search.fill('保留检索草稿');
  await tabs.getByRole('tab', { name: '单位身份与授权', exact: true }).click();
  assert.equal(await members.isVisible(), false);
  await page.screenshot({ path: path.join(output, 'organization-access.png') });
  await tabs.getByRole('tab', { name: '任职与岗位', exact: true }).click();
  assert.equal(await search.inputValue(), '保留检索草稿', 'workspace switching preserves pending input');
  await tabs.getByRole('tab', { name: '任职与岗位', exact: true }).press('Home');
  assert.equal(await tabs.getByRole('tab', { name: '公司与部门', exact: true }).getAttribute('aria-selected'), 'true');

  await page.goto(`${url}/#/system/access-control?view=templates`);
  const schemes = page.getByRole('region', { name: '权限方案', exact: true });
  await schemes.locator('.permission-template-card').first().waitFor();
  const category = schemes.getByRole('combobox', { name: '模块分类', exact: true });
  assert.notEqual(await category.inputValue(), '', 'asynchronously loaded resources start with one category');
  const categoryCount = await schemes.locator('.permission-resource-card').count();
  assert(categoryCount > 0 && categoryCount < 20, 'default editor avoids showing the entire permission catalog');
  await category.selectOption('');
  assert(await schemes.locator('.permission-resource-card').count() > categoryCount);
  await schemes.getByRole('searchbox', { name: '搜索功能模块' }).fill('发票');
  await schemes.getByRole('searchbox', { name: '搜索功能模块' }).fill('');
  assert.notEqual(await category.inputValue(), '', 'clearing cross-category search restores a compact category');
  await page.screenshot({ path: path.join(output, 'permission-categories.png'), fullPage: true });
}

import assert from 'node:assert/strict';

export async function chooseUnitIdentity(page, label) {
  await page.getByRole('button', { name: /^账号菜单：/u }).click();
  await page.getByRole('menuitemradio', { name: label, exact: true }).click();
}

export async function assertUnitIdentity(page, label) {
  await page.waitForFunction(() => !document.querySelector('.account-menu-trigger')?.disabled);
  await page.getByRole('button', { name: /^账号菜单：/u }).click();
  assert.equal(await page.getByRole('menuitemradio', { name: label, exact: true }).getAttribute('aria-checked'), 'true');
  await page.waitForFunction(() => document.activeElement?.closest('[role="menu"]'));
  await page.keyboard.press('Escape');
  await page.getByRole('menu').waitFor({ state: 'hidden' });
}

export async function switchUnitIdentity(page, label) {
  const [response] = await Promise.all([
    page.waitForResponse(response => response.url().endsWith('/api/auth/unit-context')),
    chooseUnitIdentity(page, label),
  ]);
  assert(response.ok());
  await assertUnitIdentity(page, label);
  assert.equal(await page.getByText('当前页面不可用', { exact: true }).count(), 0);
}

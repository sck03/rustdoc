import assert from 'node:assert/strict';
import path from 'node:path';
import { withOfficeUi } from './lib/native-office-ui.mjs';

await withOfficeUi('foundation-options-ui', async ({ output, url, invoke, openPage, require }) => {
  const token = (await invoke('Login', { username: 'oa-review', password: 'Review-2026-Test' })).accessToken;
  const directory = await invoke('GetOrganizationDirectory', undefined, {}, token);
  const unitId = String(directory.companies.find(unit => unit.code === 'DEFAULT').id);
  const candidates = await invoke('GetIdentityCandidates', undefined, { unitId }, token);
  assert.equal(candidates.departmentCount, undefined, 'Candidate search no longer repeats the department directory');
  const appointmentId = candidates.items[0].appointments[0].id;
  const projects = [];
  for (let index = 0; index < 25; index++) projects.push(await invoke('SaveProject', {
    unitId, code: `PAGED-${String(index).padStart(3, '0')}`, name: `分页项目 ${index}`,
    ownerAppointmentId: appointmentId, members: [], isActive: true, expectedVersion: 0, note: '分页与范围验收',
  }, {}, token));
  const page = await openPage('oa-review', 'Review-2026-Test');
  await page.goto(`${url}/#/system/organization`);
  await page.getByRole('tab', { name: '任职与岗位', exact: true }).click();
  await page.locator('summary').filter({ hasText: '岗位、雇佣与审批职责' }).click();
  await page.getByRole('button', { name: '业务职责', exact: true }).click();
  await page.getByRole('button', { name: '新增业务职责', exact: true }).click();
  const editor = page.getByRole('dialog', { name: '新增业务职责', exact: true });
  await editor.getByLabel('名称', { exact: true }).fill('有项目限制的职责');
  await editor.getByLabel('代码', { exact: true }).fill('PAGED-RESPONSIBILITY');
  await editor.getByLabel('通用申请', { exact: true }).check();
  await editor.getByRole('button', { name: '增加职责承担人', exact: true }).click();
  await editor.getByLabel('承担任职', { exact: true }).selectOption(appointmentId);
  const project = editor.getByRole('region', { name: '限定项目（选填）', exact: true });
  await project.locator('summary').click();
  await project.getByRole('button', { name: '下一页', exact: true }).click();
  await project.getByRole('combobox', { name: '限定项目（选填）', exact: true }).selectOption(projects[24].id);
  await project.getByRole('searchbox').fill('PAGED-000');
  await project.getByRole('combobox').getByRole('option', { name: '分页项目 0 · PAGED-000', exact: true }).waitFor({ state: 'attached' });
  assert.equal(await project.getByRole('combobox').inputValue(), projects[24].id, 'Search retains selected project outside the page');
  await editor.getByLabel('生效日', { exact: true }).fill('2026-01-01');
  await editor.getByLabel('维护原因', { exact: true }).fill('职责限定到明确项目');
  const saving = page.waitForResponse(r => r.url().endsWith('/api/organization/responsibilities') && r.request().method() === 'POST');
  await editor.getByRole('button', { name: '保存业务职责', exact: true }).click();
  const savedResponse = await saving;
  assert.equal(savedResponse.status(), 200, await savedResponse.text());
  const saved = await savedResponse.json();
  assert.equal(saved.assignments[0].projectId, projects[24].id);
  await editor.waitFor({ state: 'hidden' });
  await page.getByRole('button', { name: '编辑业务职责', exact: true }).click();
  const reopened = page.getByRole('dialog', { name: '编辑业务职责', exact: true });
  await reopened.getByRole('region', { name: '限定项目（选填）', exact: true }).locator('summary').click();
  await reopened.getByRole('combobox', { name: '限定项目（选填）', exact: true }).getByRole('option', { name: '分页项目 24 · PAGED-024', exact: true }).waitFor({ state: 'attached' });
  assert.equal(await reopened.getByRole('combobox', { name: '限定项目（选填）', exact: true }).inputValue(), projects[24].id);
  await page.evaluate(require('axe-core').source);
  assert.deepEqual((await page.evaluate(() => window.axe.run(document.querySelector('[role="dialog"]'), { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] } }))).violations, []);
  await page.screenshot({ path: path.join(output, 'project-scoped-responsibility.png'), fullPage: true });
});
